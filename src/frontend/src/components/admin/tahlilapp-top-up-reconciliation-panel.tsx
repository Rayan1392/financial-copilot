import { useCallback, useEffect, useState } from "react";
import { LoaderCircle, RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { adminApi, type TahlilAppTopUpReconciliation } from "@/integrations/financial-copilot/admin-client";

export function TahlilAppTopUpReconciliationPanel() {
  const [report, setReport] = useState<TahlilAppTopUpReconciliation | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setReport(await adminApi.tahlilAppTopUpReconciliation());
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "Could not load top-up reconciliation.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  return (
    <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="text-base font-bold text-slate-900">TahlilApp credit top-up reconciliation</h2>
          <p className="mt-1 text-xs text-slate-500">
            Invoice receipts, committed grants, unfulfilled payments and invoice identity conflicts.
            {report && ` Generated ${new Date(report.generatedAtUtc).toLocaleString("en-GB", { timeZone: "UTC" })} UTC.`}
          </p>
        </div>
        <Button size="sm" variant="outline" className="rounded-lg" disabled={loading} onClick={() => void load()}>
          {loading ? <LoaderCircle className="mr-2 size-4 animate-spin" /> : <RefreshCw className="mr-2 size-4" />}
          Refresh
        </Button>
      </div>

      {error && <p role="alert" className="mb-3 text-sm text-rose-700">{error}</p>}

      <h3 className="mb-2 text-sm font-semibold text-slate-800">Purchase outcomes</h3>
      <div className="overflow-x-auto rounded-xl border border-slate-100">
        <Table className="admin-table">
          <TableHeader><TableRow>
            <TableHead>Invoice / legacy payment</TableHead><TableHead>Account</TableHead><TableHead>Credits</TableHead>
            <TableHead>Status</TableHead><TableHead>Original window</TableHead><TableHead>Received / granted</TableHead>
          </TableRow></TableHeader>
          <TableBody>
            {(report?.purchases ?? []).map((item) => (
              <TableRow key={item.invoiceId}>
                <TableCell><code dir="ltr" className="block text-left text-xs">{item.invoiceId}</code>{item.legacyPaymentId && <code dir="ltr" className="block text-left text-[10px] text-slate-500">Legacy payment: {item.legacyPaymentId}</code>}</TableCell>
                <TableCell><code dir="ltr" className="text-xs">{item.customerAccountId}</code></TableCell>
                <TableCell>{item.creditsPurchased.toLocaleString("en-US")}</TableCell>
                <TableCell className={item.status === "granted" ? "font-medium text-emerald-700" : "font-semibold text-amber-700"}>{item.status}</TableCell>
                <TableCell>{item.planSlug && item.planStartsAtUtc && item.planExpiresAtUtc ? <><code dir="ltr" className="block text-left text-xs">{item.planSlug}</code><span className="text-[10px] text-slate-500">{new Date(item.planStartsAtUtc).toLocaleDateString("en-GB")} – {new Date(item.planExpiresAtUtc).toLocaleDateString("en-GB")}</span></> : <span className="text-xs text-amber-700">Source unresolved</span>}</TableCell>
                <TableCell><span className="block text-xs">{new Date(item.receivedAtUtc).toLocaleString("en-GB", { timeZone: "UTC" })}</span><span className="text-[10px] text-slate-500">{item.grantedAtUtc ? new Date(item.grantedAtUtc).toLocaleString("en-GB", { timeZone: "UTC" }) : "Not granted"}</span></TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
        {!loading && !(report?.purchases.length) && <p className="py-5 text-center text-sm text-slate-500">No top-up purchases found.</p>}
      </div>

      <h3 className="mb-2 mt-5 text-sm font-semibold text-slate-800">Identity conflicts</h3>
      <div className="overflow-x-auto rounded-xl border border-slate-100">
        <Table className="admin-table">
          <TableHeader><TableRow><TableHead>Time</TableHead><TableHead>Stored receipt</TableHead><TableHead>Incoming invoice</TableHead><TableHead>Stored identity</TableHead><TableHead>Incoming identity</TableHead></TableRow></TableHeader>
          <TableBody>
            {(report?.identityConflicts ?? []).map((item, index) => (
              <TableRow key={`${item.receiptId}-${item.occurredAtUtc}-${index}`}>
                <TableCell>{new Date(item.occurredAtUtc).toLocaleString("en-GB", { timeZone: "UTC" })}</TableCell>
                <TableCell><code dir="ltr" className="text-xs">{item.receiptId}</code></TableCell>
                <TableCell><code dir="ltr" className="text-xs">{item.invoiceId}</code></TableCell>
                <TableCell><code dir="ltr" className="text-[10px]">{item.existingIdentity}</code></TableCell>
                <TableCell><code dir="ltr" className="text-[10px]">{item.incomingIdentity}</code></TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
        {!loading && !(report?.identityConflicts.length) && <p className="py-5 text-center text-sm text-slate-500">No identity conflicts recorded.</p>}
      </div>
    </section>
  );
}
