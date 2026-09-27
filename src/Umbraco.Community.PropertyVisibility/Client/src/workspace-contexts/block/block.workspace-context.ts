import type { HiddenFieldsResponseModel } from '../../api/index.js';
import type { UmbPropertyVisibilityAppliedTarget } from '../../applier/applied-event.js';
import {
	UmbHiddenFieldsApplierController,
	type UmbHiddenFieldsApplySubject,
} from '../../applier/hidden-fields-applier.controller.js';
import { LOG_PREFIX } from '../../constants.js';
import { debugLog, describeError } from '../../debug.js';
import {
	EMPTY_HIDDEN_FIELDS,
	type UmbPropertyVisibilityAnchor,
	type UmbPropertyVisibilitySiteContext,
} from '../../site/site.context.js';
import { UMB_PROPERTY_VISIBILITY_SITE_CONTEXT } from '../../site/site.context-token.js';
import { UMB_BLOCK_WORKSPACE_CONTEXT } from '@umbraco-cms/backoffice/block';
import { UmbControllerBase } from '@umbraco-cms/backoffice/class-api';
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';

type BlockWorkspace = typeof UMB_BLOCK_WORKSPACE_CONTEXT.TYPE;

/** The content or the settings element manager of a block; both expose `propertyViewGuard` and `structure`. */
type BlockElementManager = BlockWorkspace['content'];

/** The anchor before the site context arrives, and after it leaves. */
const NO_ANCHOR: UmbPropertyVisibilityAnchor = Object.freeze({ documentKey: undefined, parentKey: undefined });

/** The content or the settings half of the block: its element manager, applier and request bookkeeping. */
interface BlockHalf {
	readonly target: Extract<UmbPropertyVisibilityAppliedTarget, 'block-content' | 'block-settings'>;
	readonly applier: UmbHiddenFieldsApplierController;
	/** Alias of the observer of the element manager's `contentTypeId`. */
	readonly observerAlias: string;
	manager?: BlockElementManager;
	/** The element type of this half; undefined until the block data arrives, and forever for a block without settings. */
	contentTypeKey?: string;
	/** `documentKey|parentKey|contentTypeKey` of the request started last and not reset since. */
	requestKey?: string;
	/** Moved on by every reset and every new request: a response that finds another token was superseded. */
	token: number;
}

/**
 * Registered as a workspaceContext for the block workspace (`Umb.Workspace.Block`), which Umbraco 17.6.2 and 17.7.0
 * instantiate for a block edited in a modal (Block List, Block Grid, Single Block, rich text editor blocks) and for an
 * inline-edited block (Block List and Single Block inline editing, Block Grid inline editing).
 *
 * The site comes from the package's site context, provided by the document workspace context on the document
 * workspace. The request reaches it through the DOM for an inline block and through the context proxy of `umb-modal`
 * (one per nesting level) for a block in a modal; the alias is unique to this package, so no other provider stops it.
 * A block edited outside a document (a media or member workspace, a document blueprint) never receives a site context:
 * then nothing is hidden and nothing is requested. That also holds when the media item or member is opened in a modal
 * from inside a document, whose modal proxy would otherwise forward the request to the document: the site boundary
 * workspace context (site-boundary.workspace-context.ts) stops it at the media, member or blueprint workspace.
 *
 * Each half (content and settings) has its own applier. The hidden fields are requested for the half's element type
 * through the site context, which memoises one request per document, parent and content type, so both halves, every
 * inline block and every reopened modal of the same document share them.
 *
 * Resets, verified against Umbraco 17.6.2 and 17.7.0: `UmbBlockWorkspaceContext.resetState()` has no call site, so the
 * element managers never clear their guard rules and the structure is only cleared when a different element type is
 * loaded.
 * A half is therefore reset here, synchronously, when its `contentTypeId` changes or becomes undefined, when the site
 * context's anchor moves to another document (or to none) and when the site context goes away.
 */
export class UmbPropertyVisibilityBlockWorkspaceContext extends UmbControllerBase {
	#content = this.#createHalf('block-content', 'observeContentTypeId');
	#settings = this.#createHalf('block-settings', 'observeSettingsTypeId');

	#site?: UmbPropertyVisibilitySiteContext;
	#anchor: UmbPropertyVisibilityAnchor = NO_ANCHOR;

	constructor(host: UmbControllerHost) {
		super(host);

		this.consumeContext(UMB_BLOCK_WORKSPACE_CONTEXT, (workspace) => {
			this.#bind(this.#content, workspace?.content);
			this.#bind(this.#settings, workspace?.settings);
		});

		// Never awaited: outside a document the site context never arrives, and that is not an error.
		this.consumeContext(UMB_PROPERTY_VISIBILITY_SITE_CONTEXT, (site) => this.#onSite(site));
	}

	override destroy(): void {
		this.#site = undefined;
		this.#anchor = NO_ANCHOR;
		this.#reset(this.#content);
		this.#reset(this.#settings);
		super.destroy();
	}

	#createHalf(target: BlockHalf['target'], observerAlias: string): BlockHalf {
		return { target, applier: new UmbHiddenFieldsApplierController(this, target), observerAlias, token: 0 };
	}

	#bind(half: BlockHalf, manager: BlockElementManager | undefined): void {
		if (half.manager !== manager) {
			this.#reset(half);
			half.manager = manager;
			half.contentTypeKey = undefined;
		}
		// Emits the current value right away; undefined for a half whose data has not arrived, and for the settings of a
		// block type without a settings element type (its settings data is never set).
		this.observe(
			manager?.contentTypeId,
			(contentTypeKey) => this.#onContentType(half, contentTypeKey ?? undefined),
			half.observerAlias,
		);
	}

	#onContentType(half: BlockHalf, contentTypeKey: string | undefined): void {
		if (contentTypeKey === half.contentTypeKey) return;

		// Synchronously, in the emission itself: the element manager starts loading the new type from the same emission
		// and clears its structure only after an await, so a response for the previous type never touches the new
		// structure, and the previous type's rules are gone before the new type renders.
		this.#reset(half);
		half.contentTypeKey = contentTypeKey;
		this.#refresh(half);
	}

	#onSite(site: UmbPropertyVisibilitySiteContext | undefined): void {
		if (site !== this.#site) {
			// Another document workspace (or none): its anchor and its memoised responses are not ours.
			this.#site = site;
			this.#anchor = NO_ANCHOR;
			this.#reset(this.#content);
			this.#reset(this.#settings);
		}
		this.observe(site?.anchor, (anchor) => this.#onAnchor(anchor ?? NO_ANCHOR), 'observeAnchor');
	}

	#onAnchor(anchor: UmbPropertyVisibilityAnchor): void {
		const previous = this.#anchor;
		if (previous.documentKey === anchor.documentKey && previous.parentKey === anchor.parentKey) return;
		this.#anchor = anchor;

		if (previous.documentKey !== anchor.documentKey) {
			// Another document, or none while the document workspace resets: the previous document's rules must not stay.
			this.#reset(this.#content);
			this.#reset(this.#settings);
		}

		// The same document with another parent (the create-under parent arrived, or the first save made the key
		// resolvable): the stored response stays until its replacement is applied (apply() resets and re-adds the rules
		// in one task, so nothing flickers), and a response still in flight for the previous anchor is dropped because
		// the request token moves on.
		this.#refresh(this.#content);
		this.#refresh(this.#settings);
	}

	/** Synchronous: drops any response in flight, removes the half's rules and stops its structure observer. */
	#reset(half: BlockHalf): void {
		half.token++;
		half.requestKey = undefined;
		half.applier.reset();
	}

	/** Requests and applies the half's hidden fields when everything is known and differs from the last request. */
	#refresh(half: BlockHalf): void {
		const site = this.#site;
		const manager = half.manager;
		const contentTypeKey = half.contentTypeKey;
		const { documentKey, parentKey } = this.#anchor;
		// Without a type there is nothing to apply to (the structure never loads); without a document there is no site.
		if (!site || !manager || !contentTypeKey || !documentKey) return;

		const requestKey = `${documentKey}|${String(parentKey)}|${contentTypeKey}`;
		if (requestKey === half.requestKey) return;
		half.requestKey = requestKey;

		const token = ++half.token;
		void this.#requestAndApply(half, token, site, manager, { documentKey, contentTypeKey });
	}

	async #requestAndApply(
		half: BlockHalf,
		token: number,
		site: UmbPropertyVisibilitySiteContext,
		manager: BlockElementManager,
		subject: UmbHiddenFieldsApplySubject,
	): Promise<void> {
		let hidden: HiddenFieldsResponseModel;
		try {
			// Called before the first await, so the site context reads the same anchor this request was keyed on.
			hidden = await site.getHiddenFields(subject.contentTypeKey);
		} catch (error) {
			// The site context resolves a failed request with the empty response, so a rejection is unexpected. Fail open
			// and still report the pass, so a listener does not wait.
			if (token === half.token) await half.applier.apply(manager, EMPTY_HIDDEN_FIELDS, subject);
			console.warn(
				`${LOG_PREFIX} the hidden-fields request failed; nothing is hidden in this block.`,
				describeError(error),
			);
			return;
		}

		// A response for an earlier document, anchor or element type that resolves late is dropped, not applied.
		if (token !== half.token) {
			debugLog(`dropped a hidden-fields response (${half.target}) superseded by a newer document, anchor or type`, {
				...subject,
			});
			return;
		}

		try {
			await half.applier.apply(manager, hidden, subject);
		} catch (error) {
			// The element type's structure failed to load; the applier has reported nothing hidden. Fail open.
			if (token === half.token) half.applier.reset();
			console.warn(
				`${LOG_PREFIX} could not apply the hidden fields; nothing is hidden in this block.`,
				describeError(error),
			);
		}
	}
}

export { UmbPropertyVisibilityBlockWorkspaceContext as api };
export default UmbPropertyVisibilityBlockWorkspaceContext;
