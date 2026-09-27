#!/usr/bin/env bash
# Runs the unit tests against the output of build.sh (same environment variables; results in TestResults/).
#
#   bash build/ci/test.sh
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

group "Unit tests"
run dotnet test "$SOLUTION" -c "$CONFIGURATION" --no-build --logger trx --results-directory TestResults "${msbuild_properties[@]}"
end_group
