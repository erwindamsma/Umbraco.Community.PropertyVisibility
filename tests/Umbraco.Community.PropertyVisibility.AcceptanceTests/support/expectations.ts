import { expect, type Page } from '@playwright/test';
import {
	contentTab,
	expectPropertyHidden,
	expectPropertyVisible,
	openContentTab,
	propertyGroup,
	propertySlot,
} from './backoffice.js';

/**
 * A `site` document, on any site. The `site` type and its `siteSettings` composition each have a tab named "Legacy
 * tab" (alias `legacyTab`), which the backoffice shows as one tab: the site type's own copy holds `siteNotes`, the
 * composition's copy holds the General group with siteTitle and footerText. The sample's top-level rule keyed by
 * `siteSettings` removes only the composition's copy (option A in docs/configuration.md, Compositions), so the Legacy
 * tab stays with `siteNotes`, and the General group and both of its properties are not rendered. The tab is the type's
 * only tab, so the backoffice renders its content without a tab bar. Call it after the applied event of the document's
 * response, which says the containers left the workspace structure.
 */
export async function expectSiteLegacyTabKeepsOwnProperty(page: Page): Promise<void> {
	await expect(page.locator('uui-input[data-mark="input:entity-name"]')).toBeVisible();
	await expectPropertyVisible(page, 'siteNotes');
	await expect(propertyGroup(page, 'General')).toHaveCount(0);
	await expect(propertySlot(page, 'siteTitle')).toHaveCount(0);
	await expect(propertySlot(page, 'footerText')).toHaveCount(0);
}

/**
 * A landingPage under "Corporate site": bannerImage and relatedLinks hidden, the seoTab tab gone,
 * the settingsTab/advanced group gone. Leaves the "Content" tab active.
 */
export async function expectCorporateLandingPage(page: Page): Promise<void> {
	await expect(contentTab(page, 'Content')).toBeVisible();
	await openContentTab(page, 'Content');
	await expectPropertyVisible(page, 'title');
	await expectPropertyHidden(page, 'bannerImage');
	await expectPropertyHidden(page, 'relatedLinks');

	await expect(contentTab(page, 'Seo tab')).toHaveCount(0);

	await openContentTab(page, 'Settings tab');
	await expect(propertyGroup(page, 'General')).toBeVisible();
	await expectPropertyVisible(page, 'hideFromNavigation');
	await expectPropertyVisible(page, 'trackingId');
	await expect(propertyGroup(page, 'Advanced')).toHaveCount(0);
	await expect(propertySlot(page, 'legacyRedirect')).toHaveCount(0);
	await expect(propertySlot(page, 'enableExperimental')).toHaveCount(0);

	await openContentTab(page, 'Content');
}

/** A landingPage under "Campaign site": everything visible except metaKeywords. Leaves the "Content" tab active. */
export async function expectCampaignLandingPage(page: Page): Promise<void> {
	await expect(contentTab(page, 'Content')).toBeVisible();
	await openContentTab(page, 'Content');
	await expectPropertyVisible(page, 'title');
	await expectPropertyVisible(page, 'bannerImage');
	await expectPropertyVisible(page, 'relatedLinks');

	await openContentTab(page, 'Seo tab');
	await expect(propertyGroup(page, 'Meta')).toBeVisible();
	await expectPropertyVisible(page, 'metaTitle');
	await expectPropertyVisible(page, 'metaDescription');
	await expectPropertyHidden(page, 'metaKeywords');

	await openContentTab(page, 'Settings tab');
	await expect(propertyGroup(page, 'General')).toBeVisible();
	await expect(propertyGroup(page, 'Advanced')).toBeVisible();
	await expectPropertyVisible(page, 'legacyRedirect');
	await expectPropertyVisible(page, 'enableExperimental');

	await openContentTab(page, 'Content');
}

/** An article under "Corporate site": the shareTab tab (and its social group) gone. */
export async function expectCorporateArticle(page: Page): Promise<void> {
	await expectPropertyVisible(page, 'title');
	await expectPropertyVisible(page, 'bodyText');
	await expect(contentTab(page, 'Share tab')).toHaveCount(0);
	await expect(propertyGroup(page, 'Social')).toHaveCount(0);
	await expect(propertySlot(page, 'shareTitle')).toHaveCount(0);
}

/** An article under "Campaign site": the shareTab tab visible with its fields. */
export async function expectCampaignArticle(page: Page): Promise<void> {
	await expect(contentTab(page, 'Share tab')).toBeVisible();
	await openContentTab(page, 'Share tab');
	await expect(propertyGroup(page, 'Social')).toBeVisible();
	await expectPropertyVisible(page, 'shareTitle');
	await expectPropertyVisible(page, 'shareImage');
	// Back to the root groups, so the next document starts from its default view.
	const rootTab = page.locator('uui-tab[data-mark="content-tab:root"]');
	await rootTab.click();
	await expectPropertyVisible(page, 'title');
}
