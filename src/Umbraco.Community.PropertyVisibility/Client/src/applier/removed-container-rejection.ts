import { debugLog } from '../debug.js';

/**
 * The rejection Umbraco throws for a validation message on a property whose tab or group is gone. Written by
 * `UmbContentValidationToHintsManager` (Umbraco 17.6.2, unchanged in 17.7.0): "Could not find the declared container of
 * id "<id>" for property with alias: "<alias>"".
 */
const MISSING_CONTAINER_MESSAGE = /^Could not find the declared container of id "([^"]+)"/;

/** Every tab and group id this package removed from a structure in this browser session. */
const removedContainerIds = new Set<string>();

let listening = false;

/**
 * Records tab and group ids the applier is about to remove, and installs the `unhandledrejection` listener once.
 *
 * Why: the applier removes a tab or group with `removeContainer(..., { preventRemovingProperties: true })`, so its
 * properties stay in the structure (their values survive every reload) and keep pointing at the removed container.
 * Umbraco's `UmbContentValidationToHintsManager`, which the document workspace and every block element manager run,
 * turns each validation message under `$.values` into a hint on the tab or group of its property. For a property of a
 * removed container it finds no container and throws inside an unhandled `.then()`, again on every later emission of
 * the messages. That happens when a hidden property fails validation, for example a hidden mandatory property (health
 * check `PV203`) on Save and publish. Umbraco still blocks the publish and shows its own notification; only the hint,
 * which would point at a tab or group that is not there, is missing.
 *
 * The listener marks exactly that rejection as handled, and only when the container id in the message is one this
 * package removed, so the console stays clean and every other rejection reaches the console as before.
 */
export function rememberRemovedContainers(ids: Iterable<string>): void {
	for (const id of ids) removedContainerIds.add(id);
	if (listening || typeof window === 'undefined') return;
	listening = true;
	window.addEventListener('unhandledrejection', onUnhandledRejection);
}

function onUnhandledRejection(event: PromiseRejectionEvent): void {
	const reason: unknown = event.reason;
	if (!(reason instanceof Error)) return;
	const match = MISSING_CONTAINER_MESSAGE.exec(reason.message);
	if (!match || !removedContainerIds.has(match[1])) return;

	event.preventDefault();
	debugLog('a validation message of a property in a removed tab or group has no tab or group to show its hint on', {
		containerId: match[1],
	});
}
