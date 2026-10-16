#!/usr/bin/env bash
# Builds a copy of the test site that installs Umbraco.Community.PropertyVisibility as a package instead of the
# ProjectReference: the package the way a site gets it, with its buildTransitive props (the JSON schemas) and its static
# web assets (the backoffice client). compat.yml installs the latest published version from nuget.org, ci.yml the nupkg
# its own run packed (PV_PACKAGE_SOURCE). The copy goes to PV_TEST_SITE_DIR (default artifacts/published-package-site);
# start-test-site.sh, acceptance.sh and stop-test-site.sh run it when PV_TEST_SITE_DIR names the same folder.
#
#   export UMBRACO_CMS_VERSION=18.2.1 PV_TEST_SITE_DIR=artifacts/published-package-site
#   bash build/ci/published-package-site.sh && bash build/ci/start-test-site.sh && bash build/ci/acceptance.sh; \
#     bash build/ci/stop-test-site.sh
#
#   PV_PACKAGE_SOURCE=artifacts/nupkg bash build/ci/published-package-site.sh    (the nupkg of pack.sh)
#
# Environment (besides common.sh):
#   UMBRACO_CMS_VERSION   the exact Umbraco version of the site. Its major picks the package line: 1.x for Umbraco 17,
#                         2.x for Umbraco 18. Required for a package from nuget.org; with PV_PACKAGE_SOURCE it defaults to
#                         the lowest version the nupkg supports (the floor of its Umbraco dependencies)
#   PV_PACKAGE_VERSION    the package version to install from nuget.org (default: the latest listed, stable version of
#                         that line); with PV_PACKAGE_SOURCE it must be the nupkg's version
#   PV_PACKAGE_SOURCE     a folder with one nupkg of the package, installed instead of a version from nuget.org. A 0.0.0
#                         build version (0.0.0-local, 0.0.0-ci.<run>) is packed again under the same number, so its copy in
#                         the NuGet global packages folder is removed before the build
#
# The copy holds the test site's tracked files (the uSync seed, views, appsettings) and a project file of its own with
# exact versions and no central package management; uSync and ICU get the versions src/Directory.Packages.props gives
# that Umbraco major. After the build it checks that the site runs the expected Umbraco and package versions (with
# PV_PACKAGE_SOURCE: the nupkg from that folder, byte for byte), and that the package's buildTransitive props copied the
# rules file schema and referenced the appsettings schema. In GitHub Actions it sets the step output `package-version`.
# Needs git, tar, curl, unzip, cmp and node.
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

package_id=Umbraco.Community.PropertyVisibility
site_dir=${PV_TEST_SITE_DIR:-artifacts/published-package-site}
source_dir=${PV_PACKAGE_SOURCE:-}
version=${PV_PACKAGE_VERSION:-}
nupkg=

case "$site_dir" in
	artifacts/?*) ;;
	*) fail "PV_TEST_SITE_DIR must be a folder under artifacts/ (got '$site_dir')" ;;
esac
case "$site_dir" in *..*) fail "PV_TEST_SITE_DIR must not contain '..' (got '$site_dir')" ;; esac

if [ -n "$source_dir" ]; then
	group "Package in $source_dir"
	[ -d "$source_dir" ] || fail "PV_PACKAGE_SOURCE $source_dir is not a folder"
	nupkgs=()
	for candidate in "$source_dir/$package_id".[0-9]*.nupkg; do
		if [ -f "$candidate" ]; then nupkgs+=("$candidate"); fi
	done
	[ ${#nupkgs[@]} -eq 1 ] || fail "PV_PACKAGE_SOURCE must hold one $package_id nupkg, $source_dir holds ${#nupkgs[@]}"
	nupkg=${nupkgs[0]}
	nuspec=$(unzip -p "$nupkg" '*.nuspec') || fail "could not read the nuspec of $nupkg"
	nupkg_version=$(sed -n 's:.*<version>\(.*\)</version>.*:\1:p' <<< "$nuspec" | head -n 1)
	# [18.2.1, 19.0.0) gives 18.2.1, the version ci.yml's build resolved.
	floor=$(sed -n -E 's/.*<dependency id="Umbraco\.Cms\.Web\.Common" version="\[([^,]+),.*/\1/p' <<< "$nuspec" | head -n 1)
	[ -n "$nupkg_version" ] && [ -n "$floor" ] || fail "the nuspec of $nupkg names no version or no Umbraco.Cms.Web.Common range"
	if [ -n "$version" ] && ! same_version "$version" "$nupkg_version"; then
		fail "PV_PACKAGE_VERSION is $version, but $nupkg is $nupkg_version"
	fi
	version=$nupkg_version
	if [ -z "$UMBRACO_CMS_VERSION" ]; then
		UMBRACO_CMS_VERSION=$floor
	elif [ "${UMBRACO_CMS_VERSION%%.*}" != "${floor%%.*}" ]; then
		fail "$nupkg supports Umbraco $floor and later ${floor%%.*}.x, UMBRACO_CMS_VERSION is $UMBRACO_CMS_VERSION"
	fi
	echo "$nupkg: $package_id $version for Umbraco $floor and later ${floor%%.*}.x"
	end_group
fi

[ -n "$UMBRACO_CMS_VERSION" ] || fail "set UMBRACO_CMS_VERSION to the Umbraco version of the site"
major=${UMBRACO_CMS_VERSION%%.*}
case "$major" in
	17) line=1 ;;
	18) line=2 ;;
	*) fail "Umbraco $major is not supported: the package has 1.x for Umbraco 17 and 2.x for Umbraco 18" ;;
esac
[ -z "$UMBRACO_MAJOR" ] || [ "$UMBRACO_MAJOR" = "$major" ] \
	|| fail "the site would run Umbraco $UMBRACO_CMS_VERSION, but UMBRACO_MAJOR is $UMBRACO_MAJOR"

group "Package version"
if [ -z "$version" ]; then
	# The registration index says per version whether it is listed; the flat container also lists unlisted versions.
	registration=https://api.nuget.org/v3/registration5-gz-semver2/$(tr '[:upper:]' '[:lower:]' <<< "$package_id")/index.json
	# shellcheck disable=SC2016  # JavaScript, not a shell expansion
	latest='
const line = process.argv[1];
const document = JSON.parse(require("fs").readFileSync(0, "utf8"));
const stable = new RegExp(`^${line}\\.\\d+\\.\\d+$`);
const versions = [];
for (const page of document.items ?? []) {
	if (!Array.isArray(page.items)) throw new Error(`registration page ${page["@id"]} is not inlined`);
	for (const leaf of page.items) {
		const entry = leaf.catalogEntry;
		if (entry.listed !== false && stable.test(entry.version)) versions.push(entry.version);
	}
}
const parts = (version) => version.split(".").map(Number);
versions.sort((a, b) => { const [x, y] = [parts(a), parts(b)]; return x[0] - y[0] || x[1] - y[1] || x[2] - y[2]; });
if (versions.length > 0) console.log(versions[versions.length - 1]);
'
	version=$(curl -fsSL --compressed --retry 3 "$registration" | node -e "$latest" "$line") \
		|| fail "could not read the versions of $package_id from $registration"
	[ -n "$version" ] || fail "nuget.org lists no stable $package_id $line.x"
fi
# A 0.0.0 build version (0.0.0-local, 0.0.0-ci.<run>) has no line; its nupkg's Umbraco range gave the major above.
if [ -z "$nupkg" ] || [[ $version != 0.0.0-* ]]; then
	[[ $version =~ ^$line\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] \
		|| fail "$package_id $version is not a $line.x version, the package line for Umbraco $major"
fi
echo "$package_id $version${nupkg:+ from $source_dir} on Umbraco.Cms $UMBRACO_CMS_VERSION"
end_group

group "Versions of uSync and ICU (src/Directory.Packages.props, Umbraco $major)"
package_versions=$(dotnet msbuild "$TEST_SITE_PROJECT_DIR/$TEST_SITE_NAME.csproj" -p:UmbracoMajor="$major" -getItem:PackageVersion)
package_version_of() {
	node -e '
const [id] = process.argv.slice(1);
const items = JSON.parse(require("fs").readFileSync(0, "utf8")).Items.PackageVersion;
const item = items.find((candidate) => candidate.Identity === id);
if (item) console.log(item.Version);
' "$1" <<< "$package_versions"
}
usync=$(package_version_of uSync)
icu=$(package_version_of Microsoft.ICU.ICU4C.Runtime)
[ -n "$usync" ] && [ -n "$icu" ] || fail "src/Directory.Packages.props gives no uSync or Microsoft.ICU.ICU4C.Runtime version for Umbraco $major"
echo "uSync $usync, Microsoft.ICU.ICU4C.Runtime $icu"
end_group

group "Copy the test site to $site_dir"
rm -rf "${site_dir:?}"
mkdir -p "$site_dir"
# Tracked files only: no build output, database, logs or generated schemas. The project file is written below.
(cd "$TEST_SITE_PROJECT_DIR" && git ls-files -z -- . ':!:*.csproj' | tar --null -T - -cf -) | tar -C "$site_dir" -xf -
# The ProjectReference becomes a PackageReference; the JSON schema props come from the package's buildTransitive folder.
cat > "$site_dir/$TEST_SITE_NAME.csproj" << EOF
<Project Sdk="Microsoft.NET.Sdk.Web">

	<!-- Written by build/ci/published-package-site.sh: the test site with $package_id from ${source_dir:-nuget.org}. -->
	<PropertyGroup>
		<TargetFramework>$TARGET_FRAMEWORK</TargetFramework>
		<ImplicitUsings>enable</ImplicitUsings>
		<Nullable>enable</Nullable>
		<CompressionEnabled>false</CompressionEnabled>
		<CopyRazorGenerateFilesToPublishDirectory>true</CopyRazorGenerateFilesToPublishDirectory>
	</PropertyGroup>

	<ItemGroup>
		<PackageReference Include="Umbraco.Cms" Version="[$UMBRACO_CMS_VERSION]" />
		<PackageReference Include="uSync" Version="[$usync]" />
		<PackageReference Include="Microsoft.ICU.ICU4C.Runtime" Version="[$icu]" />
		<PackageReference Include="$package_id" Version="[$version]" />
		<RuntimeHostConfigurationOption Include="System.Globalization.AppLocalIcu" Value="$icu" Condition="\$(RuntimeIdentifier.StartsWith('linux')) or \$(RuntimeIdentifier.StartsWith('win')) or ('\$(RuntimeIdentifier)' == '' and !\$([MSBuild]::IsOSPlatform('osx')))" />
	</ItemGroup>

</Project>
EOF
if [ -n "$nupkg" ]; then
	# <clear /> drops the sources of the repository's nuget.config, which applies to the copy as well.
	source_path=$(cd "$source_dir" && pwd)
	if command -v cygpath > /dev/null; then source_path=$(cygpath -w "$source_path"); fi
	cat > "$site_dir/nuget.config" << EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
	<!-- Written by build/ci/published-package-site.sh: $package_id from $source_dir, everything else from nuget.org. -->
	<packageSources>
		<clear />
		<add key="local" value="$source_path" />
		<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
	</packageSources>
</configuration>
EOF
fi
echo "$(find "$site_dir" -type f | wc -l | tr -d ' ') files"
end_group

group "Build the site ($CONFIGURATION)"
if [ -n "$nupkg" ]; then
	global_packages=$(dotnet nuget locals global-packages --list | tr -d '\r' | sed -n 's/^global-packages: //p')
	[ -n "$global_packages" ] || fail "dotnet nuget locals names no global packages folder"
	if command -v cygpath > /dev/null; then global_packages=$(cygpath -u "$global_packages"); fi
	cached=${global_packages%/}/$(tr '[:upper:]' '[:lower:]' <<< "$package_id/$version")
	# Restore takes a cached version as it is, so an earlier 0.0.0-local (of either major) would win over this nupkg.
	if [[ $version == 0.0.0-* ]] && [ -d "$cached" ]; then
		run rm -rf "${cached:?}"
	fi
fi
run dotnet build "$site_dir/$TEST_SITE_NAME.csproj" -c "$CONFIGURATION"
end_group

group "Check the site"
runtime=$(test_site_umbraco_version "$site_dir")
same_version "$runtime" "$UMBRACO_CMS_VERSION" || fail "the site runs Umbraco.Cms.Core ${runtime:-unknown}, expected $UMBRACO_CMS_VERSION"
deps=$site_dir/bin/$CONFIGURATION/$TARGET_FRAMEWORK/$TEST_SITE_NAME.deps.json
grep -qiF "\"$package_id/$version\"" "$deps" || fail "$deps does not name $package_id $version"
if [ -n "$nupkg" ]; then
	installed=$cached/$(tr '[:upper:]' '[:lower:]' <<< "$package_id.$version").nupkg
	cmp -s "$nupkg" "$installed" \
		|| fail "the site restored $installed, which is not $nupkg (another $package_id $version in the NuGet global packages folder? remove $cached)"
fi
[ -f "$site_dir/PropertyVisibility.config-schema.json" ] \
	|| fail "the package's buildTransitive props did not copy PropertyVisibility.config-schema.json into the site"
grep -qF "appsettings-schema.$package_id.json" "$site_dir/appsettings-schema.json" \
	|| fail "appsettings-schema.json does not reference appsettings-schema.$package_id.json"
echo "Umbraco.Cms.Core $runtime, $package_id $version, both JSON schemas in place"
end_group

if in_github_actions; then
	echo "package-version=$version" >> "$GITHUB_OUTPUT"
fi
