import { expect, type Locator, type Page, type Response } from '@playwright/test';
import { settleApplied, waitForAppliedResponse } from './applied.js';
import { ADMIN_EMAIL, ADMIN_PASSWORD, DIRECT_CALL_HEADER } from './env.js';
import { getDocument } from './management-api.js';

export { installAppliedRecorder, waitForApplied, waitForAppliedResponse, type AppliedEvent, type AppliedFilter } from './applied.js';

/**
 * Logs in through the backoffice login page. Each test logs in on its own, in its own browser context: the backoffice
 * rotates the refresh-token cookie on every page load, so a storage state saved once and reused by later contexts
 * would present an already-redeemed refresh token.
 */
export async function loginAsAdmin(page: Page): Promise<void> {
	await page.goto('/umbraco');
	await page.locator('#username-input').fill(ADMIN_EMAIL);
	await page.locator('#password-input').fill(ADMIN_PASSWORD);
	await page.locator('#umb-login-button').click();
	await page.waitForURL(/\/umbraco\/section\//, { timeout: 60_000 });
	// The backoffice shell (header sections) is up once the session is established.
	await expect(page.locator('uui-tab[data-mark="section-link:Umb.Section.Content"]')).toBeVisible();
}

/**
 * Resolves on the package's hidden-fields response for a document key, optionally narrowed to a content type (blocks
 * request their element types under the same document key) and a parent key.
 */
export function waitForHiddenFields(
	page: Page,
	documentKey: string,
	filter: { contentTypeKey?: string; parentKey?: string } = {},
): Promise<Response> {
	return page.waitForResponse((response) => {
		const url = new URL(response.url());
		if (!/\/umbraco\/property-visibility\/v1\/hiddenfields$/i.test(url.pathname)) return false;
		if (url.searchParams.get('documentKey')?.toLowerCase() !== documentKey.toLowerCase()) return false;
		if (filter.contentTypeKey !== undefined && url.searchParams.get('contentTypeKey')?.toLowerCase() !== filter.contentTypeKey.toLowerCase()) {
			return false;
		}
		if (filter.parentKey !== undefined && url.searchParams.get('parentKey') !== filter.parentKey) return false;
		return true;
	});
}

/** The document type of a document, for waiting on the document's own hidden-fields response. */
async function documentTypeOf(page: Page, documentKey: string): Promise<string> {
	return (await getDocument(page, documentKey)).documentType.id;
}

/**
 * Records the URL of every hidden-fields request the page sends from now on, in order. Start it before the action,
 * so a request that goes out early (for example one without the parent key) is not missed.
 */
export function recordHiddenFieldsRequests(page: Page): URL[] {
	const requests: URL[] = [];
	page.on('request', (request) => {
		const url = new URL(request.url());
		if (/\/umbraco\/property-visibility\/v1\/hiddenfields$/i.test(url.pathname)) requests.push(url);
	});
	return requests;
}

/**
 * The package's hidden-fields requests of a page from now on, keyed by document and content type (the suite's own
 * direct API calls, which carry DIRECT_CALL_HEADER, are left out). Start it before the navigation whose requests count.
 */
export class HiddenFieldsRequestLog {
	readonly requests: Array<{ documentKey: string; contentTypeKey: string; parentKey: string | null }> = [];

	private constructor() {}

	static start(page: Page): HiddenFieldsRequestLog {
		const log = new HiddenFieldsRequestLog();
		page.on('request', (request) => {
			const url = new URL(request.url());
			if (!/\/umbraco\/property-visibility\/v1\/hiddenfields$/i.test(url.pathname)) return;
			if (request.headers()[DIRECT_CALL_HEADER] !== undefined) return;
			log.requests.push({
				documentKey: (url.searchParams.get('documentKey') ?? '').toLowerCase(),
				contentTypeKey: (url.searchParams.get('contentTypeKey') ?? '').toLowerCase(),
				parentKey: url.searchParams.get('parentKey'),
			});
		});
		return log;
	}

	/** A position in the log, for counting only what comes after it. */
	mark(): number {
		return this.requests.length;
	}

	/** Requests for a document and content type, from a mark (default: the start) on. */
	count(documentKey: string, contentTypeKey: string, from = 0): number {
		return this.requests
			.slice(from)
			.filter((r) => r.documentKey === documentKey.toLowerCase() && r.contentTypeKey === contentTypeKey.toLowerCase()).length;
	}

	/** `documentKey|contentTypeKey` -> number of requests, from a mark (default: the start) on. */
	byPair(from = 0): Record<string, number> {
		const pairs: Record<string, number> = {};
		for (const r of this.requests.slice(from)) {
			const pair = `${r.documentKey}|${r.contentTypeKey}`;
			pairs[pair] = (pairs[pair] ?? 0) + 1;
		}
		return pairs;
	}
}

/**
 * Resolves on the hidden-fields response of a not-yet-saved document created at the content root: sent without a
 * parent key, for the given document type (the scaffold key is unknown up front). The suite's own direct API calls do
 * not count.
 */
export function waitForHiddenFieldsAtRoot(page: Page, contentTypeKey: string): Promise<Response> {
	return page.waitForResponse((response) => {
		const url = new URL(response.url());
		return (
			/\/umbraco\/property-visibility\/v1\/hiddenfields$/i.test(url.pathname) &&
			url.searchParams.get('contentTypeKey')?.toLowerCase() === contentTypeKey.toLowerCase() &&
			url.searchParams.get('parentKey') === null &&
			response.request().headers()[DIRECT_CALL_HEADER] === undefined
		);
	});
}

/** Resolves on the hidden-fields response of a not-yet-saved document (its scaffold key is unknown up front). */
export function waitForHiddenFieldsWithParent(page: Page, parentKey: string): Promise<Response> {
	return page.waitForResponse((response) => {
		const url = new URL(response.url());
		return (
			/\/umbraco\/property-visibility\/v1\/hiddenfields$/i.test(url.pathname) &&
			url.searchParams.get('parentKey') === parentKey
		);
	});
}

/**
 * The applier runs after the response resolves (guard rules, then removeContainer for each hidden tab or group) and
 * dispatches the package's applied event when the pass is complete. Waits until no hidden-fields request is in flight
 * and the newest responses of the page are applied, then two animation frames, so assertions that something is still
 * visible run against the finished pass. It does not wait for a request that has not gone out yet (support/applied.ts).
 */
export async function settle(page: Page, timeout?: number): Promise<void> {
	await settleApplied(page, timeout);
}

/**
 * Opens a document by URL (a full page load) and waits until the document workspace has applied the response for the
 * document's own type. The document type is looked up when not given.
 */
export async function openDocument(page: Page, documentKey: string, contentTypeKey?: string): Promise<void> {
	const documentType = contentTypeKey ?? (await documentTypeOf(page, documentKey));
	const response = waitForHiddenFields(page, documentKey, { contentTypeKey: documentType });
	await page.goto(`/umbraco/section/content/workspace/document/edit/${documentKey}`);
	const result = await response;
	expect(result.status()).toBe(200);
	await waitForAppliedResponse(page, result, { target: 'document' });
}

/** A content tab button in the document workspace (tabs of the document type, not the workspace views). */
export function contentTab(page: Page, name: string): Locator {
	return page.locator(`uui-tab[data-mark^="content-tab:"][label="${name}"]`);
}

export async function openContentTab(page: Page, name: string): Promise<void> {
	const tab = contentTab(page, name);
	await tab.click();
	await expect(tab).toHaveAttribute('active', '');
}

/** A property group box in the active tab. */
export function propertyGroup(page: Page, name: string): Locator {
	return page.locator(`[data-mark="property-group:${name}"]`);
}

/**
 * The rendered editor of a property in the active tab. A property hidden by a view guard keeps its
 * umb-content-workspace-property element but renders no umb-property-type-based-property inside it.
 */
export function renderedProperty(page: Page, alias: string): Locator {
	return page.locator(`umb-content-workspace-property[alias="${alias}"] umb-property-type-based-property`);
}

/** The workspace property slot, rendered or not; present for every property of the active tab. */
export function propertySlot(page: Page, alias: string): Locator {
	return page.locator(`umb-content-workspace-property[alias="${alias}"]`);
}

export async function expectPropertyVisible(page: Page, alias: string): Promise<void> {
	await expect(renderedProperty(page, alias), `property ${alias} should be visible`).toBeVisible();
}

/** Hidden, while the tab that holds it is rendered (its slot exists but renders nothing). */
export async function expectPropertyHidden(page: Page, alias: string): Promise<void> {
	await expect(propertySlot(page, alias), `property ${alias} should have a slot in the active tab`).toHaveCount(1);
	await expect(renderedProperty(page, alias), `property ${alias} should be hidden`).toHaveCount(0);
}

// ---- Content tree --------------------------------------------------------------------------------------------------

export function treeItem(page: Page, name: string): Locator {
	return page.locator(`uui-menu-item[label="${name}"]`);
}

export async function expandTreeItem(page: Page, name: string): Promise<void> {
	const item = treeItem(page, name);
	await expect(item).toBeVisible();
	if ((await item.getAttribute('show-children')) === null) {
		await item.locator('#caret-button').first().click();
	}
	await expect(item).toHaveAttribute('show-children', '');
}

/**
 * Starts creating a document under a tree item with the tree's create action ("+" next to the item, then the
 * document type in the dialog). Starts from the Content section dashboard, where the tree item's hover actions
 * are not covered by an open workspace.
 */
export async function startCreateUnder(page: Page, parentName: string, documentTypeName: string): Promise<void> {
	await page.goto('/umbraco/section/content');
	const parent = treeItem(page, parentName);
	await parent.hover();
	await parent.locator('umb-entity-actions-bundle uui-button[label^="Create"]').first().click();
	await page.locator('uui-ref-node-document-type').filter({ hasText: documentTypeName }).first().click();
	await page.waitForURL(/\/workspace\/document\/create\/parent\/document\//);
}

/**
 * Starts creating a document at the content root with the Create action of the Content tree's root (the "+" next to
 * the "Content" heading), then the document type in the dialog. Only types allowed at the root are offered.
 */
export async function startCreateAtRoot(page: Page, documentTypeName: string): Promise<void> {
	await page.goto('/umbraco/section/content');
	const sidebar = page.locator('umb-section-sidebar-menu-with-entity-actions[data-mark="section-sidebar:Umb.SidebarMenu.Content"]');
	await sidebar.locator('uui-button[data-mark="entity-action:Umb.EntityAction.Document.Create"]').first().click();
	await page.locator('uui-ref-node-document-type').filter({ hasText: documentTypeName }).first().click();
	await page.waitForURL(/\/workspace\/document\/create\/parent\/document-root\//);
}

/**
 * Opens a document from the content tree (client-side navigation, no page reload) and waits until the document
 * workspace has applied the response for the document's own type. The document type is looked up when not given.
 */
export async function clickTreeItem(page: Page, name: string, documentKey: string, contentTypeKey?: string): Promise<void> {
	const documentType = contentTypeKey ?? (await documentTypeOf(page, documentKey));
	const response = waitForHiddenFields(page, documentKey, { contentTypeKey: documentType });
	await treeItem(page, name).locator('#label-button').first().click();
	await page.waitForURL(new RegExp(`/workspace/document/edit/${documentKey}`));
	const result = await response;
	expect(result.status()).toBe(200);
	// `since` is when this response arrived, so an earlier visit to the same document in this page does not count, nor
	// does a pass of the previous response for the same document and type.
	await waitForAppliedResponse(page, result, { target: 'document' });
}

// ---- Workspace actions -------------------------------------------------------------------------------------------

export function workspaceAction(page: Page, alias: 'Save' | 'SaveAndPublish'): Locator {
	return page.locator(`uui-button[data-mark="workspace-action:Umb.WorkspaceAction.Document.${alias}"]`);
}

/** Clicks Save and waits for the create (POST) or update (PUT) of the document. */
export async function save(page: Page): Promise<Response> {
	const response = page.waitForResponse(
		(r) =>
			/\/umbraco\/management\/api\/v1\/document(\/[0-9a-f-]{36})?$/.test(new URL(r.url()).pathname) &&
			['POST', 'PUT'].includes(r.request().method()),
	);
	await workspaceAction(page, 'Save').click();
	const result = await response;
	expect(result.ok(), `save returned ${result.status()}`).toBeTruthy();
	return result;
}

/**
 * Clicks Save and publish (invariant documents publish without a dialog) and waits until the workspace has finished
 * with it: the update-and-publish request, then Umbraco's "Document published" notification, which the workspace shows
 * after it has read the published document back into the editor.
 */
export async function saveAndPublish(page: Page): Promise<Response> {
	const response = page.waitForResponse(
		(r) =>
			/\/umbraco\/management\/api\/v1\/document\/[0-9a-f-]{36}\/(update-and-publish|publish)$/.test(
				new URL(r.url()).pathname,
			) && r.request().method() === 'PUT',
	);
	// Notifications already on screen (an earlier publish) are marked, so only the one for this click counts.
	await page.locator('uui-toast-notification').evaluateAll((toasts) => toasts.forEach((toast) => toast.setAttribute('data-pv-seen', '')));
	await workspaceAction(page, 'SaveAndPublish').click();
	const result = await response;
	expect(result.ok(), `save and publish returned ${result.status()}`).toBeTruthy();
	await expect(
		page.locator('uui-toast-notification:not([data-pv-seen])').filter({ hasText: 'Document published' }),
		'Umbraco\'s "Document published" notification',
	).toHaveCount(1);
	return result;
}
