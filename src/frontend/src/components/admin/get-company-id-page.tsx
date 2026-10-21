import { useRef, useState } from "react";
import { Search } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { financialCopilotApi } from "@/integrations/financial-copilot/api-client";

export function GetCompanyIdPage() {
  const [activeTab, setActiveTab] = useState("company-id");
  const [symbol, setSymbol] = useState("");
  const [externalCompanyId, setExternalCompanyId] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [backfillSymbol, setBackfillSymbol] = useState("");
  const [shamsiYear, setShamsiYear] = useState("");
  const [shamsiMonth, setShamsiMonth] = useState("");
  const [backfillLoading, setBackfillLoading] = useState(false);
  const [backfillError, setBackfillError] = useState<string | null>(null);
  const [backfillResult, setBackfillResult] = useState<SingleCompanyMonthResponse | null>(null);
  const [financialSymbol, setFinancialSymbol] = useState("");
  const [financialYear, setFinancialYear] = useState("");
  const [financialPeriod, setFinancialPeriod] = useState("3");
  const [financialType, setFinancialType] = useState("BalanceSheet");
  const [financialLoading, setFinancialLoading] = useState(false);
  const [financialError, setFinancialError] = useState<string | null>(null);
  const [financialResult, setFinancialResult] = useState<ManualFinancialStatementResponse | null>(null);
  const [inventoryTicker, setInventoryTicker] = useState("");
  const [inventoryInput, setInventoryInput] = useState("");
  const [inventoryItems, setInventoryItems] = useState<FinancialStatementInventoryItem[] | null>(null);
  const [inventoryPage, setInventoryPage] = useState(1);
  const [inventoryTotalPages, setInventoryTotalPages] = useState(0);
  const [inventoryTotalCount, setInventoryTotalCount] = useState(0);
  const [inventoryLoading, setInventoryLoading] = useState(false);
  const [inventoryError, setInventoryError] = useState<string | null>(null);
  const [expandedStatementId, setExpandedStatementId] = useState<string | null>(null);
  const inventoryRequestId = useRef(0);

  function handleInventoryTickerChange(value: string) {
    inventoryRequestId.current += 1;
    setInventoryInput(value);
    setInventoryPage(1);
    setInventoryItems(null);
    setInventoryTicker("");
    setInventoryTotalCount(0);
    setInventoryTotalPages(0);
    setInventoryError(null);
    setInventoryLoading(false);
  }

  async function loadInventory(ticker: string, page = 1) {
    const requestId = ++inventoryRequestId.current;
    setInventoryLoading(true);
    setInventoryError(null);
    try {
      const result = await financialCopilotApi<FinancialStatementInventoryResponse>(
        `/api/v1/admin/noavaran-current/financial-statements/inventory?ticker=${encodeURIComponent(ticker)}&page=${page}`,
      );
      if (requestId !== inventoryRequestId.current) return;
      setInventoryTicker(result.ticker);
      setInventoryItems(result.items);
      setInventoryPage(result.page);
      setInventoryTotalPages(result.totalPages);
      setInventoryTotalCount(result.totalCount);
    } catch (requestError) {
      if (requestId !== inventoryRequestId.current) return;
      setInventoryError(requestError instanceof Error ? requestError.message : "\u062f\u0631\u06cc\u0627\u0641\u062a \u0641\u0647\u0631\u0633\u062a \u0635\u0648\u0631\u062a\u200c\u0647\u0627\u06cc \u0645\u0627\u0644\u06cc \u0646\u0627\u0645\u0648\u0641\u0642 \u0628\u0648\u062f.");
      setInventoryTicker(ticker);
      setInventoryItems(null);
    } finally {
      if (requestId === inventoryRequestId.current) setInventoryLoading(false);
    }
  }

  async function handleInventorySubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const ticker = inventoryInput.trim() || financialSymbol.trim() || backfillSymbol.trim() || symbol.trim();
    if (!ticker) {
      setInventoryError("\u0644\u0637\u0641\u0627\u064b \u0646\u0645\u0627\u062f \u0631\u0627 \u0648\u0627\u0631\u062f \u06a9\u0646\u06cc\u062f.");
      return;
    }
    await loadInventory(ticker);
  }

  async function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const normalizedSymbol = symbol.trim();

    if (!normalizedSymbol) {
      setError("لطفاً نماد را وارد کنید.");
      setExternalCompanyId(null);
      return;
    }

    setLoading(true);
    setError(null);
    setExternalCompanyId(null);

    try {
      const result = await financialCopilotApi<string>(
        `/api/v1/market/external-company-id?symbol=${encodeURIComponent(normalizedSymbol)}`,
      );
      setExternalCompanyId(result);
    } catch (requestError) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : "دریافت شناسه شرکت ناموفق بود.",
      );
    } finally {
      setLoading(false);
    }
  }

  async function handleBackfillSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const normalizedBackfillSymbol = backfillSymbol.trim();
    const parsedYear = Number(shamsiYear);
    const parsedMonth = Number(shamsiMonth);

    if (!normalizedBackfillSymbol) {
      setBackfillError("لطفاً نماد را وارد کنید.");
      setBackfillResult(null);
      return;
    }
    if (!Number.isInteger(parsedYear) || parsedYear < 1404 || parsedYear > 1500) {
      setBackfillError("سال شمسی باید بین ۱۴۰۴ و ۱۵۰۰ باشد.");
      setBackfillResult(null);
      return;
    }
    if (!Number.isInteger(parsedMonth) || parsedMonth < 1 || parsedMonth > 12) {
      setBackfillError("ماه شمسی باید بین ۱ و ۱۲ باشد.");
      setBackfillResult(null);
      return;
    }

    setBackfillLoading(true);
    setBackfillError(null);
    setBackfillResult(null);

    try {
      const result = await financialCopilotApi<SingleCompanyMonthResponse>(
        "/api/v1/admin/noavaran-current/monthly-backfill/single-company-month",
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            symbol: normalizedBackfillSymbol,
            shamsiYear: parsedYear,
            shamsiMonth: parsedMonth,
          }),
        },
      );
      setBackfillResult(result);
    } catch (requestError) {
      setBackfillError(
        requestError instanceof Error
          ? requestError.message
          : "اجرای بازپردازش ماهانه ناموفق بود.",
      );
    } finally {
      setBackfillLoading(false);
    }
  }

  async function handleFinancialStatementSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const normalizedSymbol = financialSymbol.trim();
    const parsedYear = Number(financialYear);
    const parsedPeriod = Number(financialPeriod);
    if (!normalizedSymbol) {
      setFinancialError("لطفاً نماد را وارد کنید.");
      setFinancialResult(null);
      return;
    }
    if (!Number.isInteger(parsedYear) || parsedYear < 1400 || parsedYear > 1500) {
      setFinancialError("سال مالی باید بین ۱۴۰۰ و ۱۵۰۰ باشد.");
      setFinancialResult(null);
      return;
    }
    if (![3, 6, 9, 12].includes(parsedPeriod)) {
      setFinancialError("دوره گزارش باید ۳، ۶، ۹ یا ۱۲ ماهه باشد.");
      setFinancialResult(null);
      return;
    }

    setFinancialLoading(true);
    setFinancialError(null);
    setFinancialResult(null);
    try {
      const result = await financialCopilotApi<ManualFinancialStatementResponse>(
        "/api/v1/admin/noavaran-current/financial-statement-backfill/single-company-period",
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            symbol: normalizedSymbol,
            fiscalYearEnding: parsedYear,
            periodMonths: parsedPeriod,
            statementType: financialType,
          }),
        },
      );
      setFinancialResult(result);
      if (["Persisted", "AlreadyExists"].includes(result.outcome) &&
          inventoryItems !== null && inventoryTicker === normalizedSymbol) {
        await loadInventory(normalizedSymbol);
      }
    } catch (requestError) {
      setFinancialError(
        requestError instanceof Error ? requestError.message : "دریافت و ذخیره صورت مالی ناموفق بود.",
      );
    } finally {
      setFinancialLoading(false);
    }
  }

  const backfillSucceeded =
    backfillResult !== null &&
    backfillResult.errorCount === 0 &&
    !["failed", "error"].includes(backfillResult.status.toLowerCase());

  return (
    <div dir="rtl" className="min-h-full w-full bg-background px-4 py-6 text-foreground sm:px-6 sm:py-8">
      <div className="mx-auto max-w-3xl">
        <div className="mb-6">
          <p className="mb-2 text-sm text-muted-foreground">ابزارهای مدیریتی</p>
          <h1 className="text-2xl font-bold">دریافت و ذخیره دستی داده‌های شرکت</h1>
          <p className="mt-2 text-sm text-muted-foreground">
            دریافت، بررسی و ذخیره اطلاعات شرکت‌ها در پایگاه داده
          </p>
        </div>

        <div role="tablist" aria-label="بخش‌های مدیریت داده شرکت" className="mb-5 grid grid-cols-2 gap-2 rounded-2xl border border-border bg-muted/40 p-2 sm:grid-cols-4">
          {[
            { id: "company-id", title: "شناسه شرکت", description: "جست‌وجوی نماد" },
            { id: "monthly-data", title: "تولید و فروش ماهانه", description: "دریافت اطلاعات ماهانه" },
            { id: "statements", title: "صورت‌های مالی دیتابیس", description: "مشاهده اطلاعات ذخیره‌شده" },
            { id: "noavaran", title: "دریافت صورت‌های مالی", description: "نوآوران امین" },
          ].map((tab, index) => {
            const selected = activeTab === tab.id;
            return (
              <button
                key={tab.id}
                id={`tab-${tab.id}`}
                type="button"
                role="tab"
                aria-selected={selected}
                aria-controls={`panel-${tab.id}`}
                tabIndex={selected ? 0 : -1}
                onClick={() => setActiveTab(tab.id)}
                className={`min-h-20 rounded-xl border px-3 py-3 text-right transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring ${selected ? "border-primary/30 bg-background text-foreground shadow-sm" : "border-transparent text-muted-foreground hover:border-border hover:bg-background/70 hover:text-foreground"}`}
              >
                <span className="flex items-center gap-2">
                  <span className={`flex size-7 shrink-0 items-center justify-center rounded-full text-xs font-semibold ${selected ? "bg-primary text-primary-foreground" : "bg-muted text-muted-foreground"}`}>{index + 1}</span>
                  <span className="min-w-0">
                    <span className="block text-sm font-semibold">{tab.title}</span>
                    <span className="mt-1 block truncate text-xs font-normal text-muted-foreground">{tab.description}</span>
                  </span>
                </span>
              </button>
            );
          })}
        </div>

        <section hidden={activeTab !== "company-id"} role="tabpanel" id="panel-company-id" aria-labelledby="tab-company-id" className="rounded-xl border border-border bg-surface p-5">
          <div className="mb-4">
            <h2 className="font-semibold">دریافت شناسه شرکت</h2>
            <p className="mt-1 text-sm text-muted-foreground">نماد بورسی را وارد کنید تا شناسه خارجی شرکت نمایش داده شود.</p>
          </div>
          <form onSubmit={handleSubmit} className="flex flex-col gap-4 sm:flex-row sm:items-end">
            <div className="flex-1">
              <label htmlFor="company-symbol" className="mb-2 block text-sm font-medium">
                نماد
              </label>
              <Input
                id="company-symbol"
                value={symbol}
                onChange={(event) => { setSymbol(event.target.value); handleInventoryTickerChange(event.target.value); }}
                placeholder="مثلاً کگل"
                autoComplete="off"
                disabled={loading}
              />
            </div>
            <Button type="submit" disabled={loading} className="sm:min-w-32">
              <Search className="ml-2 size-4" />
              {loading ? "در حال جستجو..." : "جستجو"}
            </Button>
          </form>

          {error && (
            <p className="mt-4 rounded-lg border border-destructive/30 bg-destructive/10 p-3 text-sm text-destructive">
              {error}
            </p>
          )}

          {externalCompanyId !== null && (
            <div className="mt-4 rounded-lg border border-emerald/30 bg-emerald-soft p-4">
              <p className="text-sm text-muted-foreground">ExternalCompanyId</p>
              <p className="mt-1 text-2xl font-bold tracking-wide text-emerald">
                {externalCompanyId}
              </p>
            </div>
          )}
        </section>

        <section hidden={activeTab !== "monthly-data"} role="tabpanel" id="panel-monthly-data" aria-labelledby="tab-monthly-data" className="rounded-xl border border-border bg-surface p-5">
          <div className="mb-4">
            <h2 className="font-semibold">دریافت تولید و فروش ماهانه یک شرکت</h2>
            <p className="mt-1 text-sm text-muted-foreground">
              نماد و بازه ماه شمسی را وارد کنید تا اطلاعات تولید و فروش آن ماه دریافت و ذخیره شود.
            </p>
          </div>

          <form onSubmit={handleBackfillSubmit} className="grid gap-4 sm:grid-cols-3">
            <div>
              <label htmlFor="backfill-symbol" className="mb-2 block text-sm font-medium">
                نماد
              </label>
              <Input
                id="backfill-symbol"
                value={backfillSymbol}
                onChange={(event) => { setBackfillSymbol(event.target.value); handleInventoryTickerChange(event.target.value); }}
                placeholder="مثلاً کگل"
                autoComplete="off"
                disabled={backfillLoading}
              />
            </div>
            <NumberField
              id="backfill-year"
              label="سال شمسی"
              value={shamsiYear}
              onChange={setShamsiYear}
              placeholder="مثلاً 1404"
              min={1404}
              max={1500}
              disabled={backfillLoading}
            />
            <NumberField
              id="backfill-month"
              label="ماه شمسی"
              value={shamsiMonth}
              onChange={setShamsiMonth}
              placeholder="مثلاً 3"
              min={1}
              max={12}
              disabled={backfillLoading}
            />
            <div className="sm:col-span-3">
              <Button type="submit" disabled={backfillLoading}>
                {backfillLoading ? "در حال اجرا..." : "اجرا"}
              </Button>
            </div>
          </form>

          {backfillError && (
            <p className="mt-4 rounded-lg border border-destructive/30 bg-destructive/10 p-3 text-sm text-destructive">
              اجرای سرویس ناموفق بود: {backfillError}
            </p>
          )}

          {backfillResult && (
            <div className="mt-4 rounded-lg border border-emerald/30 bg-emerald-soft p-4">
              <p className="font-medium text-emerald">سرویس با موفقیت اجرا شد.</p>
              <dl className="mt-3 grid gap-2 text-sm sm:grid-cols-2">
                <ResultItem label="وضعیت" value={backfillResult.status} />
                <ResultItem label="شناسه درخواست" value={backfillResult.requestId} />
                <ResultItem label="رکوردهای پردازش‌شده" value={backfillResult.processedRecords} />
                <ResultItem label="تعداد خطا" value={backfillResult.errorCount} />
                <ResultItem label="قبلاً پردازش شده" value={backfillResult.alreadyProcessed ? "بله" : "خیر"} />
                {backfillResult.errorMessage && (
                  <ResultItem label="پیام خطا" value={backfillResult.errorMessage} />
                )}
              </dl>
            </div>
          )}
        </section>

        <section hidden={activeTab !== "statements"} role="tabpanel" id="panel-statements" aria-labelledby="tab-statements" className="rounded-xl border border-border bg-surface p-5">
          <div className="mb-4">
            <h2 className="font-semibold">{"\u0635\u0648\u0631\u062a\u200c\u0647\u0627\u06cc \u0645\u0627\u0644\u06cc \u0645\u0648\u062c\u0648\u062f \u062f\u0631 \u062f\u06cc\u062a\u0627\u0628\u06cc\u0633"}</h2>
            <p className="mt-1 text-sm text-muted-foreground">{"\u0641\u0647\u0631\u0633\u062a \u0631\u06a9\u0648\u0631\u062f\u0647\u0627\u06cc \u0630\u062e\u06cc\u0631\u0647\u200c\u0634\u062f\u0647 \u062f\u0631 \u067e\u0627\u06cc\u06af\u0627\u0647 \u062f\u0627\u062f\u0647 \u0628\u0631\u0627\u06cc \u0646\u0645\u0627\u062f \u0627\u0646\u062a\u062e\u0627\u0628\u200c\u0634\u062f\u0647."}</p>
          </div>
          <form onSubmit={handleInventorySubmit} className="flex flex-col gap-4 sm:flex-row sm:items-end">
            <div className="flex-1">
              <label htmlFor="inventory-ticker" className="mb-2 block text-sm font-medium">{"\u0646\u0645\u0627\u062f"}</label>
              <Input
                id="inventory-ticker"
                value={inventoryInput}
                onChange={(event) => handleInventoryTickerChange(event.target.value)}
                placeholder="مثلاً کگل"
                autoComplete="off"
              />
            </div>
            <Button type="submit" disabled={inventoryLoading}>
              {inventoryLoading ? "\u062f\u0631 \u062d\u0627\u0644 \u0628\u0627\u0631\u06af\u06cc\u0631\u06cc..." : "\u0646\u0645\u0627\u06cc\u0634 \u0635\u0648\u0631\u062a\u200c\u0647\u0627\u06cc \u0645\u0627\u0644\u06cc \u0645\u0648\u062c\u0648\u062f"}
            </Button>
          </form>
          {inventoryError && <p role="alert" className="mt-4 rounded-lg border border-destructive/30 bg-destructive/10 p-3 text-sm text-destructive">{inventoryError}</p>}
          {inventoryTicker && inventoryItems !== null && (
            <div className="mt-4">
              <p className="mb-3 text-sm font-medium">{"\u0646\u0645\u0627\u062f"} {inventoryTicker} {"\u2014"} {"\u0645\u062c\u0645\u0648\u0639"}: {inventoryTotalCount} {"\u06af\u0632\u0627\u0631\u0634"}</p>
              {inventoryItems.length === 0 ? (
                <p className="rounded-lg border border-border bg-muted/30 p-4 text-sm">{"\u0647\u06cc\u0686 \u0635\u0648\u0631\u062a \u0645\u0627\u0644\u06cc \u0628\u0631\u0627\u06cc \u0627\u06cc\u0646 \u0646\u0645\u0627\u062f \u062f\u0631 \u062f\u06cc\u062a\u0627\u0628\u06cc\u0633 \u06cc\u0627\u0641\u062a \u0646\u0634\u062f."}</p>
              ) : (
                <div className="overflow-x-auto rounded-lg border border-border">
                  <table className="min-w-[720px] w-full text-right text-sm">
                    <thead className="bg-muted/50">
                      <tr>
                        <th className="w-12 p-3"><span className="sr-only">جزئیات</span></th>
                        <th className="p-3">{"\u0646\u0645\u0627\u062f"}</th>
                        <th className="p-3">{"\u0646\u0648\u0639 \u0635\u0648\u0631\u062a \u0645\u0627\u0644\u06cc"}</th>
                        <th className="p-3">{"\u0633\u0627\u0644 \u0645\u0627\u0644\u06cc \u0645\u0646\u062a\u0647\u06cc \u0628\u0647"}</th>
                        <th className="p-3">{"\u062f\u0648\u0631\u0647 \u0645\u0646\u062a\u0647\u06cc \u0628\u0647"}</th>
                        <th className="p-3">{"\u0646\u0648\u0639 \u062f\u0648\u0631\u0647"}</th>
                      </tr>
                    </thead>
                    {inventoryItems.map((item, index) => {
                      const rowId = `${item.reportId ?? item.versionId ?? item.statementType}-${item.periodEndJalali}-${index}`;
                      const expanded = expandedStatementId === rowId;
                      return (
                        <tbody key={rowId} className="border-t border-border">
                          <tr>
                            <td className="p-2">
                              <button
                                type="button"
                                aria-label={expanded ? "بستن جزئیات صورت مالی" : "نمایش جزئیات صورت مالی"}
                                aria-expanded={expanded}
                                aria-controls={`statement-details-${index}`}
                                onClick={() => setExpandedStatementId(expanded ? null : rowId)}
                                className="flex size-8 items-center justify-center rounded-full border border-border bg-background text-lg font-medium text-foreground transition-colors hover:bg-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                              >
                                <span aria-hidden="true">{expanded ? "−" : "+"}</span>
                              </button>
                            </td>
                            <td className="p-3">{inventoryTicker}</td>
                            <td className="p-3">{statementTypeLabel(item.statementType)}</td>
                            <td className="p-3">{item.fiscalYearEndJalali ?? "\u0646\u0627\u0645\u0634\u062e\u0635"}</td>
                            <td className="p-3">{item.periodEndJalali ?? "\u0646\u0627\u0645\u0634\u062e\u0635"}</td>
                            <td className="p-3">{item.periodMonths == null ? "\u0646\u0627\u0645\u0634\u062e\u0635" : `${item.periodMonths} \u0645\u0627\u0647\u0647`}</td>
                          </tr>
                          <tr id={`statement-details-${index}`} hidden={!expanded}>
                            <td colSpan={6} className="bg-muted/20 px-5 py-4">
                              <dl className="grid gap-4 text-sm sm:grid-cols-3">
                                <ResultItem label="حسابرسی شده" value={booleanLabel(item.isAudited)} />
                                <ResultItem label="تجدید ارائه شده" value={booleanLabel(item.isRestated)} />
                                <ResultItem label="تلفیقی" value={booleanLabel(item.isConsolidated)} />
                              </dl>
                            </td>
                          </tr>
                        </tbody>
                      );
                    })}
                  </table>
                </div>
              )}
              <div className="mt-4 flex items-center justify-between gap-3" dir="rtl">
                <Button type="button" variant="outline" disabled={inventoryLoading || inventoryPage <= 1}
                  onClick={() => loadInventory(inventoryTicker, inventoryPage - 1)}>
                  {"\u0642\u0628\u0644\u06cc"}
                </Button>
                <span className="text-sm" aria-live="polite">
                  {"\u0635\u0641\u062d\u0647"} {inventoryTotalPages === 0 ? 0 : inventoryPage} {"\u0627\u0632"} {inventoryTotalPages}
                </span>
                <Button type="button" variant="outline" disabled={inventoryLoading || inventoryPage >= inventoryTotalPages}
                  onClick={() => loadInventory(inventoryTicker, inventoryPage + 1)}>
                  {"\u0628\u0639\u062f\u06cc"}
                </Button>
              </div>
            </div>
          )}
        </section>

        <section hidden={activeTab !== "noavaran"} role="tabpanel" id="panel-noavaran" aria-labelledby="tab-noavaran" className="rounded-xl border border-border bg-surface p-5">
          <div className="mb-4">
            <h2 className="font-semibold">دریافت دستی صورت‌های مالی نوآوران امین</h2>
            <p className="mt-1 text-sm text-muted-foreground">
              صورت مالی شرکت را برای سال و دوره انتخاب‌شده دریافت و در پایگاه داده ذخیره کنید.
            </p>
          </div>
          <form onSubmit={handleFinancialStatementSubmit} className="grid gap-4 sm:grid-cols-2">
            <div>
              <label htmlFor="financial-statement-symbol" className="mb-2 block text-sm font-medium">نماد</label>
              <Input
                id="financial-statement-symbol"
                value={financialSymbol}
                onChange={(event) => { setFinancialSymbol(event.target.value); handleInventoryTickerChange(event.target.value); }}
                placeholder="مثلاً کگل"
                autoComplete="off"
                disabled={financialLoading}
              />
            </div>
            <NumberField
              id="financial-statement-year"
              label="سال مالی منتهی به"
              value={financialYear}
              onChange={setFinancialYear}
              placeholder="مثلاً 1404"
              min={1400}
              max={1500}
              disabled={financialLoading}
            />
            <div>
              <label htmlFor="financial-statement-period" className="mb-2 block text-sm font-medium">دوره گزارش</label>
              <select
                id="financial-statement-period"
                value={financialPeriod}
                onChange={(event) => setFinancialPeriod(event.target.value)}
                disabled={financialLoading}
                className="h-10 w-full rounded-md border border-input bg-background px-3 text-sm"
              >
                <option value="3">۳ ماهه</option>
                <option value="6">۶ ماهه</option>
                <option value="9">۹ ماهه</option>
                <option value="12">۱۲ ماهه</option>
              </select>
            </div>
            <div>
              <label htmlFor="financial-statement-type" className="mb-2 block text-sm font-medium">نوع صورت مالی</label>
              <select
                id="financial-statement-type"
                value={financialType}
                onChange={(event) => setFinancialType(event.target.value)}
                disabled={financialLoading}
                className="h-10 w-full rounded-md border border-input bg-background px-3 text-sm"
              >
                <option value="BalanceSheet">ترازنامه</option>
                <option value="IncomeStatement">سود و زیان</option>
                <option value="CashFlow">صورت جریان وجوه نقد</option>
                <option value="FinancialRatios">شاخص‌های مالی</option>
              </select>
            </div>
            <div className="sm:col-span-2">
              <Button type="submit" disabled={financialLoading}>
                {financialLoading ? "در حال دریافت و ذخیره..." : "دریافت و ذخیره اطلاعات"}
              </Button>
            </div>
          </form>
          {financialError && (
            <p role="alert" className="mt-4 rounded-lg border border-destructive/30 bg-destructive/10 p-3 text-sm text-destructive">
              {financialError}
            </p>
          )}
          {financialResult && (
            <div className={`mt-4 rounded-lg border p-4 ${financialResult.outcome === "Persisted" ? "border-emerald/30 bg-emerald-soft" : "border-border bg-muted/30"}`}>
              <p className={`font-medium ${financialResult.outcome === "Persisted" ? "text-emerald" : "text-foreground"}`}>
                {financialResult.outcome === "Persisted"
                  ? "داده دریافت و ذخیره شد."
                  : financialResult.outcome === "AlreadyExists"
                    ? "این گزارش قبلاً ذخیره شده بود؛ رکورد تکراری ایجاد نشد."
                  : financialResult.outcome === "Empty"
                    ? "برای این نماد و دوره داده‌ای برای ذخیره وجود ندارد."
                    : `وضعیت عملیات: ${financialResult.outcome}`}
              </p>
              <dl className="mt-3 grid gap-2 text-sm sm:grid-cols-2">
                <ResultItem label="وضعیت" value={financialResult.outcome} />
                <ResultItem label="شناسه اجرا" value={financialResult.runId ?? "—"} />
                <ResultItem label="رکوردهای ذخیره‌شده" value={financialResult.processedRecords} />
                <ResultItem label="اقلام ذخیره‌شده" value={financialResult.persistedItems} />
                {financialResult.message && <ResultItem label="جزئیات" value={financialResult.message} />}
              </dl>
            </div>
          )}
        </section>
      </div>
    </div>
  );
}

type ManualFinancialStatementResponse = {
  runId: string | null;
  outcome: string;
  processedRecords: number;
  persistedItems: number;
  message: string | null;
};

type FinancialStatementInventoryItem = {
  reportId: string | null;
  versionId: string | null;
  statementType: string;
  fiscalYearEndJalali: number | null;
  periodEndJalali: string | null;
  periodMonths: number | null;
  isAudited: boolean | null;
  isRestated: boolean | null;
  isConsolidated: boolean | null;
};

type FinancialStatementInventoryResponse = {
  ticker: string;
  items: FinancialStatementInventoryItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

function booleanLabel(value: boolean | null) {
  return value === null ? "\u0646\u0627\u0645\u0634\u062e\u0635" : value ? "\u0628\u0644\u0647" : "\u062e\u06cc\u0631";
}

function statementTypeLabel(value: string) {
  switch (value) {
    case "BalanceSheet": return "\u062a\u0631\u0627\u0632\u0646\u0627\u0645\u0647";
    case "IncomeStatement": return "\u0633\u0648\u062f \u0648 \u0632\u06cc\u0627\u0646";
    case "CashFlow": return "\u0635\u0648\u0631\u062a \u062c\u0631\u06cc\u0627\u0646 \u0648\u062c\u0647 \u0646\u0642\u062f";
    case "FinancialIndicators": return "\u0634\u0627\u062e\u0635\u200c\u0647\u0627\u06cc \u0645\u0627\u0644\u06cc";
    default: return value;
  }
}

type SingleCompanyMonthResponse = {
  requestId: string;
  companyId: number;
  shamsiYear: number;
  shamsiMonth: number;
  status: string;
  alreadyProcessed: boolean;
  processedRecords: number;
  errorCount: number;
  errorMessage: string | null;
  completedAt: string | null;
};

function NumberField({
  id,
  label,
  value,
  onChange,
  placeholder,
  min,
  max,
  disabled,
}: {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  placeholder: string;
  min: number;
  max?: number;
  disabled: boolean;
}) {
  return (
    <div>
      <label htmlFor={id} className="mb-2 block text-sm font-medium">
        {label}
      </label>
      <Input
        id={id}
        type="number"
        inputMode="numeric"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        placeholder={placeholder}
        min={min}
        max={max}
        step={1}
        disabled={disabled}
      />
    </div>
  );
}

function ResultItem({ label, value }: { label: string; value: string | number }) {
  return (
    <div>
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="font-medium">{value}</dd>
    </div>
  );
}
