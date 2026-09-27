import type { Locator, Page, Response } from '@playwright/test';
import {
	HiddenFieldsRequestLog,
	save,
	settle,
	startCreateUnder,
	waitForAppliedResponse,
	waitForHiddenFieldsWithParent,
} from '../support/backoffice.js';
import {
	addBlockFromCatalogue,
	addInlineBlockFromCatalogue,
	blockContentValue,
	blockEntry,
	blockValueOf,
	expandInlineBlock,
	openBlockModal,
	pageNow,
	resolveElementTypes,
	waitForBlockApplied,
} from '../support/blocks.js';
import { HIDDEN_FIELDS_PATH, SAMPLE } from '../support/env.js';
import { expectCorporateLandingPage } from '../support/expectations.js';
import { expect, test } from '../support/fixtures.js';
import { getDocument, resolveSample } from '../support/management-api.js';

function inlineProperty(editor: Locator, alias: string): Locator {
	return editor.locator(`umb-property[data-mark="property:${alias}"]`);
}

/** The hidden-fields response for a document and content type requested without a parent (a saved document). */
function waitForHiddenFieldsWithoutParent(page: Page, documentKey: string, contentTypeKey: string): Promise<Response> {
	return page.waitForResponse((response) => {
		const url = new URL(response.url());
		return (
			url.pathname.toLowerCase() === HIDDEN_FIELDS_PATH.toLowerCase() &&
			url.searchParams.get('documentKey')?.toLowerCase() === documentKey.toLowerCase() &&
			url.searchParams.get('contentTypeKey')?.toLowerCase() === contentTypeKey.toLowerCase() &&
			url.searchParams.get('parentKey') === null
		);
	});
}

test.describe('Blocks in a document that is not saved yet', () => {
	test('a new landingPage under Corporate site: a promoBanner added through the block catalogue, in a modal and inline, hides overlayColour and anchorId before and after the first save', async ({ page }, testInfo) => {
		const keys = await resolveSample(page);
		const types = await resolveElementTypes(page);
		const requests = HiddenFieldsRequestLog.start(page);

		const created = waitForHiddenFieldsWithParent(page, keys.corporateSite);
		await startCreateUnder(page, SAMPLE.corporateSite, SAMPLE.landingPageTypeName);
		const scaffoldKey = new URL((await created).url()).searchParams.get('documentKey')!;
		await settle(page);
		await expectCorporateLandingPage(page);

		// The name first: once a block modal has closed, Playwright's fill() on the name input did not take on Umbraco
		// 17.6.2 (typing did).
		const name = page.locator('uui-input[data-mark="input:entity-name"] input');
		const documentName = `Acceptance blocks ${Date.now()}`;
		await name.fill(documentName);
		await expect(name).toHaveValue(documentName);

		// Add a promoBanner to mainBlocks; the block workspace opens on its Content view, before any save.
		let since = await pageNow(page);
		let modal = await addBlockFromCatalogue(page, 'mainBlocks', types.promoBanner.name);
		const contentApplied = await waitForBlockApplied(page, {
			documentKey: scaffoldKey,
			elementTypeKey: types.promoBanner.key,
			target: 'block-content',
			since,
		});
		expect(contentApplied.propertyCount).toBe(1);
		await modal.expectPropertyVisible('heading');
		await modal.expectPropertyHidden('overlayColour');

		await modal.openView('settings');
		const settingsApplied = await waitForBlockApplied(page, {
			documentKey: scaffoldKey,
			elementTypeKey: types.promoBannerSettings.key,
			target: 'block-settings',
			since,
		});
		expect(settingsApplied.propertyCount).toBe(1);
		// The Settings view has rendered (cssClass is never hidden) before anchorId is checked.
		await modal.expectPropertyVisible('cssClass');
		await modal.expectPropertyHidden('anchorId');

		await modal.openView('content');
		await modal.property('heading').locator('input').first().fill('Promo added by the acceptance suite');
		await modal.submit();
		await expect(blockEntry(page, 'mainBlocks', types.promoBanner.alias)).toHaveCount(1);

		// Add a promoBanner to inlineBlocks as well: an inline block's workspace stays alive, also across the first save.
		since = await pageNow(page);
		const inline = await addInlineBlockFromCatalogue(page, 'inlineBlocks', types.promoBanner.alias, types.promoBanner.name);
		const inlineApplied = await waitForBlockApplied(page, {
			documentKey: scaffoldKey,
			elementTypeKey: types.promoBanner.key,
			target: 'block-content',
			since,
		});
		expect(inlineApplied.propertyCount).toBe(1);
		let inlineEditor = await expandInlineBlock(inline);
		await expect(inlineProperty(inlineEditor, 'heading'), 'inline heading').toBeVisible();
		await expect(inlineProperty(inlineEditor, 'overlayColour'), 'inline overlayColour').toHaveCount(0);
		// Marks the inline block element, to tell afterwards whether the save kept it (and its block workspace) alive.
		await inline.locator('umb-inline-list-block').first().evaluate((element) => {
			(element as unknown as { __pvBeforeSave?: boolean }).__pvBeforeSave = true;
		});

		// Before the save the block halves resolve the site from the create-under parent, like the document itself; the
		// inline block shares the modal block's responses.
		for (const typeKey of [types.promoBanner.key, types.promoBannerSettings.key]) {
			const forType = requests.requests.filter((r) => r.documentKey === scaffoldKey && r.contentTypeKey === typeKey);
			expect(forType.map((r) => r.parentKey), `requests for ${typeKey} before the first save`).toEqual([keys.corporateSite]);
		}

		// First save: the document is created under the scaffold key and the workspace re-requests without parentKey.
		// Nothing is reopened: the live inline promoBanner requests and applies again by itself.
		await expect(name).toHaveValue(documentName);
		const beforeSave = requests.mark();
		const inlineRequestAfterSave = waitForHiddenFieldsWithoutParent(page, scaffoldKey, types.promoBanner.key);
		await save(page);
		await page.waitForURL(new RegExp(`/workspace/document/edit/${scaffoldKey}`));
		const inlineReapplied = await waitForAppliedResponse(page, await inlineRequestAfterSave, { target: 'block-content' });
		expect(inlineReapplied.propertyCount, 'the inline promoBanner after the first save').toBe(1);
		await settle(page);

		const inlineAfterSave = blockEntry(page, 'inlineBlocks', types.promoBanner.alias);
		const keptAlive = await inlineAfterSave
			.locator('umb-inline-list-block')
			.first()
			.evaluate((element) => (element as unknown as { __pvBeforeSave?: boolean }).__pvBeforeSave === true);
		testInfo.annotations.push({
			type: 'inline block across the first save',
			description: keptAlive
				? 'the inline block element (and its block workspace) survived the save: the block context re-anchored'
				: 'Umbraco re-created the inline block element on save: a new block context applied the saved anchor',
		});
		inlineEditor = await expandInlineBlock(inlineAfterSave);
		await expect(inlineProperty(inlineEditor, 'heading'), 'inline heading after the save').toBeVisible();
		await expect(inlineProperty(inlineEditor, 'overlayColour'), 'inline overlayColour after the save').toHaveCount(0);
		await expectCorporateLandingPage(page);

		// Reopen the modal block after the save: still hidden, now resolved from the saved document itself.
		since = await pageNow(page);
		modal = await openBlockModal(page, blockEntry(page, 'mainBlocks', types.promoBanner.alias));
		await waitForBlockApplied(page, {
			documentKey: scaffoldKey,
			elementTypeKey: types.promoBanner.key,
			target: 'block-content',
			since,
		});
		await modal.expectPropertyVisible('heading');
		await expect(modal.property('heading').locator('input').first()).toHaveValue('Promo added by the acceptance suite');
		await modal.expectPropertyHidden('overlayColour');
		await modal.openView('settings');
		await waitForBlockApplied(page, {
			documentKey: scaffoldKey,
			elementTypeKey: types.promoBannerSettings.key,
			target: 'block-settings',
			since,
		});
		await modal.expectPropertyVisible('cssClass');
		await modal.expectPropertyHidden('anchorId');
		await modal.close();

		// One request per element type after the save, without a parent, shared by the inline block and the reopened modal.
		const afterSave = requests.requests.slice(beforeSave).filter((r) => r.documentKey === scaffoldKey);
		for (const typeKey of [types.promoBanner.key, types.promoBannerSettings.key]) {
			expect(
				afterSave.filter((r) => r.contentTypeKey === typeKey).map((r) => r.parentKey),
				`requests for ${typeKey} after the first save`,
			).toEqual([null]);
		}

		// Both blocks were saved with the document.
		const document = await getDocument(page, scaffoldKey);
		expect(blockContentValue(blockValueOf(document, 'mainBlocks'), types.promoBanner.key, 'heading')).toBe(
			'Promo added by the acceptance suite',
		);
		expect(blockValueOf(document, 'inlineBlocks').contentData.map((item) => item.contentTypeKey)).toEqual([
			types.promoBanner.key,
		]);
	});
});
