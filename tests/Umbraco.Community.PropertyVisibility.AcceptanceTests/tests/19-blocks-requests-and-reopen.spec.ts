import { HiddenFieldsRequestLog, save } from '../support/backoffice.js';
import {
	blockContentValue,
	blockEntry,
	blockSettingsValue,
	blockValueOf,
	expandInlineBlock,
	openBlockModal,
	openDocumentWithInlineBlocks,
	pageNow,
	resolveElementTypes,
	waitForBlockApplied,
} from '../support/blocks.js';
import { expect, test } from '../support/fixtures.js';
import { getDocument, resolveSample } from '../support/management-api.js';
import type { AppliedEvent } from '../support/applied.js';

test.describe('One hidden-fields request per document and element type', () => {
	test('Corporate landing: the inline block, the modal block (content and settings), the grid block, the nested block and a reopened modal share one request for promoBanner and one for promoBannerSettings', async ({ page }) => {
		const keys = await resolveSample(page);
		const types = await resolveElementTypes(page);
		const requests = HiddenFieldsRequestLog.start(page);

		// The inline promoBanner applies both halves on load, the inline callToAction its content.
		await openDocumentWithInlineBlocks(page, keys.corporateLanding);
		await expandInlineBlock(blockEntry(page, 'inlineBlocks', types.promoBanner.alias));

		// The mainBlocks promoBanner in a modal: content, then settings.
		let since = await pageNow(page);
		let modal = await openBlockModal(page, blockEntry(page, 'mainBlocks', types.promoBanner.alias));
		await waitForBlockApplied(page, { documentKey: keys.corporateLanding, elementTypeKey: types.promoBanner.key, target: 'block-content', since });
		await modal.openView('settings');
		await waitForBlockApplied(page, { documentKey: keys.corporateLanding, elementTypeKey: types.promoBannerSettings.key, target: 'block-settings', since });
		await modal.expectPropertyVisible('cssClass');
		await modal.expectPropertyHidden('anchorId');

		// Closing and reopening the same block issues no request at all.
		await modal.close();
		const beforeReopen = requests.mark();
		since = await pageNow(page);
		modal = await openBlockModal(page, blockEntry(page, 'mainBlocks', types.promoBanner.alias));
		await waitForBlockApplied(page, { documentKey: keys.corporateLanding, elementTypeKey: types.promoBanner.key, target: 'block-content', since });
		await waitForBlockApplied(page, { documentKey: keys.corporateLanding, elementTypeKey: types.promoBannerSettings.key, target: 'block-settings', since });
		await modal.expectPropertyHidden('overlayColour');
		await modal.close();
		expect(requests.byPair(beforeReopen), 'hidden-fields requests while closing and reopening the block').toEqual({});

		// The grid promoBanner and the nested promoBanner.
		since = await pageNow(page);
		modal = await openBlockModal(page, blockEntry(page, 'gridBlocks', types.promoBanner.alias));
		await waitForBlockApplied(page, { documentKey: keys.corporateLanding, elementTypeKey: types.promoBanner.key, target: 'block-content', since });
		await modal.close();

		since = await pageNow(page);
		const section = await openBlockModal(page, blockEntry(page, 'mainBlocks', types.contentSection.alias));
		await waitForBlockApplied(page, { documentKey: keys.corporateLanding, elementTypeKey: types.contentSection.key, target: 'block-content', since });
		since = await pageNow(page);
		const nested = await openBlockModal(page, blockEntry(section.root, 'items', types.promoBanner.alias));
		await waitForBlockApplied(page, { documentKey: keys.corporateLanding, elementTypeKey: types.promoBanner.key, target: 'block-content', since });
		await waitForBlockApplied(page, { documentKey: keys.corporateLanding, elementTypeKey: types.promoBannerSettings.key, target: 'block-settings', since });
		await nested.close();
		await section.close();

		expect(requests.count(keys.corporateLanding, types.promoBanner.key), 'requests for (Corporate landing, promoBanner)').toBe(1);
		expect(requests.count(keys.corporateLanding, types.promoBannerSettings.key), 'requests for (Corporate landing, promoBannerSettings)').toBe(1);
		// Every other pair was requested at most once too.
		for (const [pair, count] of Object.entries(requests.byPair())) {
			expect(count, `requests for ${pair}`).toBe(1);
		}
	});
});

test.describe('Block rules survive reopening the modal, and hidden values survive saving', () => {
	test('Corporate landing: close and reopen the promoBanner, Update, Save: overlayColour still hidden and its stored value (and anchorId) unchanged', async ({ page }) => {
		const keys = await resolveSample(page);
		const types = await resolveElementTypes(page);
		const before = blockValueOf(await getDocument(page, keys.corporateLanding), 'mainBlocks');
		const overlayBefore = blockContentValue(before, types.promoBanner.key, 'overlayColour');
		const anchorBefore = blockSettingsValue(before, types.promoBannerSettings.key, 'anchorId');
		expect(overlayBefore, 'the seeded overlayColour').toBeTruthy();
		expect(anchorBefore, 'the seeded anchorId').toBeTruthy();

		await openDocumentWithInlineBlocks(page, keys.corporateLanding);
		let since = await pageNow(page);
		let modal = await openBlockModal(page, blockEntry(page, 'mainBlocks', types.promoBanner.alias));
		await waitForBlockApplied(page, { documentKey: keys.corporateLanding, elementTypeKey: types.promoBanner.key, target: 'block-content', since });
		await modal.expectPropertyVisible('heading');
		await modal.expectPropertyHidden('overlayColour');
		await modal.close();

		since = await pageNow(page);
		modal = await openBlockModal(page, blockEntry(page, 'mainBlocks', types.promoBanner.alias));
		await waitForBlockApplied(page, { documentKey: keys.corporateLanding, elementTypeKey: types.promoBanner.key, target: 'block-content', since });
		await modal.expectPropertyVisible('heading');
		await modal.expectPropertyHidden('overlayColour');
		await modal.openView('settings');
		await waitForBlockApplied(page, { documentKey: keys.corporateLanding, elementTypeKey: types.promoBannerSettings.key, target: 'block-settings', since });
		await modal.expectPropertyVisible('cssClass');
		await modal.expectPropertyHidden('anchorId');

		// Submit the block with its hidden values, then save the document.
		await modal.submit();
		await save(page);

		const after = blockValueOf(await getDocument(page, keys.corporateLanding), 'mainBlocks');
		expect(blockContentValue(after, types.promoBanner.key, 'overlayColour')).toBe(overlayBefore);
		expect(blockSettingsValue(after, types.promoBannerSettings.key, 'anchorId')).toBe(anchorBefore);
		expect(blockContentValue(after, types.promoBanner.key, 'heading')).toBe(blockContentValue(before, types.promoBanner.key, 'heading'));
	});
});

type RecordedEvents = Array<Omit<AppliedEvent, 'at'>>;

test.describe('The applied event reports every block pass', () => {
	test('Campaign landing: every block half reports block-content or block-settings with the element type and the document', async ({ page }) => {
		const keys = await resolveSample(page);
		const types = await resolveElementTypes(page);
		// Also waits for the inline promoBanner (both halves) and the inline callToAction, the page's only callToAction
		// pass, so the recorded events below are complete.
		await openDocumentWithInlineBlocks(page, keys.campaignLanding);

		const since = await pageNow(page);
		const modal = await openBlockModal(page, blockEntry(page, 'mainBlocks', types.promoBanner.alias));
		const content = await waitForBlockApplied(page, { documentKey: keys.campaignLanding, elementTypeKey: types.promoBanner.key, target: 'block-content', since });
		const settings = await waitForBlockApplied(page, { documentKey: keys.campaignLanding, elementTypeKey: types.promoBannerSettings.key, target: 'block-settings', since });
		await modal.close();
		expect(content).toEqual(expect.objectContaining({ documentKey: keys.campaignLanding, contentTypeKey: types.promoBanner.key, target: 'block-content' }));
		expect(settings).toEqual(expect.objectContaining({ documentKey: keys.campaignLanding, contentTypeKey: types.promoBannerSettings.key, target: 'block-settings' }));

		const events = await page.evaluate(() => (window as unknown as { __pvApplied?: RecordedEvents }).__pvApplied ?? []);
		// Every event names a known target and this document.
		for (const event of events) {
			expect(['document', 'block-content', 'block-settings']).toContain(event.target);
			expect(event.documentKey).toBe(keys.campaignLanding);
		}
		// The document reports its own type; blocks report element types, content and settings halves apart.
		const typesByTarget = (target: string) => new Set(events.filter((e) => e.target === target).map((e) => e.contentTypeKey));
		expect([...typesByTarget('document')]).toEqual([keys.landingPageType]);
		expect(typesByTarget('block-content')).toEqual(new Set([types.promoBanner.key, types.callToAction.key]));
		expect([...typesByTarget('block-settings')]).toEqual([types.promoBannerSettings.key]);
	});
});
