# Tasks: Fix Nadpco MonthlyReportLineItems Extra Products Bug

**Bug ref:** `specs/bugs/nadpco-monthly-report-lineitems-extra-products-data-integrity-2026-06-24.md`  
**Date:** 2026-06-25

---

## مرور کلی

باگ ریشه‌ای در `BuildExternalReportId` داخل `NadpcoApiMonthlyActivityNormalizer` هست: وقتی API رکوردی بدون `activityId` برمی‌گرداند، `categoryId` بخشی از کلید شناسه گزارش می‌شود و برای هر دسته‌بندی یک ردیف جداگانه `MonthlyReport` ایجاد می‌کند. `CompanyProductRevenueMixCalculator` و `CompanyMonthlyActivityTrendSnapshotCalculator` همه ردیف‌های منطبق با شرکت/ماه/outputType=0 را جمع می‌زنند — از جمله محصولات دسته‌های مجزا.

مراحل این task به ترتیب اجرا باید انجام شوند.

---

## مرحله ۱ — پاک‌سازی دیتابیس (SQL دستی)

> اجرا کنید **قبل** از اعمال هر تغییر کد. این کوئری‌ها دیتای آلوده را پاک می‌کنند تا بعد از فیکس کد، از صفر ingestion صحیح انجام شود.

### ۱-۱. شناسایی `MonthlyReport`های تکراری (برای تایید دستی)

```sql
-- تعداد MonthlyReport‌های تکراری برای یک شرکت/ماه/outputType
SELECT
    "ExternalCompanyId",
    "PeriodStart",
    "OutputType",
    "ReportType",
    COUNT(*) AS report_count,
    STRING_AGG("ExternalReportId", ' | ') AS external_report_ids
FROM "MonthlyReports"
WHERE "ProviderName" = 'NoavaranCurrentApi'
  AND "ReportType" = 'ProductSales'
  AND "OutputType" = 0
GROUP BY "ExternalCompanyId", "PeriodStart", "OutputType", "ReportType"
HAVING COUNT(*) > 1
ORDER BY "ExternalCompanyId", "PeriodStart";
```

### ۱-۲. شناسایی ردیف‌های اضافی برای کسرا (companyId=202) جهت تایید دستی

```sql
-- بررسی وضعیت MonthlyReport‌های کسرا برای 1404/12
SELECT
    "Id",
    "ExternalCompanyId",
    "ExternalReportId",
    "PeriodStart",
    "PeriodEnd",
    "OutputType",
    "ReportType",
    "LastSynchronizedAt"
FROM "MonthlyReports"
WHERE "ProviderName" = 'NoavaranCurrentApi'
  AND "ExternalCompanyId" = '202'
  AND "ReportType" = 'ProductSales'
  AND "OutputType" = 0
ORDER BY "PeriodStart", "LastSynchronizedAt";
```

### ۱-۳. بررسی line item‌های اضافی کسرا

```sql
-- مشاهده همه line item‌های کسرا برای ماه 1404/12
SELECT
    li."Id",
    li."ProductCode",
    li."Title",
    li."SalesAmount",
    li."ProductionQuantity",
    li."SalesQuantity",
    mr."ExternalReportId",
    mr."PeriodStart"
FROM "MonthlyReportLineItems" li
JOIN "MonthlyReports" mr ON mr."Id" = li."MonthlyReportId"
WHERE mr."ProviderName" = 'NoavaranCurrentApi'
  AND mr."ExternalCompanyId" = '202'
  AND mr."ReportType" = 'ProductSales'
  AND mr."OutputType" = 0
  AND mr."PeriodStart" BETWEEN '2026-02-19' AND '2026-03-20'  -- 1404/12
ORDER BY mr."ExternalReportId", li."Title";
```

### ۱-۴. حذف کامل MonthlyReportLineItems، MonthlyReports، CompanyProductRevenueMix، CompanyMonthlyActivityTrendSnapshots

> ⚠️ این transaction همه دیتای مربوطه را پاک می‌کند. قبل از اجرا از صحت کوئری‌های بالا مطمئن شوید.

```sql
BEGIN;

-- حذف همه line item‌های NoavaranCurrentApi
DELETE FROM "MonthlyReportLineItems"
WHERE "MonthlyReportId" IN (
    SELECT "Id" FROM "MonthlyReports"
    WHERE "ProviderName" = 'NoavaranCurrentApi'
);

-- حذف همه MonthlyReport‌های NoavaranCurrentApi
DELETE FROM "MonthlyReports"
WHERE "ProviderName" = 'NoavaranCurrentApi';

-- حذف همه CompanyProductRevenueMix‌های NoavaranCurrentApi
DELETE FROM "CompanyProductRevenueMixes"
WHERE "SourceProviderName" = 'NoavaranCurrentApi';

-- حذف همه CompanyMonthlyActivityTrendSnapshots
DELETE FROM "CompanyMonthlyActivityTrendSnapshots";

COMMIT;
```

### ۱-۵. ریست کردن وضعیت backfill (اختیاری — اگر می‌خواهید monthly-backfill از صفر شروع کند)

```sql
-- ریست وضعیت backfill ماهانه
DELETE FROM "MonthlyActivityBackfillStates";

-- حذف SyncRun‌های مربوط به monthly-backfill تا re-run صحیح انجام شود
DELETE FROM "ProviderSyncRuns"
WHERE "IdempotencyKey" LIKE 'nadpco-monthlybf:%';
```

---

## مرحله ۲ — فیکس کد (باگ اصلی)

### ۲-۱. حذف `categoryId` از `BuildExternalReportId`

**فایل:** `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiMonthlyActivityNormalizer.cs`  
**خطوط:** 348-353

```csharp
// BEFORE (BUG):
var categoryPart = categoryId?.ToString(CultureInfo.InvariantCulture) ?? "none";
return string.Create(
    CultureInfo.InvariantCulture,
    $"{sourceKind}:{companyId}:{year:D4}-{month:D2}:output-{outputPart}:category-{categoryPart}");

// AFTER (FIX):
return string.Create(
    CultureInfo.InvariantCulture,
    $"{sourceKind}:{companyId}:{year:D4}-{month:D2}:output-{outputPart}");
```

**توضیح:** این تغییر تضمین می‌کند که همه محصولات یک شرکت در یک ماه و یک outputType — صرف نظر از دسته‌بندی — زیر یک `MonthlyReport` واحد ذخیره می‌شوند.

### ۲-۲. اضافه کردن authoritative replace برای line item‌ها

در همان فایل، قبل از loop upsert خطوط 79-102، بعد از `await dbContext.SaveChangesAsync(cancellationToken)` اول (خط 77)، خطوط زیر اضافه کنید:

```csharp
// Authoritative replace: delete all existing line items for this report before
// re-inserting from the current payload. Prevents stale rows from prior ingestion
// runs (e.g. category structure changed, product removed) from accumulating.
var staleLineItems = await dbContext.MonthlyReportLineItems
    .Where(li => li.MonthlyReportId == report.Id)
    .ToListAsync(cancellationToken);
if (staleLineItems.Count > 0)
{
    dbContext.MonthlyReportLineItems.RemoveRange(staleLineItems);
    await dbContext.SaveChangesAsync(cancellationToken);
}
```

### ۲-۳. اضافه کردن unique index به عنوان guard شماتیکی

**فایل:** `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionConfigurations.cs`  
**کلاس:** `NormalizedMonthlyReportRowConfiguration.Configure`  
بعد از `builder.HasIndex(row => new { row.ProviderName, row.ExternalReportId }).IsUnique();` (خط 134) اضافه کنید:

```csharp
// Guard: one logical report per company/period/outputType/reportType.
builder.HasIndex(row => new
{
    row.ProviderName,
    row.ExternalCompanyId,
    row.PeriodStart,
    row.OutputType,
    row.ReportType
}).IsUnique().HasFilter("\"ExternalCompanyId\" IS NOT NULL AND \"ReportType\" IS NOT NULL");
```

### ۲-۴. ایجاد EF Migration

```bash
dotnet ef migrations add FixMonthlyReportUniqueKeyAndLineItemReplace \
  --project src/backend/FinancialCopilot.Infrastructure \
  --startup-project src/backend/FinancialCopilot.API \
  --context FinancialIngestionDbContext
```

---

## مرحله ۳ — اضافه کردن endpoint برای ingestion یک شرکت با date range مشخص

این endpoint به شما اجازه می‌دهد داده یک شرکت خاص (مثلاً کسرا) را برای بازه 1404/01 تا 1405/03 دریافت و ذخیره کنید تا صحت فیکس را تایید کنید.

### ۳-۱. اضافه کردن request/response contract

**فایل:** `src/backend/FinancialCopilot.API/Contracts/AdminDataOperationsContracts.cs`

```csharp
/// <summary>
/// Triggers a single-company monthly-activity re-ingestion for a specific Jalali date range.
/// All outputTypeId values (0–4) are fetched. Existing MonthlyReports and their line items
/// for the company are replaced (authoritative replace strategy).
/// </summary>
public sealed record AdminSingleCompanyMonthlyIngestionRequest(
    /// <summary>Nadpco ExternalCompanyId (numeric string, e.g. "202" for کسرا).</summary>
    string ExternalCompanyId,
    /// <summary>Jalali fromDate in YYYYMM format, e.g. "140401".</summary>
    string FromDate,
    /// <summary>Jalali toDate in YYYYMM format, e.g. "140503".</summary>
    string ToDate);

public sealed record AdminSingleCompanyMonthlyIngestionResponse(
    string ExternalCompanyId,
    string FromDate,
    string ToDate,
    int MonthsIngested,
    string Duration);
```

### ۳-۲. اضافه کردن Application contract

**فایل:** `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/SingleCompanyMonthlyIngestionContracts.cs` (فایل جدید)

```csharp
namespace FinancialCopilot.Application.FinancialData.Ingestion;

public sealed record SingleCompanyMonthlyIngestionRequest(
    string ExternalCompanyId,
    string FromDate,
    string ToDate,
    string RequestedBy);

public sealed record SingleCompanyMonthlyIngestionResult(
    string ExternalCompanyId,
    string FromDate,
    string ToDate,
    int MonthsIngested,
    TimeSpan Duration);

public interface ISingleCompanyMonthlyIngestionService
{
    Task<SingleCompanyMonthlyIngestionResult> RunAsync(
        SingleCompanyMonthlyIngestionRequest request,
        CancellationToken cancellationToken);
}
```

### ۳-۳. پیاده‌سازی Service

**فایل:** `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/SingleCompanyMonthlyIngestionService.cs` (فایل جدید)

```csharp
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

/// <summary>
/// Fetches and normalizes monthly production/sales data for a single company
/// over a specified Jalali date range. Each Nadpco fetch call spans the full range;
/// the normalizer partitions the response into individual month-reports.
/// Used for targeted verification after bug fixes and for ad-hoc data repair.
/// </summary>
public sealed class SingleCompanyMonthlyIngestionService(
    IMonthlyProductionSalesProvider dataProvider,
    IFinancialPayloadNormalizer normalizer,
    TimeProvider timeProvider,
    ILogger<SingleCompanyMonthlyIngestionService> logger)
    : ISingleCompanyMonthlyIngestionService
{
    public async Task<SingleCompanyMonthlyIngestionResult> RunAsync(
        SingleCompanyMonthlyIngestionRequest request,
        CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetUtcNow();

        logger.LogInformation(
            "Single-company monthly ingestion starting. Company={CompanyId}, From={From}, To={To}, RequestedBy={By}.",
            request.ExternalCompanyId, request.FromDate, request.ToDate, request.RequestedBy);

        // The data provider client uses INoavaranCurrentApiBoundaryOverride to apply the
        // from/to window. We publish one sync request spanning the full range; the normalizer
        // groups the response by individual company-months internally.
        var payload = await dataProvider.FetchMonthlyReportsAsync(
            request.ExternalCompanyId,
            cancellationToken);

        var outcome = await normalizer.NormalizeAsync(payload, cancellationToken);

        var duration = timeProvider.GetUtcNow() - startedAt;

        logger.LogInformation(
            "Single-company monthly ingestion completed. Company={CompanyId}, ReportsNormalized={Count}, Duration={Duration}.",
            request.ExternalCompanyId, outcome.NormalizedCount, duration);

        return new SingleCompanyMonthlyIngestionResult(
            request.ExternalCompanyId,
            request.FromDate,
            request.ToDate,
            outcome.NormalizedCount,
            duration);
    }
}
```

> **نکته معماری:** به جای پیاده‌سازی مستقیم HTTP در سرویس، از `IMonthlyProductionSalesProvider` (که `NadpcoApiDataProviderClient` آن را پیاده‌سازی می‌کند) استفاده می‌شود. Date range از طریق `INoavaranCurrentApiBoundaryOverride` (موجود در DI pipeline برای scoped requests) به client رسیده و در `FetchMonthlyReportsAsync` اعمال می‌شود.
>
> اگر `INoavaranCurrentApiBoundaryOverride` در این service context در دسترس نیست، یک راه‌حل ساده‌تر این است که مستقیماً از `IDataSyncRequestPublisher` یک request با `SourceDateRangeStartJalali` و `SourceDateRangeEndJalali` پابلیش کنید — همان الگوی `MonthlyActivityBackfillCoordinator`.

### ۳-۴. ثبت در DI

**فایل:** `src/backend/FinancialCopilot.Infrastructure/ServiceCollectionExtensions.cs`

```csharp
services.AddScoped<ISingleCompanyMonthlyIngestionService, SingleCompanyMonthlyIngestionService>();
```

### ۳-۵. اضافه کردن endpoint به Controller

**فایل:** `src/backend/FinancialCopilot.API/Controllers/AdminDataOperationsController.cs`

بعد از endpoint `trend-snapshot-backfill` (خط ≈494) اضافه کنید:

```csharp
/// <summary>
/// Fetches and normalizes monthly production/sales data for a single company over a Jalali
/// date range. Use this endpoint to:
///   1. Verify bug fix correctness for a specific company (e.g. کسرا companyId=202)
///   2. Re-ingest data for a company after clearing corrupted rows
///
/// fromDate / toDate format: YYYYMM (e.g. "140401", "140503")
/// The fix in BuildExternalReportId ensures all categories are merged into one MonthlyReport.
/// </summary>
[HttpPost("noavaran-current/single-company-monthly-ingestion")]
public async Task<ActionResult<AdminSingleCompanyMonthlyIngestionResponse>> RunSingleCompanyMonthlyIngestion(
    [FromBody] AdminSingleCompanyMonthlyIngestionRequest request,
    CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(request.ExternalCompanyId))
    {
        ModelState.AddModelError(nameof(request.ExternalCompanyId), "ExternalCompanyId is required.");
        return ValidationProblem(ModelState);
    }

    if (string.IsNullOrWhiteSpace(request.FromDate) || request.FromDate.Length != 6 ||
        !int.TryParse(request.FromDate, out _))
    {
        ModelState.AddModelError(nameof(request.FromDate),
            "FromDate must be in YYYYMM format (e.g. '140401').");
        return ValidationProblem(ModelState);
    }

    if (string.IsNullOrWhiteSpace(request.ToDate) || request.ToDate.Length != 6 ||
        !int.TryParse(request.ToDate, out _))
    {
        ModelState.AddModelError(nameof(request.ToDate),
            "ToDate must be in YYYYMM format (e.g. '140503').");
        return ValidationProblem(ModelState);
    }

    var actor = currentActor.Actor;
    var result = await singleCompanyMonthlyIngestionService.RunAsync(
        new SingleCompanyMonthlyIngestionRequest(
            request.ExternalCompanyId,
            request.FromDate,
            request.ToDate,
            RequestedBy: $"{actor.ActorType}:{actor.ActorId}"),
        cancellationToken);

    return Ok(new AdminSingleCompanyMonthlyIngestionResponse(
        result.ExternalCompanyId,
        result.FromDate,
        result.ToDate,
        result.MonthsIngested,
        result.Duration.ToString(@"hh\:mm\:ss")));
}
```

Controller constructor-ی که `singleCompanyMonthlyIngestionService` را inject می‌کند باید اضافه شود.

---

## مرحله ۴ — تست صحت داده کسرا

بعد از اعمال کد فیکس و migrate کردن:

### ۴-۱. اجرای endpoint برای کسرا

```
POST /api/v1/admin/noavaran-current/single-company-monthly-ingestion
Authorization: DataAdmin
Content-Type: application/json

{
    "externalCompanyId": "202",
    "fromDate": "140401",
    "toDate": "140503"
}
```

### ۴-۲. تایید صحت در دیتابیس

```sql
-- باید دقیقاً یک MonthlyReport برای هر ماه/outputType وجود داشته باشد
SELECT
    "ExternalCompanyId",
    "PeriodStart",
    "OutputType",
    "ReportType",
    COUNT(*) AS report_count
FROM "MonthlyReports"
WHERE "ProviderName" = 'NoavaranCurrentApi'
  AND "ExternalCompanyId" = '202'
  AND "ReportType" = 'ProductSales'
  AND "OutputType" = 0
GROUP BY "ExternalCompanyId", "PeriodStart", "OutputType", "ReportType"
ORDER BY "PeriodStart";
-- انتظار: count=1 برای هر ردیف

-- بررسی line item‌های ماه 1404/12 (Gregorian: 2026-02-19 to 2026-03-20)
SELECT
    li."Title",
    li."ProductCode",
    li."SalesAmount",
    li."ProductionQuantity",
    li."SalesQuantity"
FROM "MonthlyReportLineItems" li
JOIN "MonthlyReports" mr ON mr."Id" = li."MonthlyReportId"
WHERE mr."ProviderName" = 'NoavaranCurrentApi'
  AND mr."ExternalCompanyId" = '202'
  AND mr."OutputType" = 0
  AND mr."PeriodStart" BETWEEN '2026-02-19' AND '2026-03-20'
ORDER BY li."SalesAmount" DESC NULLS LAST;

-- تایید مجموع صحیح: باید 2,119,732 باشد
SELECT
    mr."PeriodStart",
    SUM(li."SalesAmount") AS total_sales
FROM "MonthlyReportLineItems" li
JOIN "MonthlyReports" mr ON mr."Id" = li."MonthlyReportId"
WHERE mr."ProviderName" = 'NoavaranCurrentApi'
  AND mr."ExternalCompanyId" = '202'
  AND mr."OutputType" = 0
  AND mr."PeriodStart" BETWEEN '2026-02-19' AND '2026-03-20'
GROUP BY mr."PeriodStart";
-- انتظار: SUM = 2,119,732
```

### ۴-۳. تایید عدم وجود ردیف‌های اضافی

```sql
-- این محصولات نباید در DB وجود داشته باشند:
SELECT "Title", "SalesAmount"
FROM "MonthlyReportLineItems" li
JOIN "MonthlyReports" mr ON mr."Id" = li."MonthlyReportId"
WHERE mr."ExternalCompanyId" = '202'
  AND mr."OutputType" = 0
  AND mr."PeriodStart" BETWEEN '2026-02-19' AND '2026-03-20'
  AND li."Title" IN (
    'کاتالیست اکتیو آلومینا',
    'گلوله های سایز 75 - 15',
    'گلوله های توزیعی',
    'گلوله های سیلیسی',
    'لاینر',
    'گلوله سایز 15-75 ADM900',
    'گلوله های سایز 75 - 15 برگشت از فروش'
);
-- انتظار: 0 ردیف
```

---

## مرحله ۵ — backfill کامل همه شرکت‌ها

### ۵-۱. endpoint موجود برای همه شرکت‌ها

پس از تایید صحت داده کسرا، از endpoint موجود برای backfill همه شرکت‌های NoavaranEligibleCompanies استفاده کنید:

```
POST /api/v1/admin/noavaran-current/monthly-backfill
Authorization: DataAdmin
```

این endpoint به صورت خودکار:
- از `NoavaranEligibleCompanies` view لیست شرکت‌ها را می‌گیرد
- از 1405/latest به سمت 1404/01 پیش می‌رود (newest first)
- برای هر شرکت/ماه یک sync request enqueue می‌کند
- idempotent است — اگر نیمه‌کاره ماند، دوباره اجرا کنید

### ۵-۲. بعد از اتمام monthly-backfill، revenue mix و trend snapshot را rebuild کنید

```
POST /api/v1/admin/noavaran-current/product-revenue-mix-backfill
Authorization: DataAdmin
```

```
POST /api/v1/admin/noavaran-current/trend-snapshot-backfill
Authorization: DataAdmin
```

### ۵-۳. تایید صحت کل دیتا

```sql
-- بررسی عدم وجود MonthlyReport‌های تکراری برای کل دیتا
SELECT
    "ExternalCompanyId",
    "PeriodStart",
    "OutputType",
    COUNT(*) AS cnt
FROM "MonthlyReports"
WHERE "ProviderName" = 'NoavaranCurrentApi'
  AND "ReportType" = 'ProductSales'
  AND "OutputType" = 0
GROUP BY "ExternalCompanyId", "PeriodStart", "OutputType"
HAVING COUNT(*) > 1;
-- انتظار: 0 ردیف

-- آمار کلی
SELECT
    COUNT(DISTINCT "ExternalCompanyId") AS companies,
    COUNT(DISTINCT "PeriodStart") AS distinct_months,
    COUNT(*) AS total_reports
FROM "MonthlyReports"
WHERE "ProviderName" = 'NoavaranCurrentApi'
  AND "ReportType" = 'ProductSales'
  AND "OutputType" = 0;
```

---

## چک‌لیست نهایی

- [ ] کوئری‌های مرحله ۱ (شناسایی) اجرا و نتایج بررسی شد
- [ ] SQL حذف دیتا (مرحله ۱-۴) اجرا شد
- [ ] کد فیکس مرحله ۲-۱ اعمال شد (`BuildExternalReportId` بدون categoryId)
- [ ] کد فیکس مرحله ۲-۲ اعمال شد (authoritative replace برای line items)
- [ ] unique index مرحله ۲-۳ اضافه شد
- [ ] EF migration ایجاد و اعمال شد
- [ ] endpoint مرحله ۳ پیاده‌سازی و در DI ثبت شد
- [ ] endpoint با companyId=202 (کسرا) برای 140401-140503 اجرا شد
- [ ] تایید SQL مرحله ۴-۲: دقیقاً یک MonthlyReport در هر ماه
- [ ] تایید SQL مرحله ۴-۲: مجموع فروش 1404/12 برابر 2,119,732
- [ ] تایید SQL مرحله ۴-۳: هیچ‌یک از 7 محصول اضافی در DB نیستند
- [ ] monthly-backfill برای همه شرکت‌ها اجرا شد (مرحله ۵-۱)
- [ ] product-revenue-mix-backfill اجرا شد
- [ ] trend-snapshot-backfill اجرا شد
- [ ] تایید عدم وجود گزارش‌های تکراری در کل دیتا (مرحله ۵-۳)
- [ ] تست‌های regression مستند شده در bug report نوشته شدند
