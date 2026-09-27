import type { Page } from '@playwright/test';
import {
	clickTreeItem,
	expandTreeItem,
	openDocument,
	recordHiddenFieldsRequests,
	settle,
	startCreateUnder,
	waitForHiddenFieldsWithParent,
} from '../support/backoffice.js';
import { SAMPLE } from '../support/env.js';
import {
	expectCampaignArticle,
	expectCampaignLandingPage,
	expectCorporateArticle,
	expectCorporateLandingPage,
} from '../support/expectations.js';
import { expect, test } from '../support/fixtures.js';
import { resolveSample } from '../support/management-api.js';

/** Marks the window; the mark survives client-side navigation and disappears on a full page load. */
async function markWindow(page: Page): Promise<void> {
	await page.evaluate(() => {
		(window as unknown as { __pvNoReload?: boolean }).__pvNoReload = true;
	});
}

async function expectNoReload(page: Page): Promise<void> {
	expect(await page.evaluate(() => (window as unknown as { __pvNoReload?: boolean }).__pvNoReload)).toBe(true);
}

test.describe('Switching documents in one workspace without a reload', () => {
	test('landingPage: Corporate -> Campaign -> Corporate from the tree', async ({ page }) => {
		const keys = await resolveSample(page);
		await openDocument(page, keys.corporateLanding);
		await expectCorporateLandingPage(page);
		await markWindow(page);

		await expandTreeItem(page, SAMPLE.campaignSite);
		await clickTreeItem(page, SAMPLE.campaignLanding, keys.campaignLanding);
		await expectCampaignLandingPage(page);
		await expectNoReload(page);

		await expandTreeItem(page, SAMPLE.corporateSite);
		await clickTreeItem(page, SAMPLE.corporateLanding, keys.corporateLanding);
		await expectCorporateLandingPage(page);
		await expectNoReload(page);
	});

	test('article: Corporate -> Campaign -> Corporate, and Campaign -> Corporate -> Campaign, from the tree', async ({ page }) => {
		const keys = await resolveSample(page);

		await openDocument(page, keys.corporateArticle);
		await expectCorporateArticle(page);
		await markWindow(page);

		await expandTreeItem(page, SAMPLE.campaignSite);
		await clickTreeItem(page, SAMPLE.campaignArticle, keys.campaignArticle);
		await expectCampaignArticle(page);

		await expandTreeItem(page, SAMPLE.corporateSite);
		await clickTreeItem(page, SAMPLE.corporateArticle, keys.corporateArticle);
		await expectCorporateArticle(page);

		await clickTreeItem(page, SAMPLE.campaignArticle, keys.campaignArticle);
		await expectCampaignArticle(page);
		await expectNoReload(page);
	});

	test('leaving an unsaved new landingPage through the tree sends no further request for it', async ({ page }) => {
		const keys = await resolveSample(page);
		const requests = recordHiddenFieldsRequests(page);

		const created = waitForHiddenFieldsWithParent(page, keys.corporateSite);
		await startCreateUnder(page, SAMPLE.corporateSite, SAMPLE.landingPageTypeName);
		const scaffoldKey = new URL((await created).url()).searchParams.get('documentKey')!;
		await settle(page);
		await expectCorporateLandingPage(page);
		await markWindow(page);

		// Loading another document resets the workspace: isNew goes to undefined while the departing document's keys
		// are still set. That must not look like a first save and re-request the scaffold without its parent.
		await expandTreeItem(page, SAMPLE.campaignSite);
		await clickTreeItem(page, SAMPLE.campaignLanding, keys.campaignLanding);
		await expectCampaignLandingPage(page);
		await expectNoReload(page);

		const forScaffold = requests.filter((url) => url.searchParams.get('documentKey') === scaffoldKey);
		expect(forScaffold.map((url) => url.searchParams.get('parentKey'))).toEqual([keys.corporateSite]);
	});
});
