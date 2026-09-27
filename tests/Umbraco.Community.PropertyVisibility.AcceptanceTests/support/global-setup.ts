import { prepareSiteConfiguration, restoreSiteConfiguration } from './site-config.js';

/**
 * Runs once before the suite. Specs 10 to 12 change the running site's appsettings.json and rules file and restore them
 * in `finally` blocks, which an interrupted run (Ctrl+C, a cancelled CI job, a crashed worker) skips. So first undo what
 * an earlier run left behind, then fail fast unless the site is in the state the suite expects, and keep a copy of
 * appsettings.json for the global teardown and the next run.
 */
export default function globalSetup(): void {
	for (const action of restoreSiteConfiguration()) {
		console.warn(`[acceptance setup] ${action} (left behind by an interrupted run).`);
	}
	prepareSiteConfiguration();
}
