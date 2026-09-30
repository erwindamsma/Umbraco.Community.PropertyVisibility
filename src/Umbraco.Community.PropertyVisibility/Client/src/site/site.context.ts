import type { HiddenFieldsResponseModel, RootResolutionSource, SiteMatchReason } from '../api/index.js';
import { LOG_PREFIX } from '../constants.js';
import { countOf, debugLog, describeError, isDebugEnabled } from '../debug.js';
import { peekPackageWarning } from '../package-warning.js';
import { UmbPropertyVisibilityHiddenFieldsRepository } from '../repository/hidden-fields.repository.js';
import { UMB_PROPERTY_VISIBILITY_SITE_CONTEXT } from './site.context-token.js';
import { UmbContextBase } from '@umbraco-cms/backoffice/class-api';
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { UmbApiError } from '@umbraco-cms/backoffice/resources';

/**
 * What the server resolves a site from.
 * `documentKey` is the document in the workspace (for a new document the client-generated scaffold key, which the
 * server cannot find, so it falls back to `parentKey`). `parentKey` is only set for a not-yet-saved document:
 * `null` means created at the root, `undefined` means not applicable or not known.
 */
export interface UmbPropertyVisibilityAnchor {
	documentKey: string | undefined;
	parentKey: string | null | undefined;
}

const EMPTY_ANCHOR: UmbPropertyVisibilityAnchor = { documentKey: undefined, parentKey: undefined };

/** The fail-open response: nothing hidden. */
export const EMPTY_HIDDEN_FIELDS: HiddenFieldsResponseModel = {
	propertyTypeKeys: [],
	containerKeys: [],
	disabled: false,
	rootResolution: 'None',
	matchedSite: null,
	warnings: [],
};

/** One console warning and one notification per browser session when a hidden-fields request fails. */
let requestFailureReported = false;

function isRecord(value: unknown): value is Record<string, unknown> {
	return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isStringArray(value: unknown): value is Array<string> {
	return Array.isArray(value) && value.every((item) => typeof item === 'string');
}

/**
 * The response body as a hidden-fields response, or undefined when it does not have that shape (an HTML page from a
 * proxy or a login redirect answered with 200, a truncated body). Returns a copy with only the documented fields, and
 * the matched site reduced to its label and reason, so no root node key or name travels further even from a server
 * that still sent them. Enum values are not checked: a value a newer server adds only reaches the debug log.
 */
function readHiddenFields(data: unknown): HiddenFieldsResponseModel | undefined {
	if (!isRecord(data)) return undefined;
	const { propertyTypeKeys, containerKeys, disabled, rootResolution, matchedSite, warnings } = data;
	if (!isStringArray(propertyTypeKeys) || !isStringArray(containerKeys) || !isStringArray(warnings)) return undefined;
	if (typeof disabled !== 'boolean' || typeof rootResolution !== 'string') return undefined;

	let site: HiddenFieldsResponseModel['matchedSite'] = null;
	if (matchedSite !== null && matchedSite !== undefined) {
		if (!isRecord(matchedSite) || typeof matchedSite.label !== 'string' || typeof matchedSite.reason !== 'string') {
			return undefined;
		}
		site = { label: matchedSite.label, reason: matchedSite.reason as SiteMatchReason };
	}

	return {
		propertyTypeKeys: [...propertyTypeKeys],
		containerKeys: [...containerKeys],
		disabled,
		rootResolution: rootResolution as RootResolutionSource,
		matchedSite: site,
		warnings: [...warnings],
	};
}

/**
 * The debug summary of one response: an explicit allow-list, never the response object itself. The matched site is
 * reduced to its label and match reason, so no root node key or name reaches the console.
 */
function summarise(data: HiddenFieldsResponseModel): Record<string, unknown> {
	return {
		disabled: data.disabled,
		rootResolution: data.rootResolution,
		matchedSite: data.matchedSite ? { label: data.matchedSite.label, reason: data.matchedSite.reason } : null,
		propertyTypeKeyCount: data.propertyTypeKeys.length,
		containerKeyCount: data.containerKeys.length,
		warnings: [...data.warnings],
	};
}

/**
 * The essentials of one response as text, for the debug message itself: a copied console line, a screenshot or a
 * captured log shows the object after the message as "Object". The site with its match reason (or "no site" with the
 * root resolution, or "disabled"), the counts and the warning codes; the site is its label, never a root node's key or
 * name.
 */
function describeSummary(data: HiddenFieldsResponseModel): string {
	const codes = data.warnings.map((warning) => /^PV\d{3}\b/.exec(warning)?.[0] ?? 'uncoded');
	return [
		describeSite(data),
		countOf(data.propertyTypeKeys.length, 'property', 'properties'),
		countOf(data.containerKeys.length, 'container', 'containers'),
		codes.length === 0 ? 'no warnings' : `warnings ${codes.join(', ')}`,
	].join(', ');
}

function describeSite(data: HiddenFieldsResponseModel): string {
	if (data.disabled) return 'disabled';
	if (data.matchedSite) return `site '${data.matchedSite.label}' (${data.matchedSite.reason})`;
	return `no site (root ${data.rootResolution})`;
}

/**
 * How the parent was sent, without its key: the create-under parent of a new document is often a site root, and the
 * debug log never names a root node's key (the request URL in the network panel still shows what was sent).
 */
function describeParent(parentKey: string | null | undefined): string {
	if (parentKey === undefined) return 'not sent (existing document)';
	if (parentKey === null) return 'created at the content root';
	return 'sent (new document)';
}

/**
 * False for the failures that are not the package's to report: a cancelled request (its workspace closed) and a 401,
 * for which Umbraco shows its own login and retries the request.
 */
function isReportableFailure(error: unknown): boolean {
	if (!(error instanceof Error)) return true;
	// By name, as Umbraco's own isUmbCancelError does: UmbCancelError and Error have the same shape, so a type guard
	// would narrow every other error away.
	if (error.name === 'UmbCancelError') return false;
	return !(UmbApiError.isUmbApiError(error) && error.status === 401);
}

/**
 * Provided on the document workspace host; shared by the document workspace context and every block workspace
 * context inside that document. Holds the site anchor and memoises the hidden-fields responses per
 * `documentKey|parentKey|contentTypeKey`, so the content and settings halves of a block, and nested blocks,
 * share one request per content type.
 */
export class UmbPropertyVisibilitySiteContext extends UmbContextBase {
	#repository = new UmbPropertyVisibilityHiddenFieldsRepository(this);
	#anchor = new UmbObjectState<UmbPropertyVisibilityAnchor>(EMPTY_ANCHOR);
	#cache = new Map<string, Promise<HiddenFieldsResponseModel>>();
	#localize = new UmbLocalizationController(this);

	/** Emits when the document or parent this workspace resolves its site from changes. */
	readonly anchor = this.#anchor.asObservable();

	constructor(host: UmbControllerHost) {
		super(host, UMB_PROPERTY_VISIBILITY_SITE_CONTEXT);
	}

	getAnchor(): UmbPropertyVisibilityAnchor {
		return this.#anchor.getValue();
	}

	/** Sets the anchor; a change clears the memoised responses. */
	setAnchor(anchor: UmbPropertyVisibilityAnchor): void {
		const current = this.#anchor.getValue();
		if (current.documentKey === anchor.documentKey && current.parentKey === anchor.parentKey) return;
		this.#cache.clear();
		this.#anchor.setValue({ documentKey: anchor.documentKey, parentKey: anchor.parentKey });
	}

	/** Clears the anchor and the memoised responses (document switch, workspace reset). */
	clearAnchor(): void {
		this.#cache.clear();
		this.#anchor.setValue(EMPTY_ANCHOR);
	}

	/**
	 * The hidden fields for a content type under the current anchor. Memoised until the anchor changes.
	 * Never rejects: without a document key, or when the request fails, the empty (fail-open) response is returned.
	 */
	getHiddenFields(contentTypeKey: string): Promise<HiddenFieldsResponseModel> {
		const { documentKey, parentKey } = this.#anchor.getValue();
		if (!documentKey) {
			debugLog(`no document to resolve a site from, nothing hidden for content type ${contentTypeKey}`);
			return Promise.resolve(EMPTY_HIDDEN_FIELDS);
		}

		const cacheKey = `${documentKey}|${String(parentKey)}|${contentTypeKey}`;
		const cached = this.#cache.get(cacheKey);
		if (cached) return cached;

		// A failure is not memoised, so the next document load retries. Only this request's own entry is forgotten: when
		// the anchor moved away and back meanwhile, the key holds a newer request.
		const forget = () => {
			if (this.#cache.get(cacheKey) === pending) this.#cache.delete(cacheKey);
		};
		const pending: Promise<HiddenFieldsResponseModel> = this.#request(documentKey, parentKey, contentTypeKey).then(
			(data) => {
				if (data) return data;
				forget();
				return EMPTY_HIDDEN_FIELDS;
			},
			(error: unknown) => {
				// #request handles every failure it knows of, so this is unexpected. Fail open all the same.
				forget();
				debugLog(`hidden-fields request failed for content type ${contentTypeKey}, nothing hidden`, {
					documentKey,
					parent: describeParent(parentKey),
					contentTypeKey,
					error: describeError(error),
				});
				this.#reportFailure(error);
				return EMPTY_HIDDEN_FIELDS;
			},
		);
		this.#cache.set(cacheKey, pending);
		return pending;
	}

	/** The response, or undefined when the request failed or the body is not a hidden-fields response (reported here). */
	async #request(
		documentKey: string,
		parentKey: string | null | undefined,
		contentTypeKey: string,
	): Promise<HiddenFieldsResponseModel | undefined> {
		const { data, error } = await this.#repository.requestHiddenFields({ documentKey, contentTypeKey, parentKey });
		const hidden = error ? undefined : readHiddenFields(data);

		// The document key is logged to correlate with the applied event; the parent only through describeParent() and
		// the response only through summarise(), built only when the debug flag is on.
		if (!hidden) {
			const failure = error ?? new Error('the response is not a hidden-fields response');
			debugLog(`hidden-fields request failed for content type ${contentTypeKey}, nothing hidden`, {
				documentKey,
				parent: describeParent(parentKey),
				contentTypeKey,
				error: describeError(failure),
			});
			this.#reportFailure(failure);
			return undefined;
		}

		if (isDebugEnabled()) {
			debugLog(`hidden fields for content type ${contentTypeKey}: ${describeSummary(hidden)}`, {
				documentKey,
				parent: describeParent(parentKey),
				contentTypeKey,
				...summarise(hidden),
			});
		}

		return hidden;
	}

	/**
	 * Reports a failed request once per browser session: a console warning with the error's name, message and status,
	 * and a warning notification headed with the package name. Umbraco's own error notification is off for this request
	 * (see hidden-fields.server.data-source.ts). Later failures only reach the debug log.
	 */
	#reportFailure(error: unknown): void {
		if (requestFailureReported || !isReportableFailure(error)) return;
		requestFailureReported = true;

		console.warn(
			`${LOG_PREFIX} the hidden-fields request failed; nothing is hidden (fail open). Later failures in this browser session are logged only with the debug flag.`,
			describeError(error),
		);
		void peekPackageWarning(this, this.#localize, 'propertyVisibility_hiddenFieldsRequestFailed');
	}

	override destroy(): void {
		this.#cache.clear();
		super.destroy();
	}
}
