import { createFileRoute, Link } from "@tanstack/react-router";
import { useEffect, useRef, useState, type FormEvent } from "react";
import {
  FinancialCopilotAuthError,
  getAccountSummary,
  getOwnProfile,
  requestEmailEnrollment,
  revealOwnProfileField,
  setAccountPassword,
  type AccountAuthState,
  type AccountSummary,
  type OwnProfile,
  type ProfileRevealField,
} from "@/integrations/financial-copilot/auth";

export const Route = createFileRoute("/_app/profile")({
  head: () => ({ meta: [{ title: "پروفایل حساب — ساپیو - دستیار هوشمند بازار" }] }),
  component: ProfilePage,
});

const ACCOUNT_STATE_LABELS: Record<AccountAuthState, string> = {
  A: "ورود فقط از طریق تحلیل‌اپ",
  B: "ایمیل تایید شد؛ گذرواژه تعیین نشده",
  C: "ورود مستقیم فعال است",
  D: "حساب ایمیلی",
};

const SESSION_EXPIRED_TEXT = "نشست شما معتبر نیست. دوباره وارد شوید.";
const GENERIC_FAILURE_TEXT = "درخواست انجام نشد. کمی بعد دوباره تلاش کنید.";

type Notice = { tone: "success" | "error"; text: string; signIn?: boolean };

function ProfilePage() {
  const [summary, setSummary] = useState<AccountSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadFailure, setLoadFailure] = useState<"session" | "generic" | null>(null);

  useEffect(() => {
    let active = true;
    getAccountSummary()
      .then((value) => {
        if (!active) return;
        setSummary(value);
        setLoadFailure(null);
      })
      .catch((error: unknown) => {
        if (active) setLoadFailure(statusOf(error) === 401 ? "session" : "generic");
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, []);

  // Keeps the page mounted so per-card state (for example a revealed value or a success notice) survives updates.
  function markPasswordSet() {
    setSummary((current) => (current ? { ...current, hasPassword: true } : current));
  }

  return (
    <main dir="rtl" className="mx-auto w-full min-w-0 max-w-2xl p-4 text-right sm:p-6">
      <h1 className="text-lg font-bold text-foreground">پروفایل حساب</h1>

      {summary === null ? (
        <AccountLoadState loading={loading} failure={loadFailure} />
      ) : (
        <div className="mt-5 space-y-4">
          <AccountSummaryCard summary={summary} />
          {summary.hasTahlilAppIdentity ? <TahlilAppProfileCard /> : null}
          {!summary.emailConfirmed ? <EmailEnrollmentCard /> : null}
          <PasswordCard
            emailConfirmed={summary.emailConfirmed}
            hasPassword={summary.hasPassword}
            onPasswordSet={markPasswordSet}
          />
        </div>
      )}
    </main>
  );
}

function AccountLoadState({ loading, failure }: { loading: boolean; failure: "session" | "generic" | null }) {
  if (loading) {
    return <p role="status" className="mt-5 text-sm text-muted-foreground">در حال بارگذاری اطلاعات حساب…</p>;
  }
  if (failure === "session") {
    return (
      <div className="mt-5 space-y-3">
        <p role="alert" className="text-sm text-destructive">{SESSION_EXPIRED_TEXT}</p>
        <Link to="/auth" search={{ redirect: "/profile" }} className="block text-sm text-emerald">
          ورود به حساب
        </Link>
      </div>
    );
  }
  return <p role="alert" className="mt-5 text-sm text-destructive">اطلاعات حساب در دسترس نیست. کمی بعد دوباره تلاش کنید.</p>;
}

function AccountSummaryCard({ summary }: { summary: AccountSummary }) {
  const emailStatus = summary.email === null ? null : summary.emailConfirmed ? "تایید شده" : "تایید نشده";
  return (
    <section aria-labelledby="account-state-heading" className="rounded-2xl border border-border bg-surface p-5">
      <h2 id="account-state-heading" className="text-base font-semibold">وضعیت حساب</h2>
      <dl className="mt-3 grid grid-cols-1 gap-4 text-sm sm:grid-cols-2">
        <div className="min-w-0">
          <dt className="text-xs text-muted-foreground">نحوه ورود</dt>
          <dd className="mt-1 break-words">{ACCOUNT_STATE_LABELS[summary.accountAuthState] ?? "—"}</dd>
        </div>
        <div className="min-w-0">
          <dt className="text-xs text-muted-foreground">نشانی ایمیل</dt>
          <dd className="mt-1 break-all" dir="ltr">
            {summary.email ?? "ثبت نشده"}
            {emailStatus ? <span className="mr-2 text-xs text-muted-foreground" dir="rtl">({emailStatus})</span> : null}
          </dd>
        </div>
      </dl>
    </section>
  );
}

function TahlilAppProfileCard() {
  const [profile, setProfile] = useState<OwnProfile | null>(null);
  const [loadState, setLoadState] = useState<"loading" | "ready" | "error">("loading");
  // Revealed values live only in this component's state for the current page session.
  const [revealed, setRevealed] = useState<Partial<Record<ProfileRevealField, string>>>({});
  const [pendingField, setPendingField] = useState<ProfileRevealField | null>(null);
  const [notice, setNotice] = useState<Notice | null>(null);

  useEffect(() => {
    let active = true;
    getOwnProfile()
      .then((value) => {
        if (!active) return;
        setProfile(value);
        setLoadState("ready");
      })
      .catch(() => {
        if (active) setLoadState("error");
      });
    return () => {
      active = false;
    };
  }, []);

  async function toggleReveal(field: ProfileRevealField) {
    if (revealed[field] !== undefined) {
      setRevealed((current) => {
        const next = { ...current };
        delete next[field];
        return next;
      });
      return;
    }
    setPendingField(field);
    setNotice(null);
    try {
      const result = await revealOwnProfileField(field);
      setRevealed((current) => ({ ...current, [field]: result.value }));
    } catch (error) {
      setNotice(
        statusOf(error) === 401
          ? { tone: "error", text: SESSION_EXPIRED_TEXT, signIn: true }
          : { tone: "error", text: "نمایش این مقدار انجام نشد. کمی بعد دوباره تلاش کنید." },
      );
    } finally {
      setPendingField(null);
    }
  }

  const maskedFields: { field: ProfileRevealField; label: string; masked: string }[] = profile
    ? [
        { field: "nationalId", label: "کد ملی", masked: profile.nationalIdMasked },
        { field: "mobile", label: "شماره همراه", masked: profile.mobileMasked },
      ]
    : [];

  return (
    <section aria-labelledby="tahlilapp-profile-heading" className="rounded-2xl border border-border bg-surface p-5">
      <h2 id="tahlilapp-profile-heading" className="text-base font-semibold">اطلاعات از تحلیل‌اپ</h2>

      {loadState === "loading" ? <p role="status" className="mt-3 text-sm text-muted-foreground">در حال بارگذاری…</p> : null}
      {loadState === "error" ? (
        <p role="alert" className="mt-3 text-sm text-destructive">اطلاعات پروفایل در دسترس نیست.</p>
      ) : null}

      {profile ? (
        <>
          <dl className="mt-3 grid grid-cols-1 gap-4 text-sm sm:grid-cols-2">
            <div className="min-w-0">
              <dt className="text-xs text-muted-foreground">نام و نام خانوادگی</dt>
              <dd className="mt-1 break-words">{`${profile.firstName} ${profile.lastName}`.trim() || "—"}</dd>
            </div>
          </dl>
          <ul className="mt-4">
            {maskedFields.map(({ field, label, masked }) => {
              const isRevealed = revealed[field] !== undefined;
              return (
                <li
                  key={field}
                  className="flex flex-col gap-2 border-t border-border py-3 sm:flex-row sm:items-center sm:justify-between"
                >
                  <div className="min-w-0">
                    <p className="text-xs text-muted-foreground">{label}</p>
                    <p dir="ltr" className="mt-1 break-all font-mono text-sm">
                      {isRevealed ? revealed[field] : masked}
                    </p>
                  </div>
                  <button
                    type="button"
                    onClick={() => void toggleReveal(field)}
                    disabled={pendingField !== null}
                    aria-label={`${isRevealed ? "پنهان کردن" : "نمایش کامل"} ${label}`}
                    className="min-h-10 w-full shrink-0 rounded-lg border border-input px-4 text-sm text-foreground hover:bg-background disabled:opacity-50 sm:w-auto"
                  >
                    {pendingField === field ? "…" : isRevealed ? "پنهان کردن" : "نمایش کامل"}
                  </button>
                </li>
              );
            })}
          </ul>
        </>
      ) : null}

      <StatusMessage notice={notice} />
    </section>
  );
}

function EmailEnrollmentCard() {
  const [email, setEmail] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [notice, setNotice] = useState<Notice | null>(null);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSubmitting(true);
    setNotice(null);
    try {
      await requestEmailEnrollment(email.trim());
      // Generic on purpose: the response never reveals whether the address is already registered.
      setNotice({
        tone: "success",
        text: "در صورت امکان استفاده از این نشانی، پیام تایید ارسال شد. پوشه‌های ناخواسته را نیز بررسی کنید.",
      });
    } catch (error) {
      setNotice({ tone: "error", ...enrollmentFailure(error) });
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <section aria-labelledby="email-enrollment-heading" className="rounded-2xl border border-border bg-surface p-5">
      <h2 id="email-enrollment-heading" className="text-base font-semibold">تایید نشانی ایمیل</h2>
      <p className="mt-1 text-xs text-muted-foreground">
        با ثبت ایمیل، پیوند تایید به آن ارسال می‌شود. پس از تایید می‌توانید گذرواژه تعیین کنید.
      </p>
      <form onSubmit={submit} className="mt-4 space-y-3">
        <div>
          <label htmlFor="enrollment-email" className="mb-1.5 block text-xs text-muted-foreground">
            نشانی ایمیل
          </label>
          <input
            id="enrollment-email"
            type="email"
            dir="ltr"
            required
            autoComplete="email"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            className="w-full min-w-0 rounded-lg border border-input bg-background px-3 py-2.5 text-sm focus:outline-none focus:ring-2 focus:ring-ring"
          />
        </div>
        <button
          type="submit"
          disabled={submitting}
          className="w-full rounded-lg bg-emerald py-2.5 text-sm font-semibold text-primary-foreground disabled:opacity-50 sm:w-auto sm:px-6"
        >
          {submitting ? "در حال ارسال…" : "ارسال پیوند تایید"}
        </button>
      </form>
      <StatusMessage notice={notice} />
    </section>
  );
}

function PasswordCard({
  emailConfirmed,
  hasPassword,
  onPasswordSet,
}: {
  emailConfirmed: boolean;
  hasPassword: boolean;
  onPasswordSet: () => void;
}) {
  const [password, setPassword] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [notice, setNotice] = useState<Notice | null>(null);

  if (!emailConfirmed && !hasPassword) return null;

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (password !== confirmation) {
      setNotice({ tone: "error", text: "گذرواژه‌ها یکسان نیستند." });
      return;
    }
    setSubmitting(true);
    setNotice(null);
    try {
      await setAccountPassword(password);
      setPassword("");
      setConfirmation("");
      setNotice({ tone: "success", text: "گذرواژه تعیین شد. اکنون می‌توانید با ایمیل و گذرواژه وارد شوید." });
      onPasswordSet();
    } catch (error) {
      if (problemIs(error, "password-already-set")) onPasswordSet();
      setNotice({ tone: "error", ...passwordFailure(error) });
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <section aria-labelledby="password-heading" className="rounded-2xl border border-border bg-surface p-5">
      <h2 id="password-heading" className="text-base font-semibold">گذرواژه</h2>

      {hasPassword ? (
        <div className="mt-3 space-y-3">
          <p className="text-sm text-muted-foreground">گذرواژه این حساب تعیین شده است.</p>
          <Link to="/forgot-password" className="block text-sm text-emerald">
            بازیابی یا تغییر گذرواژه
          </Link>
        </div>
      ) : (
        <form onSubmit={submit} className="mt-4 space-y-3">
          <div>
            <label htmlFor="new-password" className="mb-1.5 block text-xs text-muted-foreground">
              گذرواژه جدید
            </label>
            <input
              id="new-password"
              type="password"
              dir="ltr"
              required
              minLength={6}
              autoComplete="new-password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              className="w-full min-w-0 rounded-lg border border-input bg-background px-3 py-2.5 text-sm focus:outline-none focus:ring-2 focus:ring-ring"
            />
          </div>
          <div>
            <label htmlFor="repeat-password" className="mb-1.5 block text-xs text-muted-foreground">
              تکرار گذرواژه
            </label>
            <input
              id="repeat-password"
              type="password"
              dir="ltr"
              required
              minLength={6}
              autoComplete="new-password"
              value={confirmation}
              onChange={(event) => setConfirmation(event.target.value)}
              className="w-full min-w-0 rounded-lg border border-input bg-background px-3 py-2.5 text-sm focus:outline-none focus:ring-2 focus:ring-ring"
            />
          </div>
          <button
            type="submit"
            disabled={submitting}
            className="w-full rounded-lg bg-emerald py-2.5 text-sm font-semibold text-primary-foreground disabled:opacity-50 sm:w-auto sm:px-6"
          >
            {submitting ? "در حال ثبت…" : "تعیین گذرواژه"}
          </button>
        </form>
      )}

      <StatusMessage notice={notice} />
    </section>
  );
}

/** Polite live region; error notices take focus so keyboard and screen-reader users land on the problem. */
function StatusMessage({ notice }: { notice: Notice | null }) {
  const ref = useRef<HTMLParagraphElement>(null);
  useEffect(() => {
    if (notice?.tone === "error") ref.current?.focus();
  }, [notice]);

  return (
    <div aria-live="polite" className="mt-3">
      {notice ? (
        <p
          ref={ref}
          tabIndex={-1}
          className={`text-sm focus:outline-none ${notice.tone === "error" ? "text-destructive" : "text-foreground"}`}
        >
          {notice.text}
          {notice.signIn ? (
            <>
              {" "}
              <Link to="/auth" search={{ redirect: "/profile" }} className="text-emerald underline">
                ورود به حساب
              </Link>
            </>
          ) : null}
        </p>
      ) : null}
    </div>
  );
}

function enrollmentFailure(error: unknown): { text: string; signIn?: boolean } {
  switch (statusOf(error)) {
    case 503:
      return { text: "ارسال ایمیل در حال حاضر در دسترس نیست." };
    case 400:
      return { text: "نشانی ایمیل معتبر نیست." };
    case 401:
      return { text: SESSION_EXPIRED_TEXT, signIn: true };
    default:
      return { text: GENERIC_FAILURE_TEXT };
  }
}

function passwordFailure(error: unknown): { text: string; signIn?: boolean } {
  if (problemIs(error, "password-rejected")) return { text: "گذرواژه شرایط لازم را ندارد. گذرواژه دیگری انتخاب کنید." };
  if (problemIs(error, "password-already-set")) return { text: "گذرواژه قبلاً برای این حساب تعیین شده است." };
  if (problemIs(error, "email-not-confirmed")) return { text: "ابتدا نشانی ایمیل خود را تایید کنید." };
  if (statusOf(error) === 401) return { text: SESSION_EXPIRED_TEXT, signIn: true };
  return { text: "تعیین گذرواژه انجام نشد. کمی بعد دوباره تلاش کنید." };
}

function statusOf(error: unknown): number | null {
  return error instanceof FinancialCopilotAuthError ? error.status : null;
}

function problemIs(error: unknown, name: string): boolean {
  return error instanceof FinancialCopilotAuthError && error.type !== undefined && error.type.endsWith(`/${name}`);
}
