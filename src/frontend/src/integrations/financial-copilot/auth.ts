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

export async function register(email: string, password: string) {
  return applySession(await authRequest("/api/auth/v1/register", { email, password }));
}

export async function login(email: string, password: string) {
  return applySession(await authRequest("/api/auth/v1/login", { email, password }));
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
  const response = await fetch(apiUrl(path), {
    method: "POST",
    credentials: "include",
    headers: body ? { "Content-Type": "application/json" } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new FinancialCopilotAuthError(
      problem?.detail ?? problem?.title ?? "Authentication request failed.",
      response.status,
      problem?.type,
      problem?.correlationId,
    );
  }
  return response.json();
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

export function apiUrl(path: string) {
  return buildFinancialCopilotApiUrl(path, import.meta.env.VITE_FINANCIAL_COPILOT_API_BASE_URL);
}
