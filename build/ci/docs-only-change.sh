#!/usr/bin/env bash
# Whether a change touches only documentation: Markdown files and the docs folder. ci.yml skips the acceptance suite for
# such a change, because none of these files reach the test site; the unit tests still check the links in the docs, and
# the pack step still packs the NuGet readme and icon.
#
#   bash build/ci/docs-only-change.sh                      (HEAD compared with its first parent, as ci.yml does)
#   bash build/ci/docs-only-change.sh origin/main          (HEAD compared with another commit)
#   bash build/ci/docs-only-change.sh <base> <commit>      (any two commits)
#
# Prints the changed files and docs-only=true or docs-only=false; in GitHub Actions it also sets the step output
# `docs-only`. When the commits cannot be compared, or nothing changed, the answer is false, so everything runs.
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

base=${1:-HEAD^1}
head=${2:-HEAD}
docs_only=false

commit_exists() {
	git rev-parse --verify --quiet "$1^{commit}" > /dev/null
}

if ! commit_exists "$base" || ! commit_exists "$head"; then
	echo "$base or $head is not in this checkout: everything runs"
else
	# Without rename detection a file renamed to Markdown also lists its old name.
	files=$(git diff --name-only --no-renames "$base" "$head")
	if [ -z "$files" ]; then
		echo "No files changed between $base and $head: everything runs"
	else
		docs_only=true
		while IFS= read -r file; do
			case $file in
				*.md | docs/*) echo "  docs   $file" ;;
				*)
					echo "  other  $file"
					docs_only=false
					;;
			esac
		done <<< "$files"
	fi
fi

echo "docs-only=$docs_only"
if in_github_actions; then
	echo "docs-only=$docs_only" >> "$GITHUB_OUTPUT"
fi
