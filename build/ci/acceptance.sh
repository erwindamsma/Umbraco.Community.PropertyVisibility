#!/usr/bin/env bash
# Runs the whole Playwright acceptance suite against a running test site (start-test-site.sh). Arguments are passed on
# to `playwright test`. The HTML report (playwright-report/) and the test output (test-results/: traces, screenshots,
# the console log of the run) stay in the acceptance project folder, also when the suite fails.
#
#   bash build/ci/acceptance.sh
#   PV_PORT=45000 bash build/ci/acceptance.sh
#
# Environment (besides common.sh):
#   PV_BASE_URL            the site to test (default https://localhost:$PV_PORT)
#   PV_SITE_CONTENT_ROOT   the site's content root, when it is not this repository's test site folder
#   PV_GLOBAL_TIMEOUT_MS   upper bound for the whole run (default 1800000, 30 minutes: below the workflows' job
#                          timeouts, so a hanging backoffice fails this step instead of cancelling the job)
#   PV_MAX_FAILURES        with CI=true, stop after this many failed tests (default 10)
#   CI                     true also installs Chromium's system libraries (playwright install --with-deps, uses sudo)
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

export PV_BASE_URL=${PV_BASE_URL:-https://localhost:$PV_PORT}

cd "$ACCEPTANCE_DIR"

report_location() {
	echo "Playwright report: $ACCEPTANCE_DIR/playwright-report, test output: $ACCEPTANCE_DIR/test-results"
}
trap report_location EXIT

group "Acceptance tests: npm ci and typecheck"
run npm ci --no-audit --no-fund
run npm run typecheck
end_group

group "Acceptance tests: Chromium"
if [ "${CI:-false}" = "true" ]; then
	run npx playwright install --with-deps chromium
else
	run npx playwright install chromium
fi
end_group

limits=("--global-timeout=${PV_GLOBAL_TIMEOUT_MS:-1800000}")
if [ "${CI:-false}" = "true" ]; then
	limits+=("--max-failures=${PV_MAX_FAILURES:-10}")
fi

echo "Running the acceptance suite against $PV_BASE_URL"
run npx playwright test "${limits[@]}" "$@"
