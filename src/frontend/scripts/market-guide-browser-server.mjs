import { once } from "node:events";
import { createMockApi } from "./mobile-workspace/mock-api.mjs";

export async function startMockApi() {
  const encode = (value) => Buffer.from(JSON.stringify(value)).toString("base64url");
  const token = [
    encode({ alg: "none", typ: "JWT" }),
    encode({
      sub: "market-guide-test",
      email: "tester@example.com",
      exp: Math.floor(Date.now() / 1000) + 3600,
    }),
    "signature",
  ].join(".");
  const mock = createMockApi({ loginToken: token });
  const requests = [];
  mock.server.on("request", ({ method, url }) => {
    const path = new URL(url, "http://mock.local").pathname;
    requests.push({ method, path });
    if (method === "POST" && path === "/api/ai/v1/query") mock.server.emit("market-guide-query");
  });
  mock.server.listen(0, "127.0.0.1");
  await once(mock.server, "listening");
  process.env.VITE_FINANCIAL_COPILOT_API_BASE_URL = `http://127.0.0.1:${mock.server.address().port}`;
  return { mock, token, requests };
}

export async function createViteServer() {
  const { createServer } = await import("vite");
  const vite = await createServer({ server: { host: "127.0.0.1", port: 0 } });
  await vite.listen();
  return vite;
}
