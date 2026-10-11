import { HiddenFieldsRequestLog, openDocument, settle } from '../support/backoffice.js';
import { blockEntry, blockEventsSince, documentProperty, ELEMENT_TYPES, openBlockModal, pageNow } from '../support/blocks.js';
import { expect, test } from '../support/fixtures.js';
import { SCHEMA_NAMES, TemporarySchemaChanges } from '../support/schema-changes.js';

/**
 * Blocks edited in a member get nothing hidden, also when the member is opened in a modal from a document's member
 * picker: the member workspace carries the package's site boundary (like media, spec 21), so the block context of the
 * member's block does not reach the document's site context.
 */
test.describe('Blocks outside a document: a member opened from a document', () => {
	test('a member picked on a document under Corporate site: its promoBanner shows overlayColour and anchorId and nothing is requested', async ({ page }) => {
		const changes = await TemporarySchemaChanges.begin(page);
		try {
			const member = await changes.addMemberWithPromoBanner();
			const pages = await changes.addBlockEditorsPages([{ alias: 'featuredMember', culture: null, segment: null, value: member }]);
			const requests = HiddenFieldsRequestLog.start(page);
			await openDocument(page, pages.corporate);

			// The picked member's card opens the member workspace in a modal.
			const mark = requests.mark();
			const since = await pageNow(page);
			await documentProperty(page, 'featuredMember').locator(`uui-ref-node-member[name="${SCHEMA_NAMES.member}"]`).click();
			const memberEditor = page.locator('umb-member-workspace-editor');
			await expect(memberEditor).toHaveCount(1);
			await expect(memberEditor.locator('uui-input[data-mark="input:entity-name"] input')).toHaveValue(SCHEMA_NAMES.member);

			// The block's properties render after its block workspace (and the package's block context) has started, so a
			// request the context sent would already be recorded; settle() waits for such a request and its apply.
			const modal = await openBlockModal(page, blockEntry(memberEditor, 'memberBlocks', ELEMENT_TYPES.promoBanner.alias));
			await expect(modal.property('heading').locator('input').first()).toHaveValue('Member promo');
			await modal.expectPropertyVisible('overlayColour');
			await modal.openView('settings');
			await modal.expectPropertyVisible('cssClass');
			await modal.expectPropertyVisible('anchorId');
			await settle(page);
			await modal.expectPropertyVisible('anchorId');
			await modal.openView('content');
			await modal.expectPropertyVisible('overlayColour');

			expect(await blockEventsSince(page, since), 'block passes inside the member').toEqual([]);
			expect(requests.byPair(mark), 'hidden-fields requests while the member is open').toEqual({});
			await modal.close();
		} finally {
			await changes.restore();
		}
	});
});
