#!/usr/bin/env bash
# Checks that the running test site (start-test-site.sh) serves the package's umbraco-package.json with the package's
# version. The backoffice adds that version to the bundle URL (?umb__rnd=), which keeps browsers from caching the
# previous release's client.
#
#   bash build/ci/check-served-manifest.sh            expects the version the site was built with (its deps.json)
#   bash build/ci/check-served-manifest.sh 2.0.0      expects 2.0.0
#
# Without an argument the expected version comes from the deps.json of the site in TEST_SITE_DIR (PV_TEST_SITE_DIR):
# the published package's version for the copy that installs it from nuget.org, PACKAGE_VERSION for the repository's
# test site.
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

package_id=Umbraco.Community.PropertyVisibility
url=https://localhost:$PV_PORT/App_Plugins/UmbracoCommunityPropertyVisibility/umbraco-package.json

expected=${1:-}
if [ -z "$expected" ]; then
	deps=$TEST_SITE_DIR/bin/$CONFIGURATION/$TARGET_FRAMEWORK/$TEST_SITE_NAME.deps.json
	[ -f "$deps" ] || fail "$deps not found; build the site first, or pass the expected version"
	expected=$(grep -oE "\"$package_id/[^\"]+\"" "$deps" | head -n 1 | sed -E "s|\"$package_id/([^\"]+)\"|\1|" || true)
	[ -n "$expected" ] || fail "$deps names no $package_id version"
fi

group "Served umbraco-package.json"
manifest=$(curl -k -fsS --max-time 30 "$url") || fail "could not read $url (is the test site running on port $PV_PORT?)"
served=$(node -e 'console.log(JSON.parse(require("fs").readFileSync(0, "utf8")).version ?? "")' <<< "$manifest") \
	|| fail "$url is not valid JSON"
same_version "$served" "$expected" || fail "$url carries version '$served', expected $expected"
echo "$url carries version $served"
end_group
