#!/usr/bin/env bash
# Leak test for tools/inspect-nupkg.sh: takes a good nupkg (pack.sh's output), writes corrupted copies and asserts that
# the inspection rejects each of them, for the expected reason, and still accepts the original:
#   - entries outside the allowlist: a source map under staticwebassets, a file of the Client/ project
#     (Client/package.json at the package root), a TypeScript source next to the chunks, a secrets.json in the client
#     output folder and one at the staticwebassets root, and a content file (content/appsettings.Production.json)
#   - a package without LICENSE, and one without THIRD-PARTY-NOTICES.md
#   - a client.gen chunk (the Hey API client code) without its licence banner
#   - a chunk that contains the Hey API buildClientParams code ($body_ in the client.gen chunk, $query_ in the bundle
#     file property-visibility.js), the code path of advisory GHSA-hhx9-57xq-r5rw
#   - an umbraco-package.json whose version differs from the package version
#   - an Umbraco dependency that declares one exact version instead of the supported range
#
#   bash build/ci/test-inspect-nupkg.sh [<folder with one nupkg>]    default: artifacts/nupkg
#
# Needs unzip and python3 (or python) for rewriting the zip.
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

folder=${1:-${PACK_OUTPUT:-artifacts/nupkg}}
assets=staticwebassets/App_Plugins/UmbracoCommunityPropertyVisibility

shopt -s nullglob
packages=("$folder"/*.nupkg)
[ ${#packages[@]} -eq 1 ] || fail "expected exactly one .nupkg in $folder, found ${#packages[@]}; run build/ci/pack.sh first"
original=${packages[0]}
package_name=$(basename "$original")
version=$(unzip -p "$original" '*.nuspec' | sed -n 's:.*<version>\(.*\)</version>.*:\1:p' | head -n 1)
[ -n "$version" ] || fail "no version in the nuspec of $original"

python=
for candidate in python3 python; do
	if command -v "$candidate" > /dev/null && "$candidate" -c 'import zipfile' > /dev/null 2>&1; then
		python=$candidate
		break
	fi
done
[ -n "$python" ] || fail "python3 (or python) is needed to write the corrupted copies"

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# corrupt <case> add <entry>                           copy of the package with one extra entry
# corrupt <case> remove <entry>                        copy of the package without that entry
# corrupt <case> set-version <entry> <version>         copy with the "version" of a JSON entry replaced
# corrupt <case> strip <entry> <text>                  copy with every occurrence of the text removed from that entry
# corrupt <case> append <entry> <text>                 copy with the text appended to that entry
# corrupt <case> set-dependency <dependency> <version> copy whose nuspec declares another version for that dependency
corrupt() {
	local case=$1
	shift
	mkdir -p "$work/$case"
	"$python" - "$original" "$work/$case/$package_name" "$@" << 'PY'
import re
import sys
import zipfile

source, target, mode, entry = sys.argv[1:5]
with zipfile.ZipFile(source) as original, zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as copy:
    names = original.namelist()
    if mode == "add" and entry in names:
        sys.exit(f"{entry} is already in the package")
    if mode in ("remove", "set-version", "strip", "append") and entry not in names:
        sys.exit(f"{entry} is not in the package")
    for info in original.infolist():
        if mode == "remove" and info.filename == entry:
            continue
        data = original.read(info.filename)
        if mode == "set-version" and info.filename == entry:
            text, count = re.subn(r'("version"\s*:\s*")[^"]*(")', lambda m: m.group(1) + sys.argv[5] + m.group(2), data.decode("utf-8"), count=1)
            if count != 1:
                sys.exit(f"no version in {entry}")
            data = text.encode("utf-8")
        if mode == "strip" and info.filename == entry:
            removed = sys.argv[5].encode("utf-8")
            if removed not in data:
                sys.exit(f"'{sys.argv[5]}' is not in {entry}")
            data = data.replace(removed, b"")
        if mode == "append" and info.filename == entry:
            data = data + sys.argv[5].encode("utf-8")
        if mode == "set-dependency" and info.filename.endswith(".nuspec"):
            pattern = r'(<dependency id="' + re.escape(entry) + r'" version=")[^"]*(")'
            text, count = re.subn(pattern, lambda m: m.group(1) + sys.argv[5] + m.group(2), data.decode("utf-8"))
            if count == 0:
                sys.exit(f"no dependency {entry} in {info.filename}")
            data = text.encode("utf-8")
        copy.writestr(info, data)
    if mode == "add":
        copy.writestr(entry, b"leaked build input\n")
PY
}

# expect_rejected <case> <text the failure must contain>
expect_rejected() {
	local case=$1 expected=$2 output
	if output=$(bash tools/inspect-nupkg.sh "$work/$case" "$version" "$PACKAGE_DIR" 2>&1); then
		tail -n 20 <<< "$output"
		fail "inspect-nupkg.sh accepted the package with $case"
	fi
	# The inspection writes GitHub error annotations; these failures are expected, so print them as plain text.
	local reason
	reason=$(grep -E '^::error::' <<< "$output" | tail -n 1 | sed 's/^::error:://')
	if ! grep -qF -- "$expected" <<< "$reason"; then
		tail -n 20 <<< "$output" | sed 's/^::error:://'
		fail "inspect-nupkg.sh rejected the package with $case, but not because of '$expected'"
	fi
	echo "ok: rejected the package with $case: $reason"
}

group "Leak test for tools/inspect-nupkg.sh ($package_name, version $version)"
manifest_entry=$assets/umbraco-package.json

# Entries outside the allowlist, each rejected by name. The two secrets.json cases are the ones a denylist let through:
# a stray file in the client output folder becomes a static web asset that every consuming site serves.
unexpected_entries=(
	"source-map $assets/property-visibility.js.map"
	"client-project Client/package.json"
	"typescript-source $assets/bundle.manifests.ts"
	"secrets-in-client-output $assets/secrets.json"
	"secrets-in-staticwebassets staticwebassets/secrets.json"
	"content-file content/appsettings.Production.json"
)
for unexpected in "${unexpected_entries[@]}"; do
	case_name=${unexpected%% *}
	entry=${unexpected#* }
	corrupt "$case_name" add "$entry"
	expect_rejected "$case_name" "$entry"
done

corrupt missing-license remove LICENSE
expect_rejected missing-license "LICENSE is missing"

corrupt missing-third-party-notices remove THIRD-PARTY-NOTICES.md
expect_rejected missing-third-party-notices "THIRD-PARTY-NOTICES.md is missing"

api_chunk=$(unzip -Z1 "$original" | grep -E "^$assets/client\.gen-[A-Za-z0-9_-]+\.js$" | head -n 1 || true)
[ -n "$api_chunk" ] || fail "$original has no $assets/client.gen-<hash>.js"
corrupt hey-api-notice strip "$api_chunk" "Copyright (c) Hey API"
expect_rejected hey-api-notice "does not carry the Hey API licence notice"

# The prefixes buildClientParams maps (src/api/core/params.gen.ts), in the chunk that holds the client code and in the
# bundle file the manifest loads: every chunk is searched.
corrupt hey-api-build-client-params-body append "$api_chunk" ';const e={$body_:"body",$headers_:"headers"};'
expect_rejected hey-api-build-client-params-body "contains \$body_"
corrupt hey-api-build-client-params-query append "$assets/property-visibility.js" ';const e={$path_:"path",$query_:"query"};'
expect_rejected hey-api-build-client-params-query "contains \$query_"

corrupt manifest-version set-version "$manifest_entry" 9.9.9
expect_rejected manifest-version "umbraco-package.json says version 9.9.9"

corrupt exact-umbraco-dependency set-dependency Umbraco.Cms.Web.Common "[17.7.0]"
expect_rejected exact-umbraco-dependency "depends on Umbraco.Cms.Web.Common [17.7.0]"

if ! output=$(bash tools/inspect-nupkg.sh "$folder" "$version" "$PACKAGE_DIR" 2>&1); then
	tail -n 20 <<< "$output"
	fail "inspect-nupkg.sh rejected the original package $original"
fi
echo "ok: accepted the original package"
end_group
