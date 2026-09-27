#!/usr/bin/env bash
# Packs the package from the output of build.sh into artifacts/nupkg and inspects it (tools/inspect-nupkg.sh). In a CI
# build (CI=true or CONTINUOUS_INTEGRATION_BUILD=true, see common.sh) it also checks that the assembly and its embedded
# PDB carry deterministic /_/ source paths and that no file in the package names the build machine's paths.
#
#   bash build/ci/pack.sh
#   PACK_OUTPUT=artifacts/other bash build/ci/pack.sh    (a folder under artifacts/; PACK_OUTPUT_ANYWHERE=true for another)
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

output=${PACK_OUTPUT:-artifacts/nupkg}
case "$output" in
	artifacts/?*) ;;
	*) [ "${PACK_OUTPUT_ANYWHERE:-false}" = "true" ] || fail "PACK_OUTPUT must be a folder under artifacts/ (got '$output'); set PACK_OUTPUT_ANYWHERE=true for another folder" ;;
esac
case "$output" in *..*) fail "PACK_OUTPUT must not contain '..' (got '$output')" ;; esac

# A package built against one exact Umbraco version would declare that exact version as its dependency; the inspection
# also checks the declared range (see below).
[ -z "$UMBRACO_CMS_VERSION" ] || fail "pack.sh packs the supported Umbraco range; unset UMBRACO_CMS_VERSION and run build.sh again"

group "Pack ($PACKAGE_VERSION)"
# inspect-nupkg.sh checks every nupkg in the folder against this version, so only this pack's packages may be there.
# Only package files are removed: the folder may hold other files (the test site's PID file and log live in artifacts/).
mkdir -p "$output"
rm -f "$output"/*.nupkg "$output"/*.snupkg
run dotnet pack "$PACKAGE_PROJECT" -c "$CONFIGURATION" --no-build -p:SkipClientBuild=true "${msbuild_properties[@]}" -o "$output"
end_group

group "Inspect package"
run bash tools/inspect-nupkg.sh "$output" "$PACKAGE_VERSION" "$PACKAGE_DIR"
end_group

if ci_build; then
	group "Deterministic source paths"
	# A changed ContinuousIntegrationBuild alone does not make MSBuild recompile: after a local non-CI build, delete the
	# package project's bin and obj folders before building with CI=true.
	run dotnet run build/ci/check-deterministic-paths.cs -- "$output" "$REPO_ROOT" \
		|| fail "the package is not built with deterministic source paths (a stale local build? delete $PACKAGE_DIR/bin and $PACKAGE_DIR/obj)"
	end_group
fi
