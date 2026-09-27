import { restoreSiteConfiguration } from './site-config.js';

/**
 * Runs once after the suite, also when it was interrupted (Playwright runs the global teardown on the first Ctrl+C):
 * restores appsettings.json from the copy taken at the start and deletes a rules file the suite wrote, when a test
 * body did not get to its own `finally`. A hard kill skips this too; the next run's global setup then restores.
 */
export default function globalTeardown(): void {
	for (const action of restoreSiteConfiguration()) {
		console.warn(`[acceptance teardown] ${action}.`);
	}
}
