import { financialCopilotApi } from "./api-client";

/** Announcement kinds a Codal alert can watch (backend CodalAnnouncementType). */
export type CodalAnnouncementType = "FinancialStatement" | "MonthlyActivity";

export const CODAL_ANNOUNCEMENT_TYPES: readonly CodalAnnouncementType[] = [
  "FinancialStatement",
  "MonthlyActivity",
];

export type CodalAlertSubscription = {
  id: string;
  externalCompanyId: string;
  symbol: string;
  companyName: string;
  announcementTypes: CodalAnnouncementType[];
  minimumImportance: string;
  rawAlertEnabled: boolean;
  aiSummaryEnabled: boolean;
  state: "Active" | "Paused";
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type CodalAlertSettings = {
  announcementTypes: CodalAnnouncementType[];
  minimumImportance: string;
  rawAlertEnabled: boolean;
  aiSummaryEnabled: boolean;
};

const BASE = "/api/v1/codal-alerts/me/subscriptions";
const json = (body: unknown): RequestInit => ({
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify(body),
});

export async function getMyCodalAlerts() {
  return financialCopilotApi<{ items: CodalAlertSubscription[] }>(BASE);
}

export async function createCodalAlert(externalCompanyId: string, settings: CodalAlertSettings) {
  return financialCopilotApi<CodalAlertSubscription>(BASE, {
    method: "POST",
    ...json({ externalCompanyId, ...settings }),
  });
}

export async function updateCodalAlert(
  id: string,
  settings: CodalAlertSettings & { state: CodalAlertSubscription["state"] },
) {
  return financialCopilotApi<CodalAlertSubscription>(`${BASE}/${encodeURIComponent(id)}`, {
    method: "PUT",
    ...json(settings),
  });
}

export async function deleteCodalAlert(id: string) {
  return financialCopilotApi<void>(`${BASE}/${encodeURIComponent(id)}`, { method: "DELETE" });
}
