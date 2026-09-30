#!/usr/bin/env bash
# Asserts what a packed Umbraco.Community.PropertyVisibility nupkg must and must not contain.
# Run by build/ci/pack.sh (ci.yml, release.yml) and proven by build/ci/test-inspect-nupkg.sh, which feeds it corrupted
# copies; runs locally in any bash with unzip (jq optional).
#
# Usage: bash tools/inspect-nupkg.sh <folder with *.nupkg> <expected version> [<package project folder>] [<Umbraco range>]
#   The package project folder (default src/Umbraco.Community.PropertyVisibility) holds the committed JSON schemas,
#   which the packed copies must equal byte for byte.
#   The Umbraco range (default: UmbracoCmsVersion in the Directory.Packages.props next to that folder, for example
#   [17.6.2, 18.0.0)) is the version every Umbraco dependency of the nuspec must declare, so a package restored against
#   one exact Umbraco version (-p:UmbracoCmsVersion=[17.7.0]) never passes.
set -euo pipefail

if [ $# -lt 2 ]; then
  echo "Usage: bash tools/inspect-nupkg.sh <folder with *.nupkg> <expected version> [<package project folder>] [<Umbraco range>]" >&2
  exit 2
fi

pkg_dir=$1
expected_version=$2
project_dir=${3:-src/Umbraco.Community.PropertyVisibility}
id=Umbraco.Community.PropertyVisibility
assets=staticwebassets/App_Plugins/UmbracoCommunityPropertyVisibility
schemas=("appsettings-schema.$id.json" "PropertyVisibility.config-schema.json")
umbraco_dependencies=(Umbraco.Cms.Api.Common Umbraco.Cms.Api.Management Umbraco.Cms.Web.Common)

fail() {
  echo "::error::$1"
  exit 1
}

if [ $# -ge 4 ]; then
  umbraco_range=$4
else
  packages_props=$(dirname "$project_dir")/Directory.Packages.props
  [ -f "$packages_props" ] || fail "$packages_props not found to read UmbracoCmsVersion from; pass the Umbraco range as the fourth argument"
  umbraco_range=$(sed -n 's:.*<UmbracoCmsVersion[^>]*>\(.*\)</UmbracoCmsVersion>.*:\1:p' "$packages_props" | head -n 1)
  [ -n "$umbraco_range" ] || fail "no UmbracoCmsVersion in $packages_props"
fi

shopt -s nullglob
packages=("$pkg_dir"/*.nupkg)
[ ${#packages[@]} -gt 0 ] || fail "no .nupkg in $pkg_dir"

for pkg in "${packages[@]}"; do
  echo "== $pkg"
  listing=$(unzip -Z1 "$pkg")
  echo "$listing"

  # Backoffice client, static web assets chain, JSON schemas and the props that wire them into a consuming site, and the
  # licence texts: LICENSE and THIRD-PARTY-NOTICES.md (the client bundle contains MIT-licensed Hey API code).
  required=(
    "LICENSE"
    "THIRD-PARTY-NOTICES.md"
    "lib/net10.0/$id.dll"
    "$assets/umbraco-package.json"
    "$assets/property-visibility.js"
    "build/Microsoft.AspNetCore.StaticWebAssets.props"
    "build/Microsoft.AspNetCore.StaticWebAssetEndpoints.props"
    "build/$id.props"
    "buildMultiTargeting/$id.props"
    "buildTransitive/$id.props"
    "buildTransitive/$id.JsonSchemas.props"
    "${schemas[@]}"
  )
  for entry in "${required[@]}"; do
    grep -qxF "$entry" <<< "$listing" || fail "$entry is missing from $pkg"
  done

  # Nothing else ships: every entry must be on this allowlist, so any stray file (a source map, a Client/ source, a
  # secrets.json left in the client output, a content file) fails the inspection, whatever its name. The client output
  # is JavaScript chunks and the manifest, flat in one folder.
  allowed_entries=(
    "[Content_Types].xml"
    "_rels/.rels"
    "$id.nuspec"
    "icon.png"
    "README_nuget.md"
    "LICENSE"
    "THIRD-PARTY-NOTICES.md"
    "${schemas[@]}"
    "lib/net10.0/$id.dll"
    "lib/net10.0/$id.xml"
    "build/Microsoft.AspNetCore.StaticWebAssets.props"
    "build/Microsoft.AspNetCore.StaticWebAssetEndpoints.props"
    "build/$id.props"
    "buildMultiTargeting/$id.props"
    "buildTransitive/$id.props"
    "buildTransitive/$id.JsonSchemas.props"
  )
  allowed_patterns=(
    '^package/services/metadata/core-properties/[A-Za-z0-9]+\.psmdcp$'
    '^staticwebassets/App_Plugins/UmbracoCommunityPropertyVisibility/([A-Za-z0-9_.-]+\.js|umbraco-package\.json)$'
  )
  unexpected=()
  while IFS= read -r entry; do
    [ -n "$entry" ] || continue
    allowed=false
    for candidate in "${allowed_entries[@]}"; do
      if [ "$entry" = "$candidate" ]; then
        allowed=true
        break
      fi
    done
    for pattern in "${allowed_patterns[@]}"; do
      if [ "$allowed" = false ] && [[ $entry =~ $pattern ]]; then
        allowed=true
      fi
    done
    [ "$allowed" = true ] || unexpected+=("$entry")
  done <<< "$listing"
  if [ ${#unexpected[@]} -gt 0 ]; then
    fail "$pkg contains files outside the allowlist, which must not ship: ${unexpected[*]}"
  fi
  echo "Every entry is on the allowlist"

  # The chunk with the Hey API client code (client.gen-<hash>.js, named in THIRD-PARTY-NOTICES.md) carries the MIT notice
  # itself, as a banner the client build prepends, so the notice travels with the file a site serves.
  api_chunks=$(grep -E "^$assets/client\.gen-[A-Za-z0-9_-]+\.js$" <<< "$listing" || true)
  [ -n "$api_chunks" ] || fail "$pkg has no $assets/client.gen-<hash>.js, the chunk THIRD-PARTY-NOTICES.md names"
  [ "$(wc -l <<< "$api_chunks")" -eq 1 ] || fail "$pkg has more than one client.gen chunk: $(tr '\n' ' ' <<< "$api_chunks")"
  api_chunk_code=$(unzip -p "$pkg" "$api_chunks")
  # The banner opens the file (a /*! comment survives minifiers) and holds the whole MIT notice: the copyright line
  # and the permission notice.
  for notice in 'Copyright (c) Hey API' 'Portions (c) Hey API, MIT licence, see THIRD-PARTY-NOTICES.md' \
    'Permission is hereby granted, free of charge'; do
    grep -qF "$notice" <<< "$api_chunk_code" \
      || fail "$api_chunks in $pkg does not carry the Hey API licence notice (the client build's banner): no '$notice'"
  done
  [ "$(head -n 1 <<< "$api_chunk_code")" = '/*!' ] \
    || fail "$api_chunks in $pkg does not carry the Hey API licence notice (the client build's banner) at its start"
  echo "$api_chunks carries the Hey API licence notice"

  # buildTransitive/<id>.props is the package's own file (the SDK's generation is switched off): it must keep the
  # static web assets import the SDK would have written, and add the JSON schema items.
  transitive=$(unzip -p "$pkg" "buildTransitive/$id.props")
  grep -qF "..\\buildMultiTargeting\\$id.props\"" <<< "$transitive" \
    || fail "buildTransitive/$id.props does not import buildMultiTargeting/$id.props (static web assets)"
  grep -qF "$id.JsonSchemas.props\"" <<< "$transitive" \
    || fail "buildTransitive/$id.props does not import $id.JsonSchemas.props"
  grep -qF "Microsoft.AspNetCore.StaticWebAssets.props\"" <<< "$(unzip -p "$pkg" "build/$id.props")" \
    || fail "build/$id.props does not import Microsoft.AspNetCore.StaticWebAssets.props"

  json_schemas=$(unzip -p "$pkg" "buildTransitive/$id.JsonSchemas.props")
  appsettings_item=$(grep -F "<UmbracoJsonSchemaFiles Include=\"\$(MSBuildThisFileDirectory)..\\appsettings-schema.$id.json\"" <<< "$json_schemas") \
    || fail "$id.JsonSchemas.props has no UmbracoJsonSchemaFiles item for appsettings-schema.$id.json"
  if grep -qF 'Reference="false"' <<< "$appsettings_item"; then
    fail "the appsettings schema must be referenced from appsettings-schema.json (no Reference=\"false\")"
  fi
  grep -F "<UmbracoJsonSchemaFiles Include=\"\$(MSBuildThisFileDirectory)..\\PropertyVisibility.config-schema.json\"" <<< "$json_schemas" \
    | grep -qF 'Reference="false"' \
    || fail "$id.JsonSchemas.props has no UmbracoJsonSchemaFiles item for PropertyVisibility.config-schema.json with Reference=\"false\""

  for schema in "${schemas[@]}"; do
    if [ -f "$project_dir/$schema" ]; then
      cmp -s <(unzip -p "$pkg" "$schema") "$project_dir/$schema" || fail "$schema in $pkg differs from $project_dir/$schema"
    else
      fail "$project_dir/$schema not found to compare with"
    fi
  done
  echo "JSON schemas and buildTransitive props are in place"

  # The package's own version: pack with -p:Version (which also stamps the manifest below), not -p:PackageVersion.
  nuspec_version=$(unzip -p "$pkg" '*.nuspec' | sed -n 's:.*<version>\(.*\)</version>.*:\1:p' | head -n 1)
  if [ "$nuspec_version" != "$expected_version" ]; then
    fail "$pkg is version $nuspec_version (nuspec), expected $expected_version; pack with -p:Version=$expected_version into an empty folder"
  fi
  echo "nuspec version $nuspec_version matches"

  # The supported Umbraco range, not the exact version of a one-off restore (dotnet pack --no-build takes the ranges from
  # the package project's last restore).
  nuspec=$(unzip -p "$pkg" '*.nuspec')
  for dependency in "${umbraco_dependencies[@]}"; do
    declared=$(grep -oE "<dependency id=\"$dependency\" version=\"[^\"]*\"" <<< "$nuspec" | sed -E 's/.*version="([^"]*)"/\1/' || true)
    [ -n "$declared" ] || fail "$pkg does not depend on $dependency"
    while read -r range; do
      [ "$range" = "$umbraco_range" ] || fail "$pkg depends on $dependency $range, expected $umbraco_range (packed after a restore with another UmbracoCmsVersion?)"
    done <<< "$declared"
  done
  echo "Umbraco dependencies declare $umbraco_range"

  manifest=$(unzip -p "$pkg" "$assets/umbraco-package.json")
  if command -v jq > /dev/null; then
    manifest_version=$(jq -r .version <<< "$manifest")
  else
    manifest_version=$(grep -oE '"version"[[:space:]]*:[[:space:]]*"[^"]*"' <<< "$manifest" | head -n 1 | sed -E 's/.*"([^"]*)"$/\1/')
  fi
  if [ "$manifest_version" != "$expected_version" ]; then
    fail "umbraco-package.json says version $manifest_version, the package is $expected_version"
  fi
  echo "umbraco-package.json version $manifest_version matches the package version"
done
