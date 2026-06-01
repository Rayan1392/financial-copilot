import { createMiddleware } from "@tanstack/react-start";
import { getAccessToken } from "./auth";

export const attachFinancialCopilotAuth = createMiddleware({ type: "function" }).client(
  async ({ next }) => {
    const token = await getAccessToken();
    return next({
      headers: token ? { Authorization: `Bearer ${token}` } : {},
    });
  },
);
