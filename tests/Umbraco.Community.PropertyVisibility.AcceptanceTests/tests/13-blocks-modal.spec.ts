import {
	blockEntry,
	openBlockModal,
	openDocumentWithInlineBlocks,
	pageNow,
	resolveElementTypes,
	waitForBlockApplied,
} from '../support/blocks.js';
import { expect, test } from '../support/fixtures.js';
import { resolveSample } from '../support/management-api.js';

test.describe('A Block List block edited in a modal (landingPage.mainBlocks)', () => {
	test('Corporate landing: the promoBanner block hides overlayColour and keeps heading', async ({ page }) => {
		const keys = await resolveSample(page);
		const types = await resolveElementTypes(page);
		await openDocumentWithInlineBlocks(page, keys.corporateLanding);

		const since = await pageNow(page);
		const modal = await openBlockModal(page, blockEntry(page, 'mainBlocks', types.promoBanner.alias));
		const applied = await waitForBlockApplied(page, {
			documentKey: keys.corporateLanding,
			elementTypeKey: types.promoBanner.key,
			target: 'block-content',
			since,
		});
		expect(applied.propertyCount).toBe(1);

		await modal.expectPropertyVisible('heading');
		await modal.expectPropertyVisible('image');
		await modal.expectPropertyHidden('overlayColour');
		await modal.close();
	});

	test('Campaign landing: the promoBanner block shows overlayColour', async ({ page }) => {
		const keys = await resolveSample(page);
		const types = await resolveElementTypes(page);
		await openDocumentWithInlineBlocks(page, keys.campaignLanding);

		const since = await pageNow(page);
		const modal = await openBlockModal(page, blockEntry(page, 'mainBlocks', types.promoBanner.alias));
		const applied = await waitForBlockApplied(page, {
			documentKey: keys.campaignLanding,
			elementTypeKey: types.promoBanner.key,
			target: 'block-content',
			since,
		});
		expect(applied.propertyCount).toBe(0);

		await modal.expectPropertyVisible('heading');
		await modal.expectPropertyVisible('overlayColour');
		await modal.expectPropertyVisible('image');
		await modal.close();
	});
});
