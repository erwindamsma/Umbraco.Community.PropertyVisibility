import {
	BLOCK_WORKSPACE_CONTEXT_ALIAS,
	DOCUMENT_WORKSPACE_CONTEXT_ALIAS,
	ENTRY_POINT_ALIAS,
	LOCALIZATION_EN_ALIAS,
	SITE_BOUNDARY_WORKSPACE_CONTEXT_ALIAS,
} from './constants.js';
import { UMB_BLOCK_WORKSPACE_ALIAS } from '@umbraco-cms/backoffice/block';
import { UMB_DOCUMENT_WORKSPACE_ALIAS } from '@umbraco-cms/backoffice/document';
import type { UMB_DOCUMENT_BLUEPRINT_WORKSPACE_ALIAS } from '@umbraco-cms/backoffice/document-blueprint';
import type { UMB_MEDIA_WORKSPACE_ALIAS } from '@umbraco-cms/backoffice/media';
import type { UMB_MEMBER_WORKSPACE_ALIAS } from '@umbraco-cms/backoffice/member';
import { UMB_WORKSPACE_CONDITION_ALIAS } from '@umbraco-cms/backoffice/workspace';

// The workspaces the site boundary covers. Literals checked against Umbraco's constants at compile time (type-only
// imports), so the bundle does not load the media, member and document blueprint packages to read three strings.
const MEDIA_WORKSPACE_ALIAS: typeof UMB_MEDIA_WORKSPACE_ALIAS = 'Umb.Workspace.Media';
const MEMBER_WORKSPACE_ALIAS: typeof UMB_MEMBER_WORKSPACE_ALIAS = 'Umb.Workspace.Member';
const DOCUMENT_BLUEPRINT_WORKSPACE_ALIAS: typeof UMB_DOCUMENT_BLUEPRINT_WORKSPACE_ALIAS = 'Umb.Workspace.DocumentBlueprint';

// The bundle registered by umbraco-package.json: every manifest of this package lives here.
export const manifests: Array<UmbExtensionManifest> = [
	{
		type: 'backofficeEntryPoint',
		alias: ENTRY_POINT_ALIAS,
		name: 'Property Visibility Entry Point',
		js: () => import('./entrypoints/entrypoint.js'),
	},
	{
		type: 'workspaceContext',
		alias: DOCUMENT_WORKSPACE_CONTEXT_ALIAS,
		name: 'Property Visibility Document Workspace Context',
		api: () => import('./workspace-contexts/document/document.workspace-context.js'),
		conditions: [
			{
				alias: UMB_WORKSPACE_CONDITION_ALIAS,
				match: UMB_DOCUMENT_WORKSPACE_ALIAS,
			},
		],
	},
	{
		// One block workspace for every block editor: modal and inline editing, Block List, Block Grid, Single Block and
		// rich text editor blocks all run 'Umb.Workspace.Block'.
		type: 'workspaceContext',
		alias: BLOCK_WORKSPACE_CONTEXT_ALIAS,
		name: 'Property Visibility Block Workspace Context',
		api: () => import('./workspace-contexts/block/block.workspace-context.js'),
		conditions: [
			{
				alias: UMB_WORKSPACE_CONDITION_ALIAS,
				match: UMB_BLOCK_WORKSPACE_ALIAS,
			},
		],
	},
	{
		// The workspaces that hold blocks but are not a document: blocks in them never reach a document's site context,
		// also when the media item or member is opened in a modal from inside a document.
		type: 'workspaceContext',
		alias: SITE_BOUNDARY_WORKSPACE_CONTEXT_ALIAS,
		name: 'Property Visibility Site Boundary Workspace Context',
		api: () => import('./workspace-contexts/site-boundary/site-boundary.workspace-context.js'),
		conditions: [
			{
				alias: UMB_WORKSPACE_CONDITION_ALIAS,
				oneOf: [MEDIA_WORKSPACE_ALIAS, MEMBER_WORKSPACE_ALIAS, DOCUMENT_BLUEPRINT_WORKSPACE_ALIAS],
			},
		],
	},
	{
		type: 'localization',
		alias: LOCALIZATION_EN_ALIAS,
		name: 'Property Visibility English',
		meta: {
			culture: 'en',
		},
		js: () => import('./localization/en.js'),
	},
];
