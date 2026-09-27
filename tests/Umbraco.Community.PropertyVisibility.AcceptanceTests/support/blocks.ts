import { expect, type Locator, type Page } from '@playwright/test';
import { waitForApplied, type AppliedEvent, type AppliedTarget } from './applied.js';
import { openDocument } from './backoffice.js';
import { managementApi, type DocumentModel } from './management-api.js';

/**
 * The element types of the seeded blocks (uSync/v17/ContentTypes in the test site). The keys are stable (the seed
 * derives them from the alias); resolveElementTypes() checks them against the running site.
 */
export const ELEMENT_TYPES = {
	promoBanner: { alias: 'promoBanner', key: '5cb61180-562e-5fc8-9974-067ad8a6ba9b', name: 'Promo banner' },
	promoBannerSettings: {
		alias: 'promoBannerSettings',
		key: '70b555a9-4d9d-5cd4-880b-593a24354dee',
		name: 'Promo banner settings',
	},
	callToAction: { alias: 'callToAction', key: '05eed208-6e7b-5366-878b-99486df679e6', name: 'Call to action' },
	contentSection: { alias: 'contentSection', key: '4bfddf8e-04ca-5197-b6d1-2724a0dc9bbc', name: 'Content section' },
} as const;

export type ElementTypeName = keyof typeof ELEMENT_TYPES;

/** Fails fast, with a clear message, when the running site's element types are not the seeded ones. */
export async function resolveElementTypes(page: Page): Promise<typeof ELEMENT_TYPES> {
	for (const type of Object.values(ELEMENT_TYPES)) {
		const model = await managementApi<{ alias: string; isElement: boolean }>(page, `/document-type/${type.key}`);
		expect(model.alias, `element type ${type.key}`).toBe(type.alias);
		expect(model.isElement, `${type.alias} is an element type`).toBe(true);
	}
	return ELEMENT_TYPES;
}

/** The block editor properties of the landingPage document type in the test site. */
export type BlockPropertyAlias = 'mainBlocks' | 'inlineBlocks' | 'gridBlocks';

// ---- Waiting ------------------------------------------------------------------------------------------------------

/**
 * `Date.now()` in the page. Pass it as `since` to waitForApplied, taken right before the action whose apply should be
 * observed: the applied events carry the page's wall clock.
 */
export function pageNow(page: Page): Promise<number> {
	return page.evaluate(() => Date.now());
}

/**
 * Waits for the applied event of one half of a block: `block-content` with the block's element type, or
 * `block-settings` with its settings element type. Only a half that has an element type ever reports; the settings
 * half of a block type without settings never does.
 */
export function waitForBlockApplied(
	page: Page,
	filter: { documentKey: string; elementTypeKey: string; target: Extract<AppliedTarget, 'block-content' | 'block-settings'>; since?: number },
	timeout?: number,
): Promise<AppliedEvent> {
	return waitForApplied(
		page,
		{ documentKey: filter.documentKey, contentTypeKey: filter.elementTypeKey, target: filter.target, since: filter.since },
		timeout,
	);
}

/**
 * Opens one of the seeded landing pages (a full page load) and waits until the document and its inline blocks have
 * applied: the inline promoBanner (content and settings) and the inline callToAction (content) of `inlineBlocks`. Their
 * block workspaces start when the entries render, right after the document, and report the same element types (and
 * counts) as the modal, grid and nested blocks of the page. Take `pageNow()` after this, so a `since` wait for a modal
 * block cannot be satisfied by an inline block's pass.
 */
export async function openDocumentWithInlineBlocks(page: Page, documentKey: string): Promise<void> {
	await openDocument(page, documentKey);
	await waitForBlockApplied(page, { documentKey, elementTypeKey: ELEMENT_TYPES.promoBanner.key, target: 'block-content' });
	await waitForBlockApplied(page, { documentKey, elementTypeKey: ELEMENT_TYPES.promoBannerSettings.key, target: 'block-settings' });
	await waitForBlockApplied(page, { documentKey, elementTypeKey: ELEMENT_TYPES.callToAction.key, target: 'block-content' });
}

// ---- Block entries in the document workspace -------------------------------------------------------------------

/** The rendered property editor of a document property (umb-property sets `data-mark="property:<alias>"`). */
export function documentProperty(scope: Page | Locator, alias: string): Locator {
	return scope.locator(`umb-property[data-mark="property:${alias}"]`).first();
}

/**
 * The block entries of one element type in a Block List or Block Grid property (the entry reflects its content element
 * type alias as `data-content-element-type-alias`), in document order.
 */
export function blockEntries(scope: Page | Locator, blockPropertyAlias: string, elementTypeAlias: string): Locator {
	const selector = `[data-content-element-type-alias="${elementTypeAlias}"]`;
	return documentProperty(scope, blockPropertyAlias).locator(
		`umb-block-list-entry${selector}, umb-block-grid-entry${selector}`,
	);
}

export function blockEntry(scope: Page | Locator, blockPropertyAlias: string, elementTypeAlias: string): Locator {
	return blockEntries(scope, blockPropertyAlias, elementTypeAlias).first();
}

// ---- The block workspace in a modal --------------------------------------------------------------------------------

/** Every block workspace editor open in a modal, outermost first (one per nesting level). */
export function blockModalEditors(page: Page): Locator {
	return page.locator('umb-block-workspace-editor');
}

/** The block workspace (Umb.Workspace.Block) of one modal: Content and Settings views, Close, Create or Update. */
export class BlockModal {
	constructor(
		readonly page: Page,
		readonly root: Locator,
	) {}

	/** A rendered property of the active view. The block workspace does not render a property its view guard denies. */
	property(alias: string): Locator {
		return this.root.locator(`umb-property[data-mark="property:${alias}"]`);
	}

	async expectPropertyVisible(alias: string): Promise<void> {
		await expect(this.property(alias), `block property ${alias} should be visible`).toBeVisible();
	}

	/**
	 * Hidden: not rendered. Pair it with a visible property of the same view (or the applied event), so it does not
	 * pass only because the view has not rendered yet.
	 */
	async expectPropertyHidden(alias: string): Promise<void> {
		await expect(this.property(alias), `block property ${alias} should be hidden`).toHaveCount(0);
	}

	/** The Content or Settings view link in the modal's header (Settings only for a block type with settings). */
	viewLink(view: 'content' | 'settings'): Locator {
		const alias = view === 'settings' ? 'Umb.WorkspaceView.Block.Settings' : 'Umb.WorkspaceView.Block.Content';
		return this.root.locator(`uui-tab[data-mark="workspace:view-link:${alias}"]`);
	}

	async openView(view: 'content' | 'settings'): Promise<void> {
		const link = this.viewLink(view);
		await link.click();
		await expect(link).toHaveAttribute('active', '');
		await expect(this.page).toHaveURL(new RegExp(`/view/${view}(/|$)`));
	}

	/**
	 * The view is rendered and active. A block type without settings has one view, and the workspace then shows no view
	 * links at all; with both views the link of this one is active.
	 */
	async expectViewShown(view: 'content' | 'settings'): Promise<void> {
		await expect(this.root.locator('umb-block-workspace-view-edit').first()).toBeAttached();
		if ((await this.root.locator('uui-tab[data-mark^="workspace:view-link:"]').count()) > 0) {
			await expect(this.viewLink(view)).toHaveAttribute('active', '');
		} else {
			expect(view, 'a block with a single view shows its content').toBe('content');
		}
	}

	/** A tab of the element type (not a view link), by its name. */
	contentTab(name: string): Locator {
		return this.root.locator(`uui-tab[data-mark^="content-tab:"][label="${name}"]`);
	}

	/** All tabs of the element type rendered in the active view. */
	contentTabs(): Locator {
		return this.root.locator('uui-tab[data-mark^="content-tab:"]');
	}

	async openContentTab(name: string): Promise<void> {
		const tab = this.contentTab(name);
		await tab.click();
		await expect(tab).toHaveAttribute('active', '');
	}

	group(name: string): Locator {
		return this.root.locator(`[data-mark="property-group:${name}"]`);
	}

	/** Closes the modal without submitting (Close for an existing block, Cancel for a new one). */
	async close(): Promise<void> {
		const count = await blockModalEditors(this.page).count();
		await this.root.locator('umb-workspace-footer uui-button[label="Close"], umb-workspace-footer uui-button[label="Cancel"]').first().click();
		await expect(blockModalEditors(this.page)).toHaveCount(count - 1);
	}

	/** Submits the block (Create for a new block, Update for an existing one) and waits for the modal to close. */
	async submit(): Promise<void> {
		const count = await blockModalEditors(this.page).count();
		await this.root
			.locator(
				'uui-button[data-mark="workspace-action:Umb.WorkspaceAction.Block.SubmitCreate"], uui-button[data-mark="workspace-action:Umb.WorkspaceAction.Block.SubmitUpdate"]',
			)
			.first()
			.click();
		await expect(blockModalEditors(this.page)).toHaveCount(count - 1);
	}
}

/**
 * Opens a block in a modal with its Edit (content) or Settings block action, the way an editor does: hover the entry,
 * click the action. Resolves with the new, innermost modal once its workspace editor is attached.
 */
export async function openBlockModal(
	page: Page,
	entry: Locator,
	view: 'content' | 'settings' = 'content',
): Promise<BlockModal> {
	const editors = blockModalEditors(page);
	const before = await editors.count();
	const action = view === 'settings' ? 'Umb.BlockAction.EditSettings' : 'Umb.BlockAction.EditContent';
	await entry.scrollIntoViewIfNeeded();
	await entry.hover();
	await entry.locator(`uui-button[data-mark="block-action:${action}"]`).first().click();
	await expect(editors).toHaveCount(before + 1);
	const modal = new BlockModal(page, editors.nth(before));
	await modal.expectViewShown(view);
	return modal;
}

// ---- Inline editing ------------------------------------------------------------------------------------------------

/**
 * Expands an inline-edited Block List entry (umb-inline-list-block) and resolves with its inline editor. The inline
 * block's workspace, and with it the package's block context, exists as soon as the entry renders, before it is
 * expanded.
 */
export async function expandInlineBlock(entry: Locator): Promise<Locator> {
	const inline = entry.locator('umb-inline-list-block').first();
	const editor = inline.locator('umb-block-workspace-view-edit-content-no-router').first();
	if ((await editor.count()) === 0) {
		await inline.locator('button#open-part').first().click();
	}
	await expect(editor).toHaveCount(1);
	return editor;
}

// ---- Creating a block ----------------------------------------------------------------------------------------------

/**
 * Adds a block to a Block List property through its create button and the block catalogue, and resolves with the
 * block workspace modal that opens for the new block (its Content view).
 */
export async function addBlockFromCatalogue(page: Page, blockPropertyAlias: string, blockName: string): Promise<BlockModal> {
	const editors = blockModalEditors(page);
	const before = await editors.count();
	const property = documentProperty(page, blockPropertyAlias);
	await property.scrollIntoViewIfNeeded();
	await property.locator('uui-button-group uui-button[look="placeholder"]').first().click();
	const card = page.locator(`umb-block-catalogue-modal uui-card-block-type[name="${blockName}"]`);
	await card.click();
	await expect(editors).toHaveCount(before + 1);
	const modal = new BlockModal(page, editors.nth(before));
	await modal.expectViewShown('content');
	return modal;
}

/**
 * Adds a block to an inline-edited Block List property through its create button and the block catalogue. With inline
 * editing Umbraco creates the block in place, without a block workspace modal; resolves with the new entry.
 */
export async function addInlineBlockFromCatalogue(
	page: Page,
	blockPropertyAlias: string,
	elementTypeAlias: string,
	blockName: string,
): Promise<Locator> {
	const entries = blockEntries(page, blockPropertyAlias, elementTypeAlias);
	const before = await entries.count();
	const property = documentProperty(page, blockPropertyAlias);
	await property.scrollIntoViewIfNeeded();
	await property.locator('uui-button-group uui-button[look="placeholder"]').first().click();
	await page.locator(`umb-block-catalogue-modal uui-card-block-type[name="${blockName}"]`).click();
	await expect(page.locator('umb-block-catalogue-modal')).toHaveCount(0);
	await expect(entries).toHaveCount(before + 1);
	return entries.nth(before);
}

// ---- Stored block values -------------------------------------------------------------------------------------------

interface BlockItemData {
	key: string;
	contentTypeKey: string;
	values: Array<{ alias: string; value: unknown }>;
}

export interface BlockValue {
	contentData: BlockItemData[];
	settingsData: BlockItemData[];
}

/** The block value of a document property, as the server stored it. */
export function blockValueOf(document: DocumentModel, blockPropertyAlias: string): BlockValue {
	const value = document.values.find((v) => v.alias === blockPropertyAlias && v.culture === null && v.segment === null)?.value;
	if (!value || typeof value !== 'object') throw new Error(`${blockPropertyAlias} holds no block value`);
	return value as BlockValue;
}

/** A value of the first content item of an element type in a block value. */
export function blockContentValue(block: BlockValue, elementTypeKey: string, alias: string): unknown {
	const item = block.contentData.find((data) => data.contentTypeKey === elementTypeKey);
	if (!item) throw new Error(`no content item of element type ${elementTypeKey}`);
	return item.values.find((v) => v.alias === alias)?.value;
}

/** A value of the first settings item of an element type in a block value. */
export function blockSettingsValue(block: BlockValue, elementTypeKey: string, alias: string): unknown {
	const item = block.settingsData.find((data) => data.contentTypeKey === elementTypeKey);
	if (!item) throw new Error(`no settings item of element type ${elementTypeKey}`);
	return item.values.find((v) => v.alias === alias)?.value;
}
