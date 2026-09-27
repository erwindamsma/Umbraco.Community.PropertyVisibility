import { writeFileSync } from 'node:fs';
import { join } from 'node:path';
import type { Page } from '@playwright/test';
import { contentTab, openDocument, waitForApplied } from '../support/backoffice.js';
import { ELEMENT_TYPES, waitForBlockApplied } from '../support/blocks.js';
import { knownUpstreamIssue, test } from '../support/fixtures.js';
import { resolveSample } from '../support/management-api.js';

/**
 * Opt-in measurement, not a pass/fail check: how often the known Umbraco inline-block error of 17.6.2 and 17.7.0
 * (support/fixtures.ts, KNOWN_UPSTREAM_ERRORS) appears when a landing page with inline-edited blocks is opened. Run it
 * with PV_MEASURE=1
 * (optionally PV_MEASURE_RUNS, default 45 per document, and PV_MEASURE_MODE):
 *
 *   PV_MEASURE=1 PV_MEASURE_MODE=tabclick npx playwright test tests/99-measure-inline-block-error.spec.ts --no-deps
 *
 * Modes:
 * - `fresh` (default): a full navigation to the document that waits for the document's applied event and for the inline
 *   promoBanner block's applied event. A fresh load does not re-render the inline blocks while they initialize, so it
 *   does not reproduce the error (0 of 90 loads in the 2026-09-25 measurement, with and without the package).
 * - `tabclick`: the same navigation, then a click on the already selected "Content" tab as soon as it is visible. The
 *   click moves the route to `.../tab/content` and re-creates the tab view while the inline blocks initialize, the
 *   trigger of the error (the stock-site reproduction without the package used the same click).
 *
 * The counts are printed, added as annotations and written to test-results/inline-block-error-measurement.json.
 */
const RUNS = Number(process.env.PV_MEASURE_RUNS ?? 45);
const MODE = process.env.PV_MEASURE_MODE === 'tabclick' ? 'tabclick' : 'fresh';
const ISSUE = 'umbraco-17.6.2-inline-list-block-detached-during-load';

/** Navigates to the document, clicks the already selected Content tab as soon as it shows, then waits for the applies. */
async function openDocumentAndClickContentTab(page: Page, documentKey: string, documentTypeKey: string): Promise<void> {
	const since = await page.evaluate(() => Date.now());
	await page.goto(`/umbraco/section/content/workspace/document/edit/${documentKey}`);
	await contentTab(page, 'Content').click();
	await waitForApplied(page, { documentKey, contentTypeKey: documentTypeKey, target: 'document', since });
}

test.describe('Measurement: frequency of the known upstream inline-block error', () => {
	test.skip(process.env.PV_MEASURE !== '1', 'opt-in: set PV_MEASURE=1');
	test.describe.configure({ timeout: 30 * 60_000 });

	test(`open Corporate landing and Campaign landing ${RUNS} times each (${MODE})`, async ({ page }, testInfo) => {
		const keys = await resolveSample(page);

		let matches = 0;
		page.on('pageerror', (error) => {
			if (knownUpstreamIssue(`${error.name} ${error.message}`) === ISSUE) matches++;
		});
		page.on('console', (message) => {
			if (message.type() === 'error' && knownUpstreamIssue(message.text()) === ISSUE) matches++;
		});

		const results: Record<string, { mode: string; runs: number; navigationsWithError: number; errorEvents: number; inlineApplyMissing: number }> = {};
		for (const [name, documentKey] of [
			['Corporate landing', keys.corporateLanding],
			['Campaign landing', keys.campaignLanding],
		] as const) {
			const result = { mode: MODE, runs: 0, navigationsWithError: 0, errorEvents: 0, inlineApplyMissing: 0 };
			for (let run = 0; run < RUNS; run++) {
				const before = matches;
				if (MODE === 'tabclick') {
					await openDocumentAndClickContentTab(page, documentKey, keys.landingPageType);
				} else {
					await openDocument(page, documentKey, keys.landingPageType);
				}
				try {
					await waitForBlockApplied(
						page,
						{ documentKey, elementTypeKey: ELEMENT_TYPES.promoBanner.key, target: 'block-content' },
						10_000,
					);
				} catch {
					result.inlineApplyMissing++;
				}
				// Let a late rejection of the inline block's load surface before the next navigation.
				await page.waitForTimeout(250);
				result.runs++;
				const found = matches - before;
				result.errorEvents += found;
				if (found > 0) result.navigationsWithError++;
			}
			results[name] = result;
			testInfo.annotations.push({
				type: 'measurement',
				description: `${name} (${MODE}): ${result.navigationsWithError} of ${result.runs} navigations logged the ${ISSUE} error (${result.errorEvents} error events; inline apply missing in ${result.inlineApplyMissing})`,
			});
		}

		console.log(`[measurement] ${JSON.stringify(results)}`);
		writeFileSync(join(testInfo.project.outputDir, 'inline-block-error-measurement.json'), `${JSON.stringify(results, null, 2)}\n`);
	});
});
