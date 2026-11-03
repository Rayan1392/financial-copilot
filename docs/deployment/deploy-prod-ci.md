# Deploy خودکار شاخه `prod` (GitHub Actions)

هر push به شاخه `prod` همان commit را روی سرور production (`/opt/sapio/financial-copilot`) deploy می‌کند.

- Workflow: `.github/workflows/deploy-prod.yml`
- اسکریپت سمت سرور: `ops/deploy/prod-deploy.sh` (workflow آن را از طریق SSH اجرا می‌کند)
- بررسی دستی و فقط‌خواندنی سرور: workflow «Prod server check» (`.github/workflows/prod-server-check.yml`)

## مراحل هر deploy

1. اگر فایل‌های tracked روی سرور تغییر محلی داشته باشند، deploy متوقف می‌شود. اگر فضای دیسک کمتر از ۴ گیگابایت باشد، اول imageهای بی‌استفاده پاک می‌شوند.
2. checkout سرور فقط به‌صورت fast-forward روی همان commit شاخه `prod` می‌رود.
3. اگر compose سرویس‌های api، worker و frontend را به `IMAGE_TAG` resolve نکند (مثلاً چون در `docker-compose.override.yml` pin شده‌اند)، deploy قبل از build متوقف می‌شود.
4. imageهای `api`، `worker` و `frontend` روی خود سرور build می‌شوند. tag هر image، hash کوتاه همان commit است.
5. مقدار `IMAGE_TAG` در `.env` به tag جدید تغییر می‌کند و فقط همین سه سرویس recreate می‌شوند (`--no-deps`).
6. تأیید سلامت:
   - `http://127.0.0.1:5074/health` باید تا ۱۰ دقیقه `Healthy` برگرداند (migrationها هنگام شروع API اجرا می‌شوند)؛
   - frontend باید `200` برگرداند؛
   - worker باید در حال اجرا بماند و restart نشود.
7. اگر نسخه جدید سالم نشود، `IMAGE_TAG` و سه container به نسخه قبلی برمی‌گردند و workflow شکست می‌خورد. **دیتابیس برنمی‌گردد**: migrationهای اجراشده باقی می‌مانند.
8. از هر image، نسخه فعلی، نسخه قبلی و یک نسخه قدیمی‌تر نگه داشته می‌شود.

PostgreSQL، Redis، RabbitMQ و Telegram gateway هرگز لمس نمی‌شوند. تنها فایلی که ویرایش می‌شود خط `IMAGE_TAG` در `.env` است. قبل از deploy از دیتابیس backup گرفته نمی‌شود (تصمیم مالک، 2026-11-03).

## دسترسی و secretها

- ورود با یک SSH key اختصاصی که با پیشوند `restrict` در `/root/.ssh/authorized_keys` سرور ثبت شده است.
- Secretها در environment به نام `production` در GitHub هستند و فقط شاخه‌های `prod` و `develop` به آن دسترسی دارند:
  - `PROD_SSH_PRIVATE_KEY`
  - `PROD_SSH_KNOWN_HOSTS` (host key سرور pin شده است)
  - `PROD_SSH_HOST`، `PROD_SSH_PORT`، `PROD_SSH_USER`
- مخزن public است، پس logهای Actions هم عمومی‌اند. اسکریپت‌ها نباید مقادیر `.env`، logهای container یا آدرس remote را چاپ کنند.

## فایل override سرور

`docker-compose.override.yml` روی سرور track نمی‌شود و فقط باید image نسخه Telegram gateway را pin کند. اگر api، worker یا frontend در آن pin شوند، deploy عمداً متوقف می‌شود.

## Deploy مجدد یا rollback دستی

- **اجرای دوباره همان commit:** در Actions، workflow «Deploy prod» را روی شاخه `prod` اجرا کنید (Run workflow).
- **برگشت به نسخه قبلی:** revert را روی `prod` commit و push کنید، تا مسیر عادی deploy طی شود.
- **مسیر اضطراری روی سرور:** مقدار قبلی را در `IMAGE_TAG` فایل `.env` بگذارید و دستور زیر را اجرا کنید:

  `docker compose up -d --no-deps api worker frontend`

## بررسی بعد از deploy

`docker compose ps --format '{{.Service}} {{.Image}} {{.Status}}'`

`curl -fsS http://127.0.0.1:5074/health`

`curl -i --max-time 15 https://api.sapioai.ir/health`
