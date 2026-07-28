import { financialCopilotApi } from "./api-client";

export type TelegramLinkPreview = {
  maskedTelegramUserId: string;
  username?: string | null;
  expiresAtUtc: string;
};

export type TelegramLinkChallenge = {
  deepLink: string;
  expiresAtUtc: string;
  correlationId: string;
};

export type TelegramLinkView = {
  telegramUserId: number;
  telegramChatId: number;
  username?: string | null;
  linkedAtUtc: string;
  lastVerifiedAtUtc: string;
};

export type TelegramMembershipVerification = {
  status: number;
  isEligible: boolean;
  verifiedAtUtc: string;
  validUntilUtc: string;
  channelId: string;
  correlationId: string;
  failureCategory: number;
  actions?: TelegramInlineAction[];
};

export type TelegramInlineAction = {
  kind: string;
  label: string;
  url?: string | null;
  callbackData?: string | null;
  isPrimary: boolean;
};

export type TelegramDailyFreeAllowance = {
  allowanceDateKey: string;
  policyVersion: string;
  totalCredits: number;
  usedCredits: number;
  remainingCredits: number;
  expiresAtUtc: string;
};

export type TelegramEntitlement = {
  link?: TelegramLinkView | null;
  membership?: TelegramMembershipVerification | null;
  freeDailyAllowance: TelegramDailyFreeAllowance;
  paidAvailableSpendingCapacity: number;
  consumptionOrder: string;
  nextAction: string;
  actions: TelegramInlineAction[];
  generatedAtUtc: string;
};

export async function createTelegramLinkChallenge() {
  return financialCopilotApi<TelegramLinkChallenge>("/api/v1/telegram/link-token", { method: "POST" });
}

export async function getTelegramLink() {
  return financialCopilotApi<TelegramLinkView>("/api/v1/telegram/link/me");
}

export async function unlinkTelegram() {
  return financialCopilotApi<void>("/api/v1/telegram/link/me", { method: "DELETE" });
}

export async function verifyTelegramMembership() {
  return financialCopilotApi<TelegramMembershipVerification>("/api/v1/telegram/membership/verify", { method: "POST" });
}

export async function getTelegramEntitlement() {
  return financialCopilotApi<TelegramEntitlement>("/api/v1/telegram/entitlement/me");
}

export async function previewTelegramLink(token: string) {
  return financialCopilotApi<TelegramLinkPreview>("/api/v1/telegram/link/web-preview", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ token }),
  });
}

export async function confirmTelegramLink(token: string) {
  return financialCopilotApi<void>("/api/v1/telegram/link/web-confirm", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ token }),
  });
}
