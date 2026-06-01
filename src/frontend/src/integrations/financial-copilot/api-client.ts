import { apiUrl, getAccessToken } from "./auth";

export class FinancialCopilotApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly type?: string,
    public readonly correlationId?: string,
  ) {
    super(message);
  }
}

export async function financialCopilotApi<T>(path: string, init: RequestInit = {}): Promise<T> {
  const token = await getAccessToken();
  const headers = new Headers(init.headers);
  if (token) headers.set("Authorization", `Bearer ${token}`);
  headers.set("X-Correlation-Id", crypto.randomUUID());

  const response = await fetch(apiUrl(path), {
    ...init,
    credentials: "include",
    headers,
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new FinancialCopilotApiError(
      problem?.detail ?? problem?.title ?? "FinancialCopilot API request failed.",
      response.status,
      problem?.type,
      problem?.correlationId,
    );
  }
  return response.status === 204 ? (undefined as T) : response.json();
}
