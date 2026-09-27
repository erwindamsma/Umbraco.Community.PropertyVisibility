import { UmbHiddenFieldsApplierController } from '../../applier/hidden-fields-applier.controller.js';
import { LOG_PREFIX } from '../../constants.js';
import { debugLog, describeError } from '../../debug.js';
import type { HiddenFieldsResponseModel } from '../../api/index.js';
import { EMPTY_HIDDEN_FIELDS, UmbPropertyVisibilitySiteContext } from '../../site/site.context.js';
import { UmbControllerBase } from '@umbraco-cms/backoffice/class-api';
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UMB_DOCUMENT_WORKSPACE_CONTEXT } from '@umbraco-cms/backoffice/document';
import { UMB_PARENT_ENTITY_CONTEXT } from '@umbraco-cms/backoffice/entity';
import { observeMultiple } from '@umbraco-cms/backoffice/observable-api';

type DocumentWorkspace = typeof UMB_DOCUMENT_WORKSPACE_CONTEXT.TYPE;

/** `null` is a document created at the root; `undefined` is unknown or not applicable. */
type ParentKey = string | null | undefined;

/**
 * Registered as a workspaceContext for the document workspace. Provides the site context on the workspace host,
 * requests the hidden fields for the document's type and hands the response to the applier.
 *
 * Reset ordering, verified against Umbraco 17.6.2 and 17.7.0 for navigating A -> B inside one workspace:
 * load(B) -> resetState() -> _data.clear() [contentTypeUnique emits undefined: applier.reset() runs here, synchronously]
 * -> structure.clear() + propertyViewGuard.clearRules() -> setUnique(B) -> request -> structure.loadType
 * -> contentTypeUnique emits B's type -> apply(). Without that synchronous reset a still-subscribed observer holding
 * A's response would remove A's containers from B's fresh structure before B's response arrives.
 */
export class UmbPropertyVisibilityDocumentWorkspaceContext extends UmbControllerBase {
	#site = new UmbPropertyVisibilitySiteContext(this);
	#applier = new UmbHiddenFieldsApplierController(this, 'document');

	#workspace?: DocumentWorkspace;
	#unique?: string;
	#contentTypeUnique?: string;
	#isNew = false;
	#parentFromContext: ParentKey = undefined;
	#requestToken = 0;

	constructor(host: UmbControllerHost) {
		super(host);

		this.consumeContext(UMB_DOCUMENT_WORKSPACE_CONTEXT, (workspace) => {
			this.#workspace = workspace;
			this.#reset();
			this.observe(
				workspace ? observeMultiple([workspace.unique, workspace.contentTypeUnique, workspace.isNew]) : undefined,
				(values) => {
					if (!values) return;
					const [unique, contentTypeUnique, isNew] = values;
					this.#onWorkspaceChange(unique, contentTypeUnique, isNew);
				},
				'observeWorkspace',
			);
		});

		// Provided inside the document workspace by the document menu structure context; for a new document it is
		// filled from the create-under parent after an ancestors request. Never awaited: a document created at the
		// root may never get a parent, and parentKey null is the correct anchor for it.
		this.consumeContext(UMB_PARENT_ENTITY_CONTEXT, (parentContext) => {
			this.observe(parentContext?.parent, (parent) => this.#onParent(parent?.unique), 'observeParent');
		});
	}

	override destroy(): void {
		this.#reset();
		super.destroy();
	}

	#onWorkspaceChange(unique: string | null | undefined, contentTypeUnique: string | undefined, isNew: boolean | undefined): void {
		// Fires inside resetState() (contentTypeUnique becomes undefined) and when the workspace is reused for
		// another document: reset synchronously, before the workspace clears its structure and guard rules.
		if (contentTypeUnique === undefined || unique !== this.#unique) {
			this.#reset();
		}

		if (!unique || !contentTypeUnique) return;

		// resetState() sets isNew to undefined before it clears the data, so for one emission the previous document's
		// keys arrive with isNew undefined. Not ready: treating it as false would look like a new-to-saved flip and
		// fire a request for the departing document. load() and createScaffold() set a boolean before the data arrives.
		if (isNew === undefined) return;

		const nextIsNew = isNew === true;
		if (this.#unique === unique && this.#contentTypeUnique === contentTypeUnique && this.#isNew === nextIsNew) return;

		this.#unique = unique;
		this.#contentTypeUnique = contentTypeUnique;
		this.#isNew = nextIsNew;

		// A new document's key does not exist server side, so its parent is sent along. When isNew flips to false after
		// the first save the anchor changes, the cache clears and the request repeats with a key that now resolves.
		this.#site.setAnchor({ documentKey: unique, parentKey: nextIsNew ? this.#resolveParentKey() : undefined });
		void this.#requestAndApply();
	}

	#onParent(parentUnique: ParentKey): void {
		if (parentUnique === undefined) return;
		this.#parentFromContext = parentUnique;

		if (!this.#unique || !this.#isNew) return;

		// Only a parent that was not known when the first request went out is filled in; a known one is never replaced.
		// On Umbraco 17 getParentUnique() is authoritative: the first request already carries the create-under parent, and
		// the parent entity context can emit a different, later value (the tree's parent) that would re-anchor the
		// document to another site's rules. Replacing a known parent would also be one-way: removeContainer cannot be
		// undone until the workspace clears its structure, so tabs and groups the first response removed would not come
		// back when the second hides fewer. Umbraco 18 (no getParentUnique) sends the first request without a parent
		// unless the context was faster, and this path fills it in.
		if (this.#site.getAnchor().parentKey !== undefined) return;

		this.#site.setAnchor({ documentKey: this.#unique, parentKey: parentUnique });
		void this.#requestAndApply();
	}

	#resolveParentKey(): ParentKey {
		// Umbraco 17: the create-under parent is available synchronously through getParentUnique() (deprecated, removed
		// in 18), so the first request already carries it; the parent entity context covers the rest.
		const probe = this.#workspace as unknown as { getParentUnique?: () => ParentKey } | undefined;
		const immediate = typeof probe?.getParentUnique === 'function' ? probe.getParentUnique() : undefined;
		return immediate !== undefined ? immediate : this.#parentFromContext;
	}

	#reset(): void {
		this.#requestToken++;
		this.#applier.reset();
		this.#site.clearAnchor();
		this.#unique = undefined;
		this.#contentTypeUnique = undefined;
		this.#isNew = false;
		// The next document's parent context emits its own parent after setUnique; a value kept from the previous
		// document would anchor the next new document to the wrong parent.
		this.#parentFromContext = undefined;
	}

	async #requestAndApply(): Promise<void> {
		const workspace = this.#workspace;
		const documentKey = this.#unique;
		const contentTypeUnique = this.#contentTypeUnique;
		if (!workspace || !documentKey || !contentTypeUnique) return;

		const token = ++this.#requestToken;
		const subject = { documentKey, contentTypeKey: contentTypeUnique };

		let hidden: HiddenFieldsResponseModel;
		try {
			hidden = await this.#site.getHiddenFields(contentTypeUnique);
		} catch (error) {
			// The site context resolves a failed request with the empty response, so a rejection is unexpected. Fail open
			// and still report the pass (an empty response is applied as "nothing hidden"), so a listener does not wait.
			if (token === this.#requestToken) await this.#applier.apply(workspace, EMPTY_HIDDEN_FIELDS, subject);
			console.warn(
				`${LOG_PREFIX} the hidden-fields request failed; nothing is hidden for this document.`,
				describeError(error),
			);
			return;
		}

		// A response for an earlier document (or an earlier anchor) that resolves late is dropped, not applied.
		if (token !== this.#requestToken) {
			debugLog('dropped a hidden-fields response superseded by a newer document or anchor', subject);
			return;
		}

		try {
			await this.#applier.apply(workspace, hidden, subject);
		} catch (error) {
			// The structure failed to load (a missing composition, for example); the applier has reported nothing hidden.
			// Fail open: nothing hidden, no unhandled rejection.
			if (token === this.#requestToken) this.#applier.reset();
			console.warn(
				`${LOG_PREFIX} could not apply the hidden fields; nothing is hidden for this document.`,
				describeError(error),
			);
		}
	}
}

export { UmbPropertyVisibilityDocumentWorkspaceContext as api };
export default UmbPropertyVisibilityDocumentWorkspaceContext;
