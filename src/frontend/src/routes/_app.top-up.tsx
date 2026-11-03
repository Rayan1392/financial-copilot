import { createFileRoute } from "@tanstack/react-router";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useServerFn } from "@tanstack/react-start";
import { useEffect } from "react";
import { getTopUpStatus } from "@/lib/market-view.functions";

export const Route = createFileRoute("/_app/top-up")({
  validateSearch: (search: Record<string, unknown>) => ({
    invoiceId: typeof search.invoiceId === "string" ? search.invoiceId : "",
  }),
  component: TopUpStatusPage,
});

function TopUpStatusPage() {
  const { invoiceId } = Route.useSearch();
  const queryClient = useQueryClient();
  const getStatus = useServerFn(getTopUpStatus);
  const status = useQuery({
    queryKey: ["top-up", invoiceId],
    queryFn: () => getStatus({ data: { invoiceId } }),
    enabled: invoiceId.length > 0,
    retry: false,
    refetchInterval: (query) =>
      query.state.data?.status === "granted" || query.state.data?.status === "expired-unfulfilled" ||
      query.state.data?.status === "ineligible-unfulfilled" || query.state.data?.status === "ambiguous-unfulfilled" || query.state.fetchFailureCount >= 10 ||
      query.state.dataUpdateCount >= 10
        ? false
        : 2000,
  });

  useEffect(() => {
    if (status.data?.status === "granted") void queryClient.invalidateQueries({ queryKey: ["usage"] });
  }, [queryClient, status.data?.status]);

  const text = !invoiceId
    ? "شناسه خرید در پیوند بازگشت وجود ندارد."
    : status.isError
      ? "وضعیت خرید دریافت نشد. کمی بعد دوباره تلاش کنید."
      : status.data?.status === "granted"
        ? status.data.creditsGranted.toLocaleString("fa-IR") + " اعتبار به حساب شما اضافه شد."
        : status.data?.status === "expired-unfulfilled"
          ? "اعتبار این خرید پس از پایان بازه اشتراک قابل تخصیص نبود. وضعیت بازپرداخت را از TahlilApp پیگیری کنید."
          : status.data?.status === "ineligible-unfulfilled"
            ? "این خرید برای تخصیص اعتبار نیازمند بررسی پشتیبانی است."
            : status.data?.status === "ambiguous-unfulfilled"
              ? "این خرید برای تطبیق دستی به پشتیبانی ارجاع شد."
              : status.fetchFailureCount >= 10 || status.dataUpdateCount >= 10
              ? "پردازش هنوز تأیید نشده است. بعداً این صفحه را دوباره باز کنید."
              : "در انتظار تأیید دریافت اعتبار هستیم. این صفحه به‌روزرسانی می‌شود.";

  return (
    <main className="mx-auto w-full max-w-2xl p-6" dir="rtl">
      <section className="rounded-xl border bg-card p-6 shadow-sm">
        <h1 className="text-xl font-semibold">وضعیت خرید اعتبار</h1>
        <p className="mt-3 text-muted-foreground" role="status">{text}</p>
        {status.data?.status === "granted" && (
          <p className="mt-2 text-sm text-muted-foreground">
            اعتبار تا {new Date(status.data.expiresAtUtc!).toLocaleDateString("fa-IR")} قابل استفاده است.
          </p>
        )}
      </section>
    </main>
  );
}
