#!/usr/bin/env bash
# Compares the backoffice sources the client relies on (backoffice-sources.txt next to this script) between the last
# verified Umbraco version of the same major, the highest of that major in Constants.TestedUmbracoVersions (one list per
# major), and UMBRACO_CMS_VERSION, from the
# @umbraco-cms/backoffice npm packages, as compat.yml does. Each file is reprinted without comments by the client's
# TypeScript before the diff, so changes to comments and formatting do not show.
#
#   UMBRACO_CMS_VERSION=17.8.0 bash build/ci/compare-backoffice-sources.sh
#   UMBRACO_CMS_VERSION=17.8.0 bash build/ci/compare-backoffice-sources.sh 17.6.2    (another version to compare with)
#
# Writes to PV_COMPARE_DIR (default artifacts/backoffice-compare): summary.md (the table), report.md (the table and the
# diff of every changed file, each cut at PV_COMPARE_MAX_LINES lines, default 200) and sources.diff (every diff in
# full). In GitHub Actions it also appends report.md to the step summary and sets the step outputs `base-version`,
# `tested` (true when UMBRACO_CMS_VERSION is in TestedUmbracoVersions) and `changed` (the number of files that differ).
# Needs the client's node_modules (build.sh installs them), node, npm, tar and diff.
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

sources_file=build/ci/backoffice-sources.txt
compare_dir=${PV_COMPARE_DIR:-artifacts/backoffice-compare}
max_lines=${PV_COMPARE_MAX_LINES:-200}
typescript=$CLIENT_DIR/node_modules/typescript

[ -n "$UMBRACO_CMS_VERSION" ] || fail "set UMBRACO_CMS_VERSION to the Umbraco version to compare with the last verified one"
[ -d "$typescript" ] || fail "$typescript not found; run build/ci/build.sh first"
[[ $max_lines =~ ^[0-9]+$ ]] || fail "PV_COMPARE_MAX_LINES must be a number, got '$max_lines'"

target=$UMBRACO_CMS_VERSION
target_major=${target%%.*}
# The versions of the target's major in `TestedUmbracoVersions = [new Version(17, 6, 2), new Version(17, 7, 0)];` (one
# such line per Umbraco major), one per line.
tested_versions=$(grep -E 'TestedUmbracoVersions *=' "$PACKAGE_DIR/Constants.cs" |
	grep -oE 'new Version\( *[0-9]+, *[0-9]+, *[0-9]+ *\)' |
	sed -E 's/new Version\( *([0-9]+), *([0-9]+), *([0-9]+) *\)/\1.\2.\3/' |
	grep -E "^$target_major\." || true)
[ -n "$tested_versions" ] || fail "no Umbraco $target_major versions found in TestedUmbracoVersions in $PACKAGE_DIR/Constants.cs"
if [ -n "${1:-}" ]; then
	base=$1
	base_label="Umbraco $base"
else
	base=$(sort -V <<< "$tested_versions" | tail -n 1)
	base_label="Umbraco $base (the last verified version)"
fi
tested=false
if grep -qxF "$target" <<< "$tested_versions"; then tested=true; fi

trim() {
	local text=$1
	text=${text#"${text%%[![:space:]]*}"}
	printf '%s' "${text%"${text##*[![:space:]]}"}"
}

paths=()
descriptions=()
while IFS= read -r line || [ -n "$line" ]; do
	line=${line%$'\r'}
	[[ -z $(trim "$line") || $line == \#* ]] && continue
	[[ $line == *"|"* ]] || fail "$sources_file: no \"|\" between the path and the description in: $line"
	paths+=("$(trim "${line%%|*}")")
	descriptions+=("$(trim "${line#*|}")")
done < "$sources_file"
[ ${#paths[@]} -gt 0 ] || fail "$sources_file lists no files"

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# Downloads the npm package of one version (npm pack runs no package scripts) and reprints the listed files, without
# comments, into $work/normalized/<version>. A file the version does not have is left out.
# shellcheck disable=SC2016  # JavaScript, not a shell expansion
reprint='
const [typescript, from, to, ...files] = process.argv.slice(1);
const fs = require("fs");
const path = require("path");
const ts = require(path.resolve(typescript));
const printer = ts.createPrinter({ removeComments: true, newLine: ts.NewLineKind.LineFeed });
for (const file of files) {
	const source = path.join(from, file);
	if (!fs.existsSync(source)) continue;
	const parsed = ts.createSourceFile(file, fs.readFileSync(source, "utf8"), ts.ScriptTarget.Latest, false, ts.ScriptKind.JS);
	const target = path.join(to, file);
	fs.mkdirSync(path.dirname(target), { recursive: true });
	fs.writeFileSync(target, printer.printFile(parsed));
}
'
prepare() {
	local version=$1 tarball
	group "@umbraco-cms/backoffice $version"
	mkdir -p "$work/$version"
	tarball=$(cd "$work/$version" && npm pack --silent --ignore-scripts "@umbraco-cms/backoffice@$version" | tail -n 1) ||
		fail "could not download @umbraco-cms/backoffice $version from npm"
	run tar -xzf "$work/$version/$tarball" -C "$work/$version"
	node -e "$reprint" "$typescript" "$work/$version/package/dist-cms" "$work/normalized/$version" "${paths[@]}"
	end_group
}

mkdir -p "$compare_dir"
summary=$compare_dir/summary.md
report=$compare_dir/report.md
full_diff=$compare_dir/sources.diff
: > "$full_diff"
changed=0

if same_version "$base" "$target"; then
	printf '## Backoffice sources: Umbraco %s\n\nThe same version as %s: nothing to compare.\n' "$target" "$base_label" > "$summary"
	cp "$summary" "$report"
else
	prepare "$base"
	prepare "$target"

	rows=""
	details=""
	for i in "${!paths[@]}"; do
		path=${paths[$i]}
		old=$work/normalized/$base/$path
		new=$work/normalized/$target/$path
		if [ ! -f "$old" ] && [ ! -f "$new" ]; then
			status="not in either version"
		elif [ ! -f "$new" ]; then
			status="**not found** (moved or removed)"
			changed=$((changed + 1))
		elif [ ! -f "$old" ]; then
			status="**new** (not in $base)"
			changed=$((changed + 1))
		else
			file_diff=$(diff -u --label "$base/$path" --label "$target/$path" "$old" "$new" || true)
			if [ -z "$file_diff" ]; then
				status=unchanged
			else
				changed=$((changed + 1))
				count=$(grep -E '^[-+]' <<< "$file_diff" | grep -cvE '^(\+\+\+|---) ' || true)
				status="**$count lines changed**"
				printf '%s\n' "$file_diff" >> "$full_diff"
				total=$(wc -l <<< "$file_diff" | tr -d ' ')
				cut_note=""
				if [ "$total" -gt "$max_lines" ]; then
					cut_note=$'\n'"(the first $max_lines of $total lines; sources.diff in the run's artifact has all of them)"
				fi
				details+=$'\n'"<details><summary><code>$path</code>: $count lines changed</summary>"$'\n\n'
				details+='````diff'$'\n'"$(head -n "$max_lines" <<< "$file_diff")"$'\n''````'"$cut_note"$'\n\n'"</details>"$'\n'
			fi
		fi
		rows+="| \`$path\` | ${descriptions[$i]} | $status |"$'\n'
	done

	{
		printf '## Backoffice sources: Umbraco %s compared with %s\n\n' "$target" "$base"
		printf 'The %d files the client relies on (`build/ci/backoffice-sources.txt`), from the `@umbraco-cms/backoffice` npm packages of %s and of Umbraco %s, reprinted without comments: %d differ.\n\n' \
			"${#paths[@]}" "$base_label" "$target" "$changed"
		printf '| File | What the package relies on | In %s |\n|---|---|---|\n%s' "$target" "$rows"
	} > "$summary"
	{
		cat "$summary"
		printf '%s' "$details"
	} > "$report"
fi

cat "$summary"
if in_github_actions; then
	{
		echo "base-version=$base"
		echo "tested=$tested"
		echo "changed=$changed"
	} >> "$GITHUB_OUTPUT"
	cat "$report" >> "$GITHUB_STEP_SUMMARY"
fi
