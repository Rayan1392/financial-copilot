# Deploy نسخه `develop` روی VPS

مسیر پروژه: `/opt/sapio/financial-copilot`

## اتصال و بررسی

به سرور وصل شوید و وارد پروژه شوید:

`ssh -p 22033 root@185.126.203.173`

سپس این دستورات را اجرا کنید:

`cd /opt/sapio/financial-copilot`

`git branch --show-current && git rev-parse HEAD && git status --short`

`git fetch origin develop && git rev-parse origin/develop`

اگر تغییر محلی وجود دارد، قبل از pull آن را محافظت کنید:

`git stash push -u -m "before-deploy-$(date +%Y%m%d-%H%M%S)"`

## دریافت آخرین نسخه

`git pull --ff-only origin develop`

اگر stash ساخته‌اید، آن را بررسی و برگردانید:

`git stash list`

`git stash pop`

در صورت conflict، فایل‌های تنظیمات production و secretها را با دقت merge کنید.

## Build API و Worker

`docker compose -f docker-compose.yml -f docker-compose.local-build.yml build api worker`

## Restart سرویس‌ها

`docker compose up -d api worker`

این دستور فقط API و Worker را recreate می‌کند و به volume دیتابیس دست نمی‌زند.

## بررسی کانتینرها

`docker ps --format '{{.Names}} {{.Status}} {{.Image}}' | grep financial-copilot`

API و Worker باید `Up` باشند و PostgreSQL، Redis و RabbitMQ باید `healthy` باشند.

## Health check

`curl -fsS --max-time 10 http://127.0.0.1:5074/health`

`curl -i --max-time 15 https://api.sapioai.ir/health`

پاسخ endpoint داخلی باید `Healthy` باشد.

## مشاهده logها

`docker logs --tail 200 -f financial-copilot-worker-1`

`docker logs --tail 200 -f financial-copilot-api-1`

`docker logs --tail 200 -f financial-copilot-frontend-1`

برای مشاهده فقط logهای اخیر:

`docker logs --since 10m financial-copilot-worker-1`

`Ctrl+C` فقط نمایش log را متوقف می‌کند و کانتینر را متوقف نمی‌کند.

## بررسی commit نهایی

`git rev-parse HEAD`

`git rev-parse origin/develop`

`git status --short`

دو مقدار اول باید برابر باشند. تغییرات محلی عمدی می‌توانند باعث شوند `git status` خالی نباشد.

## Rollback

ابتدا imageهای موجود و commit سالم قبلی را شناسایی کنید:

`docker images --format '{{.Repository}}:{{.Tag}} {{.CreatedAt}} {{.ID}}' | grep financial-copilot`

`git log --oneline --decorate -10`

پس از شناسایی دقیق commit سالم، API و Worker را با همان commit دوباره build و restart کنید. از `git reset --hard` و حذف volumeهای Docker بدون backup استفاده نکنید.

## نکات تنظیمات

- فایل `.env` production را حفظ کنید؛ شامل secretهای سرویس است.
- آدرس API سمت مرورگر: `https://api.sapioai.ir/`
- آدرس داخلی Server Function فرانت‌اند: `http://api:8080/`
- دامنه‌های `https://sapioai.ir` و `https://sapio.ir` باید در CORS باشند.
- تغییرات دستی compose یا فرانت‌اند را قبل از pull با stash یا backup branch محافظت کنید.

## Deploying TelegramGateway to Linode

The Telegram gateway is deployed separately from the Iran API/Worker. Build the
image on the local Windows machine, copy one fixed-name archive to Linode, and
recreate only the gateway container. Do not run API/Worker or database commands
on Linode.

### 1. Build and export locally

Run from `D:\Source\TahlilApp-AI` in PowerShell:

```powershell
Set-Location D:\Source\TahlilApp-AI
docker build -f docker/telegram-gateway.Dockerfile -t financial-copilot-telegram-gateway:prod .
docker image inspect financial-copilot-telegram-gateway:prod
docker save financial-copilot-telegram-gateway:prod -o "$env:TEMP\financial-copilot-telegram-gateway-prod.tar"
Get-Item "$env:TEMP\financial-copilot-telegram-gateway-prod.tar" | Select-Object FullName,Length
```

Do not continue unless the archive exists and has a non-zero size. The local
Docker engine must be running.

### 2. Copy the image and compose file to Linode

Enter the Linode root password when prompted:

```powershell
ssh -p 22033 root@172.105.37.62 "mkdir -p /opt/financial-copilot-telegram-gateway /var/lib/financial-copilot/telegram-gateway; chmod 750 /opt/financial-copilot-telegram-gateway /var/lib/financial-copilot/telegram-gateway"
scp -P 22033 "$env:TEMP\financial-copilot-telegram-gateway-prod.tar" root@172.105.37.62:/tmp/
scp -P 22033 docker/telegram-gateway.compose.yml root@172.105.37.62:/opt/financial-copilot-telegram-gateway/docker-compose.yml
```

The Linode deployment directory is
`/opt/financial-copilot-telegram-gateway`. Its `.env` file is a secret and must
not be replaced from source control. It must retain the existing
`TELEGRAM_BOT_TOKEN` and `TELEGRAM_PRIMARY_API_KEY`, and set only the new image:

```bash
cd /opt/financial-copilot-telegram-gateway
sed -i 's#^TELEGRAM_GATEWAY_IMAGE=.*#TELEGRAM_GATEWAY_IMAGE=financial-copilot-telegram-gateway:prod#' .env
chmod 600 .env
```

If the compose file is copied from the repository, verify its volume is the
persistent bind mount below (do not switch to an anonymous volume):

```yaml
- /var/lib/financial-copilot/telegram-gateway:/var/lib/telegram-gateway
```

If the copied compose file still contains the repository's named-volume line,
run this on Linode before starting the service:

```bash
sed -i 's#      - telegram_gateway_state:/var/lib/telegram-gateway#      - /var/lib/financial-copilot/telegram-gateway:/var/lib/telegram-gateway#' docker-compose.yml
```

### 3. Load and restart only the gateway on Linode

```bash
cd /opt/financial-copilot-telegram-gateway
docker load -i /tmp/financial-copilot-telegram-gateway-prod.tar
docker compose --env-file .env up -d --force-recreate
```

This recreates only `telegram-gateway`; it does not touch TelePain or any
Iran-VPS service. The offset and idempotency state must remain under
`/var/lib/financial-copilot/telegram-gateway`.

### 4. Verify the deployment

```bash
docker compose ps
docker inspect --format '{{.Config.Image}} {{.State.Health.Status}}' \
  financial-copilot-telegram-gateway-telegram-gateway-1
curl -fsS --max-time 10 http://127.0.0.1:5088/health
ls -la /var/lib/financial-copilot/telegram-gateway
docker logs --since 2m financial-copilot-telegram-gateway-telegram-gateway-1
```

The container image must equal `financial-copilot-telegram-gateway:prod` and health
must be `healthy`. Logs
must show successful Telegram polling and no `401`, `403`, `409`, or permanent
`TelegramError` entries. Send one smoke-test message through the linked Telegram
account and verify both the API request and the Telegram `sendMessage` result.

### Rollback gateway only

Keep the previous archive before replacing it. To roll back, load that archive,
set `.env` back to the last known-good image name, and recreate the gateway:

```bash
docker images 'financial-copilot-telegram-gateway' \
  --format '{{.Repository}}:{{.Tag}} {{.CreatedAt}}'
sed -i 's#^TELEGRAM_GATEWAY_IMAGE=.*#TELEGRAM_GATEWAY_IMAGE=financial-copilot-telegram-gateway:prod#' .env
docker compose --env-file .env up -d --force-recreate
```

Do not delete `/var/lib/financial-copilot/telegram-gateway`; removing it loses
the durable Telegram offset and idempotency records.
