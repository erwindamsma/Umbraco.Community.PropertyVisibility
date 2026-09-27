import type { Locator, Page } from '@playwright/test';
import { openDocument } from '../support/backoffice.js';
import { blockEntry, BlockModal, openBlockModal, pageNow, resolveElementTypes, waitForBlockApplied } from '../support/blocks.js';
import { SAMPLE } from '../support/env.js';
import { expectedHidden } from '../support/expected.js';
import { expect, test } from '../support/fixtures.js';
import { findDocumentByPath, getContentTypeStructure, getHiddenFields, resolveSample } from '../support/management-api.js';

/**
 * Split view on the seeded culture-variant document "Corporate variant page" (document type variantPage, cultures en-US
 * and da-DK, both published). The sample's Corporate site rule for variantPage hides the seoTab tab (with its Meta group:
 * the variant variantMetaTitle and the invariant canonicalUrl) and the variant variantTagline; the variant variantTitle,
 * the invariant sharedNote and the invariant Block List "blocks" stay.
 *
 * Both panes share one document workspace, so one hidden-fields response serves both: the property guard rules deny
 * every culture and the removed containers are gone from the one structure both panes render.
 */
const VARIANT_RULE = { properties: ['variantTagline'], containers: ['seoTab'] };
const CULTURES = [
	{ culture: 'en-US', language: 'English (United States)' },
	{ culture: 'da-DK', language: 'Danish (Denmark)' },
] as const;

/** One pane of the document workspace: `umb-workspace-split-view` marks itself with its culture. */
function pane(page: Page, culture: string): Locator {
	return page.locator(`umb-workspace-split-view[data-mark="workspace-split-view:${culture}"]`);
}

function renderedIn(scope: Locator, alias: string): Locator {
	return scope.locator(`umb-content-workspace-property[alias="${alias}"] umb-property-type-based-property`);
}

async function expectPaneFiltered(scope: Locator, culture: string): Promise<void> {
	// Visible: a variant and an invariant property, and the invariant block list.
	await expect(renderedIn(scope, 'variantTitle'), `variantTitle in ${culture}`).toBeVisible();
	await expect(renderedIn(scope, 'variantTitle').locator('input').first()).toHaveValue(`Corporate variant title (${culture})`);
	await expect(renderedIn(scope, 'sharedNote'), `sharedNote in ${culture}`).toBeVisible();
	await expect(renderedIn(scope, 'blocks'), `blocks in ${culture}`).toBeVisible();
	// Hidden by name: the slot of the active tab stays, its editor is not rendered.
	await expect(scope.locator('umb-content-workspace-property[alias="variantTagline"]')).toHaveCount(1);
	await expect(renderedIn(scope, 'variantTagline'), `variantTagline in ${culture}`).toHaveCount(0);
	// Hidden with the seoTab tab: no tab bar entry (Content would be the only tab left) and none of its properties.
	await expect(scope.locator('uui-tab[data-mark^="content-tab:"][label="Seo tab"]'), `Seo tab in ${culture}`).toHaveCount(0);
	await expect(scope.locator('umb-content-workspace-property[alias="variantMetaTitle"]')).toHaveCount(0);
	await expect(scope.locator('umb-content-workspace-property[alias="canonicalUrl"]')).toHaveCount(0);
}

test.describe('Split view on a culture-variant document', () => {
	test('Corporate variant page: both cultures side by side hide the seoTab tab and variantTagline, show the rest, and hide overlayColour in the block opened from either pane', async ({ page }) => {
		const keys = await resolveSample(page);
		const types = await resolveElementTypes(page);
		const variantPage = await findDocumentByPath(page, [SAMPLE.corporateSite, SAMPLE.corporateVariantPage]);
		const documentKey = variantPage.id;
		const typeKey = variantPage.documentType.id;
		const structure = await getContentTypeStructure(page, typeKey);

		// The API: the Corporate rule, matched by key; under Campaign site nothing is hidden for this type.
		const { body } = await getHiddenFields(page, { documentKey, contentTypeKey: typeKey });
		const expected = expectedHidden(structure, VARIANT_RULE);
		expect([...body.propertyTypeKeys].sort()).toEqual(expected.propertyKeys);
		expect([...body.containerKeys].sort()).toEqual(expected.containerKeys);
		expect(body.matchedSite).toEqual({ label: 'corporate', reason: 'Key' });
		const underCampaign = (await getHiddenFields(page, { documentKey: crypto.randomUUID(), contentTypeKey: typeKey, parentKey: keys.campaignSite })).body;
		expect(underCampaign.matchedSite).toEqual({ label: 'campaign', reason: 'Name' });
		expect(underCampaign.propertyTypeKeys).toEqual([]);
		expect(underCampaign.containerKeys).toEqual([]);

		// One culture first (the default, en-US), then the variant selector's "Open in split view" for da-DK.
		await openDocument(page, documentKey, typeKey);
		const left = pane(page, 'en-US');
		await expectPaneFiltered(left, 'en-US');

		await left.locator('uui-button[label="Open version selector"]').first().click();
		const split = page.locator(`uui-button[label="Open ${CULTURES[1].language} in split view"]`);
		// The button shows when the pointer is over the culture's row.
		await page.locator('button.switch-button', { hasText: 'Corporate variant page (da-DK)' }).hover();
		await split.click();
		await expect(page).toHaveURL(/\/en-US_&_da-DK(\/|$)/);
		const right = pane(page, 'da-DK');
		await expect(page.locator('umb-workspace-split-view')).toHaveCount(2);
		for (const { culture } of CULTURES) await expectPaneFiltered(pane(page, culture), culture);

		// The same block from each pane: a modal over the workspace, hidden in both halves either way.
		for (const { culture } of CULTURES) {
			const since = await pageNow(page);
			const modal: BlockModal = await openBlockModal(page, blockEntry(pane(page, culture), 'blocks', types.promoBanner.alias));
			const content = await waitForBlockApplied(page, { documentKey, elementTypeKey: types.promoBanner.key, target: 'block-content', since });
			expect(content.propertyCount, `promoBanner opened from ${culture}`).toBe(1);
			await modal.expectPropertyVisible('heading');
			await modal.expectPropertyHidden('overlayColour');
			await modal.openView('settings');
			await waitForBlockApplied(page, { documentKey, elementTypeKey: types.promoBannerSettings.key, target: 'block-settings', since });
			await modal.expectPropertyVisible('cssClass');
			await modal.expectPropertyHidden('anchorId');
			await modal.close();
		}

		// Closing the da-DK pane leaves en-US as it was. The "x" of its variant selector has no label in Umbraco 17.6.2 and
		// the pane's action menu overlaps it at this width, so the click is dispatched on the button itself.
		await right.locator('uui-button#variant-close').first().dispatchEvent('click');
		await expect(page.locator('umb-workspace-split-view')).toHaveCount(1);
		await expect(page).not.toHaveURL(/_&_/);
		await expectPaneFiltered(left, 'en-US');
	});
});
