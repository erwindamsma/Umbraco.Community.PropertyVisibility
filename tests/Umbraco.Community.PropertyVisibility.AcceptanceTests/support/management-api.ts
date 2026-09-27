import type { Page } from '@playwright/test';
import { DIRECT_CALL_HEADER, HIDDEN_FIELDS_PATH, SAMPLE } from './env.js';

/**
 * Calls the backoffice from inside an authenticated backoffice page, the way the backoffice itself does: same origin,
 * credentials included and the `Bearer [redacted]` placeholder, which Umbraco swaps for the HttpOnly access-token cookie.
 * The page must be on a /umbraco/ URL of a logged-in session. The call carries DIRECT_CALL_HEADER, so `settle()` does
 * not wait for an apply of a hidden-fields response nobody applies.
 */
export async function backofficeFetch<T = unknown>(
	page: Page,
	path: string,
	init: { method?: string; body?: unknown } = {},
): Promise<{ status: number; body: T }> {
	return page.evaluate(
		async ({ path, method, body, directCallHeader }) => {
			const response = await fetch(path, {
				method,
				credentials: 'include',
				headers: {
					Authorization: 'Bearer [redacted]',
					Accept: 'application/json',
					[directCallHeader]: '1',
					...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
				},
				body: body === undefined ? undefined : JSON.stringify(body),
			});
			const text = await response.text();
			let parsed: unknown = text;
			try {
				parsed = text ? JSON.parse(text) : undefined;
			} catch {
				// Not JSON: keep the text.
			}
			return { status: response.status, body: parsed as never };
		},
		{ path, method: init.method ?? 'GET', body: init.body, directCallHeader: DIRECT_CALL_HEADER },
	);
}

export async function managementApi<T = unknown>(page: Page, path: string): Promise<T> {
	const { status, body } = await backofficeFetch<T>(page, `/umbraco/management/api/v1${path}`);
	if (status !== 200) {
		throw new Error(`GET /umbraco/management/api/v1${path} returned ${status}: ${JSON.stringify(body)}`);
	}
	return body;
}

// ---- Documents ---------------------------------------------------------------------------------------------------

interface TreeItem {
	id: string;
	hasChildren: boolean;
	documentType: { id: string };
	variants: Array<{ name: string; culture: string | null }>;
}

interface PagedTree {
	total: number;
	items: TreeItem[];
}

export interface DocumentValue {
	alias: string;
	culture: string | null;
	segment: string | null;
	value: unknown;
}

export interface DocumentModel {
	id: string;
	documentType: { id: string };
	values: DocumentValue[];
	variants: Array<{ name: string; state: string }>;
}

/** A tree item has one variant (culture null) when invariant, one per culture when culture-variant. */
function hasName(item: TreeItem, name: string): boolean {
	return item.variants.some((variant) => variant.name === name);
}

/**
 * Finds a document by its names from the root down, e.g. ['Corporate site', 'Corporate landing']. A culture-variant
 * document matches by the name of any of its cultures.
 */
export async function findDocumentByPath(page: Page, names: string[]): Promise<TreeItem> {
	let items = (await managementApi<PagedTree>(page, '/tree/document/root?skip=0&take=500')).items;
	let found: TreeItem | undefined;
	for (const [index, name] of names.entries()) {
		found = items.find((item) => hasName(item, name));
		if (!found) {
			throw new Error(`Document "${names.slice(0, index + 1).join(' > ')}" not found in the content tree.`);
		}
		if (index < names.length - 1) {
			items = (await managementApi<PagedTree>(page, `/tree/document/children?parentId=${found.id}&skip=0&take=500`))
				.items;
		}
	}
	return found!;
}

export function getDocument(page: Page, key: string): Promise<DocumentModel> {
	return managementApi<DocumentModel>(page, `/document/${key}`);
}

/**
 * Deletes a document the suite created, with everything under it: straight away, or through the recycle bin when the
 * API only deletes trashed documents. A document that is already gone is fine.
 */
export async function deleteDocument(page: Page, key: string): Promise<void> {
	const direct = await backofficeFetch(page, `/umbraco/management/api/v1/document/${key}`, { method: 'DELETE' });
	if (direct.status === 200 || direct.status === 404) return;

	const trashed = await backofficeFetch(page, `/umbraco/management/api/v1/document/${key}/move-to-recycle-bin`, { method: 'PUT' });
	const deleted = await backofficeFetch(page, `/umbraco/management/api/v1/recycle-bin/document/${key}`, { method: 'DELETE' });
	if (deleted.status !== 200) {
		throw new Error(
			`Deleting document ${key} failed: DELETE ${direct.status} ${JSON.stringify(direct.body)}, ` +
				`move to recycle bin ${trashed.status}, delete from recycle bin ${deleted.status} ${JSON.stringify(deleted.body)}`,
		);
	}
}

export function valueOf(document: DocumentModel, alias: string): unknown {
	return document.values.find((value) => value.alias === alias && value.culture === null && value.segment === null)
		?.value;
}

// ---- Document types --------------------------------------------------------------------------------------------

interface DocumentTypeModel {
	id: string;
	alias: string;
	name: string;
	properties: Array<{ id: string; alias: string; name: string; container?: { id: string } | null }>;
	containers: Array<{ id: string; parent?: { id: string } | null; name?: string | null; type: 'Group' | 'Tab' }>;
	compositions: Array<{ documentType: { id: string }; compositionType: string }>;
}

export interface ContainerInfo {
	key: string;
	/** `tab`, `tab/group` or a root-level `group`, derived from the names the way Umbraco 17 derives container aliases. */
	alias: string;
	name: string;
	type: 'Group' | 'Tab';
	parentKey: string | null;
	contentTypeAlias: string;
}

export interface PropertyInfo {
	key: string;
	alias: string;
	containerKey: string | null;
	contentTypeAlias: string;
}

/** A content type merged with its compositions, as the backoffice renders it. */
export interface ContentTypeStructure {
	key: string;
	alias: string;
	properties: PropertyInfo[];
	containers: ContainerInfo[];
}

/** Umbraco 17 derives a container alias from its name: first word lower camel, later words upper camel. */
export function aliasFromName(name: string): string {
	const words = name.split(/[^A-Za-z0-9]+/).filter(Boolean);
	return words
		.map((word, index) =>
			index === 0 ? word.charAt(0).toLowerCase() + word.slice(1) : word.charAt(0).toUpperCase() + word.slice(1),
		)
		.join('');
}

interface PropertyValidation {
	mandatory: boolean;
	mandatoryMessage?: string | null;
	regEx?: string | null;
	regExMessage?: string | null;
}

/**
 * Sets the Mandatory flag of one property of a document or element type through the Management API (GET the type, PUT
 * it back with only that flag changed). For a spec that needs a hidden mandatory property for a moment: set it back to
 * the seeded value in a `finally` block. The test site does not export on save (uSync `ExportOnSave: None`), so the
 * seed files are never touched.
 */
export async function setPropertyMandatory(page: Page, typeKey: string, alias: string, mandatory: boolean): Promise<void> {
	const path = `/document-type/${typeKey}`;
	const type = await managementApi<{ id?: string; properties: Array<{ alias: string; validation: PropertyValidation }> }>(page, path);
	const property = type.properties.find((p) => p.alias === alias);
	if (!property) throw new Error(`document type ${typeKey} has no property ${alias}`);
	if (property.validation.mandatory === mandatory) return;

	property.validation = { ...property.validation, mandatory };
	const body: Record<string, unknown> = { ...type };
	delete body.id;
	const { status, body: result } = await backofficeFetch(page, `/umbraco/management/api/v1${path}`, { method: 'PUT', body });
	if (status !== 200) {
		throw new Error(`PUT /umbraco/management/api/v1${path} returned ${status}: ${JSON.stringify(result)}`);
	}
}

/**
 * Sets whether a document type may be created at the content root (its "Allow at root" structure setting) through the
 * Management API, the same way as setPropertyMandatory, and resolves with the previous value. For a spec that needs a
 * type at the root for a moment: set it back to that value in a `finally` block. The seed files are never touched.
 */
export async function setAllowedAsRoot(page: Page, typeKey: string, allowed: boolean): Promise<boolean> {
	const path = `/document-type/${typeKey}`;
	const type = await managementApi<{ id?: string; allowedAsRoot: boolean }>(page, path);
	const previous = type.allowedAsRoot;
	if (typeof previous !== 'boolean') throw new Error(`document type ${typeKey} has no allowedAsRoot flag`);
	if (previous === allowed) return previous;

	const body: Record<string, unknown> = { ...type, allowedAsRoot: allowed };
	delete body.id;
	const { status, body: result } = await backofficeFetch(page, `/umbraco/management/api/v1${path}`, { method: 'PUT', body });
	if (status !== 200) {
		throw new Error(`PUT /umbraco/management/api/v1${path} returned ${status}: ${JSON.stringify(result)}`);
	}
	return previous;
}

/** The key of the composition with this alias among a document type's compositions. */
export async function compositionKey(page: Page, typeKey: string, alias: string): Promise<string> {
	const type = await managementApi<DocumentTypeModel>(page, `/document-type/${typeKey}`);
	for (const composition of type.compositions ?? []) {
		if (composition.compositionType !== 'Composition') continue;
		const candidate = await managementApi<DocumentTypeModel>(page, `/document-type/${composition.documentType.id}`);
		if (candidate.alias === alias) return candidate.id;
	}
	throw new Error(`Document type ${type.alias} has no composition ${alias}.`);
}

export async function getContentTypeStructure(page: Page, key: string): Promise<ContentTypeStructure> {
	const visited = new Set<string>();
	const properties: PropertyInfo[] = [];
	const containers: ContainerInfo[] = [];
	let ownerAlias = '';

	const load = async (typeKey: string, isOwner: boolean): Promise<void> => {
		if (visited.has(typeKey)) return;
		visited.add(typeKey);
		const type = await managementApi<DocumentTypeModel>(page, `/document-type/${typeKey}`);
		if (isOwner) ownerAlias = type.alias;

		const byId = new Map(type.containers.map((container) => [container.id, container]));
		for (const container of type.containers) {
			const own = aliasFromName(container.name ?? '');
			const parent = container.parent ? byId.get(container.parent.id) : undefined;
			containers.push({
				key: container.id,
				alias: parent ? `${aliasFromName(parent.name ?? '')}/${own}` : own,
				name: container.name ?? '',
				type: container.type,
				parentKey: container.parent?.id ?? null,
				contentTypeAlias: type.alias,
			});
		}
		for (const property of type.properties) {
			properties.push({
				key: property.id,
				alias: property.alias,
				containerKey: property.container?.id ?? null,
				contentTypeAlias: type.alias,
			});
		}
		for (const composition of type.compositions ?? []) {
			if (composition.compositionType === 'Composition') {
				await load(composition.documentType.id, false);
			}
		}
	};

	await load(key, true);
	return { key, alias: ownerAlias, properties, containers };
}

// ---- The package API ---------------------------------------------------------------------------------------------

export interface HiddenFieldsResponse {
	propertyTypeKeys: string[];
	containerKeys: string[];
	disabled: boolean;
	rootResolution: 'Document' | 'Parent' | 'None' | 'RecycleBin';
	/**
	 * Label and match reason only, never a root node key or name: any Content section user can call the API for any
	 * document key, so a root key or name here would let them map documents to site roots.
	 */
	matchedSite?: { label: string; reason: string } | null;
	warnings: string[];
}

export async function getHiddenFields(
	page: Page,
	query: { documentKey: string; contentTypeKey: string; parentKey?: string },
): Promise<{ status: number; body: HiddenFieldsResponse }> {
	const params = new URLSearchParams({ documentKey: query.documentKey, contentTypeKey: query.contentTypeKey });
	if (query.parentKey) params.set('parentKey', query.parentKey);
	return backofficeFetch<HiddenFieldsResponse>(page, `${HIDDEN_FIELDS_PATH}?${params}`);
}

// ---- The seeded sample ---------------------------------------------------------------------------------------------

export interface SampleKeys {
	corporateSite: string;
	campaignSite: string;
	corporateLanding: string;
	campaignLanding: string;
	corporateArticle: string;
	campaignArticle: string;
	landingPageType: string;
	articleType: string;
}

/** Resolves the seeded documents and their content types by name through the Management API. */
export async function resolveSample(page: Page): Promise<SampleKeys> {
	const corporateSite = await findDocumentByPath(page, [SAMPLE.corporateSite]);
	const campaignSite = await findDocumentByPath(page, [SAMPLE.campaignSite]);
	const corporateLanding = await findDocumentByPath(page, [SAMPLE.corporateSite, SAMPLE.corporateLanding]);
	const campaignLanding = await findDocumentByPath(page, [SAMPLE.campaignSite, SAMPLE.campaignLanding]);
	const corporateArticle = await findDocumentByPath(page, [SAMPLE.corporateSite, SAMPLE.corporateArticle]);
	const campaignArticle = await findDocumentByPath(page, [SAMPLE.campaignSite, SAMPLE.campaignArticle]);
	return {
		corporateSite: corporateSite.id,
		campaignSite: campaignSite.id,
		corporateLanding: corporateLanding.id,
		campaignLanding: campaignLanding.id,
		corporateArticle: corporateArticle.id,
		campaignArticle: campaignArticle.id,
		landingPageType: corporateLanding.documentType.id,
		articleType: corporateArticle.documentType.id,
	};
}
