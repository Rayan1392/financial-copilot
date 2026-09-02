# راهنمای استقرار Feature 130 — کانال تعاملی تلگرام

این سند کارهای عملیاتی لازم برای راه‌اندازی مسیر زیر را تفکیک می‌کند:

```text
کاربر تلگرام
→ Telegram Bot API
→ FinancialCopilot.TelegramGateway روی Linode
→ HTTPS POST https://api.sapioai.ir/api/v1/telegram/assistant/updates
→ FinancialCopilot API و پایگاه‌داده روی سرور ایران
→ پاسخ رندرشده
→ TelegramGateway
→ کاربر تلگرام
```

این Runbook مربوط به Feature 130 است. در این معماری:

- سرور ایران هیچ اتصال ورودی به Linode برقرار نمی‌کند.
- Linode به روش long polling به تلگرام وصل می‌شود؛ webhook لازم نیست.
- `TelegramGateway` فقط Channel Adapter است و هیچ تحلیل مالی یا اجرای AI انجام نمی‌دهد.
- PostgreSQL، لینک حساب تلگرام، conversationها، orchestration و داده‌های مالی روی سرور ایران باقی می‌مانند.
- Redis، RabbitMQ، دیتابیس جدید یا replication روی Linode لازم نیست.

وضعیت فعلی در زمان تهیه این سند:

- `T130-01` تا `T130-09`: تکمیل‌شده
- تست‌های متمرکز Feature 130: `23/23 PASS`
- Acceptance Criteria: `21/22` تأییدشده
- `AC-12`: منتظر smoke test واقعی
- `T130-10`: `READY_FOR_OPERATIONAL_VERIFICATION`
- تست کامل solution: `2,094 passed` و `40 failed` نامرتبط با Feature 130؛ این failureها باید به‌عنوان baseline پیش از استقرار ثبت شوند و نباید به Feature 130 نسبت داده شوند.

> نکته: فرمان‌ها الگو هستند. نام واقعی سرویس Compose، مسیر repository و نام متغیرهای پیکربندی باید با نسخه نهایی codebase تطبیق داده شود. هیچ secret واقعی را داخل این فایل، Git، shell history یا log قرار ندهید.

## 1. پیش‌نیازهای مشترک

قبل از شروع این موارد باید آماده باشند:

- implementation مربوط به `T130-01` تا `T130-09` تکمیل و تست‌ها سبز باشد.
- یک Telegram Bot فعال و `BotToken` آن در دسترس باشد.
- یک API key جدید و اختصاصی برای `TelegramGateway` تولید شده باشد.
- `ClientId` و `TenantId` پایدار برای API client اختصاصی تعیین شده باشد.
- حداقل یک کاربر تلگرام از قبل به حساب FinancialCopilot لینک شده باشد.
- DNS و TLS دامنه `api.sapioai.ir` سالم باشد.
- ساعت هر دو سرور با NTP همگام باشد.
- از فایل‌های تنظیمات و وضعیت فعلی سرویس‌ها backup گرفته شده باشد.

مقادیر حساس موردنیاز:

| مقدار | محل نگهداری | توضیح |
|---|---|---|
| Telegram Bot Token | فقط Linode | برای long polling و ارسال پاسخ |
| Raw Primary API Key | Linode و secret محیط API ایران | روی ایران از طریق `KeyEnvironmentVariable` یا فقط hash متناظر |
| TelegramGateway ClientId | سرور ایران | شناسه پایدار API client |
| TelegramGateway TenantId | سرور ایران | tenant مورد استفاده gateway |
| HMAC ServiceId/ServiceSecret | فقط در صورت فعال بودن مسیر backend-to-gateway | برای Feature 130 الزامی نیست |

## 2. ترتیب پیشنهادی استقرار

ترتیب اجرا باید این‌گونه باشد:

1. از وضعیت فعلی سرور ایران و Linode backup بگیرید.
2. API client اختصاصی را روی سرور ایران پیکربندی کنید.
3. نسخه جدید API را روی سرور ایران deploy و health آن را بررسی کنید.
4. دسترسی path-scoped API key را با `curl` از Linode آزمایش کنید.
5. TelegramGateway را روی Linode پیکربندی و deploy کنید.
6. فایل‌های durable state را ایجاد و permission آن‌ها را کنترل کنید.
7. سرویس Gateway را فعال کنید و log/health را بررسی کنید.
8. smoke test واقعی با یک کاربر لینک‌شده انجام دهید.
9. restart و replay/idempotency را آزمایش کنید.
10. نتیجه، correlation ID، نسخه image/commit و زمان استقرار را ثبت کنید.

---

# بخش اول — کارهای سرور ایران (`sapioai.ir`)

## 3. بررسی قبل از تغییر

روی سرور ایران:

```bash
cd /opt/sapio/financial-copilot
git branch --show-current
git rev-parse HEAD
git status --short
docker compose ps
curl -fsS https://api.sapioai.ir/health
```

اگر worktree دارای conflict یا تغییرات ناشناخته است، deploy را متوقف کنید. قبل از هر pull یا build مشخص کنید تغییرات متعلق به چه کسی است؛ از reset مخرب استفاده نکنید.

نسخه فعلی را برای rollback ثبت کنید:

```bash
docker compose images
docker compose config --services
```

## 4. ساخت API key اختصاصی

یک کلید تصادفی قوی ایجاد کنید. مقدار واقعی را در خروجی ticket، چت یا Git قرار ندهید.

نمونه تولید کلید روی یک محیط امن:

```bash
openssl rand -base64 48
```

پیاده‌سازی نهایی از این نام استفاده می‌کند:

```text
TELEGRAM_GATEWAY_API_KEY
```

نام نهایی باید دقیقاً با مقدار `KeyEnvironmentVariable` در تنظیم API client منطبق باشد. خود مقدار کلید نباید داخل `appsettings.json` نوشته شود.

## 5. پیکربندی API client روی سرور ایران

در configuration نهایی API یک client فعال و اختصاصی برای TelegramGateway وجود داشته باشد:

- `ClientId`: شناسه GUID پایدار و یکتا
- `TenantId`: tenant صحیح FinancialCopilot
- `IsActive`: برابر `true`
- `KeyEnvironmentVariable`: نام متغیر محیطی حاوی raw key
- یا `KeySha256`: فقط hash کلید، مطابق قابلیت موجود پروژه
- `AllowedPathPrefixes` فقط شامل:
  - `/api/v1/telegram/assistant/updates`
  - `/api/v1/telegram/link/confirm`

این client نباید به `/api/ai/v1/query` یا prefix عمومی مثل `/api` دسترسی داشته باشد.

در فایل `.env` یا secret mechanism واقعی deployment مقدار زیر را تنظیم کنید:

```dotenv
TELEGRAM_GATEWAY_API_KEY=<RAW_RANDOM_API_KEY>
```

ملاحظات:

- فایل secret نباید tracked باشد.
- permission فایل secret ترجیحاً `600` باشد.
- اگر Compose از `env_file` استفاده می‌کند، مطمئن شوید متغیر به container مربوط به API منتقل می‌شود.
- پس از rotation، کلید قبلی را غیرفعال کنید.

کنترل اینکه متغیر داخل container تعریف شده است، بدون چاپ مقدار:

```bash
docker compose exec api sh -lc 'test -n "$TELEGRAM_GATEWAY_API_KEY"'
```

## 6. Deploy نسخه API روی ایران

ابتدا تغییرات را دریافت و فقط سرویس‌های لازم را build/recreate کنید. اگر API و frontend هر دو تغییر نکرده‌اند، frontend را بی‌دلیل rebuild نکنید.

نمونه برای API:

```bash
cd /opt/sapio/financial-copilot
git fetch origin develop
git pull --ff-only origin develop
docker compose --env-file .env -f docker-compose.yml -f docker-compose.local-build.yml up -d --build --force-recreate api
```

اگر migration جدیدی در implementation اضافه نشده است، migration اجرا نکنید. اگر migration وجود دارد، طبق فرآیند استاندارد پروژه و پس از backup دیتابیس اجرا شود.

پس از deploy:

```bash
docker compose ps api
docker compose logs --since=10m --tail=300 api
curl -fsS https://api.sapioai.ir/health
```

در log نباید raw API key، BotToken، authorization header یا متن کامل پیام کاربر دیده شود.

## 7. آزمون authentication و محدودیت مسیر

این تست بهتر است از خود Linode اجرا شود تا مسیر واقعی شبکه نیز تأیید شود.

### 7.1 تست endpoint مجاز

در Linode، کلید را از secret environment بخوانید و یک request ساختاری معتبر بفرستید. شناسه‌ها را آزمایشی و غیرواقعی انتخاب کنید:

```bash
curl --fail-with-body --silent --show-error \
  --request POST 'https://api.sapioai.ir/api/v1/telegram/assistant/updates' \
  --header 'Content-Type: application/json' \
  --header 'X-Api-Key: <TELEGRAM_GATEWAY_API_KEY>' \
  --header 'X-Correlation-Id: deployment:feature130:auth-check' \
  --data '<VALID_TEST_REQUEST_JSON>'
```

این تست ممکن است به‌دلیل لینک نبودن کاربر نتیجه business-level مانند unlinked برگرداند؛ هدف این مرحله عبور از authentication و رسیدن request به controller است.

### 7.2 تست کلید نامعتبر

با یک کلید ساختگی request بفرستید و انتظار `401` یا `403` داشته باشید:

```bash
curl --silent --output /dev/null --write-out '%{http_code}\n' \
  --request POST 'https://api.sapioai.ir/api/v1/telegram/assistant/updates' \
  --header 'Content-Type: application/json' \
  --header 'X-Api-Key: invalid-deployment-check' \
  --data '<VALID_TEST_REQUEST_JSON>'
```

### 7.3 تست محدودیت path

با کلید اختصاصی Gateway درخواست به `/api/ai/v1/query` نباید مجاز شود. فقط status code را ثبت کنید و response حاوی اطلاعات حساس را منتشر نکنید.

## 8. بررسی backend prerequisites

پیش از فعال کردن Gateway تأیید کنید:

- endpoint `POST /api/v1/telegram/assistant/updates` فعال است.
- authorization policy مربوط به API client کار می‌کند.
- جدول/مکانیزم `TelegramProcessedUpdates` در دسترس است.
- account link کاربر تست وجود دارد.
- `TelegramConversationBinding` قابل ایجاد یا بازیابی است.
- `IAiQueryOrchestrationService` در API سالم است.
- rate limit مربوط به client/actor مانع تست کنترل‌شده نمی‌شود.

برای smoke test، شناسه Telegram کاربر تست باید دقیقاً با لینک ذخیره‌شده در backend منطبق باشد. Feature 130 دسترسی guest اضافه نمی‌کند.

## 9. Rollback سرور ایران

در صورت failure جدی:

1. Gateway روی Linode را stop کنید تا درخواست جدید تولید نشود.
2. image/tag یا commit قبلی API را deploy کنید.
3. اگر API client جدید علت failure است، آن را `IsActive=false` کنید یا secret آن را بردارید.
4. health و endpointهای اصلی API را دوباره بررسی کنید.
5. فایل‌های state روی Linode را حذف نکنید؛ برای retry/تحلیل بعدی نگه دارید.

Rollback نباید شامل حذف `TelegramProcessedUpdates`، conversationها یا account linkها باشد.

---

# بخش دوم — کارهای سرور Linode

## 10. آماده‌سازی میزبان

Linode باید این شرایط را داشته باشد:

- Docker Engine و Docker Compose یا process supervisor مورد استفاده پروژه نصب باشد.
- دسترسی outbound TCP/443 به `api.telegram.org` و `api.sapioai.ir` برقرار باشد.
- DNS resolution سالم باشد.
- یک instance از Gateway فعال باشد؛ اجرای همزمان دو poller با یک bot token مجاز نیست.
- مسیر durable برای offset و idempotency روی دیسک میزبان وجود داشته باشد.
- health endpoint فقط در محدوده موردنیاز قابل دسترسی باشد.

بررسی شبکه:

```bash
getent hosts api.telegram.org
getent hosts api.sapioai.ir
curl -fsS --max-time 15 https://api.telegram.org
curl -fsS --max-time 15 https://api.sapioai.ir/health
```

پاسخ غیر `2xx` از ریشه `api.telegram.org` لزوماً مشکل نیست؛ موفق بودن DNS/TLS و برقرار شدن اتصال مهم است.

## 11. مسیرهای persistent state

یک مسیر اختصاصی خارج از filesystem موقت container ایجاد کنید. مثال:

```bash
sudo install -d -m 0750 -o <SERVICE_USER> -g <SERVICE_GROUP> /var/lib/financial-copilot/telegram-gateway
```

دو فایل runtime در این مسیر نگهداری می‌شوند:

```text
/var/lib/financial-copilot/telegram-gateway/offset.json
/var/lib/financial-copilot/telegram-gateway/idempotency.json
```

الزامات:

- مسیرها absolute و writable باشند.
- اگر Gateway داخل container اجرا می‌شود، این directory به‌صورت bind mount یا volume پایدار mount شود.
- backup محدود و امن از این دو فایل تهیه شود.
- هنگام deploy/restart این فایل‌ها overwrite یا truncate نشوند.
- دو instance نباید همزمان روی این فایل‌ها بنویسند.

## 12. متغیرهای محیطی Gateway

نام دقیق متغیرها باید با binding نهایی `TelegramGatewaySettings` تطبیق داده شود. برای .NET configuration، شکل متداول زیر است:

```dotenv
TelegramGateway__Enabled=true
TelegramGateway__BotToken=<TELEGRAM_BOT_TOKEN>
TelegramGateway__PrimaryApiBaseUrl=https://api.sapioai.ir
TelegramGateway__PrimaryApiKey=<RAW_RANDOM_API_KEY>
TelegramGateway__OffsetFilePath=/var/lib/financial-copilot/telegram-gateway/offset.json
TelegramGateway__IdempotencyFilePath=/var/lib/financial-copilot/telegram-gateway/idempotency.json
TelegramGateway__RequestTimeoutSeconds=<VALUE_FROM_APPROVED_CONFIG>
TelegramGateway__LongPollingTimeoutSeconds=<VALUE_FROM_APPROVED_CONFIG>
TelegramGateway__PollingIntervalSeconds=<VALUE_FROM_APPROVED_CONFIG>
TelegramGateway__PollingLimit=<VALUE_FROM_APPROVED_CONFIG>
```

اگر section یا propertyهای واقعی codebase نام دیگری دارند، همین مقادیر را با نام واقعی آن‌ها تنظیم کنید؛ نام را حدس نزنید.

برای polling-only Feature 130:

- `ServiceId` و `ServiceSecret` نباید اجباری باشند.
- فقط اگر endpointهای backend-to-gateway واقعاً استفاده می‌شوند، HMAC pair را تنظیم کنید.
- یک عضو از pair را بدون دیگری تنظیم نکنید.

فایل env را خارج از Git و با permission محدود نگه دارید:

```bash
sudo chmod 600 <GATEWAY_ENV_FILE>
```

## 13. استقرار با Docker Compose

اگر Linode از Compose استفاده می‌کند، سرویس باید حداقل این ویژگی‌ها را داشته باشد:

```yaml
services:
  telegram-gateway:
    image: <TELEGRAM_GATEWAY_IMAGE_TAG>
    restart: unless-stopped
    env_file:
      - <GATEWAY_ENV_FILE>
    volumes:
      - /var/lib/financial-copilot/telegram-gateway:/var/lib/financial-copilot/telegram-gateway
```

سپس:

```bash
docker compose pull telegram-gateway
docker compose up -d --force-recreate telegram-gateway
docker compose ps telegram-gateway
docker compose logs --since=10m --tail=300 telegram-gateway
```

اگر image روی همان سرور build می‌شود:

```bash
docker compose up -d --build --force-recreate telegram-gateway
```

یک tag ثابت نسخه‌ای استفاده کنید؛ از اتکای عملیاتی به `latest` پرهیز شود تا rollback قابل پیش‌بینی باشد.

## 14. استقرار با systemd در صورت عدم استفاده از Docker

اگر Gateway مستقیم اجرا می‌شود، unit باید این خصوصیات را داشته باشد:

- `Restart=always` یا `Restart=on-failure`
- `EnvironmentFile=<GATEWAY_ENV_FILE>`
- `WorkingDirectory` مشخص
- `User` غیر root
- دسترسی write فقط به مسیر state
- start پس از آماده شدن network

نمونه کلی:

```ini
[Unit]
Description=FinancialCopilot Telegram Gateway
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=<SERVICE_USER>
Group=<SERVICE_GROUP>
WorkingDirectory=<GATEWAY_DEPLOY_DIRECTORY>
EnvironmentFile=<GATEWAY_ENV_FILE>
ExecStart=/usr/bin/dotnet <GATEWAY_DEPLOY_DIRECTORY>/FinancialCopilot.TelegramGateway.dll
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
```

فعال‌سازی:

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now financialcopilot-telegram-gateway
sudo systemctl status financialcopilot-telegram-gateway --no-pager
sudo journalctl -u financialcopilot-telegram-gateway --since '10 minutes ago' --no-pager
```

فقط یکی از دو مدل Docker Compose یا systemd انتخاب شود؛ Gateway را با هر دو روش همزمان اجرا نکنید.

## 15. کنترل شروع صحیح Gateway

پس از start موارد زیر را بررسی کنید:

- configuration validation بدون خطا عبور کرده است.
- base URL دقیقاً HTTPS و `https://api.sapioai.ir` است.
- long polling شروع شده است.
- خطای Telegram `409 Conflict` وجود ندارد؛ این خطا معمولاً نشان‌دهنده poller دوم یا webhook فعال است.
- API پاسخ `401/403` نمی‌دهد.
- فایل‌های offset/idempotency ایجاد یا قابل استفاده هستند.
- log شامل secret یا متن کامل پیام نیست.
- `/health` در حالت سالم قرار دارد.

بررسی فایل‌ها بدون نمایش محتوا:

```bash
sudo test -w /var/lib/financial-copilot/telegram-gateway
sudo stat /var/lib/financial-copilot/telegram-gateway
```

اگر health فقط روی loopback expose شده است:

```bash
curl -fsS http://127.0.0.1:<HEALTH_PORT>/health
```

## 16. Smoke test واقعی Feature 130

با یک کاربر از قبل لینک‌شده:

1. یک سؤال فارسی مشخص به bot بفرستید؛ مثال: «روند فروش ماهانه کاوه را نشان بده».
2. زمان ارسال و Telegram `UpdateId` را ثبت کنید.
3. در log Gateway، correlation با الگوی `telegram:<UpdateId>` را پیدا کنید.
4. همان correlation را در log API ایران پیدا کنید.
5. تأیید کنید request وارد مسیر زیر شده است:

```text
TelegramAssistantController
→ TelegramAiAssistantAdapter
→ IAiQueryOrchestrationService
```

6. پاسخ باید در همان chat و با ترتیب صحیح partها برگردد.
7. اگر پاسخ شامل چند part یا تصویر است، ترتیب و کامل بودن آن‌ها را کنترل کنید.
8. مطمئن شوید Gateway متن تحلیلی را بازنویسی نکرده است.
9. در backend تأیید کنید conversation binding و processed update ایجاد/استفاده شده‌اند.
10. نتیجه را با commit/image tag و correlation ID ثبت کنید؛ متن سؤال، پاسخ یا secret را وارد گزارش عملیاتی عمومی نکنید.

## 17. آزمون restart و persistence

پس از smoke test موفق:

```bash
docker compose restart telegram-gateway
```

یا در systemd:

```bash
sudo systemctl restart financialcopilot-telegram-gateway
```

سپس بررسی کنید:

- offset قبلی دوباره استفاده شده است.
- پیام‌های پردازش‌شده از ابتدا replay نمی‌شوند.
- partهای ثبت‌شده دوباره ارسال نمی‌شوند.
- Gateway دوباره healthy می‌شود.
- فایل‌های state حذف یا صفر نشده‌اند.

## 18. آزمون bounded outage

این تست باید در یک بازه کنترل‌شده انجام شود و باعث outage عمومی API نشود. روش امن‌تر، قطع موقت دسترسی Gateway به یک endpoint آزمایشی یا استفاده از staging است.

رفتار مورد انتظار:

- timeout، network failure، `429` یا `5xx` از Primary API: offset جلو نمی‌رود و update بعداً retry می‌شود.
- `2xx` همراه `Status=TransientError`: backend دوباره اجرا نمی‌شود؛ response رندرشده تحویل داده می‌شود.
- خطای موقت ارسال تلگرام: offset جلو نمی‌رود و partهای تأییدشده در replay skip می‌شوند.
- `401/403`: health باید unhealthy شود، پیام عمومی موقت در صورت شناخته بودن chat ارسال شود و update وارد retry بی‌نهایت نشود.

تمایز مهم:

- اگر request اصلاً به backend نرسیده باشد، اجرای بعدی اولین اجرای AI است.
- اگر backend request را پردازش و `TelegramProcessedUpdates` را ذخیره کرده ولی response در شبکه گم شده باشد، retry باید همان نتیجه ذخیره‌شده را بدون اجرای دوم AI برگرداند.

## 19. Rollback روی Linode

در صورت failure:

1. سرویس Gateway را stop کنید.
2. logها و وضعیت health را ثبت کنید؛ secretها را redacted نگه دارید.
3. image/tag یا artifact قبلی را فعال کنید.
4. همان فایل‌های durable offset/idempotency را حفظ کنید.
5. اگر API key مشکوک یا افشاشده است، آن را روی ایران rotate کنید و secret Linode را به‌روزرسانی کنید.
6. فقط یک instance را start کنید.
7. health و یک تست کنترل‌شده را تکرار کنید.

فرمان‌های نمونه:

```bash
docker compose stop telegram-gateway
docker compose up -d --force-recreate telegram-gateway
```

حذف فایل‌های state راه rollback نیست و ممکن است باعث replay یا ارسال تکراری شود.

---

# بخش سوم — چک‌لیست نهایی

## 20. چک‌لیست سرور ایران

- [ ] backup و نسخه فعلی ثبت شد.
- [ ] نسخه دارای Feature 130 deploy شد.
- [ ] API health سالم است.
- [ ] API client اختصاصی TelegramGateway فعال است.
- [ ] client فقط دو path مصوب را دارد.
- [ ] raw key داخل configuration tracked نیست.
- [ ] endpoint با کلید معتبر از Linode قابل دسترسی است.
- [ ] کلید نامعتبر با `401/403` رد می‌شود.
- [ ] کلید Gateway روی `/api/ai/v1/query` مجاز نیست.
- [ ] account link کاربر تست موجود است.
- [ ] backend duplicate replay بدون اجرای دوباره AI کار می‌کند.
- [ ] logها secret یا متن کامل پیام را ثبت نمی‌کنند.

## 21. چک‌لیست Linode

- [ ] فقط یک Gateway instance فعال است.
- [ ] BotToken و PrimaryApiKey از secret environment خوانده می‌شوند.
- [ ] PrimaryApiBaseUrl برابر `https://api.sapioai.ir` است.
- [ ] DNS/TLS و outbound 443 به Telegram و API سالم است.
- [ ] long polling فعال است و webhook لازم نیست.
- [ ] state directory پایدار، absolute و writable است.
- [ ] offset/idempotency پس از restart باقی می‌مانند.
- [ ] process تحت supervision و automatic restart است.
- [ ] polling-only بدون HMAC pair اجرا می‌شود.
- [ ] health با credential معتبر سالم است.
- [ ] سؤال فارسی کاربر لینک‌شده پاسخ گرفته است.
- [ ] correlation ID در Linode و ایران قابل ردیابی است.
- [ ] multipart response به ترتیب ارسال شده است.
- [ ] تست retry/replay کنترل‌شده انجام شده است.
- [ ] logها فاقد token، API key و payload کامل کاربر هستند.

## 22. معیار تکمیل T130-10

`T130-10` فقط زمانی `DONE` محسوب می‌شود که همه موارد زیر واقعاً اجرا و ثبت شده باشند:

- Gateway روی Linode با supervision فعال باشد.
- persistent state پس از restart حفظ شود.
- اتصال outbound به Telegram و `api.sapioai.ir` برقرار باشد.
- کاربر لینک‌شده یک سؤال واقعی فارسی ارسال کند.
- پاسخ از مسیر backend موجود به همان chat برگردد.
- correlation ID در هر دو سمت قابل ردیابی باشد.
- آزمون کنترل‌شده retry/replay نتیجه مورد انتظار بدهد.
- هیچ webhook، مسیر Iran-to-Linode، broker، دیتابیس یا agent دوم اضافه نشده باشد.

اگر دسترسی واقعی Linode یا bot در زمان implementation موجود نیست، وضعیت باید `READY_FOR_OPERATIONAL_VERIFICATION` باقی بماند و نباید smoke test انجام‌نشده به‌عنوان موفق گزارش شود.

پس از موفقیت همه مراحل، این دو فایل repository باید به‌روزرسانی شوند:

- در `Tasks.md` وضعیت `T130-10` از `READY_FOR_OPERATIONAL_VERIFICATION` به `DONE` تغییر کند.
- در `ImplementationReport.md`، `AC-12` و outcome نهایی با شواهد واقعی Linode/Telegram ثبت شود.

اگر smoke test شکست خورد، وضعیت فعلی حفظ شود و علت دقیق در بخش operational verification گزارش شود؛ AC یا Task نباید صرفاً با وجود deploy به حالت موفق تغییر کند.

## 23. اطلاعاتی که باید در گزارش استقرار ثبت شوند

```text
Feature: 130 — Telegram Gateway Interactive Channel
Deployment UTC time:
Iran API commit/image:
Linode Gateway commit/image:
API health result:
Gateway health result:
Smoke-test correlation ID:
Linked test actor/user reference (non-sensitive):
Restart persistence result:
Retry/replay result:
Rollback version:
Operator:
Final status: SUCCESS | ROLLED_BACK | PARTIAL
```

## 24. خطاهای رایج

| نشانه | علت محتمل | اقدام |
|---|---|---|
| API پاسخ `401/403` می‌دهد | key نامعتبر، env به container منتقل نشده، client غیرفعال یا path مجاز نیست | نام `KeyEnvironmentVariable`، وجود env بدون چاپ مقدار، `IsActive` و allowed paths را بررسی کنید |
| Gateway unhealthy باقی می‌ماند | authentication failure قبلی یا تنظیم ناقص | credential را اصلاح و یک request موفق اجرا کنید؛ سپس health را دوباره بررسی کنید |
| Telegram خطای `409 Conflict` می‌دهد | poller دوم یا webhook فعال است | instance اضافی را stop و webhook را طبق رفتار موجود gateway حذف کنید |
| بعد از restart پیام‌های قدیمی replay می‌شوند | state directory پایدار mount نشده یا offset خوانده نمی‌شود | mount، owner/permission و path واقعی داخل container را کنترل کنید |
| بعضی partها دوباره ارسال می‌شوند | crash در پنجره پس از پذیرش Telegram و قبل از persistence | این duplicate window در MVP پذیرفته شده؛ فایل idempotency و log را بررسی کنید، زیرساخت توزیع‌شده اضافه نکنید |
| سؤال کاربر پاسخ تحلیلی نمی‌گیرد | Telegram user لینک نشده یا orchestration backend خطا دارد | account link و correlation در API ایران را بررسی کنید؛ منطق AI را به Gateway منتقل نکنید |
| Gateway با نبود HMAC start نمی‌شود | validation قدیمی هنوز polling را به inbound controller متصل کرده است | تکمیل `T130-01` را بررسی کنید؛ HMAC برای polling-only الزامی نیست |
| timeout مکرر API | شبکه، DNS، TLS یا timeout نامتناسب | اتصال از Linode، log correlation و request timeout مصوب را بررسی کنید |

---

این سند با `Design.md`، `Story.md` و `Tasks.md` Feature 130 تنظیم شده و scope آن صرفاً deployment و operational verification همان مسیر تعاملی است.
