import { useEffect, useRef, useState } from "react";
import {
  Activity,
  Building2,
  ChevronLeft,
  ChevronRight,
  ClipboardList,
  CreditCard,
  Layers3,
  LoaderCircle,
  Plus,
  RefreshCw,
  Search,
  Shield,
  ShieldCheck,
  Users,
  X,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Switch } from "@/components/ui/switch";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  adminApi,
  type AdminAudit,
  type AdminCapability,
  type AdminCreditHistoryPage,
  type AdminCustomer360,
  type AdminPlan,
  type AdminPurchaseHistoryPage,
  type AdminRole,
  type AdminSubscription,
  type AdminTenant,
  type AdminTenantMember,
  type AdminUsageLedger,
  type AdminUser,
  type AdminUserDirectoryItem,
  type AdminUserDirectoryPage,
} from "@/integrations/financial-copilot/admin-client";
import {
  adminPermissions,
  hasPermission,
} from "@/integrations/financial-copilot/admin-permissions";
import type { AuthUser } from "@/integrations/financial-copilot/auth";

type AdminTab = "users" | "roles" | "tenants" | "billing" | "audits";
const splitCsv = (value: string) =>
  value
    .split(",")
    .map((item) => item.trim())
    .filter(Boolean);
const showError = (error: unknown) => {
  if (!(error instanceof Error)) return "درخواست ناموفق بود.";
  const details = error as Error & { type?: string; correlationId?: string };
  return [
    details.message,
    details.type,
    details.correlationId && `شناسه پیگیری: ${details.correlationId}`,
  ]
    .filter(Boolean)
    .join(" | ");
};

const permissionLabels: Record<string, { label: string; description: string }> = {
  "AiQuery.FinancialComparison": {
    label: "اجرای مقایسه مالی نمادها",
    description: "مقایسه شاخص‌ها و اطلاعات مالی چند نماد در کنار هم.",
  },
  "AiQuery.Scanner": {
    label: "غربال‌گری و فیلتر سهام",
    description: "یافتن نمادها با اجرای شرط‌ها و فیلترهای مالی.",
  },
  "AiQuery.StockAnalysis": {
    label: "دریافت تحلیل سهام",
    description: "پرس‌وجو درباره تحلیل جامع و وضعیت نمادها.",
  },
  "MarketPulse.Read": {
    label: "مشاهده نبض بازار",
    description: "خواندن خلاصه شاخص‌ها و وضعیت بازار.",
  },
  "Notifications.Telegram": {
    label: "استفاده از اعلان‌های تلگرام",
    description: "دسترسی به قابلیت دریافت و ارسال اعلان از مسیر تلگرام.",
  },
  "Radar.Symbols": {
    label: "رصد نمادها در رادار",
    description: "مشاهده قابلیت‌های رادار برای پیگیری نمادها.",
  },
  "Reports.Read": {
    label: "مشاهده گزارش‌ها",
    description: "خواندن گزارش‌های مالی و تحلیلی در دسترس.",
  },
  "Tracker.Rules": {
    label: "مدیریت قوانین رهگیری",
    description: "ساخت و مدیریت قوانین رهگیری و هشدارها.",
  },
  "Watchlist.Symbols": {
    label: "مدیریت فهرست نمادهای پیگیری",
    description: "مشاهده و مدیریت نمادهای ذخیره‌شده در فهرست پیگیری.",
  },
};

function permissionCopy(code: string) {
  return (
    permissionLabels[code] ?? {
      label: "مجوز تعریف‌شده",
      description: "شرح فارسی این مجوز هنوز ثبت نشده است.",
    }
  );
}

export function AdminPage({ user }: { user: AuthUser }) {
  const navigation = [
    hasPermission(user, adminPermissions.usersRead) && {
      value: "users" as const,
      label: "کاربران",
      description: "جست‌وجو و مدیریت حساب‌ها",
      icon: Users,
    },
    hasPermission(user, adminPermissions.rolesRead) && {
      value: "roles" as const,
      label: "نقش‌ها و مجوزها",
      description: "دسترسی‌های پنل",
      icon: ShieldCheck,
    },
    hasPermission(user, adminPermissions.tenantsRead) && {
      value: "tenants" as const,
      label: "مستاجران",
      description: "سازمان‌ها و اعضا",
      icon: Building2,
    },
    (hasPermission(user, adminPermissions.plansRead) ||
      hasPermission(user, adminPermissions.subscriptionsRead) ||
      hasPermission(user, adminPermissions.usageLedgerRead)) && {
      value: "billing" as const,
      label: "اشتراک و اعتبار",
      description: "طرح‌ها، اشتراک‌ها و دفترکل",
      icon: CreditCard,
    },
    (hasPermission(user, adminPermissions.securityAuditRead) ||
      hasPermission(user, adminPermissions.billingAuditRead)) && {
      value: "audits" as const,
      label: "رویدادهای ممیزی",
      description: "تغییرات ثبت‌شده در سامانه",
      icon: ClipboardList,
    },
  ].filter(Boolean) as {
    value: AdminTab;
    label: string;
    description: string;
    icon: typeof Users;
  }[];
  const [tab, setTab] = useState<AdminTab>(navigation[0]?.value ?? "users");
  const activeItem = navigation.find((item) => item.value === tab) ?? navigation[0];

  return (
    <div dir="rtl" className="scheme-light min-h-screen bg-slate-50 text-slate-900">
      <div className="min-h-screen lg:grid lg:grid-cols-[264px_minmax(0,1fr)]">
        <aside className="flex min-w-0 flex-col bg-[#111c2f] px-4 py-4 text-white lg:sticky lg:top-0 lg:h-screen lg:px-5 lg:py-6">
          <div className="flex items-center gap-3 px-2 pb-5 lg:pb-9">
            <div className="flex size-11 shrink-0 items-center justify-center rounded-2xl bg-emerald-400 text-[#10243a] shadow-lg shadow-emerald-950/30">
              <Activity className="size-5" />
            </div>
            <div className="min-w-0">
              <p className="truncate text-sm font-bold tracking-tight">Financial Copilot</p>
              <p className="mt-1 text-xs text-slate-400">کنسول مدیریت</p>
            </div>
          </div>

          <div className="mb-3 hidden px-3 text-[11px] font-semibold tracking-wider text-slate-500 lg:block">
            فضای مدیریت
          </div>
          <nav
            aria-label="منوی پنل مدیریت"
            className="flex gap-1 overflow-x-auto pb-1 lg:flex-col lg:overflow-visible"
          >
            {navigation.map(({ value, label, description, icon: Icon }) => {
              const isActive = tab === value;
              return (
                <button
                  key={value}
                  type="button"
                  aria-current={isActive ? "page" : undefined}
                  onClick={() => setTab(value)}
                  className={`group flex min-w-max items-center gap-3 rounded-xl px-3 py-2.5 text-right transition-colors lg:w-full lg:px-3.5 ${
                    isActive
                      ? "bg-emerald-400/15 text-emerald-200 ring-1 ring-inset ring-emerald-300/15"
                      : "text-slate-300 hover:bg-white/5 hover:text-white"
                  }`}
                >
                  <Icon
                    className={`size-[18px] shrink-0 ${isActive ? "text-emerald-300" : "text-slate-400 group-hover:text-slate-200"}`}
                  />
                  <span className="flex flex-col">
                    <span className="text-sm font-medium">{label}</span>
                    <span
                      className={`mt-0.5 hidden text-[11px] lg:block ${isActive ? "text-emerald-100/65" : "text-slate-500"}`}
                    >
                      {description}
                    </span>
                  </span>
                  {isActive && (
                    <span className="mr-auto hidden size-1.5 rounded-full bg-emerald-300 lg:block" />
                  )}
                </button>
              );
            })}
          </nav>

          <div className="mt-auto hidden rounded-2xl border border-white/10 bg-white/[0.04] p-3.5 lg:block">
            <div className="flex items-center gap-3">
              <div className="flex size-9 shrink-0 items-center justify-center rounded-full bg-slate-700 text-xs font-bold text-slate-100">
                {user.email.slice(0, 1).toLocaleUpperCase()}
              </div>
              <div className="min-w-0">
                <p className="text-xs font-semibold text-slate-100">مدیر پنل</p>
                <p className="mt-1 truncate text-[11px] text-slate-400" dir="ltr">
                  {user.email}
                </p>
              </div>
            </div>
          </div>
        </aside>

        <div className="min-w-0">
          <header className="border-b border-slate-200/80 bg-white/90 px-4 py-3 backdrop-blur sm:px-7 lg:px-10">
            <div className="mx-auto flex max-w-[1500px] items-center justify-between gap-4">
              <div>
                <p className="text-xs text-slate-500">مدیریت / {activeItem?.label}</p>
                <h1 className="mt-1 text-lg font-bold tracking-tight text-slate-900 sm:text-xl">
                  {activeItem?.label}
                </h1>
              </div>
              <div className="hidden items-center gap-2 rounded-full border border-emerald-100 bg-emerald-50 px-3 py-1.5 text-xs font-medium text-emerald-800 sm:flex">
                <span className="size-2 rounded-full bg-emerald-500" />
                نشست مدیریت فعال
              </div>
            </div>
          </header>
          <main className="mx-auto w-full max-w-[1500px] min-w-0 space-y-6 p-4 sm:p-7 lg:p-10">
            <div className="rounded-2xl border border-slate-200 bg-white px-5 py-4 shadow-sm shadow-slate-900/[0.02] sm:flex sm:items-center sm:justify-between sm:px-6">
              <div>
                <p className="text-sm font-semibold text-slate-800">
                  سلام، {user.email.split("@")[0]}
                </p>
                <p className="mt-1 text-xs text-slate-500">
                  از این بخش می‌توانید {activeItem?.description} را مدیریت کنید.
                </p>
              </div>
              <div className="mt-3 flex items-center gap-2 text-xs text-slate-500 sm:mt-0">
                <Shield className="size-4 text-emerald-600" />
                <span>دسترسی‌ها بر اساس مجوز حساب شما نمایش داده می‌شوند.</span>
              </div>
            </div>

            {tab === "users" && <UsersPanel user={user} />}
            {tab === "roles" && <RolesPanel user={user} />}
            {tab === "tenants" && <TenantsPanel user={user} />}
            {tab === "billing" && <BillingPanel user={user} />}
            {tab === "audits" && <AuditsPanel user={user} />}
          </main>
        </div>
      </div>
    </div>
  );
}

function Panel({ title, children }: { title: string; children: React.ReactNode }) {
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
function Message({ error, success }: { error?: string | null; success?: string | null }) {
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
function Loading({ active }: { active: boolean }) {
  return active ? (
    <div className="flex items-center gap-2 py-3 text-sm text-slate-500" role="status">
      <LoaderCircle className="size-4 animate-spin" />
      در حال بارگذاری...
    </div>
  ) : null;
}

type AdminDialogChoice = { value: string; label: string; description?: string };
type AdminDialogField = {
  name: string;
  label: string;
  type?: "text" | "number" | "textarea" | "select" | "multiselect";
  placeholder?: string;
  initialValue?: string;
  required?: boolean;
  min?: string;
  dir?: "ltr" | "rtl";
  choices?: AdminDialogChoice[];
};
type AdminDialogConfig = {
  title: string;
  description?: string;
  confirmLabel?: string;
  danger?: boolean;
  fields: AdminDialogField[];
};
type AdminDialogRequest = {
  config: AdminDialogConfig;
  values: Record<string, string>;
  validationError?: string;
};

function useAdminDialog() {
  const [request, setRequest] = useState<AdminDialogRequest | null>(null);
  const resolver = useRef<((value: Record<string, string> | null) => void) | null>(null);

  function ask(config: AdminDialogConfig) {
    return new Promise<Record<string, string> | null>((resolve) => {
      resolver.current?.(null);
      resolver.current = resolve;
      setRequest({
        config,
        values: Object.fromEntries(
          config.fields.map((field) => [field.name, field.initialValue ?? ""]),
        ),
      });
    });
  }

  function finish(value: Record<string, string> | null) {
    resolver.current?.(value);
    resolver.current = null;
    setRequest(null);
  }

  function update(name: string, value: string) {
    setRequest((current) =>
      current
        ? { ...current, values: { ...current.values, [name]: value }, validationError: undefined }
        : current,
    );
  }

  function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!request) return;
    const missing = request.config.fields.some(
      (field) => field.required && !request.values[field.name]?.trim(),
    );
    if (missing) {
      setRequest({ ...request, validationError: "لطفاً همه فیلدهای ضروری را تکمیل کنید." });
      return;
    }
    finish(request.values);
  }

  const dialog = (
    <Dialog open={Boolean(request)} onOpenChange={(open) => !open && finish(null)}>
      <DialogContent
        dir="rtl"
        className="scheme-light max-h-[90vh] overflow-y-auto rounded-2xl border-slate-200 p-0 text-right shadow-2xl sm:max-w-xl"
      >
        {request && (
          <form onSubmit={submit}>
            <DialogHeader className="border-b border-slate-100 px-6 py-5 text-right">
              <DialogTitle className="text-base font-bold text-slate-900">
                {request.config.title}
              </DialogTitle>
              {request.config.description && (
                <DialogDescription className="pt-1 leading-6 text-slate-500">
                  {request.config.description}
                </DialogDescription>
              )}
            </DialogHeader>
            <div className="space-y-4 px-6 py-5">
              {request.config.fields.map((field) => {
                const value = request.values[field.name] ?? "";
                const controlClass =
                  "mt-1.5 w-full rounded-xl border-slate-200 bg-white focus-visible:ring-emerald-500";
                return (
                  <div key={field.name} className="space-y-1">
                    <label
                      className="text-sm font-medium text-slate-700"
                      htmlFor={`admin-dialog-${field.name}`}
                    >
                      {field.label}
                      {field.required && <span className="mr-1 text-rose-500">*</span>}
                    </label>
                    {field.type === "textarea" ? (
                      <Textarea
                        id={`admin-dialog-${field.name}`}
                        className={`${controlClass} min-h-24 resize-y`}
                        placeholder={field.placeholder}
                        value={value}
                        onChange={(event) => update(field.name, event.target.value)}
                        required={field.required}
                      />
                    ) : field.type === "select" ? (
                      <select
                        id={`admin-dialog-${field.name}`}
                        className={`${controlClass} h-10 px-3 text-sm`}
                        value={value}
                        onChange={(event) => update(field.name, event.target.value)}
                      >
                        {field.choices?.map((choice) => (
                          <option key={choice.value} value={choice.value}>
                            {choice.label}
                          </option>
                        ))}
                      </select>
                    ) : field.type === "multiselect" ? (
                      <div className="grid gap-2 rounded-xl border border-slate-200 bg-slate-50/70 p-3 sm:grid-cols-2">
                        {field.choices?.map((choice) => {
                          const checked = value.split(",").filter(Boolean).includes(choice.value);
                          return (
                            <label
                              key={choice.value}
                              className="flex cursor-pointer items-start gap-2 rounded-lg border border-transparent bg-white p-2.5 text-sm shadow-sm hover:border-emerald-200"
                            >
                              <input
                                type="checkbox"
                                className="mt-0.5 size-4 accent-emerald-600"
                                checked={checked}
                                onChange={(event) => {
                                  const current = value.split(",").filter(Boolean);
                                  const next = event.target.checked
                                    ? [...current, choice.value]
                                    : current.filter((item) => item !== choice.value);
                                  update(field.name, next.join(","));
                                }}
                              />
                              <span className="min-w-0">
                                <span className="block font-medium text-slate-700">
                                  {choice.label}
                                </span>
                                {choice.description && (
                                  <span className="mt-0.5 block text-xs leading-5 text-slate-500">
                                    {choice.description}
                                  </span>
                                )}
                              </span>
                            </label>
                          );
                        })}
                      </div>
                    ) : (
                      <Input
                        id={`admin-dialog-${field.name}`}
                        type={field.type === "number" ? "number" : "text"}
                        min={field.type === "number" ? field.min : undefined}
                        dir={field.dir ?? (field.type === "number" ? "ltr" : "rtl")}
                        className={controlClass}
                        placeholder={field.placeholder}
                        value={value}
                        onChange={(event) => update(field.name, event.target.value)}
                        required={field.required}
                      />
                    )}
                  </div>
                );
              })}
              {request.validationError && (
                <p role="alert" className="text-sm text-rose-700">
                  {request.validationError}
                </p>
              )}
            </div>
            <DialogFooter className="gap-2 border-t border-slate-100 bg-slate-50/70 px-6 py-4 sm:space-x-0">
              <Button
                type="button"
                variant="outline"
                className="rounded-xl"
                onClick={() => finish(null)}
              >
                انصراف
              </Button>
              <Button
                type="submit"
                variant={request.config.danger ? "destructive" : "default"}
                className="rounded-xl"
              >
                {request.config.confirmLabel ?? "ثبت تغییر"}
              </Button>
            </DialogFooter>
          </form>
        )}
      </DialogContent>
    </Dialog>
  );

  return { ask, dialog };
}

function useAdminPagination<T>(items: T[], pageSize = 10) {
  const [pageIndex, setPageIndex] = useState(0);
  const pageCount = Math.max(1, Math.ceil(items.length / pageSize));
  useEffect(() => {
    setPageIndex((current) => Math.min(current, pageCount - 1));
  }, [pageCount]);
  return {
    pageIndex,
    setPageIndex,
    pageCount,
    total: items.length,
    pageItems: items.slice(pageIndex * pageSize, (pageIndex + 1) * pageSize),
    firstItem: items.length === 0 ? 0 : pageIndex * pageSize + 1,
    lastItem: Math.min((pageIndex + 1) * pageSize, items.length),
  };
}

function AdminPagination({
  pageIndex,
  pageCount,
  firstItem,
  lastItem,
  total,
  onPageChange,
}: {
  pageIndex: number;
  pageCount: number;
  firstItem: number;
  lastItem: number;
  total: number;
  onPageChange: (page: number) => void;
}) {
  return (
    <div className="mt-4 flex flex-wrap items-center justify-between gap-3 border-t border-slate-100 pt-4 text-xs text-slate-500">
      <span>
        نمایش {firstItem} تا {lastItem} از {total} مورد · ۱۰ مورد در هر صفحه
      </span>
      <div className="flex items-center gap-2">
        <Button
          size="sm"
          variant="outline"
          className="h-8 rounded-lg px-2.5"
          disabled={pageIndex === 0}
          onClick={() => onPageChange(pageIndex - 1)}
        >
          <ChevronRight className="ml-1 size-4" /> قبلی
        </Button>
        <span className="min-w-16 text-center font-medium text-slate-600">
          صفحه {pageIndex + 1} از {pageCount}
        </span>
        <Button
          size="sm"
          variant="outline"
          className="h-8 rounded-lg px-2.5"
          disabled={pageIndex + 1 >= pageCount}
          onClick={() => onPageChange(pageIndex + 1)}
        >
          بعدی <ChevronLeft className="mr-1 size-4" />
        </Button>
      </div>
    </div>
  );
}

function UsersPanel({ user }: { user: AuthUser }) {
  const { ask, dialog: actionDialog } = useAdminDialog();
  const [search, setSearch] = useState("");
  const [externalId, setExternalId] = useState("");
  const [statusFilter, setStatusFilter] = useState("any");
  const [sourceFilter, setSourceFilter] = useState("");
  const [providerFilter, setProviderFilter] = useState("");
  const [registeredFrom, setRegisteredFrom] = useState("");
  const [registeredTo, setRegisteredTo] = useState("");
  const [lastLoginFrom, setLastLoginFrom] = useState("");
  const [lastLoginTo, setLastLoginTo] = useState("");
  const [directory, setDirectory] = useState<AdminUserDirectoryPage | null>(null);
  const [pageCursors, setPageCursors] = useState<(string | null)[]>([null]);
  const [pageIndex, setPageIndex] = useState(0);
  const [selectedUsers, setSelectedUsers] = useState<Map<string, AdminUserDirectoryItem>>(
    () => new Map(),
  );
  const [bulkActionLoading, setBulkActionLoading] = useState(false);
  const [roles, setRoles] = useState<AdminRole[]>([]);
  const [selected, setSelected] = useState<AdminUser | null>(null);
  const [selectedDirectoryItem, setSelectedDirectoryItem] = useState<AdminUserDirectoryItem | null>(
    null,
  );
  const [customer, setCustomer] = useState<AdminCustomer360 | null>(null);
  const [creditHistory, setCreditHistory] = useState<AdminCreditHistoryPage | null>(null);
  const [purchaseHistory, setPurchaseHistory] = useState<AdminPurchaseHistoryPage | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const creditHistoryRows = creditHistory?.items ?? [];
  const purchaseHistoryRows =
    purchaseHistory?.items ?? customer?.purchases.data?.recentLocalPurchases ?? [];
  const creditHistoryPagination = useAdminPagination(creditHistoryRows);
  const purchaseHistoryPagination = useAdminPagination(purchaseHistoryRows);
  const creditPeriodsPagination = useAdminPagination(customer?.credits.data?.activePeriods ?? []);
  const providerLinksPagination = useAdminPagination(customer?.linkedProviders ?? []);
  const canManage = hasPermission(user, adminPermissions.usersManage);
  const canManageRoles = hasPermission(user, adminPermissions.rolesManage);
  const canReadCredits = hasPermission(user, adminPermissions.usageLedgerRead);
  const canReadPurchases = hasPermission(user, adminPermissions.billingAuditRead);
  const currentCursor = pageCursors[pageIndex] ?? null;
  const currentPageActiveUsers =
    directory?.items.filter((item) => item.status !== "disabled") ?? [];
  const allCurrentPageSelected =
    currentPageActiveUsers.length > 0 &&
    currentPageActiveUsers.every((item) => selectedUsers.has(item.userId));
  const dateFilter = (value: string) => (value ? new Date(value).toISOString() : undefined);

  async function load(cursor: string | null = currentCursor) {
    setLoading(true);
    setError(null);
    try {
      const nextDirectory = await adminApi.userDirectory({
        pageSize: 10,
        cursor,
        search: search.trim() || undefined,
        status: statusFilter as "any" | "enabled" | "disabled" | "locked",
        acquisitionSource: (sourceFilter || undefined) as
          | "native"
          | "tahlilapp"
          | "unknown"
          | undefined,
        linkedProvider: (providerFilter || undefined) as "tahlilapp" | "telegram" | undefined,
        externalProvider: externalId.trim() ? "tahlilapp" : undefined,
        externalUserId: externalId.trim() || undefined,
        registeredFromUtc: dateFilter(registeredFrom),
        registeredToUtc: dateFilter(registeredTo),
        lastLoginFromUtc: dateFilter(lastLoginFrom),
        lastLoginToUtc: dateFilter(lastLoginTo),
      });
      setDirectory(nextDirectory);
      setSelectedUsers((previous) => {
        const refreshed = new Map(previous);
        for (const item of nextDirectory.items) {
          if (item.status === "disabled") refreshed.delete(item.userId);
          else if (refreshed.has(item.userId)) refreshed.set(item.userId, item);
        }
        return refreshed;
      });
      if (hasPermission(user, adminPermissions.rolesRead)) setRoles(await adminApi.roles());
    } catch (e) {
      setError(showError(e));
    } finally {
      setLoading(false);
    }
  }
  useEffect(() => {
    void load(null);
  }, []); // eslint-disable-line react-hooks/exhaustive-deps -- load the initial admin snapshot once

  function applyFilters() {
    setPageCursors([null]);
    setPageIndex(0);
    setSelectedUsers(new Map());
    void load(null);
  }

  function toggleCurrentPageSelection(checked: boolean) {
    setSelectedUsers((previous) => {
      const next = new Map(previous);
      for (const item of currentPageActiveUsers) {
        if (checked) next.set(item.userId, item);
        else next.delete(item.userId);
      }
      return next;
    });
  }

  async function deactivateSelectedUsers() {
    const targets = [...selectedUsers.values()].filter((item) => item.status !== "disabled");
    if (!targets.length) return;

    const form = await ask({
      title: "غیرفعال‌سازی کاربران انتخاب‌شده",
      description: `${targets.length.toLocaleString("fa-IR")} حساب غیرفعال می‌شود. دلیل این اقدام برای هر کاربر در ممیزی ثبت خواهد شد.`,
      confirmLabel: `غیرفعال‌سازی ${targets.length.toLocaleString("fa-IR")} کاربر`,
      danger: true,
      fields: [{ name: "reason", label: "دلیل غیرفعال‌سازی", type: "textarea", required: true }],
    });
    const reason = form?.reason.trim();
    if (!reason) return;

    setBulkActionLoading(true);
    setError(null);
    setSuccess(null);
    try {
      const failed: { item: AdminUserDirectoryItem; reason: unknown }[] = [];
      let succeeded = 0;
      for (const item of targets) {
        try {
          await adminApi.setUserStatus(item.userId, false, false, reason);
          succeeded += 1;
        } catch (reason) {
          failed.push({ item, reason });
        }
      }
      setSelectedUsers(new Map(failed.map(({ item }) => [item.userId, item])));
      await load();
      if (succeeded > 0) {
        setSuccess(`${succeeded.toLocaleString("fa-IR")} کاربر غیرفعال شد.`);
      }
      if (failed.length > 0) {
        setError(
          `${failed.length.toLocaleString("fa-IR")} کاربر غیرفعال نشد. ${showError(failed[0].reason)}`,
        );
      }
    } catch (e) {
      setError(showError(e));
    } finally {
      setBulkActionLoading(false);
    }
  }

  async function selectUser(item: AdminUserDirectoryItem) {
    setSelectedDirectoryItem(item);
    setSelected(null);
    setCustomer(null);
    setCreditHistory(null);
    setPurchaseHistory(null);
    setDetailLoading(true);
    setError(null);
    try {
      const [legacyUser, customer360] = await Promise.all([
        adminApi.user(item.userId),
        adminApi.customer360(item.userId),
      ]);
      setSelected(legacyUser);
      setCustomer(customer360);
    } catch (e) {
      setError(showError(e));
    } finally {
      setDetailLoading(false);
    }
  }

  async function status(isEnabled: boolean, unlock = false) {
    if (!selected) return;
    const form = await ask({
      title: unlock ? "رفع قفل کاربر" : isEnabled ? "فعال‌سازی کاربر" : "غیرفعال‌سازی کاربر",
      description: `${selected.email} · این تغییر در رویدادهای ممیزی ثبت می‌شود.`,
      confirmLabel: unlock ? "رفع قفل" : isEnabled ? "فعال‌سازی" : "غیرفعال‌سازی",
      danger: !isEnabled && !unlock,
      fields: [{ name: "reason", label: "دلیل تغییر", type: "textarea", required: true }],
    });
    const reason = form?.reason.trim();
    if (!reason) return;
    try {
      const next = await adminApi.setUserStatus(selected.userId, isEnabled, unlock, reason);
      setSelected(next);
      if (!next.isEnabled) {
        setSelectedUsers((previous) => {
          const remaining = new Map(previous);
          remaining.delete(next.userId);
          return remaining;
        });
      }
      await load();
      setCustomer(await adminApi.customer360(selected.userId));
      setSuccess("وضعیت کاربر به‌روزرسانی شد.");
    } catch (e) {
      setError(showError(e));
    }
  }
  async function revoke() {
    if (!selected) return;
    const form = await ask({
      title: "ابطال نشست‌های فعال",
      description: `همه نشست‌های فعال ${selected.email} باطل می‌شوند و این اقدام در ممیزی ثبت خواهد شد.`,
      confirmLabel: "ابطال نشست‌ها",
      danger: true,
      fields: [{ name: "reason", label: "دلیل ابطال", type: "textarea", required: true }],
    });
    const reason = form?.reason.trim();
    if (!reason) return;
    try {
      const count = await adminApi.revokeSessions(selected.userId, reason);
      setSuccess(`${count} نشست باطل شد.`);
      setCustomer(await adminApi.customer360(selected.userId));
    } catch (e) {
      setError(showError(e));
    }
  }
  async function saveRoles() {
    if (!selected) return;
    const selectedRoleIds = roles
      .filter((role) => selected.roles.includes(role.name))
      .map((role) => role.roleId);
    const form = await ask({
      title: "تنظیم نقش‌های کاربر",
      description: `نقش‌های فعال برای ${selected.email} را انتخاب کنید.`,
      confirmLabel: "ذخیره نقش‌ها",
      fields: [
        {
          name: "roleIds",
          label: "نقش‌های فعال",
          type: "multiselect",
          initialValue: selectedRoleIds.join(","),
          choices: roles.map((role) => ({ value: role.roleId, label: role.name })),
        },
        { name: "reason", label: "دلیل تغییر", type: "textarea", required: true },
      ],
    });
    const reason = form?.reason.trim();
    if (!reason) return;
    try {
      const chosen = (form?.roleIds ?? "").split(",").filter(Boolean);
      const next = await adminApi.setUserRoles(selected.userId, chosen, reason);
      setSelected(next);
      setSuccess("نقش‌های کاربر ثبت شد.");
    } catch (e) {
      setError(showError(e));
    }
  }
  async function loadCreditHistory() {
    if (!selected || !canReadCredits) return;
    try {
      const page = await adminApi.customerCreditHistory(
        selected.userId,
        10,
        creditHistory?.nextCursor,
      );
      setCreditHistory((previous) =>
        previous ? { ...page, items: [...previous.items, ...page.items] } : page,
      );
    } catch (e) {
      setError(showError(e));
    }
  }
  async function loadPurchaseHistory() {
    if (!selected || !canReadPurchases) return;
    try {
      const page = await adminApi.customerPurchaseHistory(
        selected.userId,
        10,
        purchaseHistory?.nextCursor,
      );
      setPurchaseHistory((previous) =>
        previous ? { ...page, items: [...previous.items, ...page.items] } : page,
      );
    } catch (e) {
      setError(showError(e));
    }
  }

  return (
    <>
      <div className="space-y-4">
        <Message error={error} success={success} />
        <Panel title="مدیریت یکپارچه کاربران">
          <form
            className="grid gap-3 md:grid-cols-2 xl:grid-cols-3"
            onSubmit={(e) => {
              e.preventDefault();
              applyFilters();
            }}
          >
            <Input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="ایمیل یا شناسه کاربر"
              className="rounded-xl border-slate-200 bg-white"
            />
            <Input
              value={externalId}
              onChange={(e) => setExternalId(e.target.value)}
              placeholder="شناسه خارجی TahlilApp"
              className="rounded-xl border-slate-200 bg-white"
            />
            <select
              className="h-10 rounded-xl border border-slate-200 bg-white px-3 text-sm"
              value={statusFilter}
              onChange={(e) => setStatusFilter(e.target.value)}
            >
              <option value="any">همه وضعیت‌ها</option>
              <option value="enabled">فعال</option>
              <option value="disabled">غیرفعال</option>
              <option value="locked">قفل‌شده</option>
            </select>
            <select
              className="h-10 rounded-xl border border-slate-200 bg-white px-3 text-sm"
              value={sourceFilter}
              onChange={(e) => setSourceFilter(e.target.value)}
            >
              <option value="">همه منابع جذب</option>
              <option value="native">Native</option>
              <option value="tahlilapp">TahlilApp</option>
              <option value="unknown">نامشخص</option>
            </select>
            <select
              className="h-10 rounded-xl border border-slate-200 bg-white px-3 text-sm"
              value={providerFilter}
              onChange={(e) => setProviderFilter(e.target.value)}
            >
              <option value="">همه اتصال‌ها</option>
              <option value="tahlilapp">TahlilApp</option>
              <option value="telegram">Telegram</option>
            </select>
            <Button type="submit" className="rounded-xl">
              <RefreshCw className="ml-2 size-4" /> اعمال فیلترها
            </Button>
            <label className="space-y-1 text-xs text-muted-foreground">
              ثبت‌نام از
              <Input
                type="datetime-local"
                value={registeredFrom}
                onChange={(e) => setRegisteredFrom(e.target.value)}
                className="rounded-xl border-slate-200 bg-white"
              />
            </label>
            <label className="space-y-1 text-xs text-muted-foreground">
              ثبت‌نام تا
              <Input
                type="datetime-local"
                value={registeredTo}
                onChange={(e) => setRegisteredTo(e.target.value)}
                className="rounded-xl border-slate-200 bg-white"
              />
            </label>
            <label className="space-y-1 text-xs text-muted-foreground">
              آخرین ورود از
              <Input
                type="datetime-local"
                value={lastLoginFrom}
                onChange={(e) => setLastLoginFrom(e.target.value)}
                className="rounded-xl border-slate-200 bg-white"
              />
            </label>
            <label className="space-y-1 text-xs text-muted-foreground">
              آخرین ورود تا
              <Input
                type="datetime-local"
                value={lastLoginTo}
                onChange={(e) => setLastLoginTo(e.target.value)}
                className="rounded-xl border-slate-200 bg-white"
              />
            </label>
          </form>
          <Loading active={loading} />
          {canManage && (
            <div className="mt-4 flex flex-wrap items-center justify-between gap-3 rounded-xl border border-slate-200 bg-slate-50 px-3 py-3 sm:px-4">
              <p className="text-sm text-slate-600" aria-live="polite">
                {selectedUsers.size
                  ? `${selectedUsers.size.toLocaleString("fa-IR")} کاربر انتخاب شده`
                  : "از یک یا چند صفحه کاربران را برای غیرفعال‌سازی گروهی انتخاب کنید."}
              </p>
              <div className="flex flex-wrap items-center gap-2">
                {selectedUsers.size > 0 && (
                  <Button
                    size="sm"
                    variant="ghost"
                    className="rounded-lg"
                    onClick={() => setSelectedUsers(new Map())}
                    disabled={bulkActionLoading}
                  >
                    پاک‌کردن انتخاب
                  </Button>
                )}
                <Button
                  size="sm"
                  variant="destructive"
                  className="rounded-lg"
                  onClick={() => void deactivateSelectedUsers()}
                  disabled={selectedUsers.size === 0 || bulkActionLoading || loading}
                >
                  {bulkActionLoading && <LoaderCircle className="ml-2 size-4 animate-spin" />}
                  غیرفعال‌سازی {selectedUsers.size.toLocaleString("fa-IR")} کاربر
                </Button>
              </div>
            </div>
          )}
          <div className="mt-4 overflow-x-auto rounded-xl border border-slate-100">
            <Table className="admin-table">
              <TableHeader>
                <TableRow>
                  {canManage && (
                    <TableHead className="w-12">
                      <input
                        type="checkbox"
                        aria-label="انتخاب همه کاربران فعال این صفحه"
                        checked={allCurrentPageSelected}
                        disabled={currentPageActiveUsers.length === 0 || loading}
                        onChange={(event) => toggleCurrentPageSelection(event.target.checked)}
                        className="size-4 cursor-pointer accent-emerald-600"
                      />
                    </TableHead>
                  )}
                  <TableHead>ایمیل کاربر</TableHead>
                  <TableHead>وضعیت</TableHead>
                  <TableHead>منبع جذب</TableHead>
                  <TableHead>اتصال‌ها</TableHead>
                  <TableHead>ثبت‌نام</TableHead>
                  <TableHead>آخرین ورود موفق</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {directory?.items.map((item) => (
                  <TableRow key={item.userId} className="hover:bg-slate-50/80">
                    {canManage && (
                      <TableCell>
                        <input
                          type="checkbox"
                          aria-label={`انتخاب ${item.email ?? item.displayName ?? "کاربر"}`}
                          checked={selectedUsers.has(item.userId)}
                          disabled={item.status === "disabled" || loading}
                          onChange={(event) =>
                            setSelectedUsers((previous) => {
                              const next = new Map(previous);
                              if (event.target.checked) next.set(item.userId, item);
                              else next.delete(item.userId);
                              return next;
                            })
                          }
                          className="size-4 cursor-pointer accent-emerald-600 disabled:cursor-not-allowed"
                        />
                      </TableCell>
                    )}
                    <TableCell>
                      <button
                        type="button"
                        onClick={() => void selectUser(item)}
                        className="group block max-w-full text-right focus-visible:rounded-md focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald-500"
                        aria-label={`نمایش جزئیات ${item.email ?? "کاربر"}`}
                      >
                        <span className="font-semibold text-slate-800 group-hover:text-emerald-700">
                          {item.displayName ?? "بدون نام"}
                        </span>
                        <span dir="ltr" className="mt-0.5 block text-left text-xs text-slate-500">
                          {item.email ?? "بدون ایمیل"}
                        </span>
                        <span
                          dir="ltr"
                          className="mt-1 block text-left font-mono text-[10px] text-slate-400"
                        >
                          {item.userId}
                        </span>
                      </button>
                    </TableCell>
                    <TableCell>
                      <span
                        className={`inline-flex items-center rounded-full px-2.5 py-1 text-xs font-semibold ${item.status === "enabled" ? "bg-emerald-50 text-emerald-700" : item.status === "locked" ? "bg-amber-50 text-amber-700" : "bg-slate-100 text-slate-600"}`}
                      >
                        <span
                          className={`ml-1.5 size-1.5 rounded-full ${item.status === "enabled" ? "bg-emerald-500" : item.status === "locked" ? "bg-amber-500" : "bg-slate-400"}`}
                        />
                        {statusLabel(item.status)}
                      </span>
                    </TableCell>
                    <TableCell>{sourceLabel(item.acquisitionSourceCode)}</TableCell>
                    <TableCell>{item.linkedProviderCodes.join("، ") || "-"}</TableCell>
                    <TableCell>{dateLabel(item.registeredAtUtc)}</TableCell>
                    <TableCell>{dateLabel(item.lastSuccessfulLoginAtUtc)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
          {!loading && !directory?.items.length && (
            <p className="py-3 text-sm text-muted-foreground">کاربری یافت نشد.</p>
          )}
          <div className="mt-4 flex flex-wrap items-center justify-between gap-3 border-t border-slate-100 pt-4 text-xs text-slate-500">
            <span>
              ۱۰ کاربر در هر صفحه · صفحه {pageIndex + 1} · زمان مبنا{" "}
              {directory ? dateLabel(directory.asOfUtc) : "-"}
            </span>
            <div className="flex gap-2">
              <Button
                size="sm"
                variant="outline"
                className="h-8 rounded-lg px-2.5"
                disabled={loading || pageIndex === 0}
                onClick={() => {
                  const nextIndex = pageIndex - 1;
                  setPageIndex(nextIndex);
                  void load(pageCursors[nextIndex] ?? null);
                }}
              >
                <ChevronRight className="ml-1 size-4" /> قبلی
              </Button>
              <Button
                size="sm"
                variant="outline"
                className="h-8 rounded-lg px-2.5"
                disabled={loading || !directory?.hasMore || !directory.nextCursor}
                onClick={() => {
                  if (!directory?.nextCursor) return;
                  const nextIndex = pageIndex + 1;
                  const next = [...pageCursors.slice(0, nextIndex), directory.nextCursor];
                  setPageCursors(next);
                  setPageIndex(nextIndex);
                  void load(directory.nextCursor);
                }}
              >
                بعدی <ChevronLeft className="mr-1 size-4" />
              </Button>
            </div>
          </div>
        </Panel>

        <Dialog
          open={Boolean(selectedDirectoryItem)}
          onOpenChange={(open) => {
            if (!open) {
              setSelectedDirectoryItem(null);
              setSelected(null);
              setCustomer(null);
              setCreditHistory(null);
              setPurchaseHistory(null);
              setError(null);
              setSuccess(null);
            }
          }}
        >
          <DialogContent
            dir="rtl"
            className="scheme-light max-h-[92vh] w-[calc(100vw-2rem)] overflow-y-auto bg-white p-4 sm:max-w-[min(96vw,1100px)] sm:p-6"
          >
            <DialogHeader className="text-right">
              <DialogTitle>
                {selectedDirectoryItem?.displayName ?? "نمای ۳۶۰ درجه مشتری"}
              </DialogTitle>
              <DialogDescription className="text-right">
                نمای ۳۶۰ درجه مشتری ·{" "}
                <span dir="ltr" className="inline-block">
                  {selectedDirectoryItem?.email ?? selectedDirectoryItem?.userId}
                </span>
              </DialogDescription>
            </DialogHeader>
            <div className="space-y-4">
              <Loading active={detailLoading} />
              {customer && selected && selectedDirectoryItem ? (
                <div className="space-y-4 text-sm">
                  <div className="flex flex-wrap items-start justify-between gap-4 rounded-xl border border-slate-100 bg-slate-50/80 p-4">
                    <div className="min-w-0 space-y-2">
                      <div className="flex flex-wrap items-center gap-2">
                        <p className="font-semibold text-slate-900">
                          {selectedDirectoryItem.displayName ??
                            selectedDirectoryItem.email ??
                            "بدون ایمیل"}
                        </p>
                        <span
                          className={`inline-flex items-center rounded-full px-2.5 py-1 text-xs font-semibold ${customer.isEnabled ? (customer.isLockedOut ? "bg-amber-50 text-amber-700" : "bg-emerald-50 text-emerald-700") : "bg-slate-200 text-slate-600"}`}
                        >
                          {customer.isEnabled
                            ? customer.isLockedOut
                              ? "قفل‌شده"
                              : "فعال"
                            : "غیرفعال"}
                        </span>
                      </div>
                      <p dir="ltr" className="text-left font-mono text-xs text-slate-400">
                        {customer.userId}
                      </p>
                      <p className="text-xs leading-6 text-slate-600">
                        وضعیت:{" "}
                        {customer.isEnabled
                          ? customer.isLockedOut
                            ? "قفل‌شده"
                            : "فعال"
                          : "غیرفعال"}{" "}
                        · منبع جذب: {sourceLabel(customer.acquisitionSourceCode)}
                      </p>
                      <p className="text-xs leading-6 text-slate-600">
                        ثبت‌نام: {dateLabel(customer.registeredAtUtc)} · آخرین ورود موفق:{" "}
                        {dateLabel(customer.lastSuccessfulLoginAtUtc)} (
                        {customer.lastLoginProviderCode ?? "نامشخص"})
                      </p>
                      <p className="text-xs leading-6 text-slate-600">
                        اعتبار بومی: {customer.hasNativeCredential ? "دارد" : "ندارد"} · ایمیل
                        تأییدشده: {customer.emailConfirmed ? "بله" : "خیر"}
                      </p>
                      <p className="text-xs leading-6 text-slate-600">
                        نشست‌های نوسازی فعال:{" "}
                        {customer.activeRefreshSessions.toLocaleString("fa-IR")}
                      </p>
                    </div>
                    <div className="flex flex-wrap gap-2">
                      {canManage && (
                        <>
                          <Button
                            size="sm"
                            className="rounded-lg"
                            onClick={() => void status(!selected.isEnabled)}
                          >
                            {selected.isEnabled ? "غیرفعال‌سازی" : "فعال‌سازی"}
                          </Button>
                          <Button
                            size="sm"
                            className="rounded-lg"
                            variant="outline"
                            onClick={() => void status(true, true)}
                          >
                            رفع قفل
                          </Button>
                          <Button
                            size="sm"
                            className="rounded-lg"
                            variant="destructive"
                            onClick={() => void revoke()}
                          >
                            ابطال نشست‌ها
                          </Button>
                        </>
                      )}
                      {canManageRoles && (
                        <Button
                          size="sm"
                          className="rounded-lg"
                          variant="outline"
                          onClick={() => void saveRoles()}
                        >
                          تنظیم نقش‌ها
                        </Button>
                      )}
                    </div>
                  </div>
                  <p className="rounded-lg bg-slate-50 px-3 py-2 text-xs text-slate-600">
                    عضویت در مستأجر: {customer.tenantMemberships.join("، ") || "عضویتی ثبت نشده"}
                  </p>
                  <div>
                    <h3 className="mb-2 font-semibold text-slate-800">هویت‌های پیوندخورده</h3>
                    {customer.linkedProviders.length ? (
                      providerLinksPagination.pageItems.map((link) => (
                        <p
                          key={`${link.providerCode}:${link.maskedExternalId}`}
                          className="mb-2 rounded-lg border border-slate-100 p-3 text-xs leading-6 text-slate-600"
                        >
                          {link.providerCode} · {link.status} · {link.maskedExternalId}{" "}
                          {link.displayMetadata ? `(${link.displayMetadata})` : ""} · پیوند:{" "}
                          {dateLabel(link.linkedAtUtc)} · تازگی پروفایل:{" "}
                          {dateLabel(link.profileSyncedAtUtc)}
                        </p>
                      ))
                    ) : (
                      <p className="text-muted-foreground">هویت پیوندخورده‌ای ثبت نشده است.</p>
                    )}
                    {customer.linkedProviders.length > 10 && (
                      <AdminPagination
                        {...providerLinksPagination}
                        onPageChange={providerLinksPagination.setPageIndex}
                      />
                    )}
                  </div>
                  <CustomerSection title="اشتراک" section={customer.subscription}>
                    {(data) => (
                      <div className="space-y-1">
                        <p>
                          طرح: {data.planName ?? data.planCode ?? "نامشخص"} · فعال:{" "}
                          {data.isActive ? "بله" : "خیر"}
                        </p>
                        <p>
                          بازه: {dateLabel(data.effectiveFromUtc)} تا{" "}
                          {dateLabel(data.effectiveToUtc)} · بازبینی: {data.revision}
                        </p>
                        <p>
                          قابلیت‌ها: {data.capabilities.map((item) => item.code).join("، ") || "-"}
                        </p>
                        <p>
                          همگام‌سازی بیرونی: {data.externalSyncStatus ?? "نامشخص"} · آخرین
                          همگام‌سازی: {dateLabel(data.externalLastSyncedAtUtc)}
                        </p>
                        <p className="text-xs text-muted-foreground">منبع: {data.sourceSystem}</p>
                      </div>
                    )}
                  </CustomerSection>
                  <CustomerSection title="اعتبارها" section={customer.credits}>
                    {(data) => (
                      <div className="space-y-1">
                        <p>
                          ظرفیت قابل‌مصرف: {data.spendableCredits ?? "نامشخص"} · رزروشده:{" "}
                          {data.reservedCredits ?? "نامشخص"} · سقف اعتباری:{" "}
                          {data.approvedCreditLine ?? "ندارد"}
                        </p>
                        <p>
                          مانده دوره‌های اشتراک: {data.activeSubscriptionCredits} · نزدیک‌ترین
                          انقضا: {dateLabel(data.nextExpiryUtc)}
                        </p>
                        {creditPeriodsPagination.pageItems.map((period) => (
                          <p
                            key={period.id}
                            className="rounded-lg bg-slate-50 px-3 py-2 text-xs leading-6 text-slate-600"
                          >
                            {period.planCode}: {period.remainingCredits} از {period.grantedCredits}{" "}
                            · انقضا {dateLabel(period.expiresAtUtc)} · {period.status}
                          </p>
                        ))}
                        {data.activePeriods.length > 0 && (
                          <AdminPagination
                            {...creditPeriodsPagination}
                            onPageChange={creditPeriodsPagination.setPageIndex}
                          />
                        )}
                        <p className="text-xs text-muted-foreground">منبع: {data.sourceSystem}</p>
                        {!creditHistory && (
                          <Button
                            size="sm"
                            variant="outline"
                            className="rounded-lg"
                            onClick={() => void loadCreditHistory()}
                          >
                            بارگذاری تاریخچه اعتبار
                          </Button>
                        )}
                        {creditHistoryPagination.pageItems.map((entry) => (
                          <div
                            key={`${entry.recordType}:${entry.id}`}
                            className="rounded-lg border border-slate-100 px-3 py-2 text-xs leading-6 text-slate-600"
                          >
                            <p>
                              {dateLabel(entry.occurredAtUtc)} ·{" "}
                              {entry.operationCode === "Billing.ManualAdjustment"
                                ? "افزودن دستی اعتبار"
                                : (entry.entryType ?? entry.operationCode ?? "-")}{" "}
                              ·{" "}
                              <span
                                className={`font-semibold ${entry.credits < 0 ? "text-rose-700" : "text-emerald-700"}`}
                              >
                                {entry.operationCode === "Billing.ManualAdjustment" ? "+" : ""}
                                {entry.credits.toLocaleString("fa-IR")}
                              </span>
                              {entry.allocationSource && ` · ${entry.allocationSource}`} ·{" "}
                              {entry.operationCode === "Billing.ManualAdjustment"
                                ? "ثبت‌شده"
                                : entry.status}
                            </p>
                            {entry.operationCode === "Billing.ManualAdjustment" &&
                              entry.auditDescription && (
                                <p className="mt-1 text-slate-500">
                                  دلیل: {entry.auditDescription}
                                </p>
                              )}
                          </div>
                        ))}
                        {creditHistory?.hasMore && (
                          <Button
                            size="sm"
                            variant="outline"
                            className="rounded-lg"
                            onClick={() => void loadCreditHistory()}
                          >
                            دریافت ۱۰ مورد بعدی
                          </Button>
                        )}
                        {creditHistoryRows.length > 0 && (
                          <AdminPagination
                            {...creditHistoryPagination}
                            onPageChange={creditHistoryPagination.setPageIndex}
                          />
                        )}
                      </div>
                    )}
                  </CustomerSection>
                  <CustomerSection title="خریدها" section={customer.purchases}>
                    {(data) => (
                      <div className="space-y-1">
                        <p>
                          تعداد خرید محلی تکمیل‌شده:{" "}
                          {data.completedLocalPurchaseCount ?? "در دسترس نیست"} · وضعیت استحقاق
                          بیرونی: {data.externalEntitlementsStatus}
                        </p>
                        {purchaseHistoryPagination.pageItems.map((purchase) => (
                          <p
                            key={purchase.checkoutIntentId}
                            className="rounded-lg border border-slate-100 px-3 py-2 text-xs leading-6 text-slate-600"
                          >
                            {dateLabel(purchase.createdAtUtc)} · {purchase.productDisplayName} ·{" "}
                            {purchase.amount} {purchase.currency} · {purchase.status} ·{" "}
                            {purchase.channel} · {purchase.providerName ?? "بدون ارائه‌دهنده"} ·
                            تطبیق: {purchase.reconciliationState ?? "نامشخص"}
                          </p>
                        ))}
                        <p className="text-xs text-muted-foreground">منبع: {data.sourceSystem}</p>
                        {!purchaseHistory && (
                          <Button
                            size="sm"
                            variant="outline"
                            className="rounded-lg"
                            onClick={() => void loadPurchaseHistory()}
                          >
                            بارگذاری تاریخچه خرید
                          </Button>
                        )}
                        {purchaseHistory?.hasMore && (
                          <Button
                            size="sm"
                            variant="outline"
                            className="rounded-lg"
                            onClick={() => void loadPurchaseHistory()}
                          >
                            دریافت ۱۰ مورد بعدی
                          </Button>
                        )}
                        {purchaseHistoryRows.length > 0 && (
                          <AdminPagination
                            {...purchaseHistoryPagination}
                            onPageChange={purchaseHistoryPagination.setPageIndex}
                          />
                        )}
                      </div>
                    )}
                  </CustomerSection>
                  <p className="text-xs text-muted-foreground">
                    زمان مبنای پاسخ: {dateLabel(customer.asOfUtc)} · نشست‌های نوسازی شاخص حضور
                    آنلاین نیستند.
                  </p>
                </div>
              ) : selectedDirectoryItem ? (
                <p className="text-sm text-muted-foreground">جزئیات بارگذاری نشد.</p>
              ) : (
                <p className="text-sm text-muted-foreground">یک کاربر را از فهرست انتخاب کنید.</p>
              )}
            </div>
          </DialogContent>
        </Dialog>
      </div>
      {actionDialog}
    </>
  );
}

function CustomerSection<T>({
  title,
  section,
  children,
}: {
  title: string;
  section: { status: string; data: T | null; reason?: string | null };
  children: (data: T) => React.ReactNode;
}) {
  return (
    <section className="space-y-2 border-t border-border pt-3">
      <h3 className="font-semibold">{title}</h3>
      {section.status === "available" && section.data ? (
        children(section.data)
      ) : (
        <p className="text-muted-foreground">
          {section.status === "not_authorized"
            ? "دسترسی به این بخش ندارید."
            : `داده در دسترس نیست${section.reason ? ` (${section.reason})` : ""}.`}
        </p>
      )}
    </section>
  );
}

const statusLabel = (status: string) =>
  ({ enabled: "فعال", disabled: "غیرفعال", locked: "قفل‌شده" })[status] ?? status;
const sourceLabel = (source: string) =>
  ({ native: "Native", tahlilapp: "TahlilApp", unknown: "نامشخص" })[source] ?? source;
const dateLabel = (value: string | null) =>
  value ? new Date(value).toLocaleString("fa-IR") : "نامشخص";

function RolesPanel({ user }: { user: AuthUser }) {
  const { ask, dialog: actionDialog } = useAdminDialog();
  const [roles, setRoles] = useState<AdminRole[]>([]);
  const [permissions, setPermissions] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
  const pagination = useAdminPagination(roles);
  const permissionsPagination = useAdminPagination(permissions);
  const manage = hasPermission(user, adminPermissions.rolesManage);
  const managePermissions = hasPermission(user, adminPermissions.permissionsManage);
  async function load() {
    try {
      setRoles(await adminApi.roles());
      if (hasPermission(user, adminPermissions.permissionsRead))
        setPermissions(await adminApi.permissions());
    } catch (e) {
      setError(showError(e));
    }
  }
  useEffect(() => {
    void load();
  }, []); // eslint-disable-line react-hooks/exhaustive-deps -- load the initial admin snapshot once
  async function create() {
    const form = await ask({
      title: "ایجاد نقش جدید",
      description: "نامی روشن انتخاب کنید تا مدیران بتوانند سطح دسترسی این نقش را تشخیص دهند.",
      confirmLabel: "ایجاد نقش",
      fields: [
        { name: "name", label: "نام نقش", required: true },
        { name: "reason", label: "دلیل ایجاد", type: "textarea", required: true },
      ],
    });
    const name = form?.name.trim();
    const reason = form?.reason.trim();
    if (!name || !reason) return;
    try {
      await adminApi.createRole(name, reason);
      await load();
    } catch (e) {
      setError(showError(e));
    }
  }
  async function toggle(role: AdminRole) {
    const form = await ask({
      title: role.isEnabled ? "غیرفعال‌سازی نقش" : "فعال‌سازی نقش",
      description: `وضعیت نقش «${role.name}» تغییر می‌کند.`,
      confirmLabel: role.isEnabled ? "غیرفعال‌سازی" : "فعال‌سازی",
      danger: role.isEnabled,
      fields: [{ name: "reason", label: "دلیل تغییر", type: "textarea", required: true }],
    });
    const reason = form?.reason.trim();
    if (!reason) return;
    try {
      await adminApi.updateRole(role.roleId, role.name, !role.isEnabled, reason);
      await load();
    } catch (e) {
      setError(showError(e));
    }
  }
  async function rename(role: AdminRole) {
    const form = await ask({
      title: "تغییر نام نقش",
      confirmLabel: "ذخیره نام",
      fields: [
        { name: "name", label: "نام نقش", initialValue: role.name, required: true },
        { name: "reason", label: "دلیل تغییر", type: "textarea", required: true },
      ],
    });
    const name = form?.name.trim();
    const reason = form?.reason.trim();
    if (!name || !reason) return;
    try {
      await adminApi.updateRole(role.roleId, name, role.isEnabled, reason);
      await load();
    } catch (e) {
      setError(showError(e));
    }
  }
  async function savePermissions(role: AdminRole) {
    const form = await ask({
      title: "تنظیم مجوزهای نقش",
      description:
        "موارد علامت‌خورده به نقش داده می‌شوند؛ برای پس‌گرفتن دسترسی، علامت را بردارید. عنوان فارسی و توضیح هر مورد، محدوده دسترسی را روشن می‌کند.",
      confirmLabel: "ذخیره مجوزها",
      fields: [
        {
          name: "permissions",
          label: "مجوزهای فعال",
          type: "multiselect",
          initialValue: role.permissions.join(", "),
          choices: permissions.map((permission) => ({
            value: permission,
            label: permissionCopy(permission).label,
            description: `${permissionCopy(permission).description} کد: ${permission}`,
          })),
        },
        { name: "reason", label: "دلیل تغییر", type: "textarea", required: true },
      ],
    });
    if (!form) return;
    const reason = form.reason.trim();
    if (!reason) return;
    try {
      await adminApi.setRolePermissions(role.roleId, splitCsv(form.permissions), reason);
      await load();
    } catch (e) {
      setError(showError(e));
    }
  }
  return (
    <Panel title="نقش‌ها و مجوزها">
      <Message error={error} />
      {manage && (
        <Button className="mb-4 rounded-xl" onClick={() => void create()}>
          <Plus className="ml-2 size-4" /> نقش جدید
        </Button>
      )}
      <div className="overflow-x-auto rounded-xl border border-slate-100">
        <Table className="admin-table">
          <TableHeader>
            <TableRow>
              <TableHead>نقش</TableHead>
              <TableHead>وضعیت</TableHead>
              <TableHead>مجوزها</TableHead>
              <TableHead />
            </TableRow>
          </TableHeader>
          <TableBody>
            {pagination.pageItems.map((role) => (
              <TableRow key={role.roleId}>
                <TableCell>{role.name}</TableCell>
                <TableCell>{role.isEnabled ? "فعال" : "غیرفعال"}</TableCell>
                <TableCell className="max-w-xl">
                  {role.permissions.length ? (
                    <div className="flex flex-wrap gap-1.5">
                      {role.permissions.map((permission) => (
                        <span
                          key={permission}
                          title={`${permissionCopy(permission).description} · ${permission}`}
                          className="inline-flex max-w-full flex-col rounded-lg border border-slate-200 bg-slate-50 px-2.5 py-1.5"
                        >
                          <span className="text-xs font-medium text-slate-700">
                            {permissionCopy(permission).label}
                          </span>
                          <code dir="ltr" className="mt-0.5 text-left text-[10px] text-slate-400">
                            {permission}
                          </code>
                        </span>
                      ))}
                    </div>
                  ) : (
                    <span className="text-xs text-slate-400">بدون مجوز</span>
                  )}
                </TableCell>
                <TableCell className="space-x-2 space-x-reverse">
                  {manage && (
                    <>
                      <Button size="sm" variant="outline" onClick={() => void rename(role)}>
                        تغییر نام
                      </Button>
                      <Button size="sm" variant="outline" onClick={() => void toggle(role)}>
                        تغییر وضعیت
                      </Button>
                    </>
                  )}
                  {managePermissions && (
                    <Button size="sm" variant="outline" onClick={() => void savePermissions(role)}>
                      تنظیم مجوز
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
      {!roles.length && (
        <p className="py-8 text-center text-sm text-slate-500">نقشی برای نمایش وجود ندارد.</p>
      )}
      <AdminPagination {...pagination} onPageChange={pagination.setPageIndex} />
      {permissions.length > 0 && (
        <section className="mt-5 border-t border-slate-100 pt-4">
          <h3 className="mb-3 text-xs font-semibold text-slate-600">کاتالوگ مجوزها</h3>
          <div className="grid gap-2 sm:grid-cols-2">
            {permissionsPagination.pageItems.map((permission) => (
              <div
                key={permission}
                className="rounded-xl border border-slate-200 bg-slate-50/80 p-3"
              >
                <p className="text-sm font-semibold text-slate-800">
                  {permissionCopy(permission).label}
                </p>
                <p className="mt-1 text-xs leading-5 text-slate-500">
                  {permissionCopy(permission).description}
                </p>
                <code dir="ltr" className="mt-2 block text-left text-[10px] text-slate-400">
                  {permission}
                </code>
              </div>
            ))}
          </div>
          <AdminPagination
            {...permissionsPagination}
            onPageChange={permissionsPagination.setPageIndex}
          />
        </section>
      )}
      {actionDialog}
    </Panel>
  );
}

function TenantsPanel({ user }: { user: AuthUser }) {
  const { ask, dialog: actionDialog } = useAdminDialog();
  const [tenants, setTenants] = useState<AdminTenant[]>([]);
  const [tenant, setTenant] = useState<AdminTenant | null>(null);
  const [members, setMembers] = useState<AdminTenantMember[]>([]);
  const [error, setError] = useState<string | null>(null);
  const tenantPagination = useAdminPagination(tenants);
  const memberPagination = useAdminPagination(members);
  const manage = hasPermission(user, adminPermissions.tenantsManage);
  async function loadTenants() {
    try {
      const rows = await adminApi.tenants();
      setTenants(rows);
      if (!tenant && rows[0]) void select(rows[0]);
    } catch (e) {
      setError(showError(e));
    }
  }
  async function select(row: AdminTenant) {
    setTenant(row);
    memberPagination.setPageIndex(0);
    try {
      setMembers(await adminApi.tenantMembers(row.tenantId));
    } catch (e) {
      setError(showError(e));
    }
  }
  useEffect(() => {
    void loadTenants();
  }, []); // eslint-disable-line react-hooks/exhaustive-deps -- load the initial admin snapshot once
  async function add() {
    if (!tenant) return;
    const form = await ask({
      title: "افزودن عضو به مستأجر",
      description: `شناسه کاربر را برای «${tenant.name}» وارد کنید.`,
      confirmLabel: "افزودن عضو",
      fields: [
        { name: "userId", label: "شناسه کاربر", dir: "ltr", required: true },
        { name: "reason", label: "دلیل افزودن", type: "textarea", required: true },
      ],
    });
    const id = form?.userId.trim();
    const reason = form?.reason.trim();
    if (!id || !reason) return;
    try {
      await adminApi.setTenantMember(tenant.tenantId, id, false, reason);
      await select(tenant);
    } catch (e) {
      setError(showError(e));
    }
  }
  async function remove(member: AdminTenantMember) {
    if (!tenant) return;
    const form = await ask({
      title: "حذف عضویت",
      description: `دسترسی ${member.email} به «${tenant.name}» حذف می‌شود.`,
      confirmLabel: "حذف عضویت",
      danger: true,
      fields: [{ name: "reason", label: "دلیل حذف", type: "textarea", required: true }],
    });
    const reason = form?.reason.trim();
    if (!reason) return;
    try {
      await adminApi.removeTenantMember(tenant.tenantId, member.userId, reason);
      await select(tenant);
    } catch (e) {
      setError(showError(e));
    }
  }
  async function makeDefault(member: AdminTenantMember) {
    if (!tenant) return;
    const form = await ask({
      title: "تغییر مستأجر پیش‌فرض",
      description: `«${tenant.name}» برای ${member.email} به‌عنوان مستأجر پیش‌فرض ثبت می‌شود.`,
      confirmLabel: "ثبت به‌عنوان پیش‌فرض",
      fields: [{ name: "reason", label: "دلیل تغییر", type: "textarea", required: true }],
    });
    const reason = form?.reason.trim();
    if (!reason) return;
    try {
      await adminApi.setTenantMember(tenant.tenantId, member.userId, true, reason);
      await select(tenant);
    } catch (e) {
      setError(showError(e));
    }
  }
  return (
    <div className="grid gap-4 lg:grid-cols-[.4fr_.6fr]">
      <Panel title="مستاجران">
        <div className="space-y-1">
          {tenantPagination.pageItems.map((row) => (
            <Button
              key={row.tenantId}
              variant={tenant?.tenantId === row.tenantId ? "default" : "ghost"}
              className="w-full justify-start rounded-xl px-3"
              onClick={() => void select(row)}
            >
              {row.name}
            </Button>
          ))}
        </div>
        {!tenants.length && (
          <p className="py-8 text-center text-sm text-slate-500">مستأجری برای نمایش وجود ندارد.</p>
        )}
        <AdminPagination {...tenantPagination} onPageChange={tenantPagination.setPageIndex} />
      </Panel>
      <Panel title="اعضای مستاجر">
        <Message error={error} />
        {manage && (
          <Button className="mb-4 rounded-xl" onClick={() => void add()}>
            <Plus className="ml-2 size-4" /> افزودن عضو
          </Button>
        )}
        <div className="overflow-x-auto rounded-xl border border-slate-100">
          <Table className="admin-table">
            <TableHeader>
              <TableRow>
                <TableHead>ایمیل</TableHead>
                <TableHead>پیش‌فرض</TableHead>
                <TableHead />
              </TableRow>
            </TableHeader>
            <TableBody>
              {memberPagination.pageItems.map((member) => (
                <TableRow key={member.userId}>
                  <TableCell>{member.email}</TableCell>
                  <TableCell>{member.isDefault ? "بله" : "خیر"}</TableCell>
                  <TableCell>
                    {manage && (
                      <div className="flex gap-2">
                        {!member.isDefault && (
                          <Button
                            size="sm"
                            variant="outline"
                            onClick={() => void makeDefault(member)}
                          >
                            پیش‌فرض
                          </Button>
                        )}
                        <Button size="sm" variant="destructive" onClick={() => void remove(member)}>
                          حذف
                        </Button>
                      </div>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
        {!members.length && (
          <p className="py-8 text-center text-sm text-slate-500">
            عضوی برای این مستأجر ثبت نشده است.
          </p>
        )}
        <AdminPagination {...memberPagination} onPageChange={memberPagination.setPageIndex} />
      </Panel>
      {actionDialog}
    </div>
  );
}

function BillingPanel({ user }: { user: AuthUser }) {
  const { ask, dialog: actionDialog } = useAdminDialog();
  const [plans, setPlans] = useState<AdminPlan[]>([]);
  const [customerId, setCustomerId] = useState("");
  const [customerSearch, setCustomerSearch] = useState("");
  const [customerDirectory, setCustomerDirectory] = useState<AdminUserDirectoryPage | null>(null);
  const [customerSearchCursors, setCustomerSearchCursors] = useState<(string | null)[]>([null]);
  const [customerSearchPage, setCustomerSearchPage] = useState(0);
  const [customerSearchLoading, setCustomerSearchLoading] = useState(false);
  const [customerLookupLoading, setCustomerLookupLoading] = useState(false);
  const [selectedCustomer, setSelectedCustomer] = useState<AdminUserDirectoryItem | null>(null);
  const [creditSaving, setCreditSaving] = useState(false);
  const [subscription, setSubscription] = useState<AdminSubscription | null>(null);
  const [ledger, setLedger] = useState<AdminUsageLedger[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [creditKey, setCreditKey] = useState(() => `admin-ui-credit-${crypto.randomUUID()}`);
  const [featureDialogOpen, setFeatureDialogOpen] = useState(false);
  const [featureDialogReadOnly, setFeatureDialogReadOnly] = useState(false);
  const [featurePlan, setFeaturePlan] = useState<AdminPlan | null>(null);
  const [featureRows, setFeatureRows] = useState<AdminCapability[]>([]);
  const [featureReason, setFeatureReason] = useState("");
  const [featureLoading, setFeatureLoading] = useState(false);
  const [featureSaving, setFeatureSaving] = useState(false);
  const [featureError, setFeatureError] = useState<string | null>(null);
  const plansPagination = useAdminPagination(plans);
  const ledgerPagination = useAdminPagination(ledger);
  const featureRowsPagination = useAdminPagination(featureRows);
  const canLookupCustomer =
    hasPermission(user, adminPermissions.subscriptionsRead) ||
    hasPermission(user, adminPermissions.subscriptionsManage) ||
    hasPermission(user, adminPermissions.usageLedgerRead) ||
    hasPermission(user, adminPermissions.creditsAdjust);
  const canSearchCustomers = hasPermission(user, adminPermissions.usersRead) && canLookupCustomer;
  async function loadPlans() {
    if (hasPermission(user, adminPermissions.plansRead))
      try {
        setPlans(await adminApi.plans());
      } catch (e) {
        setError(showError(e));
      }
  }
  useEffect(() => {
    void loadPlans();
  }, []); // eslint-disable-line react-hooks/exhaustive-deps -- load the initial admin snapshot once
  async function loadCustomerSearchPage(cursor: string | null, searchTerm = customerSearch) {
    const term = searchTerm.trim();
    if (!term) return;
    setCustomerSearchLoading(true);
    setError(null);
    try {
      setCustomerDirectory(await adminApi.userDirectory({ pageSize: 10, cursor, search: term }));
    } catch (e) {
      setCustomerDirectory(null);
      setError(showError(e));
    } finally {
      setCustomerSearchLoading(false);
    }
  }
  function searchCustomers() {
    const term = customerSearch.trim();
    if (!term) return;
    setCustomerDirectory(null);
    setCustomerSearchCursors([null]);
    setCustomerSearchPage(0);
    setSelectedCustomer(null);
    setCustomerId("");
    setSubscription(null);
    setLedger([]);
    setSuccess(null);
    void loadCustomerSearchPage(null, term);
  }
  function nextCustomerSearchPage() {
    const cursor = customerDirectory?.nextCursor;
    if (!cursor) return;
    const nextPage = customerSearchPage + 1;
    setCustomerSearchCursors((cursors) => [...cursors.slice(0, nextPage), cursor]);
    setCustomerSearchPage(nextPage);
    void loadCustomerSearchPage(cursor);
  }
  function previousCustomerSearchPage() {
    if (customerSearchPage === 0) return;
    const previousPage = customerSearchPage - 1;
    setCustomerSearchPage(previousPage);
    void loadCustomerSearchPage(customerSearchCursors[previousPage] ?? null);
  }
  async function lookup(accountId = customerId) {
    const normalizedAccountId = accountId.trim();
    if (!normalizedAccountId) return;
    setCustomerId(normalizedAccountId);
    setError(null);
    setCustomerLookupLoading(true);
    try {
      const [nextSubscription, nextLedger] = await Promise.all([
        hasPermission(user, adminPermissions.subscriptionsRead)
          ? adminApi.subscription(normalizedAccountId)
          : Promise.resolve(null),
        hasPermission(user, adminPermissions.usageLedgerRead)
          ? adminApi.ledger(normalizedAccountId)
          : Promise.resolve([] as AdminUsageLedger[]),
      ]);
      setSubscription(nextSubscription);
      setLedger(nextLedger);
    } catch (e) {
      setError(showError(e));
    } finally {
      setCustomerLookupLoading(false);
    }
  }
  async function selectCustomer(item: AdminUserDirectoryItem) {
    setSelectedCustomer(item);
    setSubscription(null);
    setLedger([]);
    const accountId = item.customerAccountId ?? item.subscription.data?.customerAccountId;
    if (!accountId) {
      setCustomerId("");
      setError("برای این کاربر حساب مالی پیدا نشد یا دسترسی لازم برای بازیابی آن را ندارید.");
      return;
    }
    await lookup(accountId);
  }
  const manualLookupForm = (
    <form
      className="grid gap-2 sm:grid-cols-[minmax(0,1fr)_auto]"
      onSubmit={(event) => {
        event.preventDefault();
        void lookup();
      }}
    >
      <Input
        dir="ltr"
        aria-label="شناسه حساب مشتری"
        value={customerId}
        onChange={(event) => {
          setCustomerId(event.target.value);
          setSelectedCustomer(null);
          setSubscription(null);
          setLedger([]);
        }}
        placeholder="شناسه حساب مشتری"
        className="rounded-xl border-slate-200 bg-white"
      />
      <Button
        type="submit"
        disabled={!customerId.trim() || customerLookupLoading}
        className="rounded-xl"
      >
        {customerLookupLoading ? (
          <LoaderCircle className="ml-2 size-4 animate-spin" />
        ) : (
          <RefreshCw className="ml-2 size-4" />
        )}
        بازیابی با شناسه
      </Button>
    </form>
  );
  async function addCredits() {
    const form = await ask({
      title: "افزودن اعتبار به مشتری",
      description:
        "اعتبار به مانده مشتری افزوده می‌شود. مبلغ، زمان و دلیل این افزایش در تاریخچه مالی و ممیزی ثبت خواهد شد.",
      confirmLabel: "افزودن اعتبار",
      fields: [
        {
          name: "amount",
          label: "مقدار اعتبار برای افزودن",
          type: "number",
          min: "1",
          required: true,
          placeholder: "مثلاً ۲۵۰",
        },
        { name: "reason", label: "دلیل افزودن اعتبار", type: "textarea", required: true },
      ],
    });
    if (!form) return;
    const amount = Number(form.amount);
    const reason = form.reason.trim();
    if (!Number.isInteger(amount) || amount <= 0 || !reason) {
      setError("مقدار اعتبار باید عدد صحیح و بزرگ‌تر از صفر باشد و دلیل را هم وارد کنید.");
      return;
    }
    setCreditSaving(true);
    try {
      const result = await adminApi.adjustCredits(customerId, amount, reason, creditKey);
      setSuccess(
        `${amount.toLocaleString("fa-IR")} اعتبار به حساب افزوده شد. مانده جدید: ${result.updatedBalance.toLocaleString("fa-IR")}`,
      );
      setCreditKey(`admin-ui-credit-${crypto.randomUUID()}`);
      ledgerPagination.setPageIndex(0);
      await lookup(customerId);
    } catch (e) {
      setError(showError(e));
    } finally {
      setCreditSaving(false);
    }
  }
  async function changeSubscription() {
    if (!subscription) return;
    const form = await ask({
      title: "تغییر اشتراک مشتری",
      description: "طرح جدید را انتخاب کنید. گزینه بدون اشتراک، طرح فعلی را حذف می‌کند.",
      confirmLabel: "ثبت اشتراک",
      fields: [
        {
          name: "planCode",
          label: "طرح اشتراک",
          type: "select",
          initialValue: subscription.planCode ?? "",
          choices: [
            { value: "", label: "بدون اشتراک" },
            ...plans.map((plan) => ({ value: plan.code, label: `${plan.name} (${plan.code})` })),
          ],
        },
        { name: "reason", label: "دلیل تغییر", type: "textarea", required: true },
      ],
    });
    if (!form) return;
    const planCode = form.planCode;
    const reason = form.reason.trim();
    if (!reason) return;
    try {
      setSubscription(
        await adminApi.setSubscription(customerId, {
          planCode: planCode || null,
          effectiveFrom: null,
          effectiveTo: null,
          expectedRevision: subscription.revision,
          reason,
        }),
      );
      setSuccess("اشتراک ثبت شد.");
    } catch (e) {
      setError(showError(e));
    }
  }
  async function editIncludedCredits(plan: AdminPlan) {
    const form = await ask({
      title: "ویرایش اعتبار طرح",
      description: `اعتبار پایه طرح «${plan.name}» را تغییر دهید. نسخه سیاست قیمت‌گذاری و دلیل تغییر برای ممیزی ثبت می‌شوند.`,
      confirmLabel: "ذخیره تغییرات",
      fields: [
        {
          name: "includedCredits",
          label: "اعتبار طرح",
          type: "number",
          min: "0",
          initialValue: String(plan.includedCredits),
          required: true,
        },
        {
          name: "pricingPolicyVersion",
          label: "نسخه سیاست قیمت‌گذاری",
          dir: "ltr",
          initialValue: plan.pricingPolicyVersion,
          required: true,
        },
        { name: "reason", label: "دلیل تغییر", type: "textarea", required: true },
      ],
    });
    if (!form) return;
    const includedCredits = Number(form.includedCredits);
    const pricingPolicyVersion = form.pricingPolicyVersion.trim();
    const reason = form.reason.trim();
    if (
      !Number.isFinite(includedCredits) ||
      includedCredits < 0 ||
      !pricingPolicyVersion ||
      !reason
    )
      return;
    try {
      await adminApi.updatePlanIncludedCredits(plan.code, {
        includedCredits,
        pricingPolicyVersion,
        reason,
      });
      setSuccess("اعتبار طرح به‌روزرسانی شد.");
      await loadPlans();
    } catch (e) {
      setError(showError(e));
    }
  }
  async function viewCapabilities(plan: AdminPlan) {
    try {
      const rows = await adminApi.capabilities(plan.code);
      setFeaturePlan(plan);
      setFeatureRows(rows);
      setFeatureReason("");
      setFeatureError(null);
      setFeatureLoading(false);
      setFeatureDialogReadOnly(true);
      setFeatureDialogOpen(true);
    } catch (e) {
      setError(showError(e));
    }
  }
  async function editCapabilities(plan: AdminPlan) {
    setFeaturePlan(plan);
    setFeatureRows([]);
    setFeatureReason("");
    setFeatureError(null);
    setFeatureDialogReadOnly(false);
    setFeatureDialogOpen(true);
    setFeatureLoading(true);
    try {
      setFeatureRows(await adminApi.capabilities(plan.code));
    } catch (e) {
      setFeatureError(showError(e));
    } finally {
      setFeatureLoading(false);
    }
  }
  function updateCapability(index: number, patch: Partial<AdminCapability>) {
    setFeatureRows((rows) =>
      rows.map((row, rowIndex) => (rowIndex === index ? { ...row, ...patch } : row)),
    );
  }
  async function publishCapabilities() {
    if (!featurePlan) return;
    const capabilities = featureRows.map((row) => ({
      ...row,
      capabilityCode: row.capabilityCode.trim(),
      policyVersion: row.policyVersion.trim(),
      limit: row.limit == null ? null : Number(row.limit),
    }));
    if (
      capabilities.some(
        (row) =>
          !row.capabilityCode ||
          !row.policyVersion ||
          (row.limit != null && (!Number.isFinite(row.limit) || row.limit < 0)),
      )
    ) {
      setFeatureError(
        "برای هر قابلیت، کد و نسخه سیاست را وارد کنید و حد را صفر یا بیشتر قرار دهید.",
      );
      return;
    }
    if (new Set(capabilities.map((row) => row.capabilityCode)).size !== capabilities.length) {
      setFeatureError("کد هر قابلیت باید یکتا باشد.");
      return;
    }
    if (!featureReason.trim()) {
      setFeatureError("ثبت دلیل انتشار الزامی است.");
      return;
    }
    setFeatureSaving(true);
    setFeatureError(null);
    try {
      await adminApi.publishCapabilities(featurePlan.code, capabilities, featureReason.trim());
      setSuccess("نسخه قابلیت‌های طرح منتشر شد.");
      setFeatureDialogOpen(false);
    } catch (e) {
      setFeatureError(showError(e));
    } finally {
      setFeatureSaving(false);
    }
  }
  return (
    <>
      <div className="space-y-4">
        <Panel title="طرح‌ها">
          <Message error={error} success={success} />
          <div className="overflow-x-auto rounded-xl border border-slate-100">
            <Table className="admin-table">
              <TableHeader>
                <TableRow>
                  <TableHead>کد</TableHead>
                  <TableHead>نام</TableHead>
                  <TableHead>اعتبار</TableHead>
                  <TableHead />
                </TableRow>
              </TableHeader>
              <TableBody>
                {plansPagination.pageItems.map((plan) => (
                  <TableRow key={plan.code}>
                    <TableCell>
                      <code
                        dir="ltr"
                        className="rounded-md bg-slate-100 px-2 py-1 text-xs text-slate-700"
                      >
                        {plan.code}
                      </code>
                    </TableCell>
                    <TableCell className="font-medium">{plan.name}</TableCell>
                    <TableCell>{plan.includedCredits.toLocaleString("fa-IR")}</TableCell>
                    <TableCell>
                      <div className="flex flex-wrap gap-2">
                        <Button
                          size="sm"
                          variant="outline"
                          className="rounded-lg"
                          onClick={() => void viewCapabilities(plan)}
                        >
                          <Layers3 className="ml-1.5 size-3.5" /> قابلیت‌ها
                        </Button>
                        {hasPermission(user, adminPermissions.plansManage) && (
                          <>
                            <Button
                              className="rounded-lg"
                              size="sm"
                              variant="outline"
                              onClick={() => void editIncludedCredits(plan)}
                            >
                              ویرایش اعتبار
                            </Button>
                            <Button
                              className="rounded-lg"
                              size="sm"
                              variant="outline"
                              onClick={() => void editCapabilities(plan)}
                            >
                              انتشار قابلیت
                            </Button>
                          </>
                        )}
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
          {!plans.length && (
            <p className="py-8 text-center text-sm text-slate-500">طرحی برای نمایش وجود ندارد.</p>
          )}
          <AdminPagination {...plansPagination} onPageChange={plansPagination.setPageIndex} />
        </Panel>
        <Panel title="حساب مشتری">
          {canSearchCustomers ? (
            <div className="mb-5 space-y-4 rounded-2xl border border-slate-200 bg-slate-50/70 p-4 sm:p-5">
              <div className="flex flex-col justify-between gap-3 sm:flex-row sm:items-end">
                <div>
                  <h3 className="text-sm font-semibold text-slate-900">جست‌وجوی مشتری</h3>
                  <p className="mt-1 text-xs leading-6 text-slate-500">
                    با ایمیل، نام، نام خانوادگی یا نام کامل جست‌وجو کنید.
                  </p>
                </div>
                <form
                  className="grid w-full gap-2 sm:max-w-xl sm:grid-cols-[minmax(0,1fr)_auto]"
                  onSubmit={(event) => {
                    event.preventDefault();
                    searchCustomers();
                  }}
                >
                  <Input
                    value={customerSearch}
                    onChange={(event) => setCustomerSearch(event.target.value)}
                    aria-label="جست‌وجوی مشتری بر اساس ایمیل یا نام"
                    placeholder="ایمیل یا نام و نام خانوادگی کاربر"
                    autoComplete="off"
                    className="rounded-xl border-slate-200 bg-white"
                  />
                  <Button
                    type="submit"
                    disabled={!customerSearch.trim() || customerSearchLoading}
                    className="rounded-xl"
                  >
                    {customerSearchLoading ? (
                      <LoaderCircle className="ml-2 size-4 animate-spin" />
                    ) : (
                      <Search className="ml-2 size-4" />
                    )}
                    جست‌وجو
                  </Button>
                </form>
              </div>
              {customerSearchLoading && (
                <div className="flex items-center gap-2 text-xs text-slate-500" role="status">
                  <LoaderCircle className="size-4 animate-spin" /> در حال جست‌وجوی مشتریان…
                </div>
              )}
              {customerDirectory && (
                <>
                  <div className="overflow-x-auto rounded-xl border border-slate-200 bg-white">
                    {customerDirectory.items.length ? (
                      <Table className="admin-table">
                        <TableHeader>
                          <TableRow>
                            <TableHead>مشتری</TableHead>
                            <TableHead>وضعیت</TableHead>
                            <TableHead>اشتراک و اعتبار</TableHead>
                            <TableHead />
                          </TableRow>
                        </TableHeader>
                        <TableBody>
                          {customerDirectory.items.map((item) => {
                            const statusLabel =
                              item.status === "enabled"
                                ? "فعال"
                                : item.status === "locked"
                                  ? "قفل‌شده"
                                  : "غیرفعال";
                            return (
                              <TableRow key={item.userId}>
                                <TableCell>
                                  <div className="min-w-48">
                                    <div className="font-medium text-slate-800">
                                      {item.displayName || "نام ثبت نشده"}
                                    </div>
                                    <div
                                      dir="ltr"
                                      className="mt-1 text-left text-xs text-slate-500"
                                    >
                                      {item.email || item.userId}
                                    </div>
                                  </div>
                                </TableCell>
                                <TableCell>
                                  <span
                                    className={`inline-flex rounded-full px-2.5 py-1 text-xs font-medium ${
                                      item.status === "enabled"
                                        ? "bg-emerald-50 text-emerald-700"
                                        : "bg-slate-100 text-slate-600"
                                    }`}
                                  >
                                    {statusLabel}
                                  </span>
                                </TableCell>
                                <TableCell>
                                  <div className="space-y-1 text-xs text-slate-600">
                                    {item.subscription.data?.planCode && (
                                      <div>طرح: {item.subscription.data.planCode}</div>
                                    )}
                                    {item.credits.data?.spendableCredits != null && (
                                      <div>
                                        اعتبار قابل استفاده:{" "}
                                        {item.credits.data.spendableCredits.toLocaleString("fa-IR")}
                                      </div>
                                    )}
                                    {!item.subscription.data?.planCode &&
                                      item.credits.data?.spendableCredits == null &&
                                      "—"}
                                  </div>
                                </TableCell>
                                <TableCell>
                                  <Button
                                    size="sm"
                                    variant="outline"
                                    className="rounded-lg"
                                    disabled={customerLookupLoading}
                                    aria-label={`بازیابی حساب ${item.displayName || item.email || item.userId}`}
                                    onClick={() => void selectCustomer(item)}
                                  >
                                    بازیابی حساب
                                  </Button>
                                </TableCell>
                              </TableRow>
                            );
                          })}
                        </TableBody>
                      </Table>
                    ) : (
                      <div className="px-4 py-8 text-center">
                        <p className="text-sm font-medium text-slate-700">مشتری‌ای پیدا نشد</p>
                        <p className="mt-1 text-xs text-slate-500">
                          املای ایمیل یا نام را بررسی کنید و دوباره جست‌وجو کنید.
                        </p>
                      </div>
                    )}
                  </div>
                  {customerDirectory.items.length > 0 && (
                    <div className="flex flex-wrap items-center justify-between gap-3 text-xs text-slate-500">
                      <span>صفحه {customerSearchPage + 1} · حداکثر ۱۰ نتیجه در هر صفحه</span>
                      <div className="flex items-center gap-2">
                        <Button
                          size="sm"
                          variant="outline"
                          className="h-8 rounded-lg px-2.5"
                          disabled={customerSearchPage === 0 || customerSearchLoading}
                          onClick={previousCustomerSearchPage}
                        >
                          <ChevronRight className="ml-1 size-4" /> قبلی
                        </Button>
                        <Button
                          size="sm"
                          variant="outline"
                          className="h-8 rounded-lg px-2.5"
                          disabled={!customerDirectory.hasMore || customerSearchLoading}
                          onClick={nextCustomerSearchPage}
                        >
                          بعدی <ChevronLeft className="mr-1 size-4" />
                        </Button>
                      </div>
                    </div>
                  )}
                </>
              )}
              <details className="border-t border-slate-200 pt-3">
                <summary className="cursor-pointer text-xs font-medium text-slate-600 hover:text-slate-900">
                  ورود مستقیم شناسه حساب
                </summary>
                <div className="mt-3">{manualLookupForm}</div>
              </details>
            </div>
          ) : (
            canLookupCustomer && <div className="mb-5">{manualLookupForm}</div>
          )}
          {selectedCustomer && (
            <div className="mb-5 flex flex-wrap items-center justify-between gap-3 rounded-xl border border-emerald-200 bg-emerald-50/70 p-4">
              <div>
                <div className="text-sm font-semibold text-slate-900">
                  {selectedCustomer.displayName || "نام ثبت نشده"}
                </div>
                <div dir="ltr" className="mt-1 text-left text-xs text-slate-600">
                  {selectedCustomer.email || selectedCustomer.userId}
                </div>
              </div>
              <div className="flex flex-wrap items-center gap-2">
                {customerId && (
                  <code
                    dir="ltr"
                    className="rounded-lg bg-white px-2.5 py-1.5 text-[11px] text-slate-600"
                  >
                    {customerId}
                  </code>
                )}
                {customerLookupLoading && (
                  <span className="flex items-center gap-1.5 text-xs text-slate-500">
                    <LoaderCircle className="size-4 animate-spin" /> در حال بازیابی
                  </span>
                )}
              </div>
            </div>
          )}
          {subscription && (
            <div className="mb-5 flex flex-wrap items-center justify-between gap-4 rounded-xl border border-slate-100 bg-slate-50/80 p-4">
              <div className="grid gap-4 text-sm sm:grid-cols-3">
                <div>
                  <span className="block text-xs text-slate-500">طرح فعال</span>
                  <span className="mt-1 block font-semibold text-slate-800">
                    {subscription.planCode ?? "بدون طرح"}
                  </span>
                </div>
                <div>
                  <span className="block text-xs text-slate-500">نوع حساب</span>
                  <span className="mt-1 block font-semibold text-slate-800">
                    {subscription.accountType}
                  </span>
                </div>
                <div>
                  <span className="block text-xs text-slate-500">نسخه اشتراک</span>
                  <span className="mt-1 block font-semibold text-slate-800">
                    {subscription.revision.toLocaleString("fa-IR")}
                  </span>
                </div>
              </div>
              {hasPermission(user, adminPermissions.subscriptionsManage) && (
                <Button
                  className="rounded-xl"
                  size="sm"
                  variant="outline"
                  onClick={() => void changeSubscription()}
                >
                  تغییر اشتراک
                </Button>
              )}
            </div>
          )}
          {hasPermission(user, adminPermissions.creditsAdjust) && customerId && (
            <Button
              className="mb-4 rounded-xl"
              disabled={creditSaving}
              onClick={() => void addCredits()}
            >
              {creditSaving ? (
                <LoaderCircle className="ml-2 size-4 animate-spin" />
              ) : (
                <Plus className="ml-2 size-4" />
              )}
              افزودن اعتبار
            </Button>
          )}
          <div className="overflow-x-auto rounded-xl border border-slate-100">
            <Table className="admin-table">
              <TableHeader>
                <TableRow>
                  <TableHead>زمان</TableHead>
                  <TableHead>عملیات</TableHead>
                  <TableHead>اعتبار</TableHead>
                  <TableHead>وضعیت</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {ledgerPagination.pageItems.map((row) => {
                  const isManualCreditGrant = row.operationCode === "Billing.ManualAdjustment";
                  return (
                    <TableRow key={row.id}>
                      <TableCell>{new Date(row.occurredAt).toLocaleString("fa-IR")}</TableCell>
                      <TableCell>
                        <div className="font-medium">
                          {isManualCreditGrant ? "افزودن دستی اعتبار" : row.operationCode}
                        </div>
                        {isManualCreditGrant && row.auditDescription && (
                          <div className="mt-1 max-w-md whitespace-normal text-xs leading-5 text-slate-500">
                            دلیل: {row.auditDescription}
                          </div>
                        )}
                      </TableCell>
                      <TableCell
                        className={
                          row.credits < 0
                            ? "font-semibold text-rose-700"
                            : "font-semibold text-emerald-700"
                        }
                      >
                        {isManualCreditGrant ? "+" : ""}
                        {row.credits.toLocaleString("fa-IR")}
                      </TableCell>
                      <TableCell>
                        {isManualCreditGrant ? "ثبت‌شده" : (row.completionStatus ?? row.entryType)}
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          </div>
          {!ledger.length && (
            <p className="py-8 text-center text-sm text-slate-500">
              پس از جست‌وجوی حساب، سوابق اعتبار اینجا نمایش داده می‌شود.
            </p>
          )}
          <AdminPagination {...ledgerPagination} onPageChange={ledgerPagination.setPageIndex} />
        </Panel>
      </div>
      <Dialog
        open={featureDialogOpen}
        onOpenChange={(open) => {
          if (!featureSaving) setFeatureDialogOpen(open);
        }}
      >
        <DialogContent
          dir="rtl"
          className="scheme-light max-h-[92vh] overflow-y-auto rounded-2xl border-slate-200 p-0 text-right shadow-2xl sm:max-w-3xl"
        >
          <DialogHeader className="border-b border-slate-100 px-6 py-5 text-right">
            <DialogTitle className="text-base font-bold text-slate-900">
              {featureDialogReadOnly ? "قابلیت‌های طرح" : "انتشار قابلیت‌های طرح"}
              {featurePlan ? ` · ${featurePlan.name}` : ""}
            </DialogTitle>
            <DialogDescription className="pt-1 leading-6 text-slate-500">
              {featureDialogReadOnly
                ? "قابلیت‌ها و محدودیت‌های تعریف‌شده برای این طرح."
                : "قابلیت‌ها را ساختاریافته ویرایش کنید و نسخه جدید را همراه با دلیل انتشار ثبت کنید."}
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-4 px-5 py-5 sm:px-6">
            {featureLoading ? (
              <Loading active />
            ) : featureRows.length ? (
              <div className="space-y-3">
                {featureRowsPagination.pageItems.map((row, index) => {
                  const rowIndex = featureRowsPagination.pageIndex * 10 + index;
                  return (
                    <section
                      key={`${rowIndex}-${row.capabilityCode}`}
                      className="grid gap-3 rounded-xl border border-slate-200 bg-slate-50/70 p-4 sm:grid-cols-2 xl:grid-cols-[minmax(0,1.2fr)_minmax(0,1fr)_minmax(120px,.7fr)_auto]"
                    >
                      <label className="text-xs font-medium text-slate-600">
                        کد قابلیت
                        <Input
                          dir="ltr"
                          disabled={featureDialogReadOnly}
                          value={row.capabilityCode}
                          onChange={(event) =>
                            updateCapability(rowIndex, { capabilityCode: event.target.value })
                          }
                          className="mt-1.5 rounded-lg bg-white text-left"
                          placeholder="feature.code"
                        />
                      </label>
                      <label className="text-xs font-medium text-slate-600">
                        نسخه سیاست
                        <Input
                          dir="ltr"
                          disabled={featureDialogReadOnly}
                          value={row.policyVersion}
                          onChange={(event) =>
                            updateCapability(rowIndex, { policyVersion: event.target.value })
                          }
                          className="mt-1.5 rounded-lg bg-white text-left"
                          placeholder="v1"
                        />
                      </label>
                      <label className="text-xs font-medium text-slate-600">
                        سقف استفاده
                        <Input
                          type="number"
                          min="0"
                          dir="ltr"
                          disabled={featureDialogReadOnly}
                          value={row.limit ?? ""}
                          onChange={(event) =>
                            updateCapability(rowIndex, {
                              limit: event.target.value === "" ? null : Number(event.target.value),
                            })
                          }
                          className="mt-1.5 rounded-lg bg-white"
                          placeholder="بدون محدودیت"
                        />
                      </label>
                      <div className="flex items-center justify-between gap-3 sm:col-span-2 xl:col-span-1 xl:justify-center">
                        <label className="flex items-center gap-2 text-xs font-medium text-slate-600">
                          <Switch
                            disabled={featureDialogReadOnly}
                            checked={row.isEnabled}
                            onCheckedChange={(checked) =>
                              updateCapability(rowIndex, { isEnabled: checked })
                            }
                          />
                          {row.isEnabled ? "فعال" : "غیرفعال"}
                        </label>
                        {!featureDialogReadOnly && (
                          <Button
                            type="button"
                            size="icon"
                            variant="ghost"
                            className="size-8 rounded-lg text-rose-600 hover:bg-rose-50"
                            aria-label="حذف قابلیت"
                            onClick={() =>
                              setFeatureRows((rows) =>
                                rows.filter((_, currentIndex) => currentIndex !== rowIndex),
                              )
                            }
                          >
                            <X className="size-4" />
                          </Button>
                        )}
                      </div>
                    </section>
                  );
                })}
              </div>
            ) : (
              <div className="rounded-xl border border-dashed border-slate-300 bg-slate-50 px-4 py-8 text-center text-sm text-slate-500">
                {featureDialogReadOnly
                  ? "قابلیتی برای این طرح ثبت نشده است."
                  : "هنوز قابلیتی اضافه نشده است."}
              </div>
            )}
            {featureRows.length > 10 && (
              <AdminPagination
                {...featureRowsPagination}
                onPageChange={featureRowsPagination.setPageIndex}
              />
            )}
            {!featureDialogReadOnly && !featureLoading && (
              <Button
                type="button"
                variant="outline"
                className="w-full rounded-xl border-dashed"
                onClick={() =>
                  setFeatureRows((rows) => [
                    ...rows,
                    {
                      capabilityCode: "",
                      policyVersion: featurePlan?.pricingPolicyVersion ?? "",
                      isEnabled: true,
                      limit: null,
                    },
                  ])
                }
              >
                <Plus className="ml-2 size-4" /> افزودن قابلیت
              </Button>
            )}
            {!featureDialogReadOnly && (
              <label className="block text-sm font-medium text-slate-700">
                دلیل انتشار
                <Textarea
                  value={featureReason}
                  onChange={(event) => {
                    setFeatureReason(event.target.value);
                    setFeatureError(null);
                  }}
                  className="mt-1.5 min-h-20 rounded-xl border-slate-200"
                  placeholder="دلیل تغییر نسخه قابلیت‌های این طرح را بنویسید"
                />
              </label>
            )}
            {featureError && (
              <p role="alert" className="rounded-lg bg-rose-50 p-3 text-sm text-rose-700">
                {featureError}
              </p>
            )}
          </div>
          <DialogFooter className="gap-2 border-t border-slate-100 bg-slate-50/70 px-6 py-4 sm:space-x-0">
            {featureDialogReadOnly ? (
              <Button
                type="button"
                className="rounded-xl"
                onClick={() => setFeatureDialogOpen(false)}
              >
                بستن
              </Button>
            ) : (
              <>
                <Button
                  type="button"
                  variant="outline"
                  className="rounded-xl"
                  onClick={() => setFeatureDialogOpen(false)}
                >
                  انصراف
                </Button>
                <Button
                  type="button"
                  className="rounded-xl"
                  disabled={featureLoading || featureSaving}
                  onClick={() => void publishCapabilities()}
                >
                  {featureSaving && <LoaderCircle className="ml-2 size-4 animate-spin" />}
                  انتشار نسخه
                </Button>
              </>
            )}
          </DialogFooter>
        </DialogContent>
      </Dialog>
      {actionDialog}
    </>
  );
}

function AuditsPanel({ user }: { user: AuthUser }) {
  const [security, setSecurity] = useState<AdminAudit[]>([]);
  const [billing, setBilling] = useState<AdminAudit[]>([]);
  const [error, setError] = useState<string | null>(null);
  const rows = [...security, ...billing].sort((a, b) => b.occurredAt.localeCompare(a.occurredAt));
  const pagination = useAdminPagination(rows);
  async function load() {
    try {
      if (hasPermission(user, adminPermissions.securityAuditRead))
        setSecurity(await adminApi.securityAudits());
      if (hasPermission(user, adminPermissions.billingAuditRead))
        setBilling(await adminApi.billingAudits());
    } catch (e) {
      setError(showError(e));
    }
  }
  useEffect(() => {
    void load();
  }, []); // eslint-disable-line react-hooks/exhaustive-deps -- load the initial admin snapshot once
  return (
    <Panel title="رویدادهای ممیزی">
      <div className="mb-4 flex items-center justify-between gap-3">
        <Message error={error} />
        <Button
          size="sm"
          variant="outline"
          className="mr-auto rounded-xl"
          onClick={() => void load()}
        >
          <RefreshCw className="ml-2 size-4" /> تازه‌سازی
        </Button>
      </div>
      <AuditTable rows={pagination.pageItems} />
      {!rows.length && (
        <p className="py-8 text-center text-sm text-slate-500">رویدادی برای نمایش وجود ندارد.</p>
      )}
      <AdminPagination {...pagination} onPageChange={pagination.setPageIndex} />
    </Panel>
  );
}
function AuditTable({ rows }: { rows: AdminAudit[] }) {
  return (
    <div className="overflow-x-auto rounded-xl border border-slate-100">
      <Table className="admin-table">
        <TableHeader>
          <TableRow>
            <TableHead>زمان</TableHead>
            <TableHead>عملیات</TableHead>
            <TableHead>هدف</TableHead>
            <TableHead>دلیل</TableHead>
            <TableHead>شناسه همبستگی</TableHead>
            <TableHead>شواهد</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.map((row) => (
            <TableRow key={row.auditId ?? row.id}>
              <TableCell>{new Date(row.occurredAt).toLocaleString("fa-IR")}</TableCell>
              <TableCell>{row.actionCode}</TableCell>
              <TableCell>
                {row.targetType}: {row.targetId}
              </TableCell>
              <TableCell>{row.reason ?? "-"}</TableCell>
              <TableCell className="max-w-40 truncate mono text-xs">{row.correlationId}</TableCell>
              <TableCell className="max-w-64 truncate text-xs">
                {row.before || row.after || "-"}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}
