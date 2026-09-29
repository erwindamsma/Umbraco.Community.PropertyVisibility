import type { Page, Request, Response } from '@playwright/test';
import { APPLIED_EVENT, DIRECT_CALL_HEADER, HIDDEN_FIELDS_PATH } from './env.js';

/** Which applier finished: the document workspace, or a block's Content or Settings view. */
export type AppliedTarget = 'document' | 'block-content' | 'block-settings';

/** One `umbraco-community-property-visibility:applied` event as recorded in the page. */
export interface AppliedEvent {
	documentKey: string;
	contentTypeKey: string;
	propertyCount: number;
	containerCount: number;
	target: AppliedTarget;
	/** `Date.now()` in the page when the event was dispatched (the browser's wall clock, like `request.timing()`). */
	at: number;
}

export interface AppliedFilter {
	documentKey?: string;
	contentTypeKey?: string;
	target?: AppliedTarget;
	/** Only events dispatched at or after this browser wall-clock time (ms since the epoch). */
	since?: number;
}

/** Default wait for an apply; well under the per-test timeout, so a missing event fails with its own message. */
const APPLIED_TIMEOUT_MS = 30_000;

/** How often `settle()` re-checks the recorded requests and events (a poll interval, not a delay). */
const POLL_MS = 50;

type RecorderWindow = Window & { __pvApplied?: AppliedEvent[] };

/**
 * Runs in the page (init script and current document): records every applied event with its dispatch time into
 * `window.__pvApplied`. Idempotent per document. The list lives as long as the document, so it restarts on a full
 * page load and survives the backoffice's client-side navigation.
 */
function recordAppliedEvents(eventName: string): void {
	const recorderWindow = window as RecorderWindow;
	if (recorderWindow.__pvApplied) return;
	const events: AppliedEvent[] = [];
	recorderWindow.__pvApplied = events;
	window.addEventListener(eventName, (event) => {
		const detail = (event as CustomEvent<Omit<AppliedEvent, 'at'>>).detail;
		if (detail) events.push({ ...detail, at: Date.now() });
	});
}

// ---- Hidden-fields responses seen by the page (for settle) ---------------------------------------------------------

interface HiddenFieldsRecord {
	documentKey: string;
	contentTypeKey: string;
	/** Browser wall-clock start of the request (Playwright `request.timing().startTime`). */
	startTime: number;
	/**
	 * Browser wall-clock time the response started to arrive (start plus `responseStart`). Its apply cannot be earlier:
	 * the body is still to come. An applied event for the same document and content type before this time belongs to
	 * the previous response (still applied, its structure observer live, until the new one arrives), not to this one.
	 */
	arrivedAt: number;
	/** Main-frame document the request belongs to; a full navigation starts a new one. */
	generation: number;
	/** Its apply (or a later one that supersedes it) has been observed. */
	consumed: boolean;
}

interface Tracker {
	generation: number;
	requestGeneration: WeakMap<Request, number>;
	pending: Set<Request>;
	records: HiddenFieldsRecord[];
}

const trackers = new WeakMap<Page, Tracker>();

/** A hidden-fields request sent by the package (the suite's own direct API calls are excluded). */
function isHiddenFieldsRequest(request: Request): boolean {
	return (
		new URL(request.url()).pathname.toLowerCase() === HIDDEN_FIELDS_PATH.toLowerCase() &&
		request.headers()[DIRECT_CALL_HEADER] === undefined
	);
}

function lower(value: string | null | undefined): string {
	return (value ?? '').toLowerCase();
}

/** Browser wall-clock start of a request, or undefined when Playwright has no timing for it. */
function requestStart(request: Request): number | undefined {
	const start = request.timing().startTime;
	return Number.isFinite(start) && start > 0 ? start : undefined;
}

/**
 * Browser wall-clock time the response of a request started to arrive: the earliest moment its apply can happen.
 * `responseStart` (first byte) is used rather than `responseEnd`: it is known when Playwright reports the response, and it
 * is never later than the page's own receipt of the body, so an event of this response is never filtered out.
 */
function responseArrival(request: Request): number | undefined {
	const start = requestStart(request);
	if (start === undefined) return undefined;
	const firstByte = request.timing().responseStart;
	return Number.isFinite(firstByte) && firstByte > 0 ? start + firstByte : start;
}

/**
 * Starts recording applied events (init script for every later document, plus the current one) and the page's
 * hidden-fields requests. Call it before the navigation whose apply should be observed; the fixture's `page` does it
 * before logging in. Idempotent per page.
 */
export async function installAppliedRecorder(page: Page): Promise<void> {
	if (trackers.has(page)) return;
	const tracker: Tracker = { generation: 0, requestGeneration: new WeakMap(), pending: new Set(), records: [] };
	trackers.set(page, tracker);

	page.on('request', (request) => {
		if (request.isNavigationRequest() && request.frame() === page.mainFrame()) {
			// A new document: earlier requests belong to a page whose applied events are gone.
			tracker.generation++;
			tracker.pending.clear();
			return;
		}
		if (!isHiddenFieldsRequest(request)) return;
		tracker.requestGeneration.set(request, tracker.generation);
		tracker.pending.add(request);
	});
	page.on('response', (response: Response) => {
		const request = response.request();
		if (!isHiddenFieldsRequest(request)) return;
		const url = new URL(request.url());
		const startTime = requestStart(request) ?? Date.now();
		tracker.records.push({
			documentKey: lower(url.searchParams.get('documentKey')),
			contentTypeKey: lower(url.searchParams.get('contentTypeKey')),
			startTime,
			arrivedAt: responseArrival(request) ?? startTime,
			generation: tracker.requestGeneration.get(request) ?? tracker.generation,
			consumed: false,
		});
	});
	const finished = (request: Request) => {
		tracker.pending.delete(request);
	};
	page.on('requestfinished', finished);
	page.on('requestfailed', finished);

	await page.addInitScript(recordAppliedEvents, APPLIED_EVENT);
	await page.evaluate(recordAppliedEvents, APPLIED_EVENT);
}

function consume(tracker: Tracker | undefined, match: (record: HiddenFieldsRecord) => boolean): void {
	if (!tracker) return;
	for (const record of tracker.records) {
		if (record.generation === tracker.generation && match(record)) record.consumed = true;
	}
}

function samePair(a: HiddenFieldsRecord, b: HiddenFieldsRecord): boolean {
	return a.documentKey === b.documentKey && a.contentTypeKey === b.contentTypeKey;
}

/**
 * The responses of the current page that still have to be seen applied: for the document of the newest open response,
 * the newest response per content type (the document's own type and, with blocks, each element type share the document
 * key). Older responses of the same pair are superseded by the newest one; open responses of other documents were
 * dropped by the workspace when the editor moved on, and never apply. Both kinds are marked consumed here.
 */
function responsesToAwait(tracker: Tracker): HiddenFieldsRecord[] {
	const open = tracker.records.filter((record) => !record.consumed && record.generation === tracker.generation);
	if (open.length === 0) return [];

	const newest = open.reduce((latest, record) => (record.startTime >= latest.startTime ? record : latest));
	const newestPerPair = new Map<string, HiddenFieldsRecord>();
	for (const record of open) {
		if (record.documentKey !== newest.documentKey) {
			record.consumed = true;
			continue;
		}
		const pair = `${record.documentKey}|${record.contentTypeKey}`;
		const current = newestPerPair.get(pair);
		if (!current || record.startTime >= current.startTime) newestPerPair.set(pair, record);
	}
	for (const record of open) {
		const kept = newestPerPair.get(`${record.documentKey}|${record.contentTypeKey}`);
		if (kept && kept !== record) record.consumed = true;
	}
	return [...newestPerPair.values()];
}

function normalise(filter: AppliedFilter): AppliedFilter {
	return {
		documentKey: filter.documentKey ? filter.documentKey.toLowerCase() : undefined,
		contentTypeKey: filter.contentTypeKey ? filter.contentTypeKey.toLowerCase() : undefined,
		target: filter.target,
		// Date.now() in the page has millisecond resolution, the request start time has a fraction.
		since: filter.since === undefined ? undefined : Math.floor(filter.since),
	};
}

/** Runs in the page: the first recorded event matching the filter, or null. */
function findRecorded({ filter }: { filter: AppliedFilter }): AppliedEvent | null {
	const events = (window as RecorderWindow).__pvApplied ?? [];
	return (
		events.find(
			(event) =>
				(!filter.documentKey || event.documentKey.toLowerCase() === filter.documentKey) &&
				(!filter.contentTypeKey || event.contentTypeKey.toLowerCase() === filter.contentTypeKey) &&
				(!filter.target || event.target === filter.target) &&
				(filter.since === undefined || event.at >= filter.since),
		) ?? null
	);
}

/** Lets Lit render what the applier changed (two animation frames, bounded in case frames are throttled). */
async function afterNextFrames(page: Page): Promise<void> {
	await page.evaluate(
		() =>
			new Promise<void>((resolve) => {
				const timer = setTimeout(resolve, 250);
				requestAnimationFrame(() =>
					requestAnimationFrame(() => {
						clearTimeout(timer);
						resolve();
					}),
				);
			}),
	);
}

/**
 * Waits for the package's applied event: the first one recorded in the current document that matches the filter
 * (document key, content type key, target, and `since`, a browser wall-clock time), then two animation frames so the
 * rendered workspace reflects it. Resolves with the event. Events dispatched before the recorder was installed are
 * not seen; the fixture's `page` installs it before logging in.
 */
export async function waitForApplied(
	page: Page,
	filter: AppliedFilter = {},
	timeout: number = APPLIED_TIMEOUT_MS,
): Promise<AppliedEvent> {
	await installAppliedRecorder(page);
	const normalised = normalise(filter);
	let event: AppliedEvent;
	try {
		const handle = await page.waitForFunction(findRecorded, { filter: normalised }, { timeout, polling: POLL_MS });
		event = (await handle.jsonValue()) as AppliedEvent;
		await handle.dispose();
	} catch (error) {
		throw new Error(
			`No ${APPLIED_EVENT} event matching ${JSON.stringify(normalised)} within ${timeout} ms: ${String(error)}`,
		);
	}

	// Responses for the same document and content type that arrived before this apply are accounted for.
	consume(
		trackers.get(page),
		(record) =>
			record.documentKey === event.documentKey.toLowerCase() &&
			record.contentTypeKey === event.contentTypeKey.toLowerCase() &&
			Math.floor(record.arrivedAt) <= event.at,
	);
	await afterNextFrames(page);
	return event;
}

/**
 * Waits until the response of a hidden-fields request has been applied (or superseded by a later apply): the first
 * event for its document and content type (and target, when given) dispatched after the response started to arrive.
 * An earlier event for the same pair is the previous response's pass, which stays live until this response arrives.
 */
export async function waitForAppliedResponse(
	page: Page,
	response: Response,
	options: { target?: AppliedTarget; timeout?: number } = {},
): Promise<AppliedEvent> {
	const url = new URL(response.url());
	return waitForApplied(
		page,
		{
			documentKey: url.searchParams.get('documentKey') ?? undefined,
			contentTypeKey: url.searchParams.get('contentTypeKey') ?? undefined,
			target: options.target,
			since: responseArrival(response.request()),
		},
		options.timeout,
	);
}

/**
 * Waits until the hidden-fields requests the page has sent are answered and applied, then two animation frames: for the
 * document of the newest response, the newest response of each content type (the document type and, with blocks, every
 * element type requested under the same document key). With nothing in flight and nothing waiting to be applied it
 * only waits for the frames. It never waits a fixed time for a request that may still go out: a caller that expects one
 * waits for its response first (`waitForHiddenFields...`), and a caller that expects none waits for what the page
 * renders instead (a DOM condition, a notification, a network response).
 */
export async function settleApplied(page: Page, timeout: number = APPLIED_TIMEOUT_MS): Promise<void> {
	const tracker = trackers.get(page);
	if (!tracker) throw new Error('settle: call installAppliedRecorder(page) first (the test fixture does it before logging in).');

	const deadline = Date.now() + timeout;
	let waitingFor: HiddenFieldsRecord[] = [];
	for (;;) {
		if (tracker.pending.size === 0) {
			waitingFor = responsesToAwait(tracker);
			if (waitingFor.length === 0) {
				await afterNextFrames(page);
				return;
			}

			for (const record of waitingFor) {
				const filter = normalise({
					documentKey: record.documentKey,
					contentTypeKey: record.contentTypeKey,
					since: record.arrivedAt,
				});
				if (await page.evaluate(findRecorded, { filter })) {
					// This response, and every older one of the same pair, is accounted for.
					consume(tracker, (other) => samePair(other, record) && other.startTime <= record.startTime);
				}
			}
			if (waitingFor.every((record) => record.consumed)) {
				await afterNextFrames(page);
				return;
			}
		}
		if (Date.now() >= deadline) {
			const missing = waitingFor.filter((record) => !record.consumed);
			throw new Error(
				`settle: ${missing.length} hidden-fields response(s) not applied within ${timeout} ms ` +
					`(${missing.map((record) => `document ${record.documentKey}, content type ${record.contentTypeKey}`).join('; ') || 'none known'}; ` +
					`${tracker.pending.size} request(s) still in flight).`,
			);
		}
		await page.waitForTimeout(POLL_MS);
	}
}
