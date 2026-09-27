# shellcheck shell=bash disable=SC2034  # sourced: the variables are used by the scripts that source it
# Shared settings and helpers for the scripts in build/ci. Sourced by them, not run on its own.
#
# The GitHub workflows call these scripts, so a local run executes the same commands as CI. Every script works from the
# repository root, whatever the current folder is.
#
# Environment (all optional):
#   PACKAGE_VERSION               version of this build: the client manifest (umbraco-package.json), the assemblies and the
#                                 nupkg; default 0.0.0-local (CI: 0.0.0-ci.<run number>, release: the tag)
#   UMBRACO_CMS_VERSION           an exact Umbraco.Cms version to build against (for example 17.7.0), passed as
#                                 -p:UmbracoCmsVersion=[<version>]; default: the range in src/Directory.Packages.props
#   CONTINUOUS_INTEGRATION_BUILD  true for a deterministic build (-p:ContinuousIntegrationBuild=true: source paths mapped
#                                 to /_/); defaults to $CI, which GitHub Actions sets to true
#   CONFIGURATION                 default Release

set -euo pipefail

REPO_ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
cd "$REPO_ROOT"

# Paths relative to the repository root (relative paths also keep Git Bash from rewriting arguments to dotnet.exe).
SOLUTION=Umbraco.Community.PropertyVisibility.slnx
PACKAGE_DIR=src/Umbraco.Community.PropertyVisibility
PACKAGE_PROJECT=$PACKAGE_DIR/Umbraco.Community.PropertyVisibility.csproj
CLIENT_DIR=$PACKAGE_DIR/Client
CLIENT_OUTPUT_DIR=$PACKAGE_DIR/wwwroot/App_Plugins/UmbracoCommunityPropertyVisibility
TEST_SITE_DIR=src/Umbraco.Community.PropertyVisibility.TestSite
TEST_SITE_NAME=Umbraco.Community.PropertyVisibility.TestSite
TESTS_DIR=tests/Umbraco.Community.PropertyVisibility.Tests
ACCEPTANCE_DIR=tests/Umbraco.Community.PropertyVisibility.AcceptanceTests
SCHEMA_GENERATOR_DIR=tools/Umbraco.Community.PropertyVisibility.SchemaGenerator
TARGET_FRAMEWORK=net10.0

CONFIGURATION=${CONFIGURATION:-Release}
export PACKAGE_VERSION=${PACKAGE_VERSION:-0.0.0-local}
UMBRACO_CMS_VERSION=${UMBRACO_CMS_VERSION:-}

export DOTNET_NOLOGO=${DOTNET_NOLOGO:-true}
export DOTNET_CLI_TELEMETRY_OPTOUT=${DOTNET_CLI_TELEMETRY_OPTOUT:-true}

# The test site (start-test-site.sh, acceptance.sh, stop-test-site.sh).
PV_PORT=${PV_PORT:-44300}
PV_STATE_DIR=${PV_STATE_DIR:-artifacts/test-site}

# Whether a running process is the test site: its command line names the test site's dll. A PID file can outlive its
# process and the number be reused, so a PID from it is only trusted after this check. Reads /proc/<pid>/cmdline (Linux,
# and Git Bash for the processes it started), otherwise `ps -o args=`.
is_test_site_process() {
	local pid=$1 args=
	if [ -r "/proc/$pid/cmdline" ]; then
		args=$(tr '\0' ' ' < "/proc/$pid/cmdline" 2> /dev/null || true)
	else
		args=$(ps -p "$pid" -o args= 2> /dev/null || true)
	fi
	[[ $args == *"$TEST_SITE_NAME.dll"* ]]
}

ci_build() {
	[ "${CONTINUOUS_INTEGRATION_BUILD:-${CI:-false}}" = "true" ]
}

in_github_actions() {
	[ "${GITHUB_ACTIONS:-}" = "true" ]
}

# Collapsible sections in the GitHub log, a plain header elsewhere.
group() {
	if in_github_actions; then echo "::group::$*"; else echo "== $*"; fi
}

end_group() {
	if in_github_actions; then echo "::endgroup::"; fi
}

# Prints a command, then runs it.
run() {
	echo "+ $*"
	"$@"
}

fail() {
	if in_github_actions; then echo "::error::$*" >&2; else echo "ERROR: $*" >&2; fi
	exit 1
}

# MSBuild properties shared by restore, build, test and pack, so every step sees the same global properties.
msbuild_properties=("-p:Version=$PACKAGE_VERSION")
if ci_build; then
	msbuild_properties+=("-p:ContinuousIntegrationBuild=true")
fi
if [ -n "$UMBRACO_CMS_VERSION" ]; then
	if ! [[ $UMBRACO_CMS_VERSION =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]]; then
		fail "UMBRACO_CMS_VERSION must be one exact version such as 17.7.0, got '$UMBRACO_CMS_VERSION'"
	fi
	# An exact NuGet range: restore takes this version or fails, it never floats to another one.
	msbuild_properties+=("-p:UmbracoCmsVersion=[$UMBRACO_CMS_VERSION]")
fi

# Every Umbraco.Cms and Umbraco.Cms.* package a restored project resolved (from its obj/project.assets.json), one
# "<id> <version>" line each, sorted; nothing when the project is not restored.
resolved_umbraco_packages() {
	local assets=$1/obj/project.assets.json
	[ -f "$assets" ] || return 0
	grep -oE '"Umbraco\.Cms(\.[A-Za-z0-9.]+)?/[^"]+"' "$assets" | tr -d '"' | sed -E 's|/| |' | sort -u || true
}

# The Umbraco.Cms.Core version the built test site runs (from its deps.json), or nothing.
test_site_umbraco_version() {
	local deps=$TEST_SITE_DIR/bin/$CONFIGURATION/$TARGET_FRAMEWORK/$TEST_SITE_NAME.deps.json
	[ -f "$deps" ] || return 0
	grep -oE '"Umbraco\.Cms\.Core/[^"]+"' "$deps" | head -n 1 | sed -E 's/"Umbraco\.Cms\.Core\/([^"]+)"/\1/'
}

# NuGet versions compare case-insensitively (the assets file keeps the published casing).
same_version() {
	[ "$(tr '[:upper:]' '[:lower:]' <<< "$1")" = "$(tr '[:upper:]' '[:lower:]' <<< "$2")" ]
}
