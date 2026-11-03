# انتشار Production با تگ نسخه (GitHub Actions)

push یا merge به شاخه `prod` دیگر به‌تنهایی چیزی را روی Production منتشر نمی‌کند. انتشار فقط با push تگ نسخه‌ای به شکل `vMAJOR.MINOR.PATCH`، مانند `v1.8.0`، شروع می‌شود. commit تگ باید در تاریخچهٔ شاخهٔ `prod` باشد؛ workflow قبل از اتصال SSH این موضوع را بررسی می‌کند و اسکریپت VPS نیز همین شرط را دوباره بررسی می‌کند. تگ خارج از تاریخچهٔ `prod` deploy نمی‌شود.

- Workflow: `.github/workflows/deploy-prod.yml`
- اسکریپت سمت سرور: `ops/deploy/prod-deploy.sh` (workflow آن را از طریق SSH اجرا می‌کند)
- بررسی دستی و فقط‌خواندنی سرور: workflow «Prod server check» (`.github/workflows/prod-server-check.yml`)

## ساخت و push تگ انتشار

پس از merge شدن تغییرات موردنظر به `prod`، از checkout تمیز همان شاخه تگ نسخه بسازید و push کنید:

```bash
git switch prod
git pull --ff-only origin prod
git status --short
git log -1 --oneline
git tag -a v1.8.0 -m "Release v1.8.0"
git push origin v1.8.0
```

شمارهٔ نسخه را با مقدار بعدی جایگزین کنید و مطمئن شوید commit نمایش‌داده‌شده همان نسخهٔ تأییدشدهٔ `prod` است. push معمولی به `prod` فقط کد را به شاخه می‌فرستد و deploy نمی‌کند. در صورت اجرای دستی workflow، آن را روی ref تگ نسخه اجرا کنید؛ اجرای دستی روی branch deploy نخواهد کرد.

در تنظیمات GitHub، برای الگوی تگ `v*` ruleset بگذارید تا فقط release managerهای مجاز بتوانند تگ بسازند و تگ موجود قابل تغییر یا حذف نباشد. بررسی ancestry در workflow تضمین می‌کند commit هدف داخل `prod` باشد؛ ruleset دسترسی به ایجاد/تغییر خود تگ را محدود می‌کند.

## مراحل هر deploy

1. اگر فایل‌های tracked روی سرور تغییر محلی داشته باشند، deploy متوقف می‌شود. اگر فضای دیسک کمتر از ۴ گیگابایت باشد، اول imageهای بی‌استفاده پاک می‌شوند.
2. checkout سرور فقط به‌صورت fast-forward روی commit تگ می‌رود؛ workflow و اسکریپت VPS هر دو تأیید می‌کنند که commit در تاریخچهٔ `prod` قرار دارد.
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
- Secretها در environment به نام `production` در GitHub هستند. دسترسی آن را به تگ‌های انتشار `v*` و شاخهٔ `prod` (فقط برای workflow بررسی فقط‌خواندنی سرور) محدود کنید:
  - `PROD_SSH_PRIVATE_KEY`
  - `PROD_SSH_KNOWN_HOSTS` (host key سرور pin شده است)
  - `PROD_SSH_HOST`، `PROD_SSH_PORT`، `PROD_SSH_USER`
- مخزن public است، پس logهای Actions هم عمومی‌اند. اسکریپت‌ها نباید مقادیر `.env`، logهای container یا آدرس remote را چاپ کنند.

## فایل override سرور

`docker-compose.override.yml` روی سرور track نمی‌شود و فقط باید image نسخه Telegram gateway را pin کند. اگر api، worker یا frontend در آن pin شوند، deploy عمداً متوقف می‌شود.

## Deploy مجدد یا rollback دستی

- **اجرای دوباره همان release:** در Actions، workflow «Deploy prod tag» را روی همان ref تگ اجرا کنید (Run workflow). این روش وقتی ممکن است که نسخهٔ تگ‌شده از نسخهٔ فعلی سرور جلوتر باشد؛ اسکریپت اجازهٔ fast-forward به commit قدیمی را نمی‌دهد.
- **برگشت ایمن به نسخهٔ قبلی:** تغییرات را با یک commit جدید revert کنید، آن را به `prod` برسانید و تگ نسخهٔ جدیدی روی آن commit بگذارید. انتشار همچنان از مسیر تگ انجام می‌شود.
- **مسیر اضطراری روی سرور:** مقدار قبلی را در `IMAGE_TAG` فایل `.env` بگذارید و دستور زیر را اجرا کنید:

  `docker compose up -d --no-deps api worker frontend`

## بررسی بعد از deploy

`docker compose ps --format '{{.Service}} {{.Image}} {{.Status}}'`

`curl -fsS http://127.0.0.1:5074/health`

`curl -i --max-time 15 https://api.sapioai.ir/health`
