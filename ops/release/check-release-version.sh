#!/usr/bin/env bash
# Verifies that a release tag matches the repository CHANGELOG.md, the single source of version numbers
# (docs/release/release-process.md). Run by the "Deploy prod tag" workflow before any SSH step, and by the
# release-manager agent before it creates a tag.
#
#   ops/release/check-release-version.sh v1.1.0 [path/to/CHANGELOG.md]
#
# Passes only when the tag is vMAJOR.MINOR.PATCH and the newest "## [x.y.z] - YYYY-MM-DD" heading in the
# changelog is exactly that version. Prints nothing secret; output goes to the public Actions log.

set -euo pipefail

tag="${1:?usage: check-release-version.sh <vMAJOR.MINOR.PATCH> [CHANGELOG.md]}"
changelog="${2:-CHANGELOG.md}"

fail() { printf 'release check FAILED: %s\n' "$*" >&2; exit 1; }

[[ "$tag" =~ ^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ ]] ||
  fail "tag '$tag' is not vMAJOR.MINOR.PATCH"
version="${tag#v}"

[[ -f "$changelog" ]] || fail "$changelog not found"
grep -q '^## \[Unreleased\]$' "$changelog" || fail "$changelog has no '## [Unreleased]' section"

heading="$(grep -m1 -E '^## \[[0-9]+\.[0-9]+\.[0-9]+\] - [0-9]{4}-[0-9]{2}-[0-9]{2}$' "$changelog" || true)"
[[ -n "$heading" ]] || fail "$changelog has no released version heading"
top="$(sed -E 's/^## \[([^]]+)\].*/\1/' <<<"$heading")"

[[ "$top" == "$version" ]] ||
  fail "tag $tag does not match the newest CHANGELOG.md release ($top); add the release entry before tagging"

grep -q "^\[$version\]: https://" "$changelog" || fail "$changelog has no compare link for [$version]"

printf 'release check passed: %s matches CHANGELOG.md (%s)\n' "$tag" "$heading"
