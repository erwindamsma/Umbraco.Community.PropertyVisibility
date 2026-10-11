import type { Locator, Page } from '@playwright/test';
import { openDocument } from '../support/backoffice.js';
import { createBlockInModal, documentProperty, ELEMENT_TYPES, pageNow } from '../support/blocks.js';
import { expectPromoBannerModal } from '../support/expectations.js';
import { test } from '../support/fixtures.js';
import { TemporarySchemaChanges } from '../support/schema-changes.js';

/** The Insert Block button of the `richText` property's Tiptap toolbar. */
function insertBlockButton(page: Page): Locator {
	return documentProperty(page, 'richText').locator(
		'umb-tiptap-toolbar-button[data-mark="action:tiptap-toolbar:Umb.Tiptap.Toolbar.BlockPicker"] uui-button',
	);
}

/**
 * Inserts a promoBanner into the rich text editor of a "block editors" document (support/schema-changes.ts) and checks
 * its block workspace, without saving the document.
 */
async function insertPromoBanner(page: Page, documentKey: string, site: 'corporate' | 'campaign'): Promise<void> {
	await openDocument(page, documentKey);
	await documentProperty(page, 'richText').locator('[data-mark="input:tiptap-rte"]').click();
	const since = await pageNow(page);
	const modal = await createBlockInModal(page, insertBlockButton(page), ELEMENT_TYPES.promoBanner.name);
	await expectPromoBannerModal(page, modal, { documentKey, since }, site);
	await modal.close();
}

test.describe('Blocks in the rich text editor (Tiptap, "Insert Block")', () => {
	test('a promoBanner inserted in a document under Corporate site hides overlayColour and anchorId', async ({ page }) => {
		const changes = await TemporarySchemaChanges.begin(page);
		try {
			const pages = await changes.addBlockEditorsPages();
			await insertPromoBanner(page, pages.corporate, 'corporate');
		} finally {
			await changes.restore();
		}
	});

	test('a promoBanner inserted in a document under Campaign site shows overlayColour and anchorId', async ({ page }) => {
		const changes = await TemporarySchemaChanges.begin(page);
		try {
			const pages = await changes.addBlockEditorsPages();
			await insertPromoBanner(page, pages.campaign, 'campaign');
		} finally {
			await changes.restore();
		}
	});
});
