import { debugLog, describeError } from './debug.js';
import type en from './localization/en.js';
import type { UmbClassInterface } from '@umbraco-cms/backoffice/class-api';
import type { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';

/** A localization key of this package (localization/en.ts), as `term()` expects it. */
export type UmbPropertyVisibilityTermKey = `propertyVisibility_${keyof (typeof en)['propertyVisibility']}`;

/**
 * Shows a warning notification headed with the package name, so an editor can tell it apart from Umbraco's own.
 * Callers decide how often (once per browser session) and write the details to the console themselves.
 *
 * Never rejects: `getContext` rejects when the host is disconnected (the workspace or block closed meanwhile) or no
 * notification context answers in time; then only a debug line is written.
 */
export async function peekPackageWarning(
	host: UmbClassInterface,
	localize: UmbLocalizationController,
	messageKey: UmbPropertyVisibilityTermKey,
): Promise<void> {
	try {
		const notificationContext = await host.getContext(UMB_NOTIFICATION_CONTEXT);
		notificationContext?.peek('warning', {
			data: {
				headline: localize.term('propertyVisibility_packageName'),
				message: localize.term(messageKey),
			},
		});
	} catch (error) {
		debugLog(`could not show the ${messageKey} notification`, { error: describeError(error) });
	}
}
