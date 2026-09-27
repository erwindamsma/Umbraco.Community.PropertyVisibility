#!/usr/bin/env bash
# Prints the Umbraco.Cms version the compatibility workflow tests: the argument when one is given (it must exist on
# nuget.org), otherwise the latest stable 17.x that is listed. Only the version goes to stdout.
#
#   bash build/ci/resolve-umbraco-version.sh            latest stable, listed 17.x, for example 17.7.0
#   bash build/ci/resolve-umbraco-version.sh 17.6.2     checks that 17.6.2 exists and prints it
#   UMBRACO_CMS_VERSION=$(bash build/ci/resolve-umbraco-version.sh) bash build/ci/build.sh
#
# Reads nuget.org's registration index, which says per version whether it is listed: the flat container lists unlisted
# (withdrawn) versions too, and the latest one of those must never be picked. A requested version may be unlisted (it
# still restores); that only prints a warning. Needs curl and node.
set -euo pipefail

requested=${1:-}
major=${UMBRACO_MAJOR:-17}
registration_url=https://api.nuget.org/v3/registration5-gz-semver2/umbraco.cms/index.json

fail() {
	if [ "${GITHUB_ACTIONS:-}" = "true" ]; then echo "::error::$*" >&2; else echo "ERROR: $*" >&2; fi
	exit 1
}

warn() {
	if [ "${GITHUB_ACTIONS:-}" = "true" ]; then echo "::warning::$*" >&2; else echo "WARNING: $*" >&2; fi
}

if [ -n "$requested" ]; then
	[[ $requested =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] || fail "'$requested' is not a version such as 17.7.0"
	major=${requested%%.*}
fi
[[ $major =~ ^[0-9]+$ ]] || fail "UMBRACO_MAJOR must be a number, got '$major'"
command -v node > /dev/null || fail "node is needed to read the nuget.org registration index"

# Reads one registration document (the index or a page) from stdin and prints, for the pages whose range can hold
# versions of the major:
#   pages      the URL of every page that is not inlined (large packages such as Umbraco.Cms have only these)
#   versions   "<version> listed" or "<version> unlisted" for every inlined version (no "listed" field means listed)
# shellcheck disable=SC2016  # JavaScript: ${...} is a template literal, not a shell expansion
read_registration='
const [mode, major] = process.argv.slice(1);
const document = JSON.parse(require("fs").readFileSync(0, "utf8"));
const majorOf = (version) => parseInt(String(version), 10);
const pages = [].concat(document["@type"] ?? []).includes("catalog:CatalogPage") ? [document] : document.items ?? [];
for (const page of pages) {
	if (majorOf(page.lower) > Number(major) || majorOf(page.upper) < Number(major)) continue;
	if (!Array.isArray(page.items)) {
		if (mode === "pages") console.log(page["@id"]);
		continue;
	}
	if (mode !== "versions") continue;
	for (const leaf of page.items) {
		const entry = leaf.catalogEntry;
		console.log(`${entry.version} ${entry.listed === false ? "unlisted" : "listed"}`);
	}
}
'

fetch() {
	curl -fsSL --compressed --retry 3 "$1"
}

index=$(fetch "$registration_url") || fail "could not read $registration_url"
entries=$(node -e "$read_registration" versions "$major" <<< "$index") || fail "could not read the versions in $registration_url"
pages=$(node -e "$read_registration" pages "$major" <<< "$index") || fail "could not read the pages in $registration_url"
for page in $pages; do
	case "$page" in
		https://api.nuget.org/v3/registration5-gz-semver2/umbraco.cms/*) ;;
		*) fail "unexpected registration page URL $page" ;;
	esac
	page_json=$(fetch "$page") || fail "could not read $page"
	page_entries=$(node -e "$read_registration" versions "$major" <<< "$page_json") || fail "could not read the versions in $page"
	entries+=$'\n'$page_entries
done
grep -q ' ' <<< "$entries" || fail "no Umbraco.Cms $major.x versions in $registration_url"

if [ -n "$requested" ]; then
	state=$(awk -v version="$(tr '[:upper:]' '[:lower:]' <<< "$requested")" 'tolower($1) == version { print $2; exit }' <<< "$entries")
	[ -n "$state" ] || fail "Umbraco.Cms $requested is not published on nuget.org"
	if [ "$state" = "unlisted" ]; then
		warn "Umbraco.Cms $requested is unlisted on nuget.org (withdrawn?); testing it because it was requested"
	fi
	echo "Umbraco.Cms $requested (requested, $state) exists on nuget.org" >&2
	echo "$requested"
else
	# Stable only (a prerelease has a hyphen suffix) and listed. `|| true`: no match must reach the message below instead
	# of ending the script through pipefail.
	latest=$(awk '$2 == "listed" { print $1 }' <<< "$entries" | grep -E "^$major\.[0-9]+\.[0-9]+$" | sort -V | tail -n 1 || true)
	[ -n "$latest" ] || fail "no stable, listed Umbraco.Cms $major.x on nuget.org"
	echo "Latest stable, listed Umbraco.Cms $major.x on nuget.org: $latest" >&2
	echo "$latest"
fi
