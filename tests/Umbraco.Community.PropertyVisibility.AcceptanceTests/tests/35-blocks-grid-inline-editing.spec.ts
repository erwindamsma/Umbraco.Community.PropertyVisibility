import {
	blockEntry,
	ELEMENT_TYPES,
	openBlockModal,
	openDocumentWithInlineBlocks,
	pageNow,
	waitForBlockApplied,
} from '../support/blocks.js';
import { keysOfAliases } from '../support/expected.js';
import { expect, test } from '../support/fixtures.js';
import { getContentTypeStructure, resolveSample } from '../support/management-api.js';
import { TemporarySchemaChanges } from '../support/schema-changes.js';
import { hidesExactly, pollHiddenFields, TemporaryRulesFile } from '../support/site-config.js';

// Changes the running site's rules source: no other test may run while the rules file exists.
test.describe.configure({ mode: 'serial' });

/** The rules file replaces the appsettings rules within this long (250 ms debounce plus the watcher). */
const RELOAD_TIMEOUT_MS = 5_000;

/**
 * A documented limitation (docs/configuration.md, Blocks): with `inlineEditing` on for a Block Grid block type, Umbraco
 * renders the element type's first property inline without the property view guard, so a rule cannot hide it there.
 * The block's modal still hides it, with everything else configured.
 */
test.describe('Block Grid inline editing (landingPage.gridBlocks)', () => {
	test('Corporate landing, with heading and overlayColour hidden: heading is shown inline in the grid, the modal hides both', async ({ page }) => {
		const keys = await resolveSample(page);
		const promoBanner = await getContentTypeStructure(page, ELEMENT_TYPES.promoBanner.key);
		const query = { documentKey: keys.corporateLanding, contentTypeKey: ELEMENT_TYPES.promoBanner.key };
		const appSettingsRules = { propertyKeys: keysOfAliases(promoBanner, ['overlayColour']), containerKeys: [] };
		const fileRules = { propertyKeys: keysOfAliases(promoBanner, ['heading', 'overlayColour']), containerKeys: [] };

		const changes = await TemporarySchemaChanges.begin(page);
		const rulesFile = TemporaryRulesFile.begin();
		try {
			await changes.enableGridInlineEditing();
			rulesFile.write({
				$schema: './PropertyVisibility.config-schema.json',
				Sites: {
					corporate: {
						RootNodeKey: keys.corporateSite,
						ContentTypes: { promoBanner: { Properties: ['heading', 'overlayColour'] } },
					},
				},
			});
			await pollHiddenFields(page, query, (body) => hidesExactly(body, fileRules), 'heading and overlayColour hidden by the rules file', RELOAD_TIMEOUT_MS);

			await openDocumentWithInlineBlocks(page, keys.corporateLanding);
			const entry = blockEntry(page, 'gridBlocks', ELEMENT_TYPES.promoBanner.alias);
			const inline = entry.locator('umb-block-grid-block-inline');
			await inline.scrollIntoViewIfNeeded();
			await expect(inline.locator('umb-property[data-mark="property:heading"]'), 'heading rendered inline in the grid').toBeVisible();

			const since = await pageNow(page);
			const modal = await openBlockModal(page, entry);
			const applied = await waitForBlockApplied(page, {
				documentKey: keys.corporateLanding,
				elementTypeKey: ELEMENT_TYPES.promoBanner.key,
				target: 'block-content',
				since,
			});
			expect(applied.propertyCount).toBe(2);
			await modal.expectPropertyVisible('image');
			await modal.expectPropertyHidden('heading');
			await modal.expectPropertyHidden('overlayColour');
			await modal.close();
		} finally {
			rulesFile.restore();
			await changes.restore();
		}

		// The appsettings rules again, for the specs after this one.
		await pollHiddenFields(page, query, (body) => hidesExactly(body, appSettingsRules), 'the appsettings rules again', RELOAD_TIMEOUT_MS);
	});
});
