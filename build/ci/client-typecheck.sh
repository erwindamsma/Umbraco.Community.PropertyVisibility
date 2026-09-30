#!/usr/bin/env bash
# Type-checks the backoffice client against the @umbraco-cms/backoffice typings of UMBRACO_CMS_VERSION instead of the
# committed lockfile's version, as compat.yml does. build.sh builds the client from the lockfile (the dependency floor),
# so without this step a compatibility run never sees a typings change of a newer Umbraco.
#
#   UMBRACO_CMS_VERSION=17.7.0 bash build/ci/client-typecheck.sh
#
# Works on a copy of the client in a temporary folder: the client's node_modules and lockfile stay as they are.
# --force: a newer backoffice can require a peer outside the range of one of the client's own dev dependencies (for
# example an @hey-api/openapi-ts range above the client's ^0.99.0; 17.7.0 installs without a conflict); --force keeps the
# installed peers and accepts that conflict, where --legacy-peer-deps would remove the backoffice's peers (lit, rxjs,
# @umbraco-ui/uui) and break the typings.
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

[ -n "$UMBRACO_CMS_VERSION" ] || fail "set UMBRACO_CMS_VERSION to the Umbraco version whose backoffice typings to check against"

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

group "Client typings against @umbraco-cms/backoffice $UMBRACO_CMS_VERSION"
tar -C "$CLIENT_DIR" --exclude=./node_modules -cf - . | tar -C "$work" -xf -
(
	cd "$work"
	run npm ci --no-audit --no-fund
	run npm install --no-save --no-audit --no-fund --force "@umbraco-cms/backoffice@$UMBRACO_CMS_VERSION"
	installed=$(node -p "require('./node_modules/@umbraco-cms/backoffice/package.json').version")
	same_version "$installed" "$UMBRACO_CMS_VERSION" || fail "installed @umbraco-cms/backoffice $installed, expected $UMBRACO_CMS_VERSION"
	echo "@umbraco-cms/backoffice $installed"
	run npm run typecheck
)
end_group
