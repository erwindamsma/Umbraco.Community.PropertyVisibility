import type { Page } from '@playwright/test';
import { openDocument, save } from '../support/backoffice.js';
import {
	blockContentValue,
	blockEntry,
	blockSettingsValue,
	blockValueOf,
	copyBlockToClipboard,
	ELEMENT_TYPES,
	openBlockModal,
	openDocumentWithInlineBlocks,
	pageNow,
	pasteBlockFromClipboard,
	type BlockValue,
} from '../support/blocks.js';
import { SAMPLE } from '../support/env.js';
import { expectPromoBannerModal } from '../support/expectations.js';
import { expect, test } from '../support/fixtures.js';
import { getDocument, resolveSample } from '../support/management-api.js';
import { TemporarySchemaChanges } from '../support/schema-changes.js';

const { promoBanner, promoBannerSettings } = ELEMENT_TYPES;

/** The promoBanner values the specs compare: the two the Corporate rules hide, and heading, which no rule hides. */
function promoBannerValues(block: BlockValue): Record<string, unknown> {
	return {
		heading: blockContentValue(block, promoBanner.key, 'heading'),
		overlayColour: blockContentValue(block, promoBanner.key, 'overlayColour'),
		anchorId: blockSettingsValue(block, promoBannerSettings.key, 'anchorId'),
	};
}

/**
 * Copies the `mainBlocks` promoBanner of a seeded landing page, pastes it into `mainBlocks` of a "block editors"
 * document under the other site (support/schema-changes.ts), checks the pasted block's workspace and saves: the stored
 * copy holds the source's values, the hidden ones included.
 */
async function copyAndPaste(
	page: Page,
	source: { documentKey: string; name: string },
	target: { documentKey: string; site: 'corporate' | 'campaign' },
): Promise<void> {
	const sourceValues = promoBannerValues(blockValueOf(await getDocument(page, source.documentKey), 'mainBlocks'));
	expect(sourceValues.overlayColour, 'the seeded overlayColour').toBeTruthy();
	expect(sourceValues.anchorId, 'the seeded anchorId').toBeTruthy();

	await openDocumentWithInlineBlocks(page, source.documentKey);
	await copyBlockToClipboard(blockEntry(page, 'mainBlocks', promoBanner.alias));

	await openDocument(page, target.documentKey);
	const since = await pageNow(page);
	const entry = await pasteBlockFromClipboard(page, 'mainBlocks', promoBanner.alias, `${source.name} - Main blocks - ${promoBanner.name}`);
	const modal = await openBlockModal(page, entry);
	await expect(modal.property('heading').locator('input').first()).toHaveValue(String(sourceValues.heading));
	await expectPromoBannerModal(page, modal, { documentKey: target.documentKey, since }, target.site);
	await modal.close();

	await save(page);
	const stored = blockValueOf(await getDocument(page, target.documentKey), 'mainBlocks');
	expect(promoBannerValues(stored)).toEqual(sourceValues);
}

test.describe('A block pasted from the clipboard into a document under another site (mainBlocks)', () => {
	test('Corporate landing -> Campaign site: the pasted promoBanner shows overlayColour and anchorId and keeps their values', async ({ page }) => {
		const keys = await resolveSample(page);
		const changes = await TemporarySchemaChanges.begin(page);
		try {
			const pages = await changes.addBlockEditorsPages();
			await copyAndPaste(
				page,
				{ documentKey: keys.corporateLanding, name: SAMPLE.corporateLanding },
				{ documentKey: pages.campaign, site: 'campaign' },
			);
		} finally {
			await changes.restore();
		}
	});

	test('Campaign landing -> Corporate site: the pasted promoBanner hides overlayColour and anchorId and keeps their values', async ({ page }) => {
		const keys = await resolveSample(page);
		const changes = await TemporarySchemaChanges.begin(page);
		try {
			const pages = await changes.addBlockEditorsPages();
			await copyAndPaste(
				page,
				{ documentKey: keys.campaignLanding, name: SAMPLE.campaignLanding },
				{ documentKey: pages.corporate, site: 'corporate' },
			);
		} finally {
			await changes.restore();
		}
	});
});
