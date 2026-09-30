import { DEBUG_STORAGE_KEY, LOG_PREFIX } from './constants.js';

/**
 * True when `localStorage[DEBUG_STORAGE_KEY] === '1'`. Read on every call, so the flag takes effect without a reload
 * for everything logged after it is set. False when storage is unavailable (private mode, blocked site data).
 */
export function isDebugEnabled(): boolean {
	try {
		return globalThis.localStorage?.getItem(DEBUG_STORAGE_KEY) === '1';
	} catch {
		return false;
	}
}

/**
 * Writes one `console.debug` line with the package prefix when the debug flag is on.
 * Callers pass an explicit summary object, never a whole response: the log must not carry the key or name of a
 * site root node (the server does not send them, and nothing here may add them back).
 */
export function debugLog(message: string, data?: Record<string, unknown>): void {
	if (!isDebugEnabled()) return;
	if (data === undefined) {
		console.debug(`${LOG_PREFIX} ${message}`);
	} else {
		console.debug(`${LOG_PREFIX} ${message}`, data);
	}
}

/** A count with its noun (`1 property`, `2 properties`), for the summaries in the text of the debug lines. */
export function countOf(count: number, singular: string, plural: string): string {
	return `${count} ${count === 1 ? singular : plural}`;
}

/**
 * Name, message and HTTP status of an error, for every console line the package writes about one. Never the error
 * object itself: an API error carries the request, whose URL holds the `parentKey` of a new document, often a site
 * root's key.
 */
export function describeError(error: unknown): Record<string, unknown> {
	if (!(error instanceof Error)) return { message: String(error) };
	const status = (error as { status?: unknown }).status;
	return { name: error.name, message: error.message, ...(typeof status === 'number' ? { status } : {}) };
}
