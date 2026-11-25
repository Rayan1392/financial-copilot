import { financialCopilotApi } from "./api-client";

export type MessagePage<T> = { items: T[]; nextCursor: string | null };
export type MessageFilter = "all" | "unread" | "pending";
export type ConversationSummary = {
  conversationId: string;
  subject: string;
  latestActivityUtc: string;
  unread: boolean;
  repliesEnabled: boolean;
  pendingSinceUtc: string | null;
  recipientLabel: string | null;
  recipientUserId: string | null;
  supportStatus?: "waitingForSupport" | "answered" | null;
};
export type ConversationMessage = {
  id: string;
  fromStaff: boolean;
  body: string;
  sentAtUtc: string;
  readAtUtc: string | null;
};
export type ConversationDetail = {
  conversation: ConversationSummary;
  revision: number;
  messages: MessagePage<ConversationMessage>;
};

const inbox = "/api/v1/in-app-messages/me";
const queue = "/api/v1/admin/in-app-messages/support";
const scope = (staff: boolean) => (staff ? queue : inbox);

function request<T>(path: string, init: RequestInit = {}) {
  return financialCopilotApi<T>(path, { ...init, cache: "no-store" });
}

function write<T = void>(path: string, body: object, method = "POST") {
  return request<T>(path, {
    method,
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
}

export const messageApi = {
  summary: (signal: AbortSignal) =>
    request<{ unreadCount: number }>(`${inbox}/summary`, { signal }),
  list: (staff: boolean, filter: MessageFilter, cursor: string | null, signal: AbortSignal) => {
    const params = new URLSearchParams({ filter });
    if (cursor) params.set("cursor", cursor);
    return request<MessagePage<ConversationSummary>>(`${scope(staff)}?${params}`, { signal });
  },
  detail: (staff: boolean, id: string, cursor: string | null, signal: AbortSignal) => {
    const params = cursor ? `?${new URLSearchParams({ cursor })}` : "";
    return request<ConversationDetail>(`${scope(staff)}/${encodeURIComponent(id)}${params}`, {
      signal,
    });
  },
  start: (
    userId: string,
    body: { subject: string; body: string; repliesEnabled: boolean; idempotencyKey: string },
  ) => write(`/api/v1/admin/users/${encodeURIComponent(userId)}/in-app-messages`, body),
  startSupport: (body: { subject: string; body: string; idempotencyKey: string }) =>
    write<{ conversationId: string; messageId: string; sentAtUtc: string }>(inbox, body),
  reply: (
    staff: boolean,
    id: string,
    body: string,
    idempotencyKey: string,
    expectedRevision?: number,
  ) =>
    write(`${scope(staff)}/${encodeURIComponent(id)}/replies`, {
      body,
      idempotencyKey,
      expectedRevision,
    }),
  read: (id: string, messageIds: string[]) =>
    write(`${inbox}/${encodeURIComponent(id)}/read`, { messageIds }, "PUT"),
};
