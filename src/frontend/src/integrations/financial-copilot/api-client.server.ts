import { createMiddleware } from "@tanstack/react-start";
import { getRequest } from "@tanstack/react-start/server";
import { buildFinancialCopilotApiUrl } from "./api-url";
import { formatSubscriptionCooldownDeniedMessage } from "@/lib/subscription-cooldown";

type FinancialCopilotServerContext = {
  authorization: string;
  correlationId: string;
};

/**
 * A non-success API response. The message is unchanged from earlier releases, so callers that only read
 * `message` behave as before. Server functions that need a stable failure code read `status` and `problemCode`
 * here, because TanStack serializes only an error's message to the browser.
 */
export class FinancialCopilotApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly problemCode: string | null,
  ) {
    super(message);
    this.name = "FinancialCopilotApiError";
  }
}

export const requireFinancialCopilotAuth = createMiddleware({ type: "function" }).server(
  async ({ next }) => {
    const request = getRequest();
    const authorization = request.headers.get("authorization");
    if (!authorization?.startsWith("Bearer ")) {
      throw new Error("Unauthorized: FinancialCopilot bearer token is required.");
    }

    return next({
      context: {
        authorization,
        correlationId: request.headers.get("x-correlation-id") ?? crypto.randomUUID(),
      },
    });
  },
);

export async function financialCopilotServerApi<T>(
  context: FinancialCopilotServerContext,
  path: string,
  init: RequestInit = {},
  allowNotFound = false,
): Promise<T> {
  const headers = new Headers(init.headers);
  headers.set("Authorization", context.authorization);
  headers.set("X-Correlation-Id", context.correlationId);
  if (init.body) headers.set("Content-Type", "application/json");

  const response = await fetch(apiUrl(path), { ...init, headers });
  if (response.status === 404 && allowNotFound) return null as T;
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    const code = typeof problem?.code === "string" ? problem.code : problem?.title;
    const problemCode = typeof code === "string" ? code : null;
    if (code === "SUBSCRIPTION_COOLDOWN_ACTIVE") {
      throw new FinancialCopilotApiError(
        formatSubscriptionCooldownDeniedMessage(
          typeof problem?.cooldownUntilUtc === "string" ? problem.cooldownUntilUtc : null,
          typeof problem?.freePlanCredits === "number" ? problem.freePlanCredits : null,
        ),
        response.status,
        problemCode,
      );
    }
    throw new FinancialCopilotApiError(
      problem?.detail ??
        problem?.title ??
        `FinancialCopilot API request failed (${response.status}).`,
      response.status,
      problemCode,
    );
  }

  return response.status === 204 ? (undefined as T) : response.json();
}

function apiUrl(path: string) {
  // TanStack Start runs through Wrangler/workerd in production. That runtime
  // does not reliably expose Docker variables through process.env, so keep
  // the runtime override but fall back to the build-time Vite value.
  const processEnv = (globalThis as typeof globalThis & {
    process?: { env?: Record<string, string | undefined> };
  }).process?.env;
  const baseUrl =
    processEnv?.FINANCIAL_COPILOT_API_BASE_URL ??
    processEnv?.VITE_FINANCIAL_COPILOT_INTERNAL_API_BASE_URL ??
    import.meta.env.VITE_FINANCIAL_COPILOT_INTERNAL_API_BASE_URL ??
    import.meta.env.VITE_FINANCIAL_COPILOT_API_BASE_URL;
  return buildFinancialCopilotApiUrl(path, baseUrl);
}
