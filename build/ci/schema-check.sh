#!/usr/bin/env bash
# Fails when the committed JSON schemas of the package differ from what the options model generates now.
# Uses the generator built by build.sh (--no-build: a rebuild with other global properties would rerun the client build).
#
#   bash build/ci/schema-check.sh
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

group "JSON schema drift check"
run dotnet run --project "$SCHEMA_GENERATOR_DIR" -c "$CONFIGURATION" --no-build -- --out "$PACKAGE_DIR" --check
end_group
