import { useEffect, useState, type FormEvent } from "react";
import {
  FinancialCopilotAuthError,
  changeAccountPassword,
  confirmOwnMobileCode,
  getOwnAvatarObjectUrl,
  getOwnProfile,
  logout,
  removeOwnAvatar,
  requestEmailChange,
  requestOwnMobileCode,
  updateOwnProfile,
  uploadOwnAvatar,
  type AccountSummary,
  type OwnProfile,
} from "@/integrations/financial-copilot/auth";

type Notice = { tone: "success" | "error"; text: string };

export function ProfileEditor({
  summary,
  signInRedirect,
}: {
  summary: AccountSummary;
  signInRedirect: string;
}) {
  const [profile, setProfile] = useState<OwnProfile | null>(null);
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [email, setEmail] = useState(summary.email ?? "");
  const [mobile, setMobile] = useState("");
  const [otp, setOtp] = useState("");
  const [avatarUrl, setAvatarUrl] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>("load");
  const [notice, setNotice] = useState<Notice | null>(null);

  useEffect(() => {
    let active = true;
    let objectUrl: string | null = null;
    Promise.all([getOwnProfile(), getOwnAvatarObjectUrl()])
      .then(([value, avatar]) => {
        if (!active) return;
        setProfile(value);
        setFirstName(value.firstName ?? "");
        setLastName(value.lastName ?? "");
        setAvatarUrl(avatar);
        objectUrl = avatar;
      })
      .catch(() => {
        if (active)
          setNotice({ tone: "error", text: "بارگذاری اطلاعات ویرایش پروفایل انجام نشد." });
      })
      .finally(() => {
        if (active) setBusy(null);
      });
    return () => {
      active = false;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, []);

  async function saveNames(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy("names");
    setNotice(null);
    try {
      await updateOwnProfile(firstName.trim(), lastName.trim());
      setProfile(await getOwnProfile());
      setNotice({ tone: "success", text: "نام و نام خانوادگی ذخیره شد." });
    } catch (error) {
      setNotice({ tone: "error", text: failureText(error) });
    } finally {
      setBusy(null);
    }
  }

  async function changeEmail(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy("email");
    setNotice(null);
    try {
      await requestEmailChange(email.trim());
      setNotice({
        tone: "success",
        text: "اگر نشانی قابل استفاده باشد، پیوند تأیید برای آن ارسال می‌شود. ایمیل فعلی تا زمان تأیید فعال می‌ماند.",
      });
    } catch (error) {
      setNotice({ tone: "error", text: failureText(error) });
    } finally {
      setBusy(null);
    }
  }

  async function sendMobileCode(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy("mobile");
    setNotice(null);
    try {
      await requestOwnMobileCode(mobile.trim());
      setOtp("");
      setNotice({ tone: "success", text: "در صورت امکان، کد تأیید پیامکی ارسال شد." });
    } catch (error) {
      setNotice({ tone: "error", text: failureText(error) });
    } finally {
      setBusy(null);
    }
  }

  async function verifyMobile(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy("confirm-mobile");
    setNotice(null);
    try {
      await confirmOwnMobileCode(otp.trim());
      setProfile(await getOwnProfile());
      setMobile("");
      setOtp("");
      setNotice({ tone: "success", text: "شمارهٔ همراه تأیید و ذخیره شد." });
    } catch (error) {
      setNotice({ tone: "error", text: failureText(error) });
    } finally {
      setBusy(null);
    }
  }

  async function saveAvatar(file: File | undefined) {
    if (!file) return;
    setBusy("avatar");
    setNotice(null);
    try {
      await uploadOwnAvatar(file);
      const nextUrl = await getOwnAvatarObjectUrl();
      setAvatarUrl((previous) => {
        if (previous) URL.revokeObjectURL(previous);
        return nextUrl;
      });
      setProfile(await getOwnProfile());
      setNotice({ tone: "success", text: "عکس پروفایل به‌روزرسانی شد." });
    } catch (error) {
      setNotice({ tone: "error", text: failureText(error) });
    } finally {
      setBusy(null);
    }
  }

  async function deleteAvatar() {
    setBusy("avatar");
    setNotice(null);
    try {
      await removeOwnAvatar();
      setAvatarUrl((previous) => {
        if (previous) URL.revokeObjectURL(previous);
        return null;
      });
      setProfile(await getOwnProfile());
      setNotice({ tone: "success", text: "عکس پروفایل حذف شد." });
    } catch (error) {
      setNotice({ tone: "error", text: failureText(error) });
    } finally {
      setBusy(null);
    }
  }

  async function changePassword(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const current = String(form.get("currentPassword") ?? "");
    const next = String(form.get("newPassword") ?? "");
    const repeat = String(form.get("repeatPassword") ?? "");
    if (next !== repeat) {
      setNotice({ tone: "error", text: "رمز عبور جدید و تکرار آن یکسان نیستند." });
      return;
    }
    setBusy("password");
    setNotice(null);
    try {
      await changeAccountPassword(current, next);
      await logout();
      window.location.assign(`/auth?redirect=${encodeURIComponent(signInRedirect)}`);
    } catch (error) {
      setNotice({ tone: "error", text: failureText(error) });
    } finally {
      setBusy(null);
    }
  }

  const disabled = busy !== null;
  return (
    <section aria-labelledby="profile-editor-heading" className="space-y-4">
      <h2 id="profile-editor-heading" className="sr-only">
        ویرایش پروفایل
      </h2>
      {notice ? (
        <p
          role={notice.tone === "error" ? "alert" : "status"}
          className={
            notice.tone === "error"
              ? "rounded-xl bg-destructive/10 p-3 text-sm text-destructive"
              : "rounded-xl bg-emerald-soft/50 p-3 text-sm text-emerald"
          }
        >
          {notice.text}
        </p>
      ) : null}

      <section
        aria-labelledby="profile-photo-heading"
        className="rounded-2xl border border-border bg-surface p-5"
      >
        <h3 id="profile-photo-heading" className="text-base font-semibold">
          عکس پروفایل
        </h3>
        <div className="mt-4 flex flex-wrap items-center gap-4">
          <div
            className="flex size-16 items-center justify-center overflow-hidden rounded-full bg-background text-sm text-muted-foreground"
            aria-label="پیش‌نمایش عکس پروفایل"
          >
            {avatarUrl ? (
              <img src={avatarUrl} alt="عکس پروفایل" className="size-full object-cover" />
            ) : (
              "بدون عکس"
            )}
          </div>
          <label className="min-h-11 cursor-pointer rounded-lg border border-input px-4 py-2.5 text-sm hover:bg-background">
            انتخاب عکس
            <input
              className="sr-only"
              type="file"
              accept="image/jpeg,image/png,image/webp"
              disabled={disabled}
              onChange={(event) => void saveAvatar(event.currentTarget.files?.[0])}
            />
          </label>
          {profile?.avatarAvailable ? (
            <button
              type="button"
              disabled={disabled}
              onClick={() => void deleteAvatar()}
              className="min-h-11 rounded-lg border border-input px-4 py-2.5 text-sm disabled:opacity-50"
            >
              حذف عکس
            </button>
          ) : null}
          <p className="basis-full text-xs text-muted-foreground">
            JPEG، PNG یا WebP؛ حداکثر ۵ مگابایت.
          </p>
        </div>
      </section>

      <form
        onSubmit={saveNames}
        className="space-y-3 rounded-2xl border border-border bg-surface p-5"
      >
        <h3 className="text-base font-semibold">نام و نام خانوادگی</h3>
        <div className="grid gap-3 sm:grid-cols-2">
          <label className="block text-xs text-muted-foreground">
            نام
            <input
              required
              maxLength={100}
              autoComplete="given-name"
              value={firstName}
              onChange={(event) => setFirstName(event.target.value)}
              className="mt-1.5 min-h-11 w-full rounded-lg border border-input bg-background px-3 text-sm text-foreground"
            />
          </label>
          <label className="block text-xs text-muted-foreground">
            نام خانوادگی
            <input
              required
              maxLength={100}
              autoComplete="family-name"
              value={lastName}
              onChange={(event) => setLastName(event.target.value)}
              className="mt-1.5 min-h-11 w-full rounded-lg border border-input bg-background px-3 text-sm text-foreground"
            />
          </label>
        </div>
        <button
          type="submit"
          disabled={disabled || busy === "load"}
          className="min-h-11 rounded-lg bg-emerald px-5 text-sm font-semibold text-primary-foreground disabled:opacity-50"
        >
          ذخیره نام
        </button>
      </form>

      {summary.email ? (
        <form
          onSubmit={changeEmail}
          className="space-y-3 rounded-2xl border border-border bg-surface p-5"
        >
          <h3 className="text-base font-semibold">تغییر ایمیل</h3>
          <p className="text-xs text-muted-foreground">
            ایمیل فعلی: <bdi dir="ltr">{summary.email}</bdi>. ایمیل جدید پس از تأیید جایگزین می‌شود.
          </p>
          <label className="block text-xs text-muted-foreground">
            ایمیل جدید
            <input
              required
              type="email"
              dir="ltr"
              autoComplete="email"
              maxLength={320}
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              className="mt-1.5 min-h-11 w-full rounded-lg border border-input bg-background px-3 text-sm text-foreground"
            />
          </label>
          <button
            type="submit"
            disabled={disabled}
            className="min-h-11 rounded-lg border border-input px-5 text-sm disabled:opacity-50"
          >
            ارسال پیوند تأیید
          </button>
        </form>
      ) : null}

      <section className="space-y-3 rounded-2xl border border-border bg-surface p-5">
        <h3 className="text-base font-semibold">شمارهٔ همراه</h3>
        <p className="text-xs text-muted-foreground">
          شمارهٔ تأییدشده: <bdi dir="ltr">{profile?.contactMobileMasked ?? "ثبت نشده"}</bdi>
        </p>
        <form onSubmit={sendMobileCode} className="flex flex-col gap-3 sm:flex-row">
          <label className="min-w-0 flex-1 text-xs text-muted-foreground">
            شمارهٔ همراه جدید
            <input
              required
              type="tel"
              inputMode="tel"
              autoComplete="tel"
              dir="ltr"
              maxLength={32}
              value={mobile}
              onChange={(event) => setMobile(event.target.value)}
              placeholder="0912…"
              className="mt-1.5 min-h-11 w-full rounded-lg border border-input bg-background px-3 text-sm text-foreground"
            />
          </label>
          <button
            type="submit"
            disabled={disabled}
            className="min-h-11 self-end rounded-lg border border-input px-5 text-sm disabled:opacity-50"
          >
            ارسال کد پیامکی
          </button>
        </form>
        <form onSubmit={verifyMobile} className="flex flex-col gap-3 sm:flex-row">
          <label className="min-w-0 flex-1 text-xs text-muted-foreground">
            کد تأیید
            <input
              required
              inputMode="numeric"
              autoComplete="one-time-code"
              dir="ltr"
              maxLength={10}
              value={otp}
              onChange={(event) => setOtp(event.target.value)}
              className="mt-1.5 min-h-11 w-full rounded-lg border border-input bg-background px-3 text-sm text-foreground"
            />
          </label>
          <button
            type="submit"
            disabled={disabled || otp.length < 4}
            className="min-h-11 self-end rounded-lg border border-input px-5 text-sm disabled:opacity-50"
          >
            تأیید شماره
          </button>
        </form>
      </section>

      {summary.hasPassword ? (
        <form
          onSubmit={changePassword}
          className="space-y-3 rounded-2xl border border-border bg-surface p-5"
        >
          <h3 className="text-base font-semibold">تغییر رمز عبور</h3>
          <label className="block text-xs text-muted-foreground">
            رمز فعلی
            <input
              required
              type="password"
              name="currentPassword"
              autoComplete="current-password"
              className="mt-1.5 min-h-11 w-full rounded-lg border border-input bg-background px-3 text-sm text-foreground"
            />
          </label>
          <label className="block text-xs text-muted-foreground">
            رمز جدید
            <input
              required
              type="password"
              name="newPassword"
              autoComplete="new-password"
              className="mt-1.5 min-h-11 w-full rounded-lg border border-input bg-background px-3 text-sm text-foreground"
            />
          </label>
          <label className="block text-xs text-muted-foreground">
            تکرار رمز جدید
            <input
              required
              type="password"
              name="repeatPassword"
              autoComplete="new-password"
              className="mt-1.5 min-h-11 w-full rounded-lg border border-input bg-background px-3 text-sm text-foreground"
            />
          </label>
          <p className="text-xs text-muted-foreground">
            پس از تغییر رمز، نشست‌های فعال بسته می‌شوند و باید دوباره وارد شوید.
          </p>
          <button
            type="submit"
            disabled={disabled}
            className="min-h-11 rounded-lg border border-input px-5 text-sm disabled:opacity-50"
          >
            تغییر رمز
          </button>
        </form>
      ) : null}
    </section>
  );
}

function failureText(error: unknown): string {
  if (error instanceof FinancialCopilotAuthError) {
    if (error.status === 401) return "نشست شما پایان یافته است؛ دوباره وارد شوید.";
    if (error.status === 429) return "برای درخواست دوباره کمی صبر کنید.";
    if (error.status === 503) return "این سرویس موقتاً در دسترس نیست.";
    if (error.type?.endsWith("/current-password-invalid")) return "رمز فعلی درست نیست.";
    if (error.type?.endsWith("/mobile-code-invalid")) return "کد تأیید نادرست یا منقضی شده است.";
    if (error.type?.endsWith("/mobile-already-in-use"))
      return "این شمارهٔ همراه برای حساب دیگری ثبت شده است.";
    if (error.type?.endsWith("/avatar-too-large")) return "حجم عکس پردازش‌شده بیش از حد مجاز است.";
  }
  return "درخواست انجام نشد. اطلاعات را بررسی کنید و دوباره تلاش کنید.";
}
