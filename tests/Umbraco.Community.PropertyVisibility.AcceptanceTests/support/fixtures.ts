import { test as base, expect, type ConsoleMessage } from '@playwright/test';
import { appendFileSync, mkdirSync } from 'node:fs';
import { basename, dirname, join } from 'node:path';
import { installAppliedRecorder } from './applied.js';
import { loginAsAdmin } from './backoffice.js';

export interface ConsoleError {
	test: string;
	url: string;
	text: string;
}

/**
 * A request a test fails on purpose, for which the browser logs "Failed to load resource: the server responded with a
 * status of <status>". Declared by the test itself (fixture `expectedRequestFailures`), for that test only: for example
 * the server validation request Umbraco answers with 400 when a test publishes invalid content on purpose. Only that
 * browser message, for a request whose URL matches and with that status, is taken out of the console errors.
 */
export interface ExpectedRequestFailure {
	/** Matches the full URL of the failing request (the console message's location). */
	url: RegExp;
	status: number;
	/** Why the test provokes it; logged with the line. */
	reason: string;
}

/**
 * One line of the run's console log: a console error, a known Umbraco error (kept visible, not counted), a request
 * failure the test provoked and declared (kept visible, not counted), or the marker every logged-in test writes when it
 * finishes, so the console-errors spec can tell "no errors" apart from "the earlier specs did not run".
 */
export type ConsoleLogLine =
	| (ConsoleError & { kind: 'error' })
	| (ConsoleError & { kind: 'upstream'; issue: string })
	| (ConsoleError & { kind: 'expected'; reason: string })
	| { kind: 'ran'; file: string; test: string };

/**
 * Errors raised by Umbraco itself that the suite cannot avoid and that do not come from the package. Each entry is
 * matched narrowly and recorded as an `upstream` line instead of an error. Keep this list short and remove an entry
 * when the Umbraco version the suite runs against fixes it.
 */
const KNOWN_UPSTREAM_ERRORS: ReadonlyArray<{ issue: string; pattern: RegExp }> = [
	{
		// Umbraco 17.6.2 and 17.7.0 (not fixed in 17.7.0; the issue id names the version it was first reproduced on):
		// umb-inline-list-block calls UmbBlockWorkspaceContext.load() without handling its rejection, and load() awaits
		// the block entries context, which rejects when the inline block is detached (re-rendered) while it initializes.
		// Trigger: a re-render of the tab view while the inline blocks load, as when a test clicks the already selected
		// Content tab right after the document opened. Any spec that opens a landing page with inline blocks can hit it;
		// seen in specs 05, 10 and 11. Reproduced on a stock 17.6.2 site with the package removed (no package assembly,
		// no App_Plugins folder, no manifest): 0 of 90 fresh page loads, 4 of 90 loads followed by that click, 18 of 40
		// with 4x CPU throttling.
		issue: 'umbraco-17.6.2-inline-list-block-detached-during-load',
		pattern: /host was disconnected[\s\S]*UmbBlockEntriesContext[\s\S]*UMB-INLINE-LIST-BLOCK|UmbBlockEntriesContext with API Alias: default\)\. Controller is hosted on #document-fragment > UMB-INLINE-LIST-BLOCK/,
	},
];

/** The KNOWN_UPSTREAM_ERRORS issue a console or page error text matches, if any. */
export function knownUpstreamIssue(text: string): string | undefined {
	return KNOWN_UPSTREAM_ERRORS.find((known) => known.pattern.test(text))?.issue;
}

const FAILED_RESOURCE = /^Failed to load resource: the server responded with a status of (\d{3})\b/;

/** The declared failure a console message reports, if it is one. */
function expectedRequestFailure(
	message: ConsoleMessage,
	expected: ReadonlyArray<ExpectedRequestFailure>,
): ExpectedRequestFailure | undefined {
	const status = FAILED_RESOURCE.exec(message.text())?.[1];
	if (!status) return undefined;
	const url = message.location().url;
	return expected.find((failure) => failure.status === Number(status) && failure.url.test(url));
}

/** Console errors and run markers of every logged-in page of this run, one JSON object per line. */
export function consoleErrorLogPath(outputDir: string): string {
	return join(outputDir, 'console-errors.jsonl');
}

/**
 * `page` is a logged-in backoffice page. The package's applied events and hidden-fields requests are recorded from
 * before the login (support/applied.ts: waitForApplied, settle). Console errors and uncaught page errors are recorded
 * from the moment the login has completed (the login page itself logs a failed refresh-token request by design, before
 * any session exists), attached to the test and appended to the run's console log, followed by a marker line for the test.
 * A known Umbraco error (KNOWN_UPSTREAM_ERRORS) is attached and logged as an `upstream` line instead of an error; a
 * request failure the test declared in `expectedRequestFailures` is logged as an `expected` line.
 */
export const test = base.extend<{ consoleErrors: ConsoleError[]; expectedRequestFailures: ExpectedRequestFailure[] }>({
	consoleErrors: async ({}, use) => {
		await use([]);
	},
	expectedRequestFailures: async ({}, use) => {
		await use([]);
	},
	page: async ({ page, consoleErrors, expectedRequestFailures }, use, testInfo) => {
		await installAppliedRecorder(page);
		await loginAsAdmin(page);

		const upstreamErrors: Array<ConsoleError & { issue: string }> = [];
		const expectedFailures: Array<ConsoleError & { reason: string }> = [];
		const record = (text: string, matchText: string) => {
			const entry = { test: testInfo.title, url: page.url(), text };
			const issue = knownUpstreamIssue(matchText);
			if (issue) upstreamErrors.push({ ...entry, issue });
			else consoleErrors.push(entry);
		};
		page.on('console', (message) => {
			if (message.type() !== 'error') return;
			const expected = expectedRequestFailure(message, expectedRequestFailures);
			if (expected) {
				expectedFailures.push({
					test: testInfo.title,
					url: page.url(),
					text: `${message.text()} (${message.location().url})`,
					reason: expected.reason,
				});
				return;
			}
			record(message.text(), message.text());
		});
		page.on('pageerror', (error) => {
			record(`Uncaught: ${error.message}`, `${error.name} ${error.message}`);
		});

		await use(page);

		if (consoleErrors.length > 0) {
			await testInfo.attach('console-errors', {
				body: JSON.stringify(consoleErrors, null, 2),
				contentType: 'application/json',
			});
		}
		if (upstreamErrors.length > 0) {
			await testInfo.attach('known-upstream-errors', {
				body: JSON.stringify(upstreamErrors, null, 2),
				contentType: 'application/json',
			});
		}
		if (expectedFailures.length > 0) {
			await testInfo.attach('expected-request-failures', {
				body: JSON.stringify(expectedFailures, null, 2),
				contentType: 'application/json',
			});
		}
		const logPath = consoleErrorLogPath(testInfo.project.outputDir);
		mkdirSync(dirname(logPath), { recursive: true });
		const lines: ConsoleLogLine[] = [
			...consoleErrors.map((error): ConsoleLogLine => ({ kind: 'error', ...error })),
			...upstreamErrors.map((error): ConsoleLogLine => ({ kind: 'upstream', ...error })),
			...expectedFailures.map((error): ConsoleLogLine => ({ kind: 'expected', ...error })),
			{ kind: 'ran', file: basename(testInfo.file), test: testInfo.title },
		];
		appendFileSync(logPath, lines.map((line) => `${JSON.stringify(line)}\n`).join(''));
	},
});

export { expect };
