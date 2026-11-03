# Deployment and Cutover Runbook

## Build and provision VPS 2

Build the Gateway image on a networked build machine for the VPS architecture:

```bash
docker buildx build --platform linux/amd64 --load \
  -f docker/telegram-gateway.Dockerfile \
  -t financial-copilot-telegram-gateway:RELEASE_TAG .
```

Install Docker Engine and the Compose plugin on VPS 2. Copy the image archive, the
`docker/telegram-gateway.compose.yml` file, and a secret-managed environment file to a restricted
directory such as `/opt/financial-copilot/telegram-gateway`. Do not copy the populated environment
file into source control or the application image.

Set the same random value as `TELEGRAM_GATEWAY_PRIMARY_API_KEY` in the VPS1 API environment and as
`TELEGRAM_PRIMARY_API_KEY` in the VPS2 Gateway environment. The API configuration already contains
the dedicated `telegram-gateway-vps2` client, reads the secret from the VPS1 environment, and limits
it to the assistant-update and link-confirmation paths.

Expose only the TLS reverse-proxy port publicly. Keep the container bound to loopback (`5088` in
the supplied Compose file), and proxy `https://telegram-gateway.example.com` to it. The reverse
proxy must forward only the Gateway API paths and `/health`; it must terminate TLS and redirect HTTP
to HTTPS.

The Gateway container has no database, Redis, or RabbitMQ configuration. Its only writable state is
the named volume containing the Telegram offset and idempotency metadata.

## Staged rollout

1. **Diagnostic stage:** keep `TELEGRAM_GATEWAY_ENABLED=false` and keep the VPS1 Gateway client
   disabled. Start the container, verify `/health`, TLS, firewall rules, and secret injection
   without starting a Telegram poller.
2. **Gateway activation:** set `TELEGRAM_GATEWAY_ENABLED=true`, start exactly one Gateway replica,
   and verify the persisted offset is created. The old Worker polling registration is disabled in
   code; do not run the legacy development polling worker manually.
3. **Primary activation:** configure VPS1 `Telegram:Gateway:Enabled=true`, the HTTPS BaseUrl, and
   the matching service id/secret. Restart API and Worker processes, then verify assistant updates,
   `/start` linking, callbacks, membership, and notifications.
4. **Stabilization:** observe Gateway health, API authentication failures, rate-limit responses,
   update offsets, and notification outbox outcomes before rotating the Bot Token.

## Rollback and duplicate-poller shutdown

- Disable `Telegram:Gateway:Enabled` on VPS1 and restart API/Worker processes.
- Stop the Gateway container with `docker compose -f docker/telegram-gateway.compose.yml down`.
- If a duplicate poller is suspected, stop every old Worker or manually launched development
  polling process before restarting the single Gateway replica. Never delete the offset or
  idempotency volume during rollback.
- Restore the prior transport only after confirming that no Gateway instance is polling and retain
  all API link, conversation, notification, and billing records.

## Production evidence required before completion

- Successful test-bot message, callback, `/start` linking, membership, and notification flows.
- Gateway restart with no duplicate delivery and offset recovery.
- VPS firewall evidence showing the Gateway cannot reach PostgreSQL, Redis, or RabbitMQ.
- Bot Token rotation after old pollers are stopped.
