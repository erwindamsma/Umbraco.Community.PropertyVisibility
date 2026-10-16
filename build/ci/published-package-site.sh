#!/usr/bin/env bash
# Builds a copy of the test site that installs Umbraco.Community.PropertyVisibility from nuget.org instead of the
# ProjectReference, as compat.yml does: the package the way a site gets it, with its buildTransitive props (the JSON
# schemas) and its static web assets (the backoffice client). The copy goes to PV_TEST_SITE_DIR (default
# artifacts/published-package-site); start-test-site.sh, acceptance.sh and stop-test-site.sh run it when PV_TEST_SITE_DIR
# names the same folder.
#
#   export UMBRACO_CMS_VERSION=18.2.1 PV_TEST_SITE_DIR=artifacts/published-package-site
#   bash build/ci/published-package-site.sh && bash build/ci/start-test-site.sh && bash build/ci/acceptance.sh; \
#     bash build/ci/stop-test-site.sh
#
# Environment (besides common.sh):
#   UMBRACO_CMS_VERSION   the exact Umbraco version of the site (required). Its major picks the package line: 1.x for
#                         Umbraco 17, 2.x for Umbraco 18
#   PV_PACKAGE_VERSION    the package version to install (default: the latest listed, stable version of that line)
#
# The copy holds the test site's tracked files (the uSync seed, views, appsettings) and a project file of its own with
# exact versions and no central package management; uSync and ICU get the versions src/Directory.Packages.props gives
# that Umbraco major. After the build it checks that the site runs the expected Umbraco and package versions, and that
# the package's buildTransitive props copied the rules file schema and referenced the appsettings schema. In GitHub
# Actions it sets the step output `package-version`. Needs git, tar, curl and node.
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

package_id=Umbraco.Community.PropertyVisibility
site_dir=${PV_TEST_SITE_DIR:-artifacts/published-package-site}

[ -n "$UMBRACO_CMS_VERSION" ] || fail "set UMBRACO_CMS_VERSION to the Umbraco version of the site"
major=${UMBRACO_CMS_VERSION%%.*}
case "$major" in
	17) line=1 ;;
	18) line=2 ;;
	*) fail "Umbraco $major is not supported: the package has 1.x for Umbraco 17 and 2.x for Umbraco 18" ;;
esac
case "$site_dir" in
	artifacts/?*) ;;
	*) fail "PV_TEST_SITE_DIR must be a folder under artifacts/ (got '$site_dir')" ;;
esac
case "$site_dir" in *..*) fail "PV_TEST_SITE_DIR must not contain '..' (got '$site_dir')" ;; esac

group "Package version"
version=${PV_PACKAGE_VERSION:-}
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
[[ $version =~ ^$line\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] \
	|| fail "$package_id $version is not a $line.x version, the package line for Umbraco $major"
echo "$package_id $version on Umbraco.Cms $UMBRACO_CMS_VERSION"
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

	<!-- Written by build/ci/published-package-site.sh: the test site with $package_id from nuget.org. -->
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
echo "$(find "$site_dir" -type f | wc -l | tr -d ' ') files"
end_group

group "Build the site ($CONFIGURATION)"
run dotnet build "$site_dir/$TEST_SITE_NAME.csproj" -c "$CONFIGURATION"
end_group

group "Check the site"
runtime=$(test_site_umbraco_version "$site_dir")
same_version "$runtime" "$UMBRACO_CMS_VERSION" || fail "the site runs Umbraco.Cms.Core ${runtime:-unknown}, expected $UMBRACO_CMS_VERSION"
deps=$site_dir/bin/$CONFIGURATION/$TARGET_FRAMEWORK/$TEST_SITE_NAME.deps.json
grep -qiF "\"$package_id/$version\"" "$deps" || fail "$deps does not name $package_id $version"
[ -f "$site_dir/PropertyVisibility.config-schema.json" ] \
	|| fail "the package's buildTransitive props did not copy PropertyVisibility.config-schema.json into the site"
grep -qF "appsettings-schema.$package_id.json" "$site_dir/appsettings-schema.json" \
	|| fail "appsettings-schema.json does not reference appsettings-schema.$package_id.json"
echo "Umbraco.Cms.Core $runtime, $package_id $version, both JSON schemas in place"
end_group

if in_github_actions; then
	echo "package-version=$version" >> "$GITHUB_OUTPUT"
fi
