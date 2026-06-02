export const DEFAULT_FINANCIAL_COPILOT_API_BASE_URL = "http://localhost:5074";

export function buildFinancialCopilotApiUrl(
  path: string,
  configuredBaseUrl?: string,
  fallbackBaseUrl = DEFAULT_FINANCIAL_COPILOT_API_BASE_URL,
) {
  if (!path.startsWith("/")) {
    throw new Error("FinancialCopilot API paths must start with '/'.");
  }

  const baseUrl = configuredBaseUrl?.trim() || fallbackBaseUrl;
  let parsed: URL;
  try {
    parsed = new URL(baseUrl);
  } catch {
    throw new Error(`Invalid FinancialCopilot API base URL: ${baseUrl}`);
  }

  if (
    (parsed.protocol !== "http:" && parsed.protocol !== "https:") ||
    parsed.pathname !== "/" ||
    parsed.search ||
    parsed.hash ||
    parsed.username ||
    parsed.password
  ) {
    throw new Error(`Invalid FinancialCopilot API base URL: ${baseUrl}`);
  }

  return `${parsed.origin}${path}`;
}
