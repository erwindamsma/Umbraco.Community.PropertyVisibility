import { UMB_PROPERTY_VISIBILITY_SITE_CONTEXT } from '../../site/site.context-token.js';
import { UmbControllerBase } from '@umbraco-cms/backoffice/class-api';
import { UmbContextBoundaryController } from '@umbraco-cms/backoffice/context-api';
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';

/**
 * Registered as a workspaceContext for the workspaces that can hold blocks but are not a document: media, members and
 * document blueprints. It provides nothing; it stops requests for the package's site context at the workspace.
 *
 * Why: such a workspace can be opened in a modal from inside a document (a media item from a media picker, a member from
 * a member picker). Umbraco's modal (17.6.2, unchanged in 17.7.0) forwards every context request it cannot answer to the
 * element that opened it (`umb-modal` installs a context proxy that only ignores the modal context), so without this
 * boundary a block inside that media item or member would reach the document's site context and hide that site's
 * fields, while the same item opened from its own section hides nothing. With it, a block edited outside a document
 * never gets a site context: nothing is requested and nothing is hidden, wherever the item was opened from.
 */
export class UmbPropertyVisibilitySiteBoundaryWorkspaceContext extends UmbControllerBase {
	constructor(host: UmbControllerHost) {
		super(host);
		new UmbContextBoundaryController(this, UMB_PROPERTY_VISIBILITY_SITE_CONTEXT);
	}
}

export { UmbPropertyVisibilitySiteBoundaryWorkspaceContext as api };
export default UmbPropertyVisibilitySiteBoundaryWorkspaceContext;
