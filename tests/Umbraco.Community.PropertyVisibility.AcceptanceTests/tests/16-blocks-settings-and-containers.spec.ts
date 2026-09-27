import {
	blockEntry,
	openBlockModal,
	openDocumentWithInlineBlocks,
	pageNow,
	resolveElementTypes,
	waitForBlockApplied,
	type BlockModal,
	type BlockPropertyAlias,
} from '../support/blocks.js';
import { expect, test } from '../support/fixtures.js';
import { resolveSample } from '../support/management-api.js';

test.describe('The Settings half of a block (promoBannerSettings)', () => {
	test('Corporate landing: the promoBanner Settings view hides anchorId (and its emptied group)', async ({ page }) => {
		const keys = await resolveSample(page);
		const types = await resolveElementTypes(page);
		await openDocumentWithInlineBlocks(page, keys.corporateLanding);

		const since = await pageNow(page);
		const modal = await openBlockModal(page, blockEntry(page, 'mainBlocks', types.promoBanner.alias));
		await modal.expectPropertyVisible('heading');
		await modal.openView('settings');
		const applied = await waitForBlockApplied(page, {
			documentKey: keys.corporateLanding,
			elementTypeKey: types.promoBannerSettings.key,
			target: 'block-settings',
			since,
		});
		// anchorId is the only property of the Settings group, so HideEmptiedContainers removes the group as well; the
		// Layout group (cssClass) stays.
		expect(applied.propertyCount).toBe(1);
		expect(applied.containerCount).toBe(1);

		// The Settings view has rendered (cssClass is never hidden) before its hidden parts are checked.
		await expect(modal.group('Layout')).toBeVisible();
		await modal.expectPropertyVisible('cssClass');
		await modal.expectPropertyHidden('anchorId');
		await expect(modal.group('Settings')).toHaveCount(0);

		// The Content view is still intact after visiting Settings.
		await modal.openView('content');
		await modal.expectPropertyVisible('heading');
		await modal.expectPropertyHidden('overlayColour');
		await modal.close();
	});

	test('Campaign landing: the promoBanner Settings view shows anchorId', async ({ page }) => {
		const keys = await resolveSample(page);
		const types = await resolveElementTypes(page);
		await openDocumentWithInlineBlocks(page, keys.campaignLanding);

		const since = await pageNow(page);
		const modal = await openBlockModal(page, blockEntry(page, 'mainBlocks', types.promoBanner.alias), 'settings');
		const applied = await waitForBlockApplied(page, {
			documentKey: keys.campaignLanding,
			elementTypeKey: types.promoBannerSettings.key,
			target: 'block-settings',
			since,
		});
		expect(applied.propertyCount).toBe(0);

		await expect(modal.group('Settings')).toBeVisible();
		await modal.expectPropertyVisible('anchorId');
		await expect(modal.group('Layout')).toBeVisible();
		await modal.expectPropertyVisible('cssClass');
		await modal.close();
	});
});

/** The callToAction element type: a root "Content" group (label, url) and a "Settings tab" tab with a "Tracking" group (campaignCode). */
async function expectCallToActionWithSettingsTab(modal: BlockModal): Promise<void> {
	await modal.expectPropertyVisible('label');
	await modal.expectPropertyVisible('url');
	await expect(modal.contentTab('Settings tab')).toBeVisible();
	await modal.openContentTab('Settings tab');
	await expect(modal.group('Tracking')).toBeVisible();
	await modal.expectPropertyVisible('campaignCode');
}

async function expectCallToActionWithoutSettingsTab(modal: BlockModal): Promise<void> {
	await modal.expectPropertyVisible('label');
	await modal.expectPropertyVisible('url');
	await expect(modal.contentTab('Settings tab')).toHaveCount(0);
	// With the only tab gone, the block shows its root group without a tab bar.
	await expect(modal.contentTabs()).toHaveCount(0);
	await expect(modal.group('Tracking')).toHaveCount(0);
	await modal.expectPropertyHidden('campaignCode');
}

test.describe('Removing a tab inside a block (campaign rule callToAction Containers settingsTab)', () => {
	for (const property of ['mainBlocks', 'gridBlocks'] as BlockPropertyAlias[]) {
		test(`${property}: the callToAction block has no settingsTab on Campaign landing and has it on Corporate landing`, async ({ page }) => {
			const keys = await resolveSample(page);
			const types = await resolveElementTypes(page);

			await openDocumentWithInlineBlocks(page, keys.campaignLanding);
			let since = await pageNow(page);
			let modal = await openBlockModal(page, blockEntry(page, property, types.callToAction.alias));
			let applied = await waitForBlockApplied(page, {
				documentKey: keys.campaignLanding,
				elementTypeKey: types.callToAction.key,
				target: 'block-content',
				since,
			});
			// The tab and its Tracking group are removed; campaignCode is guarded.
			expect(applied.containerCount).toBe(2);
			expect(applied.propertyCount).toBe(1);
			await expectCallToActionWithoutSettingsTab(modal);
			await modal.close();

			await openDocumentWithInlineBlocks(page, keys.corporateLanding);
			since = await pageNow(page);
			modal = await openBlockModal(page, blockEntry(page, property, types.callToAction.alias));
			applied = await waitForBlockApplied(page, {
				documentKey: keys.corporateLanding,
				elementTypeKey: types.callToAction.key,
				target: 'block-content',
				since,
			});
			expect(applied.containerCount).toBe(0);
			expect(applied.propertyCount).toBe(0);
			await expectCallToActionWithSettingsTab(modal);
			await modal.close();
		});
	}
});
