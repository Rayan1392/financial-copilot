// In-memory stand-in for the .NET API, used only by the Feature 153 mobile browser checks.
// It serves the endpoints the mobile workspace reuses, with fixture data and a few scripted failures:
//   - a first question containing «شبیه‌سازی خطا» answers 503 without creating anything (indeterminate case);
//   - a follow-up containing «ثبت و قطع» persists the exchange and then answers 502, as when a phone loses the
//     connection after the server finished (the client must reconcile and show the answer).
import { createServer } from "node:http";
import { randomUUID } from "node:crypto";

const DAY = 24 * 60 * 60 * 1000;

function assistantContent(text, extra = {}) {
  return {
    intent: "Unknown",
    clarificationRequired: false,
    textAnswer: text,
    usage: { creditsCharged: 1 },
    explainableAnswer: {
      filterChips: [],
      dataCitations: [],
      confidence: { score: 0.9 },
      suggestedFollowUpQuestions: ["گزارش فصلی کگهر"],
      explanationText: text,
    },
    ...extra,
  };
}

const WIDE_MARKDOWN = [
  "روند فروش ماهانه کگهر در ۱۲ ماه اخیر:",
  "",
  "| ماه | فروش (میلیون ریال) | تغییر نسبت به ماه قبل | تغییر نسبت به سال قبل | میانگین ۱۲ ماهه | سهم از کل |",
  "| --- | --- | --- | --- | --- | --- |",
  "| فروردین | 12,345,678 | +4.2% | +18.9% | 11,002,334 | 7.9% |",
  "| اردیبهشت | 13,045,111 | +5.7% | +21.3% | 11,250,876 | 8.3% |",
].join("\n");

const SCANNER_TABLE = {
  columns: ["symbol", "pe", "ps", "pb", "roe", "sales", "growth", "dividend"].map((identifier) => ({
    identifier,
    displayName: identifier.toUpperCase(),
  })),
  rows: ["کگهر", "فولاد", "شپنا"].map((symbolCode, index) => ({
    symbolCode,
    companyName: `${symbolCode} نمونه`,
    score: 90 - index,
    cells: Object.fromEntries(
      ["symbol", "pe", "ps", "pb", "roe", "sales", "growth", "dividend"].map((id) => [
        id,
        { formattedValue: id === "symbol" ? symbolCode : "1,234.56", freshnessStatus: "Fresh" },
      ]),
    ),
  })),
  executionFacts: {
    matchingSymbolCount: 3,
    totalSymbolsEvaluated: 400,
    fromCache: false,
    page: 1,
    pageSize: 20,
    totalPages: 1,
  },
  missingDataWarnings: [],
};

export function createMockApi(options = {}) {
  const conversations = new Map();
  const now = Date.now();

  function addConversation(title, updatedAt, messages = []) {
    const id = randomUUID();
    conversations.set(id, { id, title, startedAt: updatedAt, updatedAt, messages });
    return id;
  }

  const richId = addConversation("روند فروش ماهانه کگهر", new Date(now - 60_000).toISOString(), [
    {
      messageId: randomUUID(),
      role: "User",
      content: "روند فروش ماهانه کگهر را نشان بده",
      createdAt: new Date(now - 70_000).toISOString(),
    },
    {
      messageId: randomUUID(),
      role: "Assistant",
      content: WIDE_MARKDOWN,
      createdAt: new Date(now - 65_000).toISOString(),
      assistantContent: assistantContent(WIDE_MARKDOWN, { scannerTable: SCANNER_TABLE }),
    },
  ]);
  for (let index = 1; index <= 104; index += 1) {
    addConversation(`گفتگوی شماره ${index}`, new Date(now - index * 0.6 * DAY).toISOString());
  }

  const followed = [
    {
      externalCompanyId: "E1",
      symbol: "فولاد",
      companyName: "فولاد مبارکه اصفهان",
      followedAtUtc: new Date(now).toISOString(),
    },
    {
      externalCompanyId: "E2",
      symbol: "شپنا",
      companyName: "پالایش نفت اصفهان",
      followedAtUtc: new Date(now).toISOString(),
    },
  ];

  function exchange(conversation, message) {
    const createdAt = new Date().toISOString();
    const user = { messageId: randomUUID(), role: "User", content: message, createdAt };
    const answer = `پاسخ آزمایشی به «${message}»`;
    const assistant = {
      messageId: randomUUID(),
      role: "Assistant",
      content: answer,
      createdAt,
      assistantContent: assistantContent(answer),
    };
    conversation.messages.push(user, assistant);
    conversation.updatedAt = createdAt;
    return { user, assistant, answer };
  }

  async function route(method, url, body) {
    const path = url.pathname;
    if (path === "/api/ai/v1/conversations" && method === "GET") {
      const limit = Number(url.searchParams.get("limit") ?? 20);
      const rows = [...conversations.values()]
        .sort((a, b) => b.updatedAt.localeCompare(a.updatedAt))
        .slice(0, limit)
        .map(({ id, title, startedAt, updatedAt }) => ({
          conversationId: id,
          title,
          startedAt,
          updatedAt,
        }));
      return [200, rows];
    }
    const messagesMatch = path.match(/^\/api\/ai\/v1\/conversations\/([^/]+)\/messages$/);
    if (messagesMatch && method === "GET") {
      const conversation = conversations.get(messagesMatch[1]);
      return conversation
        ? [200, { messages: conversation.messages }]
        : [404, { title: "Not Found", status: 404 }];
    }
    const conversationMatch = path.match(/^\/api\/ai\/v1\/conversations\/([^/]+)$/);
    if (conversationMatch && method === "DELETE") {
      return conversations.delete(conversationMatch[1])
        ? [204, undefined]
        : [404, { title: "Not Found" }];
    }
    if (path === "/api/ai/v1/query" && method === "POST") {
      await new Promise((resolve) => setTimeout(resolve, 400));
      const { conversationId, message } = body;
      if (!conversationId && message.includes("شبیه‌سازی خطا")) {
        return [
          503,
          {
            title: "temporarily-unavailable",
            detail: "The AI capability is temporarily unavailable.",
          },
        ];
      }
      let conversation = conversationId ? conversations.get(conversationId) : null;
      if (conversationId && !conversation) return [404, { title: "Not Found" }];
      if (!conversation) {
        const id = addConversation(message.slice(0, 60), new Date().toISOString());
        conversation = conversations.get(id);
      }
      const { user, assistant, answer } = exchange(conversation, message);
      if (message.includes("ثبت و قطع"))
        return [502, { title: "provider-failure", detail: "Simulated loss." }];
      return [
        200,
        {
          conversationId: conversation.id,
          messageId: user.messageId,
          assistantMessageId: assistant.messageId,
          ...assistantContent(answer),
        },
      ];
    }
    if (path === "/api/v1/market/summary") {
      const asOf = new Date(now - 120_000).toISOString();
      const indices = [
        "شاخص کل",
        "شاخص هم‌وزن",
        "شاخص قیمت",
        "شاخص بازار اول",
        "شاخص بازار دوم",
        "شاخص فرابورس",
      ].map((name, index) => ({
        symbol: `IDX${index}`,
        name,
        value: 2_415_300 - index * 210_000,
        changePercent: index % 2 === 0 ? 1.24 - index * 0.1 : -0.31 * index,
        asOf,
        sourceKind: "Live",
      }));
      const mover = (symbol, changePercent, isStale = false) => ({
        symbol,
        name: `${symbol} نمونه`,
        latestPrice: 5420,
        changePercent,
        asOf,
        isStale,
      });
      return [
        200,
        {
          indices,
          topGainers: [mover("شپنا", 5), mover("کگهر", 4.2), mover("وبملت", 3.9, true)],
          topLosers: [mover("فولاد", -4.8), mover("فملی", -3.1), mover("خودرو", -2.2)],
          asOf,
        },
      ];
    }
    if (path === "/api/v1/watchlists/me") {
      const asOf = new Date(now - 60_000).toISOString();
      return [
        200,
        {
          symbols: [
            { symbol: "شپنا", latestPrice: 5420, changePercent: 5, asOf, isStale: false },
            { symbol: "فولاد", latestPrice: 8110, changePercent: -4.8, asOf, isStale: true },
          ].filter((quote) => followed.some((item) => item.symbol === quote.symbol)),
          asOf,
        },
      ];
    }
    if (path === "/api/v1/followed-symbols/me" && method === "GET")
      return [200, { symbols: followed }];
    const followMatch = path.match(/^\/api\/v1\/followed-symbols\/me\/(.+)$/);
    if (followMatch && method === "DELETE") {
      const index = followed.findIndex(
        (item) => item.externalCompanyId === decodeURIComponent(followMatch[1]),
      );
      if (index >= 0) followed.splice(index, 1);
      return [200, { symbols: followed }];
    }
    if (followMatch && method === "POST") {
      const id = decodeURIComponent(followMatch[1]);
      if (id === "E400") {
        return [
          400,
          { title: "validation", detail: "سقف نمادهای قابل دنبال کردن در اشتراک شما پر شده است." },
        ];
      }
      const item = {
        externalCompanyId: id,
        symbol: id === "E3" ? "کگهر" : id,
        companyName: "شرکت نمونه",
        followedAtUtc: new Date().toISOString(),
      };
      if (!followed.some((existing) => existing.externalCompanyId === id)) followed.push(item);
      return [200, item];
    }
    if (path === "/api/v1/market/external-company-id") {
      return url.searchParams.get("symbol") === "کگهر"
        ? [200, "E3"]
        : [404, { title: "Not Found" }];
    }
    if (path === "/api/ai/v1/metadata/symbols") {
      return [
        200,
        [
          { externalCompanyId: "E3", symbolCode: "کگهر", companyName: "سنگ آهن گهر زمین" },
          { externalCompanyId: "E400", symbolCode: "کگل", companyName: "معدنی گل‌گهر" },
        ].filter((item) => item.symbolCode.includes(url.searchParams.get("search") ?? "")),
      ];
    }
    if (path === "/api/v1/usage/me") {
      return [
        200,
        {
          balance: 1200,
          reservedCredits: 0,
          availableSpendingCapacity: 1200,
          walletUpdatedAt: new Date(now).toISOString(),
          planCode: "sapio_pro",
          planName: "حرفه‌ای",
          planIncludedCredits: 2600,
          creditQuotaMode: "Finite",
        },
      ];
    }
    if (path === "/api/auth/v1/profile") {
      return [
        200,
        {
          displayName: "کاربر آزمایشی",
          firstName: null,
          lastName: null,
          nationalIdMasked: null,
          mobileMasked: null,
          profileSyncedAtUtc: null,
        },
      ];
    }
    if (path === "/api/auth/v1/account") {
      return [
        200,
        {
          email: "tester@example.com",
          emailConfirmed: true,
          hasPassword: true,
          hasTahlilAppIdentity: false,
          accountAuthState: "C",
        },
      ];
    }
    if (path === "/api/auth/v1/login" && method === "POST") {
      return [
        200,
        {
          accessToken: options.loginToken,
          accessTokenExpiresAt: new Date(now + 3600_000).toISOString(),
          user: {},
        },
      ];
    }
    if (path === "/api/auth/v1/refresh" || path === "/api/auth/v1/logout")
      return [401, { title: "Unauthorized" }];
    return [404, { title: "Not Found" }];
  }

  const server = createServer(async (request, response) => {
    const origin = request.headers.origin;
    if (origin) {
      response.setHeader("Access-Control-Allow-Origin", origin);
      response.setHeader("Access-Control-Allow-Credentials", "true");
      response.setHeader(
        "Access-Control-Allow-Headers",
        "Authorization, Content-Type, X-Correlation-Id, Accept",
      );
      response.setHeader("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
    }
    if (request.method === "OPTIONS") {
      response.writeHead(204).end();
      return;
    }
    const chunks = [];
    for await (const chunk of request) chunks.push(chunk);
    const raw = Buffer.concat(chunks).toString("utf8");
    const url = new URL(request.url, "http://mock.local");
    const [status, payload] = await route(request.method, url, raw ? JSON.parse(raw) : {});
    if (payload === undefined) {
      response.writeHead(status).end();
      return;
    }
    response.writeHead(status, { "Content-Type": "application/json; charset=utf-8" });
    response.end(JSON.stringify(payload));
  });

  return { server, richConversationId: richId, conversations };
}
