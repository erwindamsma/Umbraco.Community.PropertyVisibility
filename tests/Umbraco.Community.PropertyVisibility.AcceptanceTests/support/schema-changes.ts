import { chromium, type Page } from '@playwright/test';
import { existsSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { loginAsAdmin } from './backoffice.js';
import { ELEMENT_TYPES } from './blocks.js';
import { BASE_URL, SAMPLE } from './env.js';
import { backofficeFetch, deleteDocument, findDocumentByPath, managementApi, type DocumentValue } from './management-api.js';
import { RUN_STATE_DIR } from './site-config.js';

const API = '/umbraco/management/api/v1';

/** Seeded types and data types (uSync/seed in the test site) the changes build on. */
const SEED = {
	siteType: '16a50c18-63e5-5204-8534-5bdf59dff45f',
	memberType: 'd59be02f-1df9-4228-aa1e-01917d806cda',
	blocksModal: 'a17a350a-358c-5476-ad6b-d60ded2dc5d9',
	blocksGrid: 'ed9f4df6-f4f5-5466-96dc-ce993603d7a1',
	richTextEditor: 'ca90c950-0aff-4e72-b976-a30b1ac57dad',
	memberPicker: '1ea2e01f-ebd8-4ce1-8d71-6b1149e63548',
} as const;

/**
 * Keys of everything TemporarySchemaChanges creates. They are fixed, so removeSchemaChanges() finds what an interrupted
 * test left behind without having seen it.
 */
const SCHEMA_KEYS = {
	richTextDataType: 'f08a7027-df0b-4cbe-977c-8d7d0bf019ba',
	singleBlockDataType: 'e4f5ff31-d55c-4428-9cc8-4f7cc280e71c',
	singleBlockInlineDataType: 'd6c9e7ae-72d0-4b10-9f48-c1558d027c1a',
	blockEditorsPageType: '96500ab3-ab0b-42e7-9673-017df55b91c0',
	blockEditorsPageGroup: '319b59e1-c282-4028-90ea-64f1c6e364dc',
	corporatePage: 'ad88b5d2-1f16-44e1-87f7-0c049e53d985',
	campaignPage: '68161e81-5142-453b-a2ae-910de8b07b22',
	memberBlocksGroup: 'e0f783f3-dfeb-4119-9d40-fa1f2b52dc6c',
	memberBlocksProperty: '14901d0c-9e06-44d0-986d-3fc671521490',
	member: '94ca8184-39e1-4d70-addb-643247757031',
	blueprint: 'c1b23953-e983-49ca-b8dc-4f353bc23b71',
} as const;

/** Names of the documents and the member TemporarySchemaChanges creates. */
export const SCHEMA_NAMES = {
	corporatePage: 'Corporate block editors',
	campaignPage: 'Campaign block editors',
	member: 'Acceptance member',
	blueprint: 'Acceptance blueprint',
} as const;

/** The properties of the `blockEditorsPage` document type, one block editor each, in one group. */
const PAGE_PROPERTIES = [
	{ key: 'e7d83292-8c76-431b-90ec-cdf43632f032', alias: 'mainBlocks', name: 'Main blocks', dataType: SEED.blocksModal },
	{ key: 'b5f48c6b-1fed-4feb-b13e-80e75938e16d', alias: 'richText', name: 'Rich text', dataType: SCHEMA_KEYS.richTextDataType },
	{ key: '89defe21-33ef-4f59-836d-413f8ce6766a', alias: 'singleBlock', name: 'Single block', dataType: SCHEMA_KEYS.singleBlockDataType },
	{
		key: '81148045-0e53-48fe-863d-fe572259d019',
		alias: 'singleBlockInline',
		name: 'Single block inline',
		dataType: SCHEMA_KEYS.singleBlockInlineDataType,
	},
	{ key: '142398e9-cf37-413a-b379-358bb61818ca', alias: 'featuredMember', name: 'Featured member', dataType: SEED.memberPicker },
] as const;

/** The promoBanner block (with its settings) every block editor of the changes allows. */
const PROMO_BANNER_BLOCK = {
	contentElementTypeKey: ELEMENT_TYPES.promoBanner.key,
	settingsElementTypeKey: ELEMENT_TYPES.promoBannerSettings.key,
};

/** Present while the database holds changes of TemporarySchemaChanges, so a later run knows to remove them. */
const SCHEMA_CHANGES_MARKER = join(RUN_STATE_DIR, 'schema-changed-by-the-suite');

interface DataTypeModel {
	name: string;
	editorAlias: string;
	editorUiAlias: string;
	values: Array<{ alias: string; value: unknown }>;
}

interface TypeModel {
	id?: string;
	properties: Array<{ id: string }>;
	containers: Array<{ id: string }>;
	allowedDocumentTypes?: Array<{ documentType: { id: string }; sortOrder: number }>;
}

async function call(page: Page, method: string, path: string, body?: unknown): Promise<void> {
	const { status, body: result } = await backofficeFetch(page, `${API}${path}`, { method, body });
	if (status !== 200 && status !== 201) {
		throw new Error(`${method} ${API}${path} returned ${status}: ${JSON.stringify(result)}`);
	}
}

/**
 * Whether an entity exists, through its item endpoint (an empty list when it does not). A GET of the entity itself would
 * answer 404, which the browser logs as a console error that spec 08 counts.
 */
async function exists(page: Page, entity: string, key: string): Promise<boolean> {
	return (await managementApi<unknown[]>(page, `/item/${entity}?id=${key}`)).length > 0;
}

/**
 * DELETE of an entity that may not exist. Looked up first: Umbraco 17.6.2 answers the DELETE of a member that does not
 * exist with 500 (operation status Unknown), not 404.
 */
async function deleteIfExists(page: Page, entity: string, key: string): Promise<void> {
	if (await exists(page, entity, key)) await call(page, 'DELETE', `/${entity}/${key}`);
}

/** PUTs a document or member type back as read, with the changes applied (the response's own id left out). */
async function putType(page: Page, path: string, type: TypeModel): Promise<void> {
	const body: Record<string, unknown> = { ...type };
	delete body.id;
	await call(page, 'PUT', path, body);
}

async function putDataType(page: Page, key: string, dataType: DataTypeModel): Promise<void> {
	const { name, editorAlias, editorUiAlias, values } = dataType;
	await call(page, 'PUT', `/data-type/${key}`, { name, editorAlias, editorUiAlias, values });
}

/** The `blocks` entry of the seeded Blocks (grid) data type for promoBanner. */
function gridPromoBanner(grid: DataTypeModel): Record<string, unknown> {
	const blocks = grid.values.find((value) => value.alias === 'blocks')?.value as Array<Record<string, unknown>> | undefined;
	const block = blocks?.find((entry) => entry.contentElementTypeKey === ELEMENT_TYPES.promoBanner.key);
	if (!block) throw new Error(`The Blocks (grid) data type ${SEED.blocksGrid} has no promoBanner block.`);
	return block;
}

/**
 * Removes everything TemporarySchemaChanges can create, by its fixed keys, whether or not it exists: the blueprint, the
 * member, the member type's block property, the two documents, the `blockEditorsPage` type (and its place under
 * `site`), the three data types, and `inlineEditing` on the grid's promoBanner block. The seeded types end up as
 * seeded.
 */
async function removeSchemaChanges(page: Page): Promise<void> {
	await deleteIfExists(page, 'document-blueprint', SCHEMA_KEYS.blueprint);
	await deleteIfExists(page, 'member', SCHEMA_KEYS.member);

	const memberTypePath = `/member-type/${SEED.memberType}`;
	const memberType = await managementApi<TypeModel>(page, memberTypePath);
	if (
		memberType.properties.some((property) => property.id === SCHEMA_KEYS.memberBlocksProperty) ||
		memberType.containers.some((container) => container.id === SCHEMA_KEYS.memberBlocksGroup)
	) {
		await putType(page, memberTypePath, {
			...memberType,
			properties: memberType.properties.filter((property) => property.id !== SCHEMA_KEYS.memberBlocksProperty),
			containers: memberType.containers.filter((container) => container.id !== SCHEMA_KEYS.memberBlocksGroup),
		});
	}

	for (const key of [SCHEMA_KEYS.corporatePage, SCHEMA_KEYS.campaignPage]) {
		if (await exists(page, 'document', key)) await deleteDocument(page, key);
	}
	const siteTypePath = `/document-type/${SEED.siteType}`;
	const siteType = await managementApi<TypeModel>(page, siteTypePath);
	const allowed = siteType.allowedDocumentTypes ?? [];
	if (allowed.some((entry) => entry.documentType.id === SCHEMA_KEYS.blockEditorsPageType)) {
		await putType(page, siteTypePath, {
			...siteType,
			allowedDocumentTypes: allowed.filter((entry) => entry.documentType.id !== SCHEMA_KEYS.blockEditorsPageType),
		});
	}
	await deleteIfExists(page, 'document-type', SCHEMA_KEYS.blockEditorsPageType);
	for (const key of [SCHEMA_KEYS.richTextDataType, SCHEMA_KEYS.singleBlockDataType, SCHEMA_KEYS.singleBlockInlineDataType]) {
		await deleteIfExists(page, 'data-type', key);
	}

	const grid = await managementApi<DataTypeModel>(page, `/data-type/${SEED.blocksGrid}`);
	const promoBanner = gridPromoBanner(grid);
	if ('inlineEditing' in promoBanner) {
		delete promoBanner.inlineEditing;
		await putDataType(page, SEED.blocksGrid, grid);
	}
}

/**
 * Changes to the test site's document types, member type and data types for one test, through the Management API: for
 * the block editors the seed does not use (rich text, Single Block), members and blueprints. The other specs must not
 * see them, so `restore()` removes them again and belongs in a `finally` block; `begin()` first removes what an
 * interrupted test left behind. A marker file in `.run-state/` stays while changes exist, so the global setup and
 * teardown remove them when a test body did not get to its `finally` (restoreSchemaChanges). The test site does not
 * export on save (uSync `ExportOnSave: None`), so the seed files are never touched.
 */
export class TemporarySchemaChanges {
	private constructor(private readonly page: Page) {}

	static async begin(page: Page): Promise<TemporarySchemaChanges> {
		await removeSchemaChanges(page);
		mkdirSync(RUN_STATE_DIR, { recursive: true });
		writeFileSync(SCHEMA_CHANGES_MARKER, '');
		return new TemporarySchemaChanges(page);
	}

	/**
	 * A `blockEditorsPage` document type (allowed under `site`) with one property per block editor: `mainBlocks` (the
	 * seeded Blocks (modal) data type), `richText` (a Tiptap rich text editor with the block picker), `singleBlock` and
	 * `singleBlockInline` (Single Block, in a modal and inline), each allowing promoBanner with its settings, and
	 * `featuredMember` (a member picker). Creates one such document, saved and not published, under each site: "Corporate
	 * block editors" (with `corporateValues`) and "Campaign block editors". Resolves with their keys.
	 */
	async addBlockEditorsPages(corporateValues: DocumentValue[] = []): Promise<{ corporate: string; campaign: string }> {
		const page = this.page;
		const richText = await managementApi<DataTypeModel>(page, `/data-type/${SEED.richTextEditor}`);
		const values = richText.values.filter((value) => value.alias !== 'blocks' && value.alias !== 'toolbar');
		const toolbar = structuredClone(richText.values.find((value) => value.alias === 'toolbar')?.value) as string[][][] | undefined;
		if (!toolbar?.[0]) throw new Error(`The rich text data type ${SEED.richTextEditor} has no toolbar.`);
		toolbar[0].push(['Umb.Tiptap.Toolbar.BlockPicker']);
		await call(page, 'POST', '/data-type', {
			id: SCHEMA_KEYS.richTextDataType,
			parent: null,
			name: 'Acceptance rich text with blocks',
			editorAlias: richText.editorAlias,
			editorUiAlias: richText.editorUiAlias,
			values: [...values, { alias: 'toolbar', value: toolbar }, { alias: 'blocks', value: [PROMO_BANNER_BLOCK] }],
		});
		for (const [key, inline] of [
			[SCHEMA_KEYS.singleBlockDataType, false],
			[SCHEMA_KEYS.singleBlockInlineDataType, true],
		] as const) {
			await call(page, 'POST', '/data-type', {
				id: key,
				parent: null,
				name: inline ? 'Acceptance single block inline' : 'Acceptance single block',
				editorAlias: 'Umbraco.SingleBlock',
				editorUiAlias: 'Umb.PropertyEditorUi.BlockSingle',
				values: [
					{ alias: 'blocks', value: [PROMO_BANNER_BLOCK] },
					{ alias: 'useInlineEditingAsDefault', value: inline },
				],
			});
		}

		await call(page, 'POST', '/document-type', {
			id: SCHEMA_KEYS.blockEditorsPageType,
			parent: null,
			alias: 'blockEditorsPage',
			name: 'Block editors page',
			description: 'Created and removed by the acceptance suite (support/schema-changes.ts).',
			icon: 'icon-science',
			allowedAsRoot: false,
			variesByCulture: false,
			variesBySegment: false,
			collection: null,
			isElement: false,
			containers: [{ id: SCHEMA_KEYS.blockEditorsPageGroup, parent: null, name: 'Blocks', type: 'Group', sortOrder: 0 }],
			properties: PAGE_PROPERTIES.map((property, index) => ({
				id: property.key,
				container: { id: SCHEMA_KEYS.blockEditorsPageGroup },
				sortOrder: index,
				alias: property.alias,
				name: property.name,
				description: null,
				dataType: { id: property.dataType },
				variesByCulture: false,
				variesBySegment: false,
				validation: { mandatory: false, mandatoryMessage: null, regEx: null, regExMessage: null },
				appearance: { labelOnTop: false },
			})),
			allowedTemplates: [],
			defaultTemplate: null,
			cleanup: { preventCleanup: false },
			allowedDocumentTypes: [],
			compositions: [],
		});
		const siteTypePath = `/document-type/${SEED.siteType}`;
		const siteType = await managementApi<TypeModel>(page, siteTypePath);
		const allowed = siteType.allowedDocumentTypes ?? [];
		await putType(page, siteTypePath, {
			...siteType,
			allowedDocumentTypes: [...allowed, { documentType: { id: SCHEMA_KEYS.blockEditorsPageType }, sortOrder: allowed.length }],
		});

		const corporateSite = await findDocumentByPath(page, [SAMPLE.corporateSite]);
		const campaignSite = await findDocumentByPath(page, [SAMPLE.campaignSite]);
		for (const [key, name, parent, documentValues] of [
			[SCHEMA_KEYS.corporatePage, SCHEMA_NAMES.corporatePage, corporateSite.id, corporateValues],
			[SCHEMA_KEYS.campaignPage, SCHEMA_NAMES.campaignPage, campaignSite.id, []],
		] as const) {
			await call(page, 'POST', '/document', {
				id: key,
				parent: { id: parent },
				documentType: { id: SCHEMA_KEYS.blockEditorsPageType },
				template: null,
				values: documentValues,
				variants: [{ culture: null, segment: null, name }],
			});
		}
		return { corporate: SCHEMA_KEYS.corporatePage, campaign: SCHEMA_KEYS.campaignPage };
	}

	/**
	 * A `memberBlocks` property (the seeded Blocks (modal) data type) in a "Blocks" group of the Member type, and a member
	 * "Acceptance member" whose `memberBlocks` holds one promoBanner ("Member promo", with an overlay colour and an
	 * anchor id). Resolves with the member's key.
	 */
	async addMemberWithPromoBanner(): Promise<string> {
		const page = this.page;
		const memberTypePath = `/member-type/${SEED.memberType}`;
		const memberType = await managementApi<TypeModel>(page, memberTypePath);
		await putType(page, memberTypePath, {
			...memberType,
			containers: [
				...memberType.containers,
				{ id: SCHEMA_KEYS.memberBlocksGroup, parent: null, name: 'Blocks', type: 'Group', sortOrder: 10 } as { id: string },
			],
			properties: [
				...memberType.properties,
				{
					id: SCHEMA_KEYS.memberBlocksProperty,
					container: { id: SCHEMA_KEYS.memberBlocksGroup },
					sortOrder: 0,
					alias: 'memberBlocks',
					name: 'Member blocks',
					description: null,
					dataType: { id: SEED.blocksModal },
					variesByCulture: false,
					variesBySegment: false,
					validation: { mandatory: false, mandatoryMessage: null, regEx: null, regExMessage: null },
					appearance: { labelOnTop: false },
					isSensitive: false,
					visibility: { memberCanView: false, memberCanEdit: false },
				} as { id: string },
			],
		});

		const contentKey = crypto.randomUUID();
		const settingsKey = crypto.randomUUID();
		await call(page, 'POST', '/member', {
			id: SCHEMA_KEYS.member,
			email: 'acceptance-member@example.com',
			username: 'acceptance-member@example.com',
			// Nobody signs in as this member; the password only satisfies the API.
			password: crypto.randomUUID(),
			memberType: { id: SEED.memberType },
			groups: [],
			isApproved: true,
			variants: [{ culture: null, segment: null, name: SCHEMA_NAMES.member }],
			values: [
				{
					alias: 'memberBlocks',
					culture: null,
					segment: null,
					value: {
						layout: { 'Umbraco.BlockList': [{ contentKey, settingsKey }] },
						contentData: [
							{
								key: contentKey,
								contentTypeKey: ELEMENT_TYPES.promoBanner.key,
								values: [
									{ alias: 'heading', culture: null, segment: null, value: 'Member promo' },
									{ alias: 'overlayColour', culture: null, segment: null, value: '#2e7d32' },
								],
							},
						],
						settingsData: [
							{
								key: settingsKey,
								contentTypeKey: ELEMENT_TYPES.promoBannerSettings.key,
								values: [{ alias: 'anchorId', culture: null, segment: null, value: 'member-promo' }],
							},
						],
						expose: [{ contentKey, culture: null, segment: null }],
					},
				},
			],
		});
		return SCHEMA_KEYS.member;
	}

	/** A document blueprint "Acceptance blueprint" made from a document. Resolves with its key. */
	async addBlueprint(documentKey: string): Promise<string> {
		await call(this.page, 'POST', '/document-blueprint/from-document', {
			id: SCHEMA_KEYS.blueprint,
			document: { id: documentKey },
			name: SCHEMA_NAMES.blueprint,
			parent: null,
		});
		return SCHEMA_KEYS.blueprint;
	}

	/** Turns on `inlineEditing` for the promoBanner block of the seeded Blocks (grid) data type (`landingPage.gridBlocks`). */
	async enableGridInlineEditing(): Promise<void> {
		const grid = await managementApi<DataTypeModel>(this.page, `/data-type/${SEED.blocksGrid}`);
		gridPromoBanner(grid).inlineEditing = true;
		await putDataType(this.page, SEED.blocksGrid, grid);
	}

	async restore(): Promise<void> {
		await removeSchemaChanges(this.page);
		rmSync(SCHEMA_CHANGES_MARKER, { force: true });
	}
}

/**
 * Removes the changes of a test that did not get to its `finally` (an interrupted or killed run), in a browser of its
 * own: the global setup and teardown have no page. Does nothing unless the marker file says changes may exist. Returns
 * what it did.
 */
export async function restoreSchemaChanges(): Promise<string[]> {
	if (!existsSync(SCHEMA_CHANGES_MARKER)) return [];
	const browser = await chromium.launch();
	try {
		const context = await browser.newContext({ baseURL: BASE_URL, ignoreHTTPSErrors: true });
		const page = await context.newPage();
		await loginAsAdmin(page);
		await removeSchemaChanges(page);
	} finally {
		await browser.close();
	}
	rmSync(SCHEMA_CHANGES_MARKER, { force: true });
	return ['removed the document type, member type and data type changes of support/schema-changes.ts'];
}
