import { defineConfig, devices } from '@playwright/test';
import { BASE_URL } from './support/env.js';

const browser = { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 1000 } };

/** The console-errors spec reads the console log every other spec appends to, so it must run after all of them. */
const CONSOLE_ERRORS_SPEC = /08-console-errors\.spec\.ts$/;

/**
 * Acceptance suite against the running test site (src/Umbraco.Community.PropertyVisibility.TestSite, launch profile https).
 * The specs share one database (they save, publish and create documents) and some change the running site's
 * configuration (appsettings.json, the rules file), so they run serially in one worker.
 *
 * Two projects: `chromium` runs every spec except 08-console-errors, in file name order; `console-errors` is its
 * teardown project, so it runs after `chromium` has finished (also when a spec failed) and checks the console log of
 * the whole run. Running a single spec also runs the teardown, whose first test then fails because the other specs did
 * not run; pass `--no-deps` to leave it out.
 *
 * The global setup and teardown protect the site's configuration files (support/site-config.ts): the setup undoes
 * what an interrupted run left behind and fails fast unless the package is enabled and no rules file exists; the
 * teardown restores appsettings.json and deletes the suite's rules file when a test did not.
 */
export default defineConfig({
	testDir: './tests',
	globalSetup: './support/global-setup.ts',
	globalTeardown: './support/global-teardown.ts',
	fullyParallel: false,
	workers: 1,
	retries: 0,
	forbidOnly: !!process.env.CI,
	timeout: 120_000,
	expect: { timeout: 15_000 },
	outputDir: 'test-results',
	reporter: [['list'], ['html', { open: 'never', outputFolder: 'playwright-report' }]],
	use: {
		baseURL: BASE_URL,
		ignoreHTTPSErrors: true,
		trace: 'retain-on-failure',
		screenshot: 'only-on-failure',
	},
	projects: [
		{
			name: 'chromium',
			use: browser,
			testIgnore: CONSOLE_ERRORS_SPEC,
			teardown: 'console-errors',
		},
		{
			name: 'console-errors',
			use: browser,
			testMatch: CONSOLE_ERRORS_SPEC,
		},
	],
});
