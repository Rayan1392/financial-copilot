#!/usr/bin/env bash
# Production deploy of one release-tag commit reachable from the prod branch. Run as root on the production server by the "Deploy prod tag"
# GitHub Actions workflow, streamed over SSH:
#
#   ssh … 'bash -s -- <release-commit-sha>' < ops/deploy/prod-deploy.sh
#
# It fast-forwards the server checkout to that commit, builds api, worker and frontend on the server, points
# IMAGE_TAG in .env at the new images, recreates only those three services, and waits until they are healthy.
# If they do not become healthy, the three containers go back to the previous IMAGE_TAG (the database is not
# rolled back: migrations applied at API startup stay applied). postgres, redis, rabbitmq and the Telegram
# gateway are never touched. The only file it edits is the IMAGE_TAG line of .env.
#
# Output goes to the public Actions log: never print .env values, container logs or remote URLs here.

set -euo pipefail

readonly COMPOSE_DIR="${FINANCIAL_COPILOT_COMPOSE_DIR:-/opt/sapio/financial-copilot}"
readonly LOCK_FILE="/run/lock/financial-copilot-deploy.lock"
readonly SERVICES=(api worker frontend)
readonly BUILD_FILES=(-f docker-compose.yml -f docker-compose.local-build.yml)
readonly MIN_FREE_GB=4
readonly KEEP_TAGS_PER_IMAGE=3
readonly API_HEALTH_URL="http://127.0.0.1:5074/health"
readonly FRONTEND_URL="http://127.0.0.1:8080/"
# The API applies pending EF Core migrations at startup, which can take minutes after a large jump.
readonly API_HEALTH_TIMEOUT_SECONDS=600
readonly FRONTEND_TIMEOUT_SECONDS=120
readonly WORKER_SETTLE_SECONDS=45

target_sha="${1:?usage: prod-deploy.sh <release commit sha>}"

log() { printf '[deploy %s] %s\n' "$(date -u +%H:%M:%S)" "$*"; }
fail() { log "FAILED: $*"; exit 1; }
free_gb() { df -BG --output=avail / | tail -1 | tr -dc '0-9'; }
env_tag() { sed -n 's/^IMAGE_TAG=//p' .env | tail -1; }
set_env_tag() { sed -i "s/^IMAGE_TAG=.*/IMAGE_TAG=$1/" .env; }
recreate() { docker compose up -d --no-deps "${SERVICES[@]}"; }

worker_state() {
  local id
  id="$(docker compose ps -q worker)"
  [[ -n "$id" ]] && docker inspect -f '{{.State.Running}} {{.RestartCount}}' "$id"
}

wait_healthy() {
  local deadline=$((SECONDS + API_HEALTH_TIMEOUT_SECONDS))
  until [[ "$(curl -fsS --max-time 10 "$API_HEALTH_URL" 2>/dev/null)" == "Healthy" ]]; do
    ((SECONDS < deadline)) || { log "api not Healthy after ${API_HEALTH_TIMEOUT_SECONDS}s"; return 1; }
    sleep 5
  done
  log "api Healthy"
  deadline=$((SECONDS + FRONTEND_TIMEOUT_SECONDS))
  until [[ "$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 "$FRONTEND_URL")" == "200" ]]; do
    ((SECONDS < deadline)) || { log "frontend not answering 200 after ${FRONTEND_TIMEOUT_SECONDS}s"; return 1; }
    sleep 5
  done
  log "frontend 200"
  local before after
  before="$(worker_state)" || { log "worker container missing"; return 1; }
  sleep "$WORKER_SETTLE_SECONDS"
  after="$(worker_state)" || { log "worker container missing"; return 1; }
  [[ "$after" == true* && "$after" == "$before" ]] || { log "worker not stable (before: $before, after: $after)"; return 1; }
  log "worker running and not restarting"
}

[[ "$EUID" -eq 0 ]] || fail "must run as root"
cd "$COMPOSE_DIR" || fail "missing $COMPOSE_DIR"
exec 9>"$LOCK_FILE"
flock -n 9 || fail "another deploy is running"

# 1. Preconditions: clean tracked files, a known previous tag, enough disk for three image builds.
[[ -z "$(git status --porcelain --untracked-files=no)" ]] ||
  fail "tracked local changes in $COMPOSE_DIR; resolve them by hand before deploying"
prev_tag="$(env_tag)"
[[ -n "$prev_tag" ]] || fail "IMAGE_TAG is not set in .env"
if (($(free_gb) < MIN_FREE_GB)); then
  log "low disk ($(free_gb)G); pruning dangling images and old build cache"
  docker image prune -f >/dev/null
  docker builder prune -f --filter until=168h >/dev/null
  (($(free_gb) >= MIN_FREE_GB)) || fail "only $(free_gb)G free; need ${MIN_FREE_GB}G"
fi

# 2. Check out exactly the tagged commit. Defense in depth: reject commits outside origin/prod.
# --no-tags: the server deploys by commit, and release tags may be renamed (docs/release/release-process.md).
git fetch --quiet --no-tags origin +refs/heads/prod:refs/remotes/origin/prod
git cat-file -e "${target_sha}^{commit}" 2>/dev/null || fail "commit $target_sha not found on origin/prod"
git merge-base --is-ancestor "$target_sha" origin/prod || fail "commit $target_sha is not reachable from origin/prod"
if git show-ref --verify --quiet refs/heads/prod; then
  git checkout --quiet prod
else
  git checkout --quiet -b prod --track origin/prod
fi
git merge --quiet --ff-only "$target_sha" || fail "local prod cannot fast-forward to $target_sha"
[[ "$(git rev-parse HEAD)" == "$(git rev-parse "$target_sha")" ]] ||
  fail "local prod is ahead of $target_sha (a newer commit was already deployed)"
new_tag="$(git rev-parse --short=7 HEAD)"
log "deploying $(git log -1 --format='%h %s') (previous IMAGE_TAG: $prev_tag)"

# 3. Compose must resolve all three services to IMAGE_TAG; an override pinning them would make the deploy a no-op.
resolved="$(IMAGE_TAG="$new_tag" docker compose config --images "${SERVICES[@]}")"
for service in "${SERVICES[@]}"; do
  grep -q "financial-copilot-$service:$new_tag\$" <<<"$resolved" ||
    fail "compose does not resolve $service to IMAGE_TAG (is it pinned in docker-compose.override.yml?)"
done

# 4. Build on the server, from the build file set (the override is runtime-only).
log "building ${SERVICES[*]} as $new_tag"
IMAGE_TAG="$new_tag" docker compose "${BUILD_FILES[@]}" build "${SERVICES[@]}"

# 5. Switch, verify, and roll the containers back if the new version is not healthy.
set_env_tag "$new_tag"
log "recreating ${SERVICES[*]}"
if ! recreate || ! wait_healthy; then
  log "new version unhealthy; rolling containers back to $prev_tag (see docker logs on the server)"
  set_env_tag "$prev_tag"
  recreate
  if wait_healthy; then
    fail "deploy of $new_tag rolled back; $prev_tag is running again"
  fi
  fail "deploy of $new_tag rolled back, but $prev_tag is not healthy either; intervene on the server"
fi

# 6. Keep the newest images per service (always the current and previous tags), drop dangling layers.
for service in "${SERVICES[@]}"; do
  repository="financial-copilot-$service"
  # grep exits 1 when only the current and previous tags exist; that is not a failure.
  docker images "$repository" --format '{{.Tag}}' |
    { grep -vx -e '<none>' -e "$new_tag" -e "$prev_tag" || true; } |
    tail -n +"$((KEEP_TAGS_PER_IMAGE - 1))" |
    while read -r old_tag; do docker rmi "$repository:$old_tag" >/dev/null 2>&1 || true; done
done
docker image prune -f >/dev/null
log "deployed $new_tag; $(free_gb)G free"
