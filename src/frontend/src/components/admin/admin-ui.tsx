import { LoaderCircle } from "lucide-react";

// Shared admin panel building blocks, moved unchanged out of admin-page.tsx so the Feature 164 panel uses them too.

export function Panel({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="admin-panel min-w-0 overflow-hidden rounded-2xl border border-slate-200/90 bg-white shadow-sm shadow-slate-900/[0.03]">
      <div className="flex items-center justify-between gap-3 border-b border-slate-100 px-4 py-4 sm:px-6">
        <h2 className="text-sm font-bold text-slate-900 sm:text-base">{title}</h2>
        <span className="size-2 rounded-full bg-emerald-400" aria-hidden="true" />
      </div>
      <div className="min-w-0 p-4 sm:p-6">{children}</div>
    </section>
  );
}
export function Message({ error, success }: { error?: string | null; success?: string | null }) {
  return (
    <>
      {error && (
        <p
          role="alert"
          className="my-3 rounded-xl border border-rose-200 bg-rose-50 p-3 text-sm text-rose-800"
        >
          {error}
        </p>
      )}
      {success && (
        <p
          role="status"
          className="my-3 rounded-xl border border-emerald-200 bg-emerald-50 p-3 text-sm text-emerald-800"
        >
          {success}
        </p>
      )}
    </>
  );
}
export function Loading({ active }: { active: boolean }) {
  return active ? (
    <div className="flex items-center gap-2 py-3 text-sm text-slate-500" role="status">
      <LoaderCircle className="size-4 animate-spin" />
      در حال بارگذاری...
    </div>
  ) : null;
}
