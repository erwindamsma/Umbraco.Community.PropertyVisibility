import { openDocument } from '../support/backoffice.js';
import { expectCampaignLandingPage, expectCorporateLandingPage } from '../support/expectations.js';
import { test } from '../support/fixtures.js';
import { resolveSample } from '../support/management-api.js';

test.describe('Per-site visibility in the document workspace', () => {
	test('Corporate landing hides bannerImage, relatedLinks, the seoTab tab and the settingsTab/advanced group', async ({ page }) => {
		const keys = await resolveSample(page);
		await openDocument(page, keys.corporateLanding);
		await expectCorporateLandingPage(page);
	});

	test('Campaign landing shows all of those and hides only metaKeywords', async ({ page }) => {
		const keys = await resolveSample(page);
		await openDocument(page, keys.campaignLanding);
		await expectCampaignLandingPage(page);
	});
});
