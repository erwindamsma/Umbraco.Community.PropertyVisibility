import type { Locator } from '@playwright/test';
import { openDocument } from '../support/backoffice.js';
import { blockEntry, expandInlineBlock, resolveElementTypes, waitForBlockApplied } from '../support/blocks.js';
import { expect, test } from '../support/fixtures.js';
import { resolveSample } from '../support/management-api.js';

function inlineProperty(editor: Locator, alias: string): Locator {
	return editor.locator(`umb-property[data-mark="property:${alias}"]`);
}

test.describe('An inline-edited Block List block (landingPage.inlineBlocks)', () => {
	test('Corporate landing: the expanded promoBanner hides overlayColour and keeps heading', async ({ page }) => {
		const keys = await resolveSample(page);
		const types = await resolveElementTypes(page);
		await openDocument(page, keys.corporateLanding);

		// The inline block's workspace, and the package's block context with it, starts when the entry renders.
		const applied = await waitForBlockApplied(page, {
			documentKey: keys.corporateLanding,
			elementTypeKey: types.promoBanner.key,
			target: 'block-content',
		});
		expect(applied.propertyCount).toBe(1);

		const editor = await expandInlineBlock(blockEntry(page, 'inlineBlocks', types.promoBanner.alias));
		await expect(inlineProperty(editor, 'heading'), 'heading').toBeVisible();
		await expect(inlineProperty(editor, 'image'), 'image').toBeVisible();
		await expect(inlineProperty(editor, 'overlayColour'), 'overlayColour').toHaveCount(0);
	});

	test('Campaign landing: the expanded promoBanner shows overlayColour', async ({ page }) => {
		const keys = await resolveSample(page);
		const types = await resolveElementTypes(page);
		await openDocument(page, keys.campaignLanding);

		const applied = await waitForBlockApplied(page, {
			documentKey: keys.campaignLanding,
			elementTypeKey: types.promoBanner.key,
			target: 'block-content',
		});
		expect(applied.propertyCount).toBe(0);

		const editor = await expandInlineBlock(blockEntry(page, 'inlineBlocks', types.promoBanner.alias));
		await expect(inlineProperty(editor, 'heading'), 'heading').toBeVisible();
		await expect(inlineProperty(editor, 'overlayColour'), 'overlayColour').toBeVisible();
		await expect(inlineProperty(editor, 'image'), 'image').toBeVisible();
	});
});
