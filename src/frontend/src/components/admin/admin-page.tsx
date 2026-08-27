import { useEffect, useState } from "react";
import { Link } from "@tanstack/react-router";
import { Activity, ArrowRight, Database, RefreshCw, Search, Shield } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
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
  type AdminPlan,
  type AdminRole,
  type AdminSubscription,
  type AdminTenant,
  type AdminTenantMember,
  type AdminUsageLedger,
  type AdminUser,
} from "@/integrations/financial-copilot/admin-client";
import {
  adminPermissions,
  hasPermission,
} from "@/integrations/financial-copilot/admin-permissions";
import type { AuthUser } from "@/integrations/financial-copilot/auth";

type AdminTab = "users" | "roles" | "tenants" | "billing" | "audits";
const askReason = (label: string) =>
  window.prompt(`${label}\nدلیل این تغییر را وارد کنید:`)?.trim() || null;
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

export function AdminPage({ user }: { user: AuthUser }) {
  const tabs = [
    hasPermission(user, adminPermissions.usersRead) && ["users", "کاربران"],
    hasPermission(user, adminPermissions.rolesRead) && ["roles", "نقش‌ها"],
    hasPermission(user, adminPermissions.tenantsRead) && ["tenants", "مستاجران"],
    (hasPermission(user, adminPermissions.plansRead) ||
      hasPermission(user, adminPermissions.subscriptionsRead) ||
      hasPermission(user, adminPermissions.usageLedgerRead)) && ["billing", "صورتحساب"],
    (hasPermission(user, adminPermissions.securityAuditRead) ||
      hasPermission(user, adminPermissions.billingAuditRead)) && ["audits", "رویدادها"],
  ].filter(Boolean) as [AdminTab, string][];
  const [tab, setTab] = useState<AdminTab>(tabs[0]?.[0] ?? "users");

  return (
    <div dir="rtl" className="min-h-screen bg-background text-foreground">
      <header className="border-b border-border bg-surface/80 px-6 py-4">
        <div className="mx-auto flex max-w-7xl items-center justify-between gap-4">
          <div className="flex items-center gap-3">
            <div className="rounded-lg bg-emerald-soft p-2 text-emerald">
              <Shield className="size-5" />
            </div>
            <div>
              <h1 className="font-bold">پنل مدیریت</h1>
              <p className="text-xs text-muted-foreground">{user.email}</p>
            </div>
          </div>
          <div className="flex items-center gap-2">
            {hasPermission(user, adminPermissions.dataSyncManage) && (
              <>
                <Button asChild variant="ghost" size="sm">
                  <Link to="/admin/data/">
                    <Database className="size-4 mr-1.5" /> Data Console
                  </Link>
                </Button>
                <Button asChild variant="ghost" size="sm">
                  <Link to="/admin/data/monitor">
                    <Activity className="size-4 mr-1.5" /> پایش زنده
                  </Link>
                </Button>
                <Button asChild variant="ghost" size="sm">
                  <Link to="/admin/data-management/fund-reports">
                    <Database className="size-4 mr-1.5" /> آپلود پرتفوی صندوق
                  </Link>
                </Button>
              </>
            )}
            {(hasPermission(user, adminPermissions.noavaranMonthlyBackfillExecute) ||
              hasPermission(user, adminPermissions.dataSyncManage)) && (
              <Button asChild variant="ghost" size="sm">
                <Link to="/admin/getCompanyId">
                  <Search className="size-4 mr-1.5" /> دریافت تولید و فروش ماهانه
                </Link>
              </Button>
            )}
            <Button asChild variant="outline">
              <Link to="/chat">
                <ArrowRight /> بازگشت به گفتگو
              </Link>
            </Button>
          </div>
        </div>
      </header>
      <main className="mx-auto max-w-7xl p-6">
        <Tabs value={tab} onValueChange={(value) => setTab(value as AdminTab)}>
          <TabsList className="mb-4 h-auto flex-wrap justify-start">
            {tabs.map(([value, label]) => (
              <TabsTrigger key={value} value={value}>
                {label}
              </TabsTrigger>
            ))}
          </TabsList>
          <TabsContent value="users">
            <UsersPanel user={user} />
          </TabsContent>
          <TabsContent value="roles">
            <RolesPanel user={user} />
          </TabsContent>
          <TabsContent value="tenants">
            <TenantsPanel user={user} />
          </TabsContent>
          <TabsContent value="billing">
            <BillingPanel user={user} />
          </TabsContent>
          <TabsContent value="audits">
            <AuditsPanel user={user} />
          </TabsContent>
        </Tabs>
      </main>
    </div>
  );
}

function Panel({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="rounded-xl border border-border bg-surface p-4">
      <h2 className="mb-4 font-semibold">{title}</h2>
      {children}
    </section>
  );
}
function Message({ error, success }: { error?: string | null; success?: string | null }) {
  return (
    <>
      {error && (
        <p className="my-3 rounded border border-destructive/30 bg-destructive/10 p-2 text-sm text-destructive">
          {error}
        </p>
      )}
      {success && (
        <p className="my-3 rounded border border-emerald/30 bg-emerald-soft p-2 text-sm text-emerald">
          {success}
        </p>
      )}
    </>
  );
}
function Loading({ active }: { active: boolean }) {
  return active ? <p className="py-3 text-sm text-muted-foreground">در حال بارگذاری...</p> : null;
}

function UsersPanel({ user }: { user: AuthUser }) {
  const [search, setSearch] = useState("");
  const [users, setUsers] = useState<AdminUser[]>([]);
  const [roles, setRoles] = useState<AdminRole[]>([]);
  const [selected, setSelected] = useState<AdminUser | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const canManage = hasPermission(user, adminPermissions.usersManage);
  const canManageRoles = hasPermission(user, adminPermissions.rolesManage);
  async function load() {
    setLoading(true);
    setError(null);
    try {
      setUsers(await adminApi.users(search));
      if (hasPermission(user, adminPermissions.rolesRead)) setRoles(await adminApi.roles());
    } catch (e) {
      setError(showError(e));
    } finally {
      setLoading(false);
    }
  }
  useEffect(() => {
    void load();
  }, []); // eslint-disable-line react-hooks/exhaustive-deps -- load the initial admin snapshot once
  async function status(isEnabled: boolean, unlock = false) {
    if (!selected) return;
    const reason = askReason(
      unlock ? "رفع قفل کاربر" : isEnabled ? "فعال‌سازی کاربر" : "غیرفعال‌سازی کاربر",
    );
    if (!reason || !window.confirm("این تغییر ثبت و ممیزی می‌شود. ادامه می‌دهید؟")) return;
    try {
      const next = await adminApi.setUserStatus(selected.userId, isEnabled, unlock, reason);
      setSelected(next);
      await load();
      setSuccess("وضعیت کاربر به‌روزرسانی شد.");
    } catch (e) {
      setError(showError(e));
    }
  }
  async function revoke() {
    if (!selected) return;
    const reason = askReason("ابطال نشست‌های فعال");
    if (!reason || !window.confirm("همه نشست‌های فعال این کاربر باطل شوند؟")) return;
    try {
      const count = await adminApi.revokeSessions(selected.userId, reason);
      setSuccess(`${count} نشست باطل شد.`);
    } catch (e) {
      setError(showError(e));
    }
  }
  async function saveRoles() {
    if (!selected) return;
    const chosen = roles
      .filter((role) => window.confirm(`نقش «${role.name}» برای این کاربر فعال باشد؟`))
      .map((role) => role.roleId);
    const reason = askReason("تغییر نقش‌های کاربر");
    if (!reason) return;
    try {
      const next = await adminApi.setUserRoles(selected.userId, chosen, reason);
      setSelected(next);
      setSuccess("نقش‌های کاربر ثبت شد.");
    } catch (e) {
      setError(showError(e));
    }
  }
  return (
    <div className="grid gap-4 lg:grid-cols-[1.2fr_.8fr]">
      <Panel title="جستجوی کاربران">
        <form
          className="mb-3 flex gap-2"
          onSubmit={(e) => {
            e.preventDefault();
            void load();
          }}
        >
          <Input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="جستجو با ایمیل"
          />
          <Button type="submit">جستجو</Button>
        </form>
        <Loading active={loading} />
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>ایمیل</TableHead>
              <TableHead>وضعیت</TableHead>
              <TableHead>نقش‌ها</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {users.map((item) => (
              <TableRow
                key={item.userId}
                className="cursor-pointer"
                onClick={() => setSelected(item)}
              >
                <TableCell>{item.email}</TableCell>
                <TableCell>
                  {item.isEnabled ? "فعال" : "غیرفعال"}
                  {item.isLockedOut ? " / قفل" : ""}
                </TableCell>
                <TableCell>{item.roles.join("، ") || "-"}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
        {!loading && !users.length && (
          <p className="py-3 text-sm text-muted-foreground">کاربری یافت نشد.</p>
        )}
      </Panel>
      <Panel title="جزئیات کاربر">
        <Message error={error} success={success} />
        {selected ? (
          <div className="space-y-3 text-sm">
            <p>{selected.email}</p>
            <p className="text-xs text-muted-foreground mono">{selected.userId}</p>
            <p>نقش‌ها: {selected.roles.join("، ") || "-"}</p>
            <div className="flex flex-wrap gap-2">
              {canManage && (
                <>
                  <Button size="sm" onClick={() => void status(!selected.isEnabled)}>
                    {selected.isEnabled ? "غیرفعال‌سازی" : "فعال‌سازی"}
                  </Button>
                  <Button size="sm" variant="outline" onClick={() => void status(true, true)}>
                    رفع قفل
                  </Button>
                  <Button size="sm" variant="destructive" onClick={() => void revoke()}>
                    ابطال نشست‌ها
                  </Button>
                </>
              )}
              {canManageRoles && (
                <Button size="sm" variant="outline" onClick={() => void saveRoles()}>
                  تنظیم نقش‌ها
                </Button>
              )}
            </div>
          </div>
        ) : (
          <p className="text-sm text-muted-foreground">یک کاربر را انتخاب کنید.</p>
        )}
      </Panel>
    </div>
  );
}

function RolesPanel({ user }: { user: AuthUser }) {
  const [roles, setRoles] = useState<AdminRole[]>([]);
  const [permissions, setPermissions] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
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
    const name = window.prompt("نام نقش جدید:")?.trim();
    const reason = name && askReason("ایجاد نقش");
    if (!name || !reason) return;
    try {
      await adminApi.createRole(name, reason);
      await load();
    } catch (e) {
      setError(showError(e));
    }
  }
  async function toggle(role: AdminRole) {
    const reason = askReason("تغییر وضعیت نقش");
    if (!reason || !window.confirm("ادامه می‌دهید؟")) return;
    try {
      await adminApi.updateRole(role.roleId, role.name, !role.isEnabled, reason);
      await load();
    } catch (e) {
      setError(showError(e));
    }
  }
  async function rename(role: AdminRole) {
    const name = window.prompt("نام نقش:", role.name)?.trim();
    const reason = name && askReason("تغییر نام نقش");
    if (!name || !reason) return;
    try {
      await adminApi.updateRole(role.roleId, name, role.isEnabled, reason);
      await load();
    } catch (e) {
      setError(showError(e));
    }
  }
  async function savePermissions(role: AdminRole) {
    const value = window.prompt("کد مجوزها را با کاما جدا کنید:", role.permissions.join(", "));
    const reason = value != null && askReason("تغییر مجوزهای نقش");
    if (value == null || !reason) return;
    try {
      await adminApi.setRolePermissions(role.roleId, splitCsv(value), reason);
      await load();
    } catch (e) {
      setError(showError(e));
    }
  }
  return (
    <Panel title="نقش‌ها و مجوزها">
      <Message error={error} />
      {manage && (
        <Button className="mb-3" onClick={() => void create()}>
          نقش جدید
        </Button>
      )}
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>نقش</TableHead>
            <TableHead>وضعیت</TableHead>
            <TableHead>مجوزها</TableHead>
            <TableHead />
          </TableRow>
        </TableHeader>
        <TableBody>
          {roles.map((role) => (
            <TableRow key={role.roleId}>
              <TableCell>{role.name}</TableCell>
              <TableCell>{role.isEnabled ? "فعال" : "غیرفعال"}</TableCell>
              <TableCell className="max-w-xl text-xs">
                {role.permissions.join("، ") || "-"}
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
      {permissions.length > 0 && (
        <p className="mt-4 text-xs text-muted-foreground">
          کاتالوگ مجوزها: {permissions.join("، ")}
        </p>
      )}
    </Panel>
  );
}

function TenantsPanel({ user }: { user: AuthUser }) {
  const [tenants, setTenants] = useState<AdminTenant[]>([]);
  const [tenant, setTenant] = useState<AdminTenant | null>(null);
  const [members, setMembers] = useState<AdminTenantMember[]>([]);
  const [error, setError] = useState<string | null>(null);
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
    const id = window.prompt("شناسه کاربر:")?.trim();
    const reason = id && askReason("ثبت عضویت");
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
    const reason = askReason("حذف عضویت");
    if (!reason || !window.confirm("عضویت حذف شود؟")) return;
    try {
      await adminApi.removeTenantMember(tenant.tenantId, member.userId, reason);
      await select(tenant);
    } catch (e) {
      setError(showError(e));
    }
  }
  async function makeDefault(member: AdminTenantMember) {
    if (!tenant) return;
    const reason = askReason("انتخاب مستاجر پیش‌فرض");
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
        {tenants.map((row) => (
          <Button
            key={row.tenantId}
            variant={tenant?.tenantId === row.tenantId ? "default" : "ghost"}
            className="mb-2 w-full justify-start"
            onClick={() => void select(row)}
          >
            {row.name}
          </Button>
        ))}
      </Panel>
      <Panel title="اعضای مستاجر">
        <Message error={error} />
        {manage && (
          <Button className="mb-3" onClick={() => void add()}>
            افزودن عضو
          </Button>
        )}
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>ایمیل</TableHead>
              <TableHead>پیش‌فرض</TableHead>
              <TableHead />
            </TableRow>
          </TableHeader>
          <TableBody>
            {members.map((member) => (
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
      </Panel>
    </div>
  );
}

function BillingPanel({ user }: { user: AuthUser }) {
  const [plans, setPlans] = useState<AdminPlan[]>([]);
  const [customerId, setCustomerId] = useState("");
  const [subscription, setSubscription] = useState<AdminSubscription | null>(null);
  const [ledger, setLedger] = useState<AdminUsageLedger[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [creditKey, setCreditKey] = useState(() => `admin-ui-credit-${crypto.randomUUID()}`);
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
  async function lookup() {
    setError(null);
    try {
      if (hasPermission(user, adminPermissions.subscriptionsRead))
        setSubscription(await adminApi.subscription(customerId));
      if (hasPermission(user, adminPermissions.usageLedgerRead))
        setLedger(await adminApi.ledger(customerId));
    } catch (e) {
      setError(showError(e));
    }
  }
  async function adjust() {
    const amount = Number(window.prompt("مقدار تغییر اعتبار:"));
    const reason = askReason("تغییر اعتبار");
    if (!Number.isFinite(amount) || !reason || !window.confirm("این عملیات مالی ثبت شود؟")) return;
    try {
      const result = await adminApi.adjustCredits(customerId, amount, reason, creditKey);
      setSuccess(`اعتبار ثبت شد. مانده جدید: ${result.updatedBalance}`);
      setCreditKey(`admin-ui-credit-${crypto.randomUUID()}`);
      await lookup();
    } catch (e) {
      setError(showError(e));
    }
  }
  async function changeSubscription() {
    if (!subscription) return;
    const planCode = window.prompt("کد طرح جدید (خالی برای حذف):", subscription.planCode ?? "");
    const reason = planCode != null && askReason("تغییر اشتراک");
    if (planCode == null || !reason) return;
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
  async function publishPlan() {
    const code = window.prompt("کد طرح:")?.trim();
    const name = code && window.prompt("نام طرح:")?.trim();
    const credits = Number(name && window.prompt("اعتبار اولیه:"));
    const version = name && window.prompt("نسخه سیاست قیمت‌گذاری:")?.trim();
    const reason = version && askReason("انتشار طرح");
    if (!code || !name || !Number.isFinite(credits) || !version || !reason) return;
    try {
      await adminApi.publishPlan({
        code,
        name,
        includedCredits: credits,
        pricingPolicyVersion: version,
        reason,
      });
      await loadPlans();
    } catch (e) {
      setError(showError(e));
    }
  }
  async function viewCapabilities(plan: AdminPlan) {
    try {
      const rows = await adminApi.capabilities(plan.code);
      window.alert(
        rows
          .map(
            (row) =>
              `${row.capabilityCode}: ${row.isEnabled ? "فعال" : "غیرفعال"} ${row.limit ?? ""}`,
          )
          .join("\n") || "قابلیتی ثبت نشده است.",
      );
    } catch (e) {
      setError(showError(e));
    }
  }
  async function publishCapabilities(plan: AdminPlan) {
    try {
      const existing = await adminApi.capabilities(plan.code);
      const value = window.prompt(
        "قابلیت‌ها را به صورت JSON ویرایش کنید:",
        JSON.stringify(existing, null, 2),
      );
      const reason = value != null && askReason("انتشار قابلیت‌های طرح");
      if (value == null || !reason) return;
      await adminApi.publishCapabilities(plan.code, JSON.parse(value) as AdminCapability[], reason);
      setSuccess("نسخه قابلیت‌های طرح منتشر شد.");
    } catch (e) {
      setError(showError(e));
    }
  }
  return (
    <div className="space-y-4">
      <Panel title="طرح‌ها">
        <Message error={error} success={success} />
        {hasPermission(user, adminPermissions.plansManage) && (
          <Button className="mb-3" onClick={() => void publishPlan()}>
            انتشار طرح
          </Button>
        )}
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>کد</TableHead>
              <TableHead>نام</TableHead>
              <TableHead>اعتبار</TableHead>
              <TableHead />
            </TableRow>
          </TableHeader>
          <TableBody>
            {plans.map((plan) => (
              <TableRow key={plan.code}>
                <TableCell>{plan.code}</TableCell>
                <TableCell>{plan.name}</TableCell>
                <TableCell>{plan.includedCredits}</TableCell>
                <TableCell>
                  <Button size="sm" variant="outline" onClick={() => void viewCapabilities(plan)}>
                    قابلیت‌ها
                  </Button>
                  {hasPermission(user, adminPermissions.plansManage) && (
                    <Button
                      className="mr-2"
                      size="sm"
                      variant="outline"
                      onClick={() => void publishCapabilities(plan)}
                    >
                      انتشار قابلیت
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Panel>
      <Panel title="حساب مشتری">
        <div className="mb-3 flex gap-2">
          <Input
            dir="ltr"
            value={customerId}
            onChange={(e) => setCustomerId(e.target.value)}
            placeholder="Customer Account ID"
          />
          <Button disabled={!customerId} onClick={() => void lookup()}>
            بازیابی
          </Button>
        </div>
        {subscription && (
          <div className="mb-3 text-sm">
            <p>طرح: {subscription.planCode ?? "-"}</p>
            <p>نسخه: {subscription.revision}</p>
            {hasPermission(user, adminPermissions.subscriptionsManage) && (
              <Button className="mt-2" size="sm" onClick={() => void changeSubscription()}>
                تغییر اشتراک
              </Button>
            )}
          </div>
        )}
        {hasPermission(user, adminPermissions.creditsAdjust) && customerId && (
          <Button className="mb-3" variant="outline" onClick={() => void adjust()}>
            تغییر اعتبار
          </Button>
        )}
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>زمان</TableHead>
              <TableHead>عملیات</TableHead>
              <TableHead>اعتبار</TableHead>
              <TableHead>وضعیت</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {ledger.map((row) => (
              <TableRow key={row.id}>
                <TableCell>{new Date(row.occurredAt).toLocaleString("fa-IR")}</TableCell>
                <TableCell>{row.operationCode}</TableCell>
                <TableCell>{row.credits}</TableCell>
                <TableCell>{row.completionStatus ?? row.entryType}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Panel>
    </div>
  );
}

function AuditsPanel({ user }: { user: AuthUser }) {
  const [security, setSecurity] = useState<AdminAudit[]>([]);
  const [billing, setBilling] = useState<AdminAudit[]>([]);
  const [error, setError] = useState<string | null>(null);
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
      <div className="mb-3 flex justify-between">
        <Message error={error} />
        <Button size="sm" variant="outline" onClick={() => void load()}>
          <RefreshCw /> تازه‌سازی
        </Button>
      </div>
      <AuditTable
        rows={[...security, ...billing].sort((a, b) => b.occurredAt.localeCompare(a.occurredAt))}
      />
    </Panel>
  );
}
function AuditTable({ rows }: { rows: AdminAudit[] }) {
  return (
    <Table>
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
  );
}
