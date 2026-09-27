import { existsSync, readFileSync } from 'node:fs';
import {
	clickTreeItem,
	expandTreeItem,
	openDocument,
	settle,
	startCreateUnder,
	treeItem,
	waitForHiddenFieldsWithParent,
} from '../support/backoffice.js';
import { blockEntry, ELEMENT_TYPES, expandInlineBlock, openBlockModal, pageNow, waitForBlockApplied } from '../support/blocks.js';
import { SAMPLE } from '../support/env.js';
import {
	expectCampaignArticle,
	expectCampaignLandingPage,
	expectCorporateArticle,
	expectCorporateLandingPage,
} from '../support/expectations.js';
import { consoleErrorLogPath, expect, test, type ConsoleError, type ConsoleLogLine } from '../support/fixtures.js';
import { resolveSample } from '../support/management-api.js';

test.describe('No console errors', () => {
	test('on the pages the earlier specs of this run exercised', async ({}, testInfo) => {
		const logPath = consoleErrorLogPath(testInfo.project.outputDir);
		// Playwright empties the output folder at the start of a run, so the log only holds this run's lines. Without
		// it, or without the marker of every spec that uses the logged-in page, there is nothing to check: run the whole
		// suite. This spec is the `console-errors` teardown project (playwright.config.ts), so it runs after all of them;
		// to run one other spec on its own, pass --no-deps.
		expect(existsSync(logPath), `${logPath} is missing: run the whole suite, not this spec alone`).toBe(true);

		const lines = readFileSync(logPath, 'utf8')
			.split('\n')
			.filter(Boolean)
			.map((line) => JSON.parse(line) as ConsoleLogLine);
		const ranFiles = new Set(lines.flatMap((line) => (line.kind === 'ran' ? [line.file] : [])));
		// Every spec with a logged-in page; 09 only uses the anonymous request fixture, and the opt-in 99 measurement is
		// skipped in a normal run.
		for (const prefix of ['03-', '04-', '05-', '06-', '07-', '10-', '11-', '12-', '13-', '14-', '15-', '16-', '17-', '18-', '19-', '20-', '21-', '22-', '23-', '24-']) {
			expect([...ranFiles].some((file) => file.startsWith(prefix)), `a ${prefix}* spec ran in this run`).toBe(true);
		}

		// Known Umbraco errors (support/fixtures.ts) are listed on the report, not counted.
		for (const line of lines) {
			if (line.kind === 'upstream') {
				testInfo.annotations.push({ type: 'known upstream error', description: `${line.issue} in "${line.test}"` });
			}
			// A request failure a test provoked on purpose and declared (fixture expectedRequestFailures).
			if (line.kind === 'expected') {
				testInfo.annotations.push({ type: 'expected request failure', description: `${line.reason} in "${line.test}"` });
			}
		}

		const errors: ConsoleError[] = lines.flatMap((line) =>
			line.kind === 'error' ? [{ test: line.test, url: line.url, text: line.text }] : [],
		);
		expect(errors).toEqual([]);
	});

	test('on a tour of every page the package touches', async ({ page, consoleErrors }) => {
		const keys = await resolveSample(page);

		await page.goto('/umbraco/section/settings/workspace/extension-root');
		await expect(page.locator('uui-table-row').first()).toBeVisible();

		await openDocument(page, keys.corporateLanding);
		// Blocks: the inline promoBanner expanded, the modal promoBanner (both views) and a nested block.
		await expandInlineBlock(blockEntry(page, 'inlineBlocks', ELEMENT_TYPES.promoBanner.alias));
		let since = await pageNow(page);
		const promo = await openBlockModal(page, blockEntry(page, 'mainBlocks', ELEMENT_TYPES.promoBanner.alias));
		await waitForBlockApplied(page, { documentKey: keys.corporateLanding, elementTypeKey: ELEMENT_TYPES.promoBanner.key, target: 'block-content', since });
		await promo.openView('settings');
		await waitForBlockApplied(page, { documentKey: keys.corporateLanding, elementTypeKey: ELEMENT_TYPES.promoBannerSettings.key, target: 'block-settings', since });
		await promo.close();
		since = await pageNow(page);
		const section = await openBlockModal(page, blockEntry(page, 'mainBlocks', ELEMENT_TYPES.contentSection.alias));
		const nested = await openBlockModal(page, blockEntry(section.root, 'items', ELEMENT_TYPES.promoBanner.alias));
		await waitForBlockApplied(page, { documentKey: keys.corporateLanding, elementTypeKey: ELEMENT_TYPES.promoBanner.key, target: 'block-content', since });
		await nested.close();
		await section.close();
		since = await pageNow(page);
		const grid = await openBlockModal(page, blockEntry(page, 'gridBlocks', ELEMENT_TYPES.callToAction.alias));
		await waitForBlockApplied(page, { documentKey: keys.corporateLanding, elementTypeKey: ELEMENT_TYPES.callToAction.key, target: 'block-content', since });
		await grid.close();
		await expectCorporateLandingPage(page);
		await expandTreeItem(page, SAMPLE.campaignSite);
		await clickTreeItem(page, SAMPLE.campaignLanding, keys.campaignLanding);
		await expectCampaignLandingPage(page);
		await clickTreeItem(page, SAMPLE.campaignArticle, keys.campaignArticle);
		await expectCampaignArticle(page);
		await expandTreeItem(page, SAMPLE.corporateSite);
		await clickTreeItem(page, SAMPLE.corporateArticle, keys.corporateArticle);
		await expectCorporateArticle(page);

		// A not-yet-saved landingPage under Corporate site (left without saving).
		const created = waitForHiddenFieldsWithParent(page, keys.corporateSite);
		await startCreateUnder(page, SAMPLE.corporateSite, SAMPLE.landingPageTypeName);
		expect((await created).status()).toBe(200);
		await settle(page);
		await expectCorporateLandingPage(page);

		// Leaving the unsaved document: the Content section's dashboard and tree have rendered again.
		await page.goto('/umbraco/section/content');
		await expect(treeItem(page, SAMPLE.corporateSite)).toBeVisible();
		await expect(page.locator('umb-section-main umb-section-main-views')).toBeVisible();

		expect(consoleErrors).toEqual([]);
	});
});
