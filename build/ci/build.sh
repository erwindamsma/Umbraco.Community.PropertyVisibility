#!/usr/bin/env bash
# Builds the backoffice client and the solution, as the CI, compatibility and release workflows do.
#
#   bash build/ci/build.sh
#   PACKAGE_VERSION=1.2.3 bash build/ci/build.sh
#   UMBRACO_CMS_VERSION=17.7.0 bash build/ci/build.sh    (fails unless every Umbraco.Cms* package of the package, test
#                                                         site, unit test and schema generator projects, and the test
#                                                         site's deps.json, resolve exactly that version)
#
# Without UMBRACO_CMS_VERSION every Umbraco.Cms* package must still resolve to one version, the package project's
# Umbraco.Cms.Core.
#
# The client is built here (npm ci, typecheck, build with PACKAGE_VERSION), so the solution build skips the BuildClient
# MSBuild target (-p:SkipClientBuild=true). See common.sh for the environment variables.
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

group "Client: npm ci, typecheck and build ($PACKAGE_VERSION)"
(
	cd "$CLIENT_DIR"
	run npm ci --no-audit --no-fund
	run npm run typecheck
	run npm run build
)
manifest=$CLIENT_OUTPUT_DIR/umbraco-package.json
[ -f "$manifest" ] || fail "$manifest was not written by the client build"
# Written by the client's vite plugin as JSON.stringify(manifest, null, '\t').
grep -qF "\"version\": \"$PACKAGE_VERSION\"" "$manifest" || fail "$manifest does not carry version $PACKAGE_VERSION"
# -print -quit instead of a pipe into grep -q: under pipefail, grep -q exiting early gives find a SIGPIPE, and the failed
# pipeline would read as "no source maps".
if [ -n "$(find "$CLIENT_OUTPUT_DIR" -name '*.map' -print -quit)" ]; then
	fail "the client build left source maps in $CLIENT_OUTPUT_DIR"
fi
echo "Client output: $(find "$CLIENT_OUTPUT_DIR" -type f | wc -l | tr -d ' ') files, umbraco-package.json version $PACKAGE_VERSION"
end_group

group "Restore${UMBRACO_CMS_VERSION:+ (Umbraco.Cms $UMBRACO_CMS_VERSION)}"
run dotnet restore "$SOLUTION" "${msbuild_properties[@]}"
end_group

group "Build ($CONFIGURATION)"
run dotnet build "$SOLUTION" -c "$CONFIGURATION" --no-restore -p:SkipClientBuild=true "${msbuild_properties[@]}"
end_group

group "Resolved Umbraco version"
# Every Umbraco.Cms* package of every project must resolve to one version: UMBRACO_CMS_VERSION when it is set, otherwise
# the version the package project resolved Umbraco.Cms.Core to. A mixed graph (for example Umbraco.Cms.Core 17.7.0 next
# to Umbraco.Cms.Persistence.Sqlite 17.6.2) fails the build.
expected=$UMBRACO_CMS_VERSION
if [ -z "$expected" ]; then
	expected=$(resolved_umbraco_packages "$PACKAGE_DIR" | awk '$1 == "Umbraco.Cms.Core" { print $2 }')
	[ -n "$expected" ] || fail "$PACKAGE_DIR/obj/project.assets.json names no Umbraco.Cms.Core version"
fi
mismatches=()
for project in "$PACKAGE_DIR" "$TEST_SITE_DIR" "$TESTS_DIR" "$SCHEMA_GENERATOR_DIR"; do
	packages=$(resolved_umbraco_packages "$project")
	[ -n "$packages" ] || fail "$project/obj/project.assets.json names no Umbraco.Cms package"
	grep -q '^Umbraco\.Cms\.Core ' <<< "$packages" || fail "$project/obj/project.assets.json names no Umbraco.Cms.Core version"
	echo "$project: $(wc -l <<< "$packages" | tr -d ' ') Umbraco.Cms packages, Umbraco.Cms.Core $(awk '$1 == "Umbraco.Cms.Core" { print $2 }' <<< "$packages")"
	while read -r id version; do
		same_version "$version" "$expected" || mismatches+=("$project: $id $version")
	done <<< "$packages"
done
site_runtime=$(test_site_umbraco_version)
[ -n "$site_runtime" ] || fail "the test site's deps.json names no Umbraco.Cms.Core version"
echo "Test site build output ($TEST_SITE_NAME.deps.json): Umbraco.Cms.Core $site_runtime"
same_version "$site_runtime" "$expected" || mismatches+=("$TEST_SITE_NAME.deps.json: Umbraco.Cms.Core $site_runtime")
if [ ${#mismatches[@]} -gt 0 ]; then
	printf '  %s\n' "${mismatches[@]}" >&2
	fail "expected every Umbraco.Cms package at $expected${UMBRACO_CMS_VERSION:+ (UMBRACO_CMS_VERSION)}, but ${#mismatches[@]} resolved another version (listed above)"
fi
echo "Every Umbraco.Cms package resolves to $expected"
end_group
