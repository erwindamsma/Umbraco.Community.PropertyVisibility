import {
	BLOCK_WORKSPACE_CONTEXT_ALIAS,
	BUNDLE_ALIAS,
	DOCUMENT_WORKSPACE_CONTEXT_ALIAS,
	ENTRY_POINT_ALIAS,
	PACKAGE_ID,
	SITE_BOUNDARY_WORKSPACE_CONTEXT_ALIAS,
} from '../support/env.js';
import { managementApi } from '../support/management-api.js';
import { expect, test } from '../support/fixtures.js';

interface PackageManifest {
	id: string | null;
	name: string;
	version: string;
	extensions: Array<{ type: string; alias: string; js?: string }>;
}

test.describe('The package is registered', () => {
	test('the manifest endpoint lists the package and its bundle', async ({ page }) => {
		const manifests = await managementApi<PackageManifest[]>(page, '/manifest/manifest');
		const manifest = manifests.find((m) => m.id === PACKAGE_ID);

		expect(manifest, 'package manifest').toBeDefined();
		expect(manifest!.extensions).toEqual([
			expect.objectContaining({
				type: 'bundle',
				alias: BUNDLE_ALIAS,
				js: '/App_Plugins/UmbracoCommunityPropertyVisibility/property-visibility.js',
			}),
		]);
	});

	test('Extension Insights lists the bundle, the entry point and the document, block and site boundary workspace contexts', async ({ page }) => {
		await page.goto('/umbraco/section/settings/workspace/extension-root');
		const filter = page.locator('umb-extension-root-workspace uui-input input, uui-input[placeholder^="Type to filter"] input').first();
		await filter.fill('Umbraco.Community.PropertyVisibility');

		const rows = page.locator('uui-table-row');
		for (const alias of [
			BUNDLE_ALIAS,
			ENTRY_POINT_ALIAS,
			DOCUMENT_WORKSPACE_CONTEXT_ALIAS,
			BLOCK_WORKSPACE_CONTEXT_ALIAS,
			SITE_BOUNDARY_WORKSPACE_CONTEXT_ALIAS,
		]) {
			await expect(rows.filter({ hasText: alias }), alias).toHaveCount(1);
		}
		await expect(rows.filter({ hasText: DOCUMENT_WORKSPACE_CONTEXT_ALIAS })).toContainText('workspaceContext');
		// The block workspace context (Umb.Workspace.Block).
		await expect(rows.filter({ hasText: BLOCK_WORKSPACE_CONTEXT_ALIAS })).toContainText('workspaceContext');
		// The boundary that keeps a document's site from blocks in media, members and document blueprints.
		await expect(rows.filter({ hasText: SITE_BOUNDARY_WORKSPACE_CONTEXT_ALIAS })).toContainText('workspaceContext');
	});
});
