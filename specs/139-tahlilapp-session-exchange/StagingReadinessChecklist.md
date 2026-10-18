# چک‌لیست آمادگی staging برای Feature 139

**قاعده evidence:** هر مورد فقط زمانی `[x]` می‌شود که شواهد قابل بازبینی، زمان اجرا، محیط و owner داشته باشد. کد موجود به‌تنهایی اثبات availability سرویس خارجی، اجرای migration یا صحت production secret نیست.

## ۱. Secrets و Security

### SEC-01 — چرخش JWT signing key

- [ ] **Owner:** DevOps/Operations
- **روش راستی‌آزمایی:** key فعلی staging را در secret store ثبت کنید، key جدید را با overlap کنترل‌شده فعال کنید، صدور و اعتبارسنجی token را در همه replicaها تست کنید.
- **نتیجه مورد انتظار:** tokenهای جدید با key جدید معتبرند؛ rollout بدون خطای بین replicaها انجام می‌شود؛ key در repository یا log نیست.
- **Evidence:** `____________________________`
- **Status:** باز — evidence ندارد.

### SEC-02 — حذف secretهای exposed از runtime

- [ ] **Owner:** Sapino + DevOps
- **روش راستی‌آزمایی:** configuration، container environment، logs و crash dump staging را برای credential بررسی و secretهای افشاشده را revoke/rotate کنید.
- **نتیجه مورد انتظار:** فقط reference به secret store/environment وجود دارد و مقدار خام چاپ یا commit نشده است.
- **Evidence:** `____________________________`
- **Status:** باز — نیازمند verification محیطی.

### SEC-03 — secret store/environment configuration

- [ ] **Owner:** DevOps/Operations
- **روش راستی‌آزمایی:** `TAHLILAPP_EXCHANGE_API_KEY` یا نام configured در staging secret store ایجاد و access policy را به process identity محدود کنید.
- **نتیجه مورد انتظار:** Sapino بدون secret fail closed است و با secret صحیح gateway قابل استفاده است.
- **Evidence:** `____________________________`
- **Status:** باز.

### SEC-04 — TahlilApp service API key

- [ ] **Owner:** TahlilApp + DevOps
- **روش راستی‌آزمایی:** key با audience/owner مشخص روی exchange و status اعمال شود؛ کلید اشتباه/منقضی با `401/403` رد شود.
- **نتیجه مورد انتظار:** فقط Sapino Backend مجاز به endpointهای server-to-server است.
- **Evidence:** `____________________________`
- **Status:** باز — قرارداد واقعی TahlilApp تأیید نشده است.

### SEC-05 — رویه rotation API key

- [ ] **Owner:** TahlilApp + DevOps
- **روش راستی‌آزمایی:** دو کلید overlap کوتاه، تعویض secret Sapino، revoke کلید قبلی و تست exchange/status را اجرا و زمان rollback را ثبت کنید.
- **نتیجه مورد انتظار:** rotation بدون downtime و بدون قرارگیری key در browser URL انجام می‌شود.
- **Evidence:** `____________________________`
- **Status:** باز.

### SEC-06 — HTTPS/TLS

- [ ] **Owner:** DevOps/Operations
- **روش راستی‌آزمایی:** certificate chain، hostname، TLS policy و عدم redirect از HTTP را با ابزار شبکه staging بررسی کنید.
- **نتیجه مورد انتظار:** ارتباط خارجی فقط HTTPS معتبر است؛ client redirect را دنبال نمی‌کند.
- **Evidence:** `____________________________`
- **Status:** باز.

### SEC-07 — trusted reverse proxies و Forwarded Headers

- [ ] **Owner:** DevOps + Sapino
- **روش راستی‌آزمایی:** IPهای proxy مورد اعتماد و network boundary را در `ForwardedHeaders` ثبت و با درخواست مستقیم از proxy غیرمجاز تست کنید.
- **نتیجه مورد انتظار:** scheme/host/client IP فقط از proxy trusted پذیرفته می‌شود و `X-Forwarded-*` قابل جعل از اینترنت نیست.
- **Evidence:** `____________________________`
- **Status:** باز — configuration محیطی.

### SEC-08 — rate limiting

- [ ] **Owner:** Sapino + DevOps
- **روش راستی‌آزمایی:** روی `POST /api/auth/v1/tahlilapp/launch` policy `PublicAuthentication` را با burst/concurrency staging تست کنید.
- **نتیجه مورد انتظار:** درخواست بیش از حد `429` می‌گیرد؛ کد معتبر سالم و replay محدود نمی‌تواند rate limit را دور بزند.
- **Evidence:** `____________________________`
- **Status:** باز.

### SEC-09 — SameSite cookie

- [ ] **Owner:** Sapino Frontend/Backend
- **روش راستی‌آزمایی:** browser test برای `financial_copilot_refresh` در HTTPS staging، redirect و cross-site navigation انجام دهید.
- **نتیجه مورد انتظار:** cookie `HttpOnly`، `Secure` در production/staging و `SameSite=Strict` با path `/api/auth/v1` است؛ shared auth cookie وجود ندارد.
- **Evidence:** `____________________________`
- **Status:** باز.

## ۲. Data Protection

### DP-01 — key ring پایدار

- [ ] **Owner:** DevOps/Operations
- **روش راستی‌آزمایی:** `DataProtection:KeyRingPath` را به directory موجود و writable در secret-managed volume متصل کنید.
- **نتیجه مورد انتظار:** startup validation موفق و key ring بعد از restart باقی می‌ماند.
- **Evidence:** `____________________________`
- **Status:** باز.

### DP-02 — کلیدهای مشترک بین replicaها

- [ ] **Owner:** DevOps
- **روش راستی‌آزمایی:** profile را replica A بنویسد و replica B decrypt/mask کند؛ rollout هم‌زمان انجام شود.
- **نتیجه مورد انتظار:** `nationalId`/`mobile` بین replicaها قابل decrypt است.
- **Evidence:** `____________________________`
- **Status:** باز.

### DP-03 — Application name

- [ ] **Owner:** Sapino
- **روش راستی‌آزمایی:** configuration و Data Protection setup را بررسی کنید.
- **نتیجه مورد انتظار:** application name برابر `FinancialCopilot` در همه replicaهاست.
- **Evidence:** `____________________________`
- **Status:** باز.

### DP-04 — file access permissions

- [ ] **Owner:** DevOps
- **روش راستی‌آزمایی:** owner/group/ACL volume را بررسی و read/write را فقط به process identity بدهید.
- **نتیجه مورد انتظار:** process برنامه دسترسی لازم دارد و کاربران/سرویس‌های غیرمرتبط ندارند.
- **Evidence:** `____________________________`
- **Status:** باز.

### DP-05 — backup و restore drill

- [ ] **Owner:** DevOps/Operations
- **روش راستی‌آزمایی:** backup رمزنگاری‌شده key ring و DB staging بگیرید، restore را در محیط ایزوله انجام دهید.
- **نتیجه مورد انتظار:** profile موجود بعد از restore/restart قابل decrypt است.
- **Evidence:** `____________________________`
- **Status:** باز.

## ۳. Database و Billing

### DB-01 — backup staging

- [ ] **Owner:** DevOps/DBA
- **روش راستی‌آزمایی:** backup قابل restore برای AuthDb و BillingDb قبل از migration بگیرید.
- **نتیجه مورد انتظار:** restore test با timestamp و checksum ثبت شده است.
- **Evidence:** `____________________________`
- **Status:** باز.

### DB-02 — Auth migrations

- [ ] **Owner:** Sapino + DBA
- **روش راستی‌آزمایی:** migrationهای Auth شامل `AddTahlilAppIdentity` را فقط در staging اجرا و `__EFMigrationsHistory` را ثبت کنید.
- **نتیجه مورد انتظار:** schema، unique mapping و profile protection tables بدون data loss آماده‌اند.
- **Evidence:** `____________________________`
- **Status:** باز — اجرای live فرض نشده است.

### DB-03 — Billing migrations

- [ ] **Owner:** Sapino + DBA
- **روش راستی‌آزمایی:** Billing migrations را در staging اجرا و status/row count را با baseline مقایسه کنید.
- **نتیجه مورد انتظار:** Billing schema و seed با نسخه برنامه هم‌خوان است.
- **Evidence:** `____________________________`
- **Status:** باز.

### DB-04 — migration credit catalog

- [ ] **Owner:** Sapino + DBA
- **روش راستی‌آزمایی:** اجرای کنترل‌شده `20261018092758_UpdateApprovedMonthlyAiCredits` در staging؛ ثبت migration history و before/after catalog.
- **نتیجه مورد انتظار:** migration اجرا شده و قابل rollback procedure است؛ عدم اجرای آن در live نباید به‌عنوان موفقیت گزارش شود.
- **Evidence:** `____________________________`
- **Status:** باز — در repository موجود، اجرای live تأیید نشده.

### DB-05 — مقادیر Free/Plus/Pro

- [ ] **Owner:** Sapino Billing
- **روش راستی‌آزمایی:** query read-only روی `SubscriptionPlans` و تست wallet creation.
- **نتیجه مورد انتظار:** `Free=25`، `Plus=260`، `Pro=2600` monthly AI credits.
- **Evidence:** `____________________________`
- **Status:** باز تا evidence staging.

### DB-06 — حفظ balance کاربران موجود

- [ ] **Owner:** Billing + DBA
- **روش راستی‌آزمایی:** snapshot wallet/ledger قبل و بعد migration و یک sync اشتراک بگیرید.
- **نتیجه مورد انتظار:** existing wallet و consumed ledger reset یا duplicate نمی‌شوند؛ subscription sync credit grant نمی‌کند.
- **Evidence:** `____________________________`
- **Status:** باز.

### DB-07 — expiry enforcement

- [ ] **Owner:** Sapino Billing
- **روش راستی‌آزمایی:** plan paid با expiry گذشته، refresh موفق و unavailable بعد از stale grace را در staging اجرا کنید.
- **نتیجه مورد انتظار:** plan منقضی به `Free` downgrade می‌شود؛ capability paid باقی نمی‌ماند.
- **Evidence:** `____________________________`
- **Status:** باز.

### DB-08 — rollback preparation

- [ ] **Owner:** DBA/Operations
- **روش راستی‌آزمایی:** rollback migration و restore backup را روی clone اجرا کنید.
- **نتیجه مورد انتظار:** زمان، owner و trigger rollback ثبت شده و restore بدون حدس‌زدن قابل انجام است.
- **Evidence:** `____________________________`
- **Status:** باز.

## ۴. Email و native authentication

### EMAIL-01 — SMTP/provider و sender

- [ ] **Owner:** DevOps + Sapino
- **روش راستی‌آزمایی:** provider، sender identity و secret را در staging تنظیم و ارسال test را trace کنید.
- **نتیجه مورد انتظار:** sender مجاز است و secret در log نیست.
- **Evidence:** `____________________________`
- **Status:** باز.

### EMAIL-02 — SPF/DKIM/DMARC

- [ ] **Owner:** DevOps/Domain owner
- **روش راستی‌آزمایی:** DNS و report provider را بررسی کنید.
- **نتیجه مورد انتظار:** policy مناسب staging/production و عدم spoofing ناخواسته.
- **Evidence:** `____________________________`
- **Status:** باز.

### EMAIL-03 — verification و password setup

- [ ] **Owner:** Sapino
- **روش راستی‌آزمایی:** local user TahlilApp از email enrollment، token expiry و password setup عبور کند.
- **نتیجه مورد انتظار:** همان local user email/password مستقل می‌گیرد؛ email در TahlilApp exchange لازم نیست.
- **Evidence:** `____________________________`
- **Status:** باز.

### EMAIL-04 — password reset، duplicate email و delivery failure

- [ ] **Owner:** Sapino + QA
- **روش راستی‌آزمایی:** reset معتبر/منقضی، email تکراری و provider failure را تست کنید.
- **نتیجه مورد انتظار:** خطاها safe و قابل پشتیبانی هستند؛ merge خودکار رخ نمی‌دهد.
- **Evidence:** `____________________________`
- **Status:** باز.

## ۵. TahlilApp integration

### TA-01 — launch endpoint

- [ ] **Owner:** TahlilApp team
- **روش راستی‌آزمایی:** کاربر signed-in واقعی staging، `POST /api/financial-copilot/launch` و redirect به Sapino `/launch` را اجرا کند.
- **نتیجه مورد انتظار:** LaunchCode کوتاه‌عمر، CSPRNG، hash‌شده و single-use صادر می‌شود.
- **Evidence:** `____________________________`
- **Status:** باز — availability بیرونی تأیید نشده.

### TA-02 — exchange endpoint و DTO

- [ ] **Owner:** TahlilApp + Sapino
- **روش راستی‌آزمایی:** `POST /api/financial-copilot/session-exchange` با `X-Api-Key`، JSON معتبر، ۸KB limit و correlation id تست شود.
- **نتیجه مورد انتظار:** fields A دقیقاً parse می‌شوند؛ پاسخ status code/content-type درست دارد.
- **Evidence:** `____________________________`
- **Status:** باز.

### TA-03 — status endpoint

- [ ] **Owner:** TahlilApp team
- **روش راستی‌آزمایی:** GET با user واقعی و API key، برای Active/Suspended/404/timeout.
- **نتیجه مورد انتظار:** بدون PII؛ `user.status` و subscription state معتبر؛ timeout حداکثر ۳ ثانیه در client Sapino.
- **Evidence:** `____________________________`
- **Status:** باز.

### TA-04 — batch endpoint تصمیم‌گیری

- [ ] **Owner:** Product + TahlilApp + Sapino
- **روش راستی‌آزمایی:** نیاز scale را با worker فعلی مقایسه کنید؛ در صورت نیاز contract جدا برای batch تصویب کنید.
- **نتیجه مورد انتظار:** worker فعلی بدون batch خارجی کار می‌کند؛ هیچ batch endpoint تأییدنشده به‌عنوان dependency گزارش نمی‌شود.
- **Evidence:** `____________________________`
- **Status:** باز — تصمیم product/scale.

### TA-05 — service authentication

- [ ] **Owner:** TahlilApp + DevOps
- **روش راستی‌آزمایی:** key صحیح، key منقضی، نبودن key و rotation overlap را تست کنید.
- **نتیجه مورد انتظار:** unauthorized بدون افشای contract داخلی رد می‌شود و Sapino fail safe است.
- **Evidence:** `____________________________`
- **Status:** باز.

### TA-06 — مصرف یک‌باره و replay

- [ ] **Owner:** TahlilApp + Sapino
- **روش راستی‌آزمایی:** همان code را هم‌زمان و سپس با فاصله دوباره ارسال کنید.
- **نتیجه مورد انتظار:** حداکثر یک session موفق؛ درخواست بعدی `launch-invalid` و بدون user/session جدید.
- **Evidence:** `____________________________`
- **Status:** باز تا اجرای staging.

### TA-07 — timeout و recovery

- [ ] **Owner:** Sapino + TahlilApp
- **روش راستی‌آزمایی:** latency/connection failure مصنوعی ایجاد و پس از recovery دوباره launch کنید.
- **نتیجه مورد انتظار:** کاربر `503 launch-unavailable` می‌گیرد، stack/body/secret افشا نمی‌شود و retry کاربر با code سالم قابل انجام است.
- **Evidence:** `____________________________`
- **Status:** باز.

### TA-08 — suspended account

- [ ] **Owner:** TahlilApp + Sapino
- **روش راستی‌آزمایی:** exchange و status را برای user با `status=Suspended` اجرا کنید.
- **نتیجه مورد انتظار:** session جدید صادر نمی‌شود و revalidation local identity را suspended می‌کند.
- **Evidence:** `____________________________`
- **Status:** باز.

## ۶. Billing و permissions

### BILL-01 — Free access

- [ ] **Owner:** Billing + Product
- **روش راستی‌آزمایی:** first launch با `Free` و wallet creation را بررسی کنید.
- **نتیجه مورد انتظار:** plan Free، allowance catalog برابر ۲۵؛ permission نام‌دار بدون mapping capability اعمال نمی‌شود.
- **Evidence:** `____________________________`
- **Status:** باز.

### BILL-02 — Plus access

- [ ] **Owner:** Billing + Product
- **روش راستی‌آزمایی:** active Plus با expiry آینده، query plan و wallet usage.
- **نتیجه مورد انتظار:** plan Plus و allowance برابر ۲۶۰؛ sync اعتبار جدید اضافه نمی‌کند.
- **Evidence:** `____________________________`
- **Status:** باز.

### BILL-03 — Pro access

- [ ] **Owner:** Billing + Product
- **روش راستی‌آزمایی:** active Pro با expiry آینده.
- **نتیجه مورد انتظار:** plan Pro و allowance برابر ۲۶۰۰؛ consumed ledger مستقل باقی می‌ماند.
- **Evidence:** `____________________________`
- **Status:** باز.

### BILL-04 — permission mapping

- [ ] **Owner:** Product + Sapino + TahlilApp
- **روش راستی‌آزمایی:** schema permission نام‌دار و mapping table را تصویب و سپس با allow/revoke تست کنید.
- **نتیجه مورد انتظار:** تا تصویب extension C، Sapino فقط count را می‌بیند و permission به capability تبدیل نمی‌شود.
- **Evidence:** `____________________________`
- **Status:** باز — gap پیاده‌سازی.

### BILL-05 — usage و no duplication

- [ ] **Owner:** Billing
- **روش راستی‌آزمایی:** login تکراری، sync تکراری و مصرف AI را با snapshot wallet/ledger مقایسه کنید.
- **نتیجه مورد انتظار:** sync idempotent است؛ credit duplicate grant نمی‌شود و مصرف فقط یک بار ثبت می‌شود.
- **Evidence:** `____________________________`
- **Status:** باز.

### BILL-06 — upgrade/downgrade و expiry

- [ ] **Owner:** Billing + Product
- **روش راستی‌آزمایی:** Free→Plus→Pro، سپس downgrade/expiry و refresh پس از TTL را اجرا کنید.
- **نتیجه مورد انتظار:** plan و capability paid درست تغییر می‌کند؛ wallet/ledger طبق policy محصول reset نمی‌شود.
- **Evidence:** `____________________________`
- **Status:** باز.

### BILL-07 — TahlilApp unavailable

- [ ] **Owner:** Sapino + Operations
- **روش راستی‌آزمایی:** status/exchange را unavailable کنید و قبل/بعد از stale grace observe کنید.
- **نتیجه مورد انتظار:** paid plan تا شش ساعت stale grace حفظ می‌شود و بعد به Free می‌رود؛ failure audit/log PII ندارد.
- **Evidence:** `____________________________`
- **Status:** باز.

## ۷. تست‌های end-to-end

### E2E-01 — First-time TahlilApp launch

- [ ] **Owner:** QA + TahlilApp + Sapino
- **روش اجرا:** user به TahlilApp login کند، launch را بزند و Sapino را بدون login دیگر باز کند.
- **نتیجه مورد انتظار:** یک local identity، tenant و session Sapino ساخته می‌شود؛ پاسخ موفق و redirect کامل است.
- **Evidence:** `____________________________`
- **Status:** باز.

### E2E-02 — Returning launch

- [ ] **Owner:** QA + Sapino Identity
- **روش اجرا:** همان `externalUserId` دوباره launch کند.
- **نتیجه مورد انتظار:** همان local user، conversationها و Billing account resolve می‌شوند؛ duplicate user ساخته نمی‌شود.
- **Evidence:** `____________________________`
- **Status:** باز.

### E2E-03 — Native credential enrollment

- [ ] **Owner:** QA + Email owner + Sapino
- **روش اجرا:** user TahlilApp یک email اضافه، verify و password ایجاد کند.
- **نتیجه مورد انتظار:** همان local user email/password می‌گیرد؛ profile و identity mapping تغییر نمی‌کند.
- **Evidence:** `____________________________`
- **Status:** باز.

### E2E-04 — Independent native login

- [ ] **Owner:** QA + Sapino
- **روش اجرا:** logout، سپس با email/password مستقیماً به Sapino login کنید.
- **نتیجه مورد انتظار:** همان local user، conversationها و Billing account قابل مشاهده‌اند.
- **Evidence:** `____________________________`
- **Status:** باز.

### E2E-05 — Existing email collision

- [ ] **Owner:** QA + Sapino Identity
- **روش اجرا:** email متعلق به user native دیگر را برای user TahlilApp enroll کنید.
- **نتیجه مورد انتظار:** merge خودکار رخ نمی‌دهد؛ خطای safe و قابل پشتیبانی ثبت می‌شود.
- **Evidence:** `____________________________`
- **Status:** باز.

### E2E-06 — Free subscription

- [ ] **Owner:** QA + Billing
- **روش اجرا:** exchange با plan Free و wallet تازه.
- **نتیجه مورد انتظار:** monthly allowance برابر ۲۵ است.
- **Evidence:** `____________________________`
- **Status:** باز.

### E2E-07 — Plus subscription

- [ ] **Owner:** QA + Billing
- **روش اجرا:** exchange/status با Plus فعال و expiry آینده.
- **نتیجه مورد انتظار:** monthly allowance برابر ۲۶۰ است و sync duplicate credit نمی‌دهد.
- **Evidence:** `____________________________`
- **Status:** باز.

### E2E-08 — Pro subscription

- [ ] **Owner:** QA + Billing
- **روش اجرا:** exchange/status با Pro فعال و expiry آینده.
- **نتیجه مورد انتظار:** monthly allowance برابر ۲۶۰۰ است و consumed ledger حفظ می‌شود.
- **Evidence:** `____________________________`
- **Status:** باز.

### E2E-09 — Subscription downgrade

- [ ] **Owner:** QA + Billing + Product
- **روش اجرا:** user paid را در TahlilApp به Free/inactive تغییر دهید و login یا worker refresh را اجرا کنید.
- **نتیجه مورد انتظار:** entitlement به Free می‌رسد؛ sync اعتبار جدید یا reset ledger انجام نمی‌دهد؛ policy credit rollover/reset باید با Product ثبت شود.
- **Evidence:** `____________________________`
- **Status:** باز.

### E2E-10 — Subscription expiry

- [ ] **Owner:** QA + Billing
- **روش اجرا:** expiry را به گذشته ببرید و پس از status refresh قابلیت paid را بررسی کنید.
- **نتیجه مورد انتظار:** paid capability دیگر grant نمی‌شود و plan به Free می‌رود.
- **Evidence:** `____________________________`
- **Status:** باز.

### E2E-11 — Invalid/replayed LaunchCode

- [ ] **Owner:** QA + Security
- **روش اجرا:** code malformed، expired و همان code مصرف‌شده را ارسال کنید.
- **نتیجه مورد انتظار:** session صادر نمی‌شود؛ پاسخ کاربرمحور `401 launch-invalid` بدون oracle است.
- **Evidence:** `____________________________`
- **Status:** باز.

### E2E-12 — TahlilApp unavailable

- [ ] **Owner:** QA + Operations
- **روش اجرا:** exchange/status را timeout یا 5xx کنید، سپس سرویس را recover کنید.
- **نتیجه مورد انتظار:** failure امن `503`، log redacted، stale grace و recovery قابل مشاهده است.
- **Evidence:** `____________________________`
- **Status:** باز.

### E2E-13 — PII protection

- [ ] **Owner:** Security + QA
- **روش اجرا:** profile با nationalId/mobile را provision کنید؛ DB، log، JWT، response و masked profile را inspect کنید.
- **نتیجه مورد انتظار:** DB فقط ciphertext/last4 لازم دارد؛ مقدار خام در log/JWT نیست؛ status PII را رد می‌کند.
- **Evidence:** `____________________________`
- **Status:** باز.

### E2E-14 — Session/logout behavior

- [ ] **Owner:** QA + Security
- **روش اجرا:** access token، refresh rotation، logout و استفاده از refresh قبلی را تست کنید.
- **نتیجه مورد انتظار:** access token حداکثر ۱۵ دقیقه عمر دارد؛ logout refresh token را revoke می‌کند؛ access token stateless قدیمی ممکن است تا پایان عمر کوتاه خود معتبر بماند و باید این behavior ثبت شود.
- **Evidence:** `____________________________`
- **Status:** باز.

## ۸. جمع‌بندی readiness

- [ ] قرارداد TahlilApp برای launch، exchange و status با ownerهای هر دو تیم تأیید شده است.
- [ ] endpointهای external در staging با TLS و API key واقعی قابل دسترس‌اند.
- [ ] migrationهای Auth/Billing و `20261018092758_UpdateApprovedMonthlyAiCredits` در staging با evidence اجرا شده‌اند.
- [ ] permission mapping یا تصمیم صریح «فعلاً unsupported» ثبت شده است.
- [ ] E2E-01 تا E2E-14 با evidence اجرا شده‌اند.

**وضعیت فعلی:** آماده تحویل مستندات integration؛ برای staging و production readiness هنوز `Blocked by external/operational gates` است.
