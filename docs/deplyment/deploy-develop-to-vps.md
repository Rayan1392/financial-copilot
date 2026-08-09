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
