import assert from "node:assert/strict";

import {
  DEFAULT_FINANCIAL_COPILOT_API_BASE_URL,
  buildFinancialCopilotApiUrl,
} from "../src/integrations/financial-copilot/api-url.ts";

assert.equal(DEFAULT_FINANCIAL_COPILOT_API_BASE_URL, "http://localhost:5074");
assert.equal(
  buildFinancialCopilotApiUrl("/api/auth/v1/register"),
  "http://localhost:5074/api/auth/v1/register",
);
assert.equal(
  buildFinancialCopilotApiUrl("/api/auth/v1/login", "http://localhost:5074/"),
  "http://localhost:5074/api/auth/v1/login",
);
assert.equal(
  buildFinancialCopilotApiUrl("/api/v1/usage/me", " https://api.example.test "),
  "https://api.example.test/api/v1/usage/me",
);
assert.throws(
  () => buildFinancialCopilotApiUrl("/api/auth/v1/register", "not-a-url"),
  /Invalid FinancialCopilot API base URL/,
);
assert.throws(
  () => buildFinancialCopilotApiUrl("/api/auth/v1/register", "http://localhost:5074/base"),
  /Invalid FinancialCopilot API base URL/,
);
assert.throws(() => buildFinancialCopilotApiUrl("api/auth/v1/register"), /must start with '\/'/);

console.log("FinancialCopilot API URL tests passed.");
