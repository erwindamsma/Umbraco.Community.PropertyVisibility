import type { Page } from '@playwright/test';
import { openDocument } from '../support/backoffice.js';
import {
	blockEntry,
	createBlockInModal,
	documentProperty,
	ELEMENT_TYPES,
	expandInlineBlock,
	pageNow,
	waitForBlockApplied,
} from '../support/blocks.js';
import { expectPromoBannerModal } from '../support/expectations.js';
import { expect, test } from '../support/fixtures.js';
import { TemporarySchemaChanges } from '../support/schema-changes.js';

/**
 * Adds a promoBanner to both Single Block properties of a "block editors" document (support/schema-changes.ts), without
 * saving it: first `singleBlockInline`, which creates the block in place (its Content half, expanded), then
 * `singleBlock`, which opens the block workspace (both halves). The inline one goes first: cancelling the modal leaves
 * the block catalogue open over the document.
 */
async function addPromoBanners(page: Page, documentKey: string, site: 'corporate' | 'campaign'): Promise<void> {
	await openDocument(page, documentKey);

	let since = await pageNow(page);
	const inlineProperty = documentProperty(page, 'singleBlockInline');
	await inlineProperty.locator('uui-button[label="Add Promo banner"]').click();
	const entry = blockEntry(page, 'singleBlockInline', ELEMENT_TYPES.promoBanner.alias);
	await expect(entry).toHaveCount(1);
	const applied = await waitForBlockApplied(page, {
		documentKey,
		elementTypeKey: ELEMENT_TYPES.promoBanner.key,
		target: 'block-content',
		since,
	});
	expect(applied.propertyCount, 'promoBanner properties hidden').toBe(site === 'corporate' ? 1 : 0);
	const editor = await expandInlineBlock(entry);
	await expect(editor.locator('umb-property[data-mark="property:heading"]'), 'heading').toBeVisible();
	const overlayColour = editor.locator('umb-property[data-mark="property:overlayColour"]');
	if (site === 'corporate') await expect(overlayColour, 'overlayColour').toHaveCount(0);
	else await expect(overlayColour, 'overlayColour').toBeVisible();

	since = await pageNow(page);
	const createButton = documentProperty(page, 'singleBlock').locator('uui-button[label="Add Promo banner"]');
	const modal = await createBlockInModal(page, createButton, ELEMENT_TYPES.promoBanner.name);
	await expectPromoBannerModal(page, modal, { documentKey, since }, site);
	await modal.close();
}

test.describe('Single Block, in a modal and inline', () => {
	test('a promoBanner in a document under Corporate site hides overlayColour (inline and in the modal) and anchorId', async ({ page }) => {
		const changes = await TemporarySchemaChanges.begin(page);
		try {
			const pages = await changes.addBlockEditorsPages();
			await addPromoBanners(page, pages.corporate, 'corporate');
		} finally {
			await changes.restore();
		}
	});

	test('a promoBanner in a document under Campaign site shows overlayColour (inline and in the modal) and anchorId', async ({ page }) => {
		const changes = await TemporarySchemaChanges.begin(page);
		try {
			const pages = await changes.addBlockEditorsPages();
			await addPromoBanners(page, pages.campaign, 'campaign');
		} finally {
			await changes.restore();
		}
	});
});
