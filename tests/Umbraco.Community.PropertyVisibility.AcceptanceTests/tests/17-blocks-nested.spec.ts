import {
	blockEntry,
	blockModalEditors,
	openBlockModal,
	openDocumentWithInlineBlocks,
	pageNow,
	resolveElementTypes,
	waitForBlockApplied,
} from '../support/blocks.js';
import { expect, test } from '../support/fixtures.js';
import { resolveSample } from '../support/management-api.js';

/**
 * A promoBanner inside the items of a contentSection block (mainBlocks): two modal levels, so the block context's request
 * for the site context crosses two modal context proxies to reach the document workspace.
 */
test.describe('A block inside a block (contentSection.items, two modal levels)', () => {
	for (const site of ['corporate', 'campaign'] as const) {
		const hidden = site === 'corporate';
		test(`${site === 'corporate' ? 'Corporate' : 'Campaign'} landing: the nested promoBanner ${hidden ? 'hides' : 'shows'} overlayColour`, async ({ page }) => {
			const keys = await resolveSample(page);
			const types = await resolveElementTypes(page);
			const documentKey = site === 'corporate' ? keys.corporateLanding : keys.campaignLanding;
			await openDocumentWithInlineBlocks(page, documentKey);

			const since = await pageNow(page);
			const section = await openBlockModal(page, blockEntry(page, 'mainBlocks', types.contentSection.alias));
			await waitForBlockApplied(page, {
				documentKey,
				elementTypeKey: types.contentSection.key,
				target: 'block-content',
				since,
			});
			await section.expectPropertyVisible('heading');
			await section.expectPropertyVisible('items');

			// Both halves of the nested block apply when its modal opens, so both waits start here.
			const promoSince = await pageNow(page);
			const promo = await openBlockModal(page, blockEntry(section.root, 'items', types.promoBanner.alias));
			await expect(blockModalEditors(page)).toHaveCount(2);
			const applied = await waitForBlockApplied(page, {
				documentKey,
				elementTypeKey: types.promoBanner.key,
				target: 'block-content',
				since: promoSince,
			});
			expect(applied.propertyCount).toBe(hidden ? 1 : 0);

			await promo.expectPropertyVisible('heading');
			await expect(promo.property('heading').locator('input').first()).toHaveValue(
				site === 'corporate' ? 'Corporate nested promo' : 'Campaign nested promo',
			);
			if (hidden) {
				await promo.expectPropertyHidden('overlayColour');
			} else {
				await promo.expectPropertyVisible('overlayColour');
			}

			// Its Settings half resolves through the same two proxies.
			await promo.openView('settings');
			await waitForBlockApplied(page, {
				documentKey,
				elementTypeKey: types.promoBannerSettings.key,
				target: 'block-settings',
				since: promoSince,
			});
			// The Settings view has rendered (cssClass is never hidden) before anchorId is checked.
			await promo.expectPropertyVisible('cssClass');
			if (hidden) {
				await promo.expectPropertyHidden('anchorId');
			} else {
				await promo.expectPropertyVisible('anchorId');
			}

			await promo.close();
			await section.close();
		});
	}
});
