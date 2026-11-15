import { buildFinancialCopilotApiUrl } from "./api-url";

const ACCESS_TOKEN_KEY = "financial_copilot_access_token";
const AUTH_CHANGED_EVENT = "financial-copilot-auth-changed";

export type AuthUser = {
  userId: string;
  email: string;
  tenantId: string;
  roles: string[];
  permissions: string[];
};

type AuthSession = {
  accessToken: string;
  accessTokenExpiresAt: string;
  user: AuthUser;
};

export class FinancialCopilotAuthError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly type?: string,
    public readonly correlationId?: string,
  ) {
    super(message);
  }
}

let accessToken: string | null = null;
let refreshPromise: Promise<string | null> | null = null;

export async function login(email: string, password: string) {
  return applySession(await authRequest("/api/auth/v1/login", { email, password }));
}

/**
 * Exchanges a one-time TahlilApp launch code for a Sapino session (Feature 139, Design §7).
 * The code is sent only in the request body; the caller must have removed it from the address bar first.
 */
export async function completeTahlilAppLaunch(code: string) {
  return applySession(await authRequest("/api/auth/v1/tahlilapp/launch", { code }));
}

export async function verifyEmail(token: string) {
  await authJsonRequest<void>("/api/auth/v1/email/verify", { token });
}

export async function resendVerification(email: string) {
  return authJsonRequest<{ message: string }>("/api/auth/v1/email/resend", { email });
}

export async function requestPasswordReset(email: string) {
  return authJsonRequest<{ message: string }>("/api/auth/v1/password/forgot", { email });
}

export async function resetPassword(token: string, newPassword: string) {
  await authJsonRequest<void>("/api/auth/v1/password/reset", { token, newPassword });
}

export async function logout() {
  try {
    await fetch(apiUrl("/api/auth/v1/logout"), {
      method: "POST",
      credentials: "include",
    });
  } finally {
    clearSession();
  }
}

export async function getAccessToken(): Promise<string | null> {
  const token = accessToken ?? readStoredToken();
  if (token && !isExpiring(token)) {
    accessToken = token;
    return token;
  }

  if (!refreshPromise) {
    refreshPromise = refreshAccessToken().finally(() => {
      refreshPromise = null;
    });
  }
  return refreshPromise;
}

export async function isAuthenticated() {
  return (await getAccessToken()) !== null;
}

export async function getAuthenticatedUser(): Promise<AuthUser | null> {
  const token = await getAccessToken();
  return token ? readUserFromToken(token) : null;
}

export function getStoredAuthenticatedUser(): AuthUser | null {
  const token = accessToken ?? readStoredToken();
  return token ? readUserFromToken(token) : null;
}

export function subscribeToAuthChanges(listener: () => void) {
  window.addEventListener(AUTH_CHANGED_EVENT, listener);
  return () => window.removeEventListener(AUTH_CHANGED_EVENT, listener);
}

async function refreshAccessToken() {
  try {
    return applySession(await authRequest("/api/auth/v1/refresh"));
  } catch {
    clearSession();
    return null;
  }
}

async function authRequest(path: string, body?: unknown): Promise<AuthSession> {
  return authJsonRequest<AuthSession>(path, body);
}

async function authJsonRequest<T>(path: string, body?: unknown): Promise<T> {
  const response = await fetch(apiUrl(path), {
    method: "POST",
    credentials: "include",
    headers: body ? { "Content-Type": "application/json" } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new FinancialCopilotAuthError(
      authProblemMessage(problem, "Authentication request failed."),
      response.status,
      problem?.type,
      problem?.correlationId,
    );
  }
  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}

function applySession(session: AuthSession) {
  accessToken = session.accessToken;
  if (typeof window !== "undefined") {
    sessionStorage.setItem(ACCESS_TOKEN_KEY, session.accessToken);
    window.dispatchEvent(new Event(AUTH_CHANGED_EVENT));
  }
  return session.accessToken;
}

function clearSession() {
  accessToken = null;
  if (typeof window !== "undefined") {
    sessionStorage.removeItem(ACCESS_TOKEN_KEY);
    window.dispatchEvent(new Event(AUTH_CHANGED_EVENT));
  }
}

function authProblemMessage(problem: unknown, fallback: string) {
  if (!problem || typeof problem !== "object") return fallback;

  const details = problem as {
    type?: unknown;
    detail?: unknown;
    title?: unknown;
  };

  if (details.type === "https://financialcopilot/errors/invalid-credentials") {
    return "ایمیل یا گذرواژه واردشده نادرست است.";
  }

  if (typeof details.detail === "string" && details.detail.trim()) return details.detail;
  if (typeof details.title === "string" && details.title.trim()) return details.title;
  return fallback;
}

function readStoredToken() {
  return typeof window === "undefined" ? null : sessionStorage.getItem(ACCESS_TOKEN_KEY);
}

function isExpiring(token: string) {
  try {
    const payload = decodeJwtPayload(token);
    return typeof payload.exp !== "number" || payload.exp * 1000 <= Date.now() + 30_000;
  } catch {
    return true;
  }
}

function readUserFromToken(token: string): AuthUser | null {
  try {
    const payload = decodeJwtPayload(token);
    const userId = stringClaim(payload, "sub");
    const tenantId = stringClaim(payload, "financial_copilot:tenant_id");
    if (!userId || !tenantId) return null;
    return {
      userId,
      tenantId,
      email: stringClaim(payload, "email") ?? "",
      roles: stringArrayClaim(payload, "role"),
      permissions: stringArrayClaim(payload, "financial_copilot:permission"),
    };
  } catch {
    return null;
  }
}

function decodeJwtPayload(token: string): Record<string, unknown> {
  const encoded = token.split(".")[1]?.replace(/-/g, "+").replace(/_/g, "/");
  if (!encoded) throw new Error("Invalid JWT payload.");
  return JSON.parse(atob(encoded.padEnd(Math.ceil(encoded.length / 4) * 4, "="))) as Record<
    string,
    unknown
  >;
}

function stringClaim(payload: Record<string, unknown>, key: string) {
  return typeof payload[key] === "string" ? payload[key] : null;
}

function stringArrayClaim(payload: Record<string, unknown>, key: string) {
  const value = payload[key];
  if (typeof value === "string") return [value];
  return Array.isArray(value)
    ? value.filter((item): item is string => typeof item === "string")
    : [];
}

// Account, credential, and profile endpoints for the signed-in user (Feature 139, Design §22).

export type AccountAuthState = "A" | "B" | "C" | "D";

export type AccountSummary = {
  email: string | null;
  emailConfirmed: boolean;
  hasPassword: boolean;
  hasTahlilAppIdentity: boolean;
  accountAuthState: AccountAuthState;
};

/** Every profile field is optional on the server and arrives as JSON null when TahlilApp never supplied it. */
export type OwnProfile = {
  displayName: string | null;
  firstName: string | null;
  lastName: string | null;
  nationalIdMasked: string | null;
  mobileMasked: string | null;
  profileSyncedAtUtc: string | null;
  contactMobileMasked: string | null;
  avatarAvailable: boolean;
};

export type ProfileRevealField = "nationalId" | "mobile";

export type ProfileRevealResult = {
  field: ProfileRevealField;
  value: string;
};

export async function getAccountSummary(): Promise<AccountSummary> {
  return authorizedRequest<AccountSummary>("GET", "/api/auth/v1/account");
}

export async function requestEmailEnrollment(email: string): Promise<{ message: string }> {
  return authorizedRequest<{ message: string }>("POST", "/api/auth/v1/credentials/email", {
    email,
  });
}

export async function confirmEmailEnrollment(token: string): Promise<void> {
  await authorizedRequest<void>("POST", "/api/auth/v1/credentials/email/confirm", { token });
}

export async function requestEmailChange(email: string): Promise<void> {
  await authorizedRequest<void>("POST", "/api/auth/v1/credentials/email/change", { email });
}

export async function confirmEmailChange(token: string): Promise<void> {
  await authJsonRequest<void>("/api/auth/v1/credentials/email/change/confirm", { token });
}

export async function setAccountPassword(newPassword: string): Promise<void> {
  await authorizedRequest<void>("POST", "/api/auth/v1/credentials/password", { newPassword });
}

export async function changeAccountPassword(
  currentPassword: string,
  newPassword: string,
): Promise<void> {
  await authorizedRequest<void>("POST", "/api/auth/v1/credentials/password/change", {
    currentPassword,
    newPassword,
  });
}

export async function getOwnProfile(): Promise<OwnProfile> {
  return authorizedRequest<OwnProfile>("GET", "/api/auth/v1/profile");
}

/** Returns one unmasked TahlilApp field. The caller must keep it in memory only and never log or persist it. */
export async function revealOwnProfileField(
  field: ProfileRevealField,
): Promise<ProfileRevealResult> {
  return authorizedRequest<ProfileRevealResult>("POST", "/api/auth/v1/profile/reveal", { field });
}

export async function updateOwnProfile(firstName: string, lastName: string): Promise<void> {
  await authorizedRequest<void>("PUT", "/api/auth/v1/profile", { firstName, lastName });
}

export async function requestOwnMobileCode(mobile: string): Promise<void> {
  await authorizedRequest<void>("POST", "/api/auth/v1/profile/mobile/request", { mobile });
}

export async function confirmOwnMobileCode(code: string): Promise<void> {
  await authorizedRequest<void>("POST", "/api/auth/v1/profile/mobile/confirm", { code });
}

export async function uploadOwnAvatar(file: File): Promise<void> {
  const form = new FormData();
  form.append("file", file);
  await authorizedRequest<void>("PUT", "/api/auth/v1/profile/avatar", form);
}

export async function removeOwnAvatar(): Promise<void> {
  await authorizedRequest<void>("DELETE", "/api/auth/v1/profile/avatar");
}

export async function getOwnAvatarObjectUrl(): Promise<string | null> {
  const token = await getAccessToken();
  if (!token) throw new FinancialCopilotAuthError("Sign-in required.", 401);
  const response = await fetch(apiUrl("/api/auth/v1/profile/avatar"), {
    method: "GET",
    credentials: "include",
    cache: "no-store",
    headers: { Authorization: `Bearer ${token}`, Accept: "image/jpeg" },
  });
  if (response.status === 404) return null;
  if (!response.ok)
    throw new FinancialCopilotAuthError("Could not load profile photo.", response.status);
  return URL.createObjectURL(await response.blob());
}

/**
 * Bearer-authorized JSON request. Uses the same access-token path as the rest of this module
 * (refresh when the token is missing or about to expire) and reports failures as FinancialCopilotAuthError.
 */
async function authorizedRequest<T>(
  method: "GET" | "POST" | "PUT" | "DELETE",
  path: string,
  body?: unknown,
): Promise<T> {
  const token = await getAccessToken();
  if (!token) throw new FinancialCopilotAuthError("Sign-in required.", 401);

  const headers: Record<string, string> = {
    Accept: "application/json",
    Authorization: `Bearer ${token}`,
    "X-Correlation-Id": crypto.randomUUID(),
  };
  const isMultipart = typeof FormData !== "undefined" && body instanceof FormData;
  if (body !== undefined && !isMultipart) headers["Content-Type"] = "application/json";

  const response = await fetch(apiUrl(path), {
    method,
    credentials: "include",
    cache: "no-store",
    headers,
    body: body === undefined ? undefined : isMultipart ? body : JSON.stringify(body),
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new FinancialCopilotAuthError(
      authProblemMessage(problem, "Account request failed."),
      response.status,
      problem?.type,
      problem?.correlationId,
    );
  }
  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}

export function apiUrl(path: string) {
  return buildFinancialCopilotApiUrl(path, import.meta.env.VITE_FINANCIAL_COPILOT_API_BASE_URL);
}
