import type { Locator, Page } from '@playwright/test';
import { HiddenFieldsRequestLog, settle } from '../support/backoffice.js';
import {
	blockEntry,
	expandInlineBlock,
	openBlockModal,
	openDocumentWithInlineBlocks,
	pageNow,
	resolveElementTypes,
	waitForBlockApplied,
} from '../support/blocks.js';
import { SAMPLE } from '../support/env.js';
import { expect, test } from '../support/fixtures.js';
import { resolveSample } from '../support/management-api.js';
import type { AppliedEvent } from '../support/applied.js';

/** The seeded media item "Promo media" (media type blockMedia): an inline Block List (`blocks`) with one promoBanner. */
const PROMO_MEDIA_KEY = '43aba5f8-52fa-40ea-b707-6dc1e9c5fee9';

/** Block applied events recorded in the page at or after a browser wall-clock time. */
async function blockEventsSince(page: Page, since: number): Promise<AppliedEvent[]> {
	const events = await page.evaluate(() => (window as unknown as { __pvApplied?: AppliedEvent[] }).__pvApplied ?? []);
	return events.filter((event) => event.at >= since && event.target !== 'document');
}

/**
 * The promoBanner of the media item, expanded in place: heading ("Media promo") and overlayColour both shown, because a
 * media item has no site. overlayColour is hidden on Corporate site, so it would disappear here if the block took the
 * rules of the document the media item was opened from. The block's properties render after its block workspace (and
 * the package's block context) has started, so a request the context sent would already be recorded by then; the
 * following settle() waits for such a request and its apply before the spec counts requests and passes.
 */
async function expectMediaPromoUnfiltered(media: Locator): Promise<void> {
	const editor = await expandInlineBlock(blockEntry(media, 'blocks', 'promoBanner'));
	const heading = editor.locator('umb-property[data-mark="property:heading"]');
	await expect(heading, 'heading').toBeVisible();
	await expect(heading.locator('input').first()).toHaveValue('Media promo');
	await expect(editor.locator('umb-property[data-mark="property:overlayColour"]'), 'overlayColour').toBeVisible();
	await expect(editor.locator('umb-property[data-mark="property:image"]'), 'image').toBeVisible();
}

/**
 * Blocks edited outside a document get nothing hidden. The media workspace (like the member and document blueprint
 * workspaces) carries the package's site boundary, so the block context of a block inside a media item does not reach
 * the site context of a document, not even when the media item is opened in a modal from inside that document (the
 * modal's context proxy forwards every other request to the document).
 */
test.describe('Blocks outside a document: a media item with a Block List', () => {
	test('opened in a modal from a block of Corporate landing: its promoBanner shows overlayColour and nothing is requested', async ({ page }) => {
		const keys = await resolveSample(page);
		const types = await resolveElementTypes(page);
		const requests = HiddenFieldsRequestLog.start(page);
		await openDocumentWithInlineBlocks(page, keys.corporateLanding);

		// The Corporate promoBanner itself follows the document's site.
		let since = await pageNow(page);
		const block = await openBlockModal(page, blockEntry(page, 'mainBlocks', types.promoBanner.alias));
		await waitForBlockApplied(page, { documentKey: keys.corporateLanding, elementTypeKey: types.promoBanner.key, target: 'block-content', since });
		await block.expectPropertyVisible('image');
		await block.expectPropertyHidden('overlayColour');

		// Its image picks the media item: the card opens the media picker's editor modal, whose "Open in Media Library"
		// opens the media workspace in a third modal.
		const mark = requests.mark();
		since = await pageNow(page);
		await block.property('image').locator(`uui-card-media[name="${SAMPLE.promoMedia}"]`).click();
		const cropper = page.locator('umb-image-cropper-editor-modal');
		await expect(cropper).toHaveCount(1);
		await cropper.locator('uui-button[label="Open in Media Library"]').click();
		const media = page.locator('umb-media-workspace-editor');
		await expect(media).toHaveCount(1);
		await expect(media.locator('uui-input[data-mark="input:entity-name"] input')).toHaveValue(SAMPLE.promoMedia);

		await expectMediaPromoUnfiltered(media);
		await settle(page);
		await expect(media.locator('umb-property[data-mark="property:overlayColour"]')).toBeVisible();

		expect(await blockEventsSince(page, since), 'block passes inside the media item').toEqual([]);
		expect(requests.byPair(mark), 'hidden-fields requests while the media item is open').toEqual({});
	});

	test('opened from the Media section: its promoBanner shows overlayColour and nothing is requested', async ({ page }) => {
		const requests = HiddenFieldsRequestLog.start(page);
		const since = await pageNow(page);
		await page.goto(`/umbraco/section/media/workspace/media/edit/${PROMO_MEDIA_KEY}`);
		const media = page.locator('umb-media-workspace-editor');
		await expect(media.locator('uui-input[data-mark="input:entity-name"] input')).toHaveValue(SAMPLE.promoMedia);

		await expectMediaPromoUnfiltered(media);
		await settle(page);
		await expect(media.locator('umb-property[data-mark="property:overlayColour"]')).toBeVisible();

		expect(await blockEventsSince(page, since), 'block passes inside the media item').toEqual([]);
		expect(requests.requests, 'hidden-fields requests').toEqual([]);
	});
});
