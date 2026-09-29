import { APPLIED_EVENT_NAME } from '../constants.js';
import { debugLog } from '../debug.js';

/** What an applier works on: the document workspace, or a block's Content or Settings view. */
export type UmbPropertyVisibilityAppliedTarget = 'document' | 'block-content' | 'block-settings';

/**
 * Detail of the `umbraco-community-property-visibility:applied` window event. Public and stable: fields may be added
 * in a minor version, none is removed or changes meaning before a major version.
 */
export interface UmbPropertyVisibilityAppliedEventDetail {
	/** The document the workspace edits (for a not-yet-saved document the scaffold key). Blocks report their document. */
	documentKey: string;
	/** The document type, or for a block half the element type, whose hidden fields were applied. */
	contentTypeKey: string;
	/** Number of property types hidden by view guard rules after this pass. */
	propertyCount: number;
	/**
	 * Number of the response's tab and group keys that are absent from the workspace structure after this pass.
	 * Normally every key of the response; fewer when hiding tabs and groups is unavailable in the running Umbraco.
	 */
	containerCount: number;
	/** Which applier finished: the document workspace, or a block's Content or Settings view. */
	target: UmbPropertyVisibilityAppliedTarget;
}

declare global {
	interface WindowEventMap {
		[APPLIED_EVENT_NAME]: CustomEvent<UmbPropertyVisibilityAppliedEventDetail>;
	}
}

/**
 * Dispatches the applied event on `window` (always, it is cheap and lets tests and integrators wait for a
 * deterministic signal instead of a timeout) and, with the debug flag on, logs it.
 */
export function dispatchAppliedEvent(detail: UmbPropertyVisibilityAppliedEventDetail): void {
	const frozen = Object.freeze({ ...detail });
	debugLog(`applied (${frozen.target})`, { ...frozen });
	window.dispatchEvent(new CustomEvent(APPLIED_EVENT_NAME, { detail: frozen }));
}
