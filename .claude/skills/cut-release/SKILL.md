---
name: cut-release
description: Cut a Sapio production release (vX.Y.Z) in this repo - review develop since the last release, choose the SemVer number, update CHANGELOG.md, merge develop into prod, push the vX.Y.Z tag that deploys production, and watch the deploy. Use only when the owner explicitly asks to release or ship a version. Pushing a v* tag deploys production.
argument-hint: "[X.Y.Z]  (optional; omit to let the skill choose the number)"
disable-model-invocation: true
---

# Cut a production release

This skill runs the procedure in `docs/release/release-process.md` (versioning, CHANGELOG, steps) and
`docs/deployment/deploy-prod-ci.md` (workflow, server behaviour, rollback). Those documents are the policy.
If they disagree with this skill, the documents win, and this skill should be corrected.
The `release-manager` agent (`.claude/agents/release-manager.md`) follows the same rules.

**A pushed `v*` tag deploys production.** Pushing to `prod` alone does not deploy, but the tag does. Before
each push that matters (develop, prod, the tag), show the user the exact commands and wait for an explicit
yes. Approval for an earlier step does not cover a later one.

Reply to the user in the language they write in.

## Stop and ask instead of continuing when

- `git status` is not clean. Another session may be mid-change. Never commit someone else's work into a release.
- The target tag exists locally or on origin. `git ls-remote --tags origin refs/tags/vX.Y.Z` must print nothing and
  `git tag -l vX.Y.Z` must be empty. Never reuse, move or delete a tag.
- The chosen version is not greater than the newest released version in `CHANGELOG.md`.
- The release contains migrations (step 2). The deploy does not back up the database, so the owner must
  acknowledge that before the tag is pushed.
- Telegram gateway changes are in `[Unreleased]`. They run on a separate host that `deploy-prod.yml` does not
  redeploy. Release them only after the owner confirms that host runs the new code.

## 1. Preflight

```bash
git fetch --prune --prune-tags origin
git status --short
git branch --show-current
git rev-parse --short develop origin/develop prod origin/prod
```

- **Baseline** = the newest released heading in `CHANGELOG.md`, that is, the first `## [x.y.z] - date` heading
  after `## [Unreleased]`.
- Do **not** use `git describe` or version-sorted tag lists to find the baseline. Legacy tags `v1.2.3`–`v1.3.6`
  still exist on origin and sort above real versions. Confirm the baseline tag is on prod:
  `git merge-base --is-ancestor v<baseline> origin/prod`.
- The main worktree may have `prod` checked out. Do the prod merge in a temporary worktree (step 6). Do not switch
  branches under other sessions.

## 2. Review what ships

```bash
git log v<baseline>..origin/develop --no-merges --format='%h %s'
git diff --stat v<baseline>..origin/develop
git diff --name-only v<baseline>..origin/develop | grep -i migrations
```

- Every user-visible change needs one line under `## [Unreleased]`. Add the missing ones. Open the diff when a
  commit subject is vague.
- Entries are Persian, written for the user ("what changed for me"). Leave out class names, endpoints, feature
  numbers, server names and secrets. Leave out internal work (refactors, CI, tooling, docs).
- **Migrations:** list them for the owner. Migrations run when the API starts, and the deploy takes no backup.
- **Quoted UI text:** if an entry quotes an app string, grep the code and confirm the exact text. A misquote is a bug.
- **Line-ending-only diffs** (for example CRLF churn in `sidebar.tsx`) are not user changes. Do not commit them.

## 3. Choose the number

- **MAJOR:** a `Removed` item, or a breaking change that users or integrators must react to.
- **MINOR:** any `Added` item, or a notable `Changed` item.
- **PATCH:** only `Fixed`, `Security` or small `Changed` items.

Use the number the owner gave in the argument if there is one, but check it against these rules and tell the owner
if they disagree. State the choice and its reason in the final report.

## 4. Update CHANGELOG.md

Edit the file with the Edit tool. Do not round-trip it through PowerShell `Get-Content`/`Set-Content`, which corrupts
the Persian text.

- Keep `## [Unreleased]` at the top, with no subsections left under it.
- Add `## [X.Y.Z] - YYYY-MM-DD`, using today's Gregorian date (`date +%F`). The heading has no `v`.
  Put a one-sentence Persian summary under it, then the sections in this order: `### Added`, `### Changed`,
  `### Fixed`, `### Security`, `### Deprecated`, `### Removed`. Section names stay in English.
- Move the entries that ship into the new version. Entries that are held back stay under `## [Unreleased]`.
- At the bottom, add `[X.Y.Z]: https://github.com/Rayan1392/financial-copilot/compare/v<baseline>...vX.Y.Z` and point
  `[Unreleased]` at `compare/vX.Y.Z...develop`.

## 5. Verify and commit locally

```bash
bash ops/release/check-release-version.sh vX.Y.Z
cd src/frontend && npx vitest run src/lib/release-notes
```

Both must pass. Commit only the release files on develop:

```bash
git commit -m "chore(release): vX.Y.Z"
```

Show the user the new CHANGELOG section and the commit, and ask for the go-ahead to push.

## 6. Push develop and merge into prod (after confirmation)

Use the scratchpad directory for `$TMP`. Stop and report on any merge conflict.

```bash
git push origin develop
git fetch origin
git worktree add --detach "$TMP/prod-release" origin/prod
cd "$TMP/prod-release"
git merge --no-ff develop -m "Merge develop into prod for vX.Y.Z"
git push origin HEAD:prod
cd - && git worktree remove "$TMP/prod-release"
```

Pushing `prod` does not deploy.

## 7. Tag and deploy (after a second confirmation)

```bash
git fetch origin
git rev-parse --short origin/prod            # the tag goes on this commit, the prod tip
git tag -a vX.Y.Z -m "vX.Y.Z: <one-line summary>" origin/prod
git push origin vX.Y.Z                        # push this one tag only
```

Before pushing, confirm that the "Deploy prod tag" workflow is enabled and that no other `v*` tag is being pushed.
Each `v*` push starts a production deploy.

## 8. Watch the deploy and verify

```bash
gh run list --workflow deploy-prod.yml --limit 1
gh run watch <run-id> --exit-status
```

A deploy usually takes about 10–15 minutes, because images are built on the server. Then check
`https://sapioai.ir/releases` and confirm that the new version is shown as the current one.

If the deploy fails:

- **Infrastructure failure with the same code** (SSH, disk, Docker): re-run the workflow on the same tag, as
  `deploy-prod-ci.md` describes.
- **Code failure, or the tag check failed:** the version number is spent. Do not retag. Fix the problem and release
  the next version.
- The server script rolls `IMAGE_TAG` and the three containers back to the previous image when the new version is
  not healthy. It does **not** roll back migrations, so report any migration that ran.

## 9. Record the release

Update `.claude/agent-memory/release-manager/MEMORY.md` with the version, date, prod commit, tag, deploy run ID and
open items. Keep it to facts and no secrets. Then commit it on develop
(`chore(release): record vX.Y.Z in release-manager memory`) and push develop, under the same confirmation as step 6.

## Never

- Push or delete a `v*` tag the owner has not approved for this release. Never move or delete a published tag.
- Retag after a failed deploy.
- Put secrets, server addresses, customer data or `.env` values in CHANGELOG, tags, commit messages or memory. The
  repository is public, and Actions logs are public.
- Write a version number anywhere except `CHANGELOG.md`.
- Push a non-semver tag such as `prod-<date>`.
- Force-push develop or prod.
- Commit work that belongs to another session.

## Final report

Give the owner: the version and why it was chosen; the develop commit, prod merge commit and tag; the deploy run
result; the live check on `/releases`; and what is still open. That means Telegram items held back, legacy tags on
origin, any migrations that ran, and the lack of a database backup before deploy.
