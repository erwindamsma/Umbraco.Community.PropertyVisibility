import { contentTab, expectPropertyVisible, HiddenFieldsRequestLog, settle } from '../support/backoffice.js';
import { blockEntry, blockEventsSince, ELEMENT_TYPES, openBlockModal, pageNow } from '../support/blocks.js';
import { expect, test } from '../support/fixtures.js';
import { resolveSample } from '../support/management-api.js';
import { SCHEMA_NAMES, TemporarySchemaChanges } from '../support/schema-changes.js';

/**
 * A document blueprint has no site: the blueprint workspace carries the package's site boundary and no document
 * workspace context, so neither the blueprint's own properties nor its blocks get anything hidden, not even when the
 * blueprint was made from a document under Corporate site.
 */
test.describe('Blocks outside a document: a document blueprint', () => {
	test('a blueprint made from Corporate landing: the Seo tab, bannerImage and its promoBanner\'s overlayColour and anchorId are shown and nothing is requested', async ({ page }) => {
		const keys = await resolveSample(page);
		const changes = await TemporarySchemaChanges.begin(page);
		try {
			const blueprint = await changes.addBlueprint(keys.corporateLanding);
			const requests = HiddenFieldsRequestLog.start(page);
			const since = await pageNow(page);
			await page.goto(`/umbraco/section/settings/workspace/document-blueprint/edit/${blueprint}`);
			const editor = page.locator('umb-document-blueprint-workspace-editor');
			await expect(editor.locator('uui-input[data-mark="input:entity-name"] input')).toHaveValue(SCHEMA_NAMES.blueprint);

			// What the Corporate rules hide on the landing page itself.
			await expectPropertyVisible(page, 'bannerImage');
			await expectPropertyVisible(page, 'relatedLinks');
			await expect(contentTab(page, 'Seo tab')).toBeVisible();

			const modal = await openBlockModal(page, blockEntry(editor, 'mainBlocks', ELEMENT_TYPES.promoBanner.alias));
			await modal.expectPropertyVisible('heading');
			await modal.expectPropertyVisible('overlayColour');
			await modal.openView('settings');
			await modal.expectPropertyVisible('cssClass');
			await modal.expectPropertyVisible('anchorId');
			await settle(page);
			await modal.expectPropertyVisible('anchorId');

			expect(await blockEventsSince(page, since), 'block passes inside the blueprint').toEqual([]);
			expect(requests.requests, 'hidden-fields requests').toEqual([]);
			await modal.close();
		} finally {
			await changes.restore();
		}
	});
});
