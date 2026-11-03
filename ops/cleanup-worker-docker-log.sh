#!/usr/bin/env bash

set -euo pipefail

readonly COMPOSE_DIR="${FINANCIAL_COPILOT_COMPOSE_DIR:-/opt/sapio/financial-copilot}"
readonly SERVICE_NAME="${FINANCIAL_COPILOT_LOG_SERVICE:-worker}"
readonly LOCK_FILE="/run/lock/financial-copilot-worker-log-cleanup.lock"

if [[ "${EUID}" -ne 0 ]]; then
  echo "This script must run as root." >&2
  exit 1
fi

if [[ ! -d "${COMPOSE_DIR}" ]]; then
  echo "Compose directory does not exist: ${COMPOSE_DIR}" >&2
  exit 1
fi

exec 9>"${LOCK_FILE}"
if ! flock -n 9; then
  echo "Another worker-log cleanup is already running; exiting."
  exit 0
fi

cd "${COMPOSE_DIR}"

container_id="$(docker compose ps -q "${SERVICE_NAME}")"
if [[ -z "${container_id}" ]]; then
  echo "No running container found for service: ${SERVICE_NAME}" >&2
  exit 1
fi

log_path="$(docker inspect --format '{{.LogPath}}' "${container_id}")"
case "${log_path}" in
  /var/lib/docker/containers/*/*-json.log) ;;
  *)
    echo "Refusing to truncate unexpected Docker log path: ${log_path}" >&2
    exit 1
    ;;
esac

if [[ ! -f "${log_path}" ]]; then
  echo "Docker log file does not exist: ${log_path}" >&2
  exit 1
fi

bytes_before="$(stat --format='%s' "${log_path}")"
truncate --size=0 -- "${log_path}"

message="Truncated ${SERVICE_NAME} Docker log; freed ${bytes_before} bytes (${log_path})."
logger --tag financial-copilot-log-cleanup -- "${message}"
echo "${message}"
