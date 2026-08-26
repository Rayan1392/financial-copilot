import { useState } from "react";
import { Search } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { financialCopilotApi } from "@/integrations/financial-copilot/api-client";

export function GetCompanyIdPage() {
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

  const backfillSucceeded =
    backfillResult !== null &&
    backfillResult.errorCount === 0 &&
    !["failed", "error"].includes(backfillResult.status.toLowerCase());

  return (
    <div dir="rtl" className="min-h-screen bg-background px-6 py-8 text-foreground">
      <div className="mx-auto max-w-3xl">
        <div className="mb-6">
          <p className="mb-2 text-sm text-muted-foreground">ابزارهای مدیریتی</p>
          <h1 className="text-2xl font-bold">دریافت شناسه شرکت</h1>
          <p className="mt-2 text-sm text-muted-foreground">
            نماد بورسی را وارد کنید تا ExternalCompanyId مربوط به آن نمایش داده شود.
          </p>
        </div>

        <section className="rounded-xl border border-border bg-surface p-5">
          <form onSubmit={handleSubmit} className="flex flex-col gap-4 sm:flex-row sm:items-end">
            <div className="flex-1">
              <label htmlFor="company-symbol" className="mb-2 block text-sm font-medium">
                نماد
              </label>
              <Input
                id="company-symbol"
                value={symbol}
                onChange={(event) => setSymbol(event.target.value)}
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

        <section className="mt-6 rounded-xl border border-border bg-surface p-5">
          <div className="mb-4">
            <h2 className="font-semibold">اجرای بازپردازش فروش ماهانه یک شرکت</h2>
            <p className="mt-1 text-sm text-muted-foreground">
              شناسه شرکت و بازه ماه شمسی را وارد کنید تا سرویس بازپردازش اجرا شود.
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
                onChange={(event) => setBackfillSymbol(event.target.value)}
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
      </div>
    </div>
  );
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
