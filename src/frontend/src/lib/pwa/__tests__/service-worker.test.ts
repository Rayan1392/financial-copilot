import { readFileSync } from "node:fs";
import path from "node:path";
import vm from "node:vm";
import { describe, expect, it, vi } from "vitest";

/** Loads a public/ worker script the way the browser would not: as plain JavaScript exposing its functions. */
function loadWorkerScript(file: string, scope: Record<string, unknown> = {}) {
  const source = readFileSync(path.resolve(__dirname, "../../../../public", file), "utf8");
  const module = { exports: {} as Record<string, unknown> };
  vm.runInNewContext(source, {
    module,
    URL,
    self: {
      location: { href: "https://sapioai.ir/sw.js?v=build42", origin: "https://sapioai.ir" },
    },
    ...scope,
  });
  return module.exports;
}

const worker = loadWorkerScript("sw.js") as {
  routeRequest: (
    request: { method: string; url: string; mode?: string },
    scopeOrigin: string,
  ) => string;
  isCacheable: (response: {
    ok: boolean;
    status: number;
    type: string;
    headers: Headers;
  }) => boolean;
  STATIC_CACHE: string;
  PRECACHE_PATHS: string[];
};
const ORIGIN = "https://sapioai.ir";
const route = (url: string, init: { method?: string; mode?: string } = {}) =>
  worker.routeRequest({ method: init.method ?? "GET", url, mode: init.mode ?? "cors" }, ORIGIN);

describe("service worker routing (spec §9.3, §9.7)", () => {
  it("never handles server functions, API paths, or the API origin", () => {
    expect(route(`${ORIGIN}/_serverFn/abc?payload=1`)).toBe("passthrough");
    expect(route(`${ORIGIN}/_serverFn/abc`, { method: "POST" })).toBe("passthrough");
    expect(route(`${ORIGIN}/api/auth/v1/refresh`)).toBe("passthrough");
    expect(route("https://api.sapioai.ir/api/v1/usage/me")).toBe("passthrough");
    expect(route("https://api.sapioai.ir/api/auth/v1/refresh", { method: "POST" })).toBe(
      "passthrough",
    );
  });

  it("serves navigations from the network with only the offline fallback", () => {
    expect(route(`${ORIGIN}/m`, { mode: "navigate" })).toBe("navigation");
    expect(route(`${ORIGIN}/launch?code=one-time`, { mode: "navigate" })).toBe("navigation");
  });

  it("caches hashed assets and the precached static files only", () => {
    expect(route(`${ORIGIN}/assets/index-abc123.js`)).toBe("static");
    expect(route(`${ORIGIN}/icons/icon-192.png`)).toBe("static");
    expect(route(`${ORIGIN}/offline.html`)).toBe("static");
    expect(route(`${ORIGIN}/icons/icon-192.png?x=1`)).toBe("passthrough");
    expect(route(`${ORIGIN}/robots.txt`)).toBe("passthrough");
  });

  it("precaches the self-hosted fonts and leaves Google Fonts hosts alone", () => {
    expect(worker.PRECACHE_PATHS).toContain("/fonts/vazirmatn-variable.woff2");
    expect(worker.PRECACHE_PATHS).toContain("/fonts/jetbrains-mono-latin-variable.woff2");
    expect(route(`${ORIGIN}/fonts/vazirmatn-variable.woff2`)).toBe("static");
    expect(route("https://fonts.googleapis.com/css2?family=Vazirmatn")).toBe("passthrough");
    expect(route("https://fonts.gstatic.com/s/vazirmatn/v1/a.woff2")).toBe("passthrough");
  });

  it("passes every non-GET request through", () => {
    for (const method of ["POST", "PUT", "DELETE", "PATCH"])
      expect(route(`${ORIGIN}/assets/a.js`, { method })).toBe("passthrough");
  });

  it("refuses to store no-store, private, partial, or opaque responses", () => {
    const response = (status: number, cacheControl: string | null, type = "basic") => ({
      ok: status >= 200 && status < 300,
      status,
      type,
      headers: new Headers(cacheControl ? { "cache-control": cacheControl } : {}),
    });
    expect(worker.isCacheable(response(200, "public, max-age=31536000, immutable"))).toBe(true);
    expect(worker.isCacheable(response(200, "no-store"))).toBe(false);
    expect(worker.isCacheable(response(200, "private, max-age=60"))).toBe(false);
    expect(worker.isCacheable(response(206, null))).toBe(false);
    expect(worker.isCacheable(response(200, null, "opaque"))).toBe(false);
  });

  it("names the static cache after the build and precaches no HTML except the offline page", () => {
    expect(worker.STATIC_CACHE).toBe("sapio-static-build42");
    expect(worker.PRECACHE_PATHS.filter((item) => item.endsWith(".html"))).toEqual([
      "/offline.html",
    ]);
  });
});

describe("kill-switch worker (AC-29)", () => {
  it("deletes SapioAI caches, unregisters, and reloads open windows", async () => {
    const deleted: string[] = [];
    const navigate = vi.fn();
    const unregister = vi.fn(async () => true);
    const { removeSapioWorker } = loadWorkerScript("sw-kill.js", {
      caches: {
        keys: async () => ["sapio-static-a", "sapio-fonts-v1", "other-app"],
        delete: async (name: string) => deleted.push(name),
      },
      self: {
        registration: { unregister },
        clients: { matchAll: async () => [{ url: "https://sapioai.ir/m", navigate }] },
      },
    }) as { removeSapioWorker: () => Promise<void> };

    await removeSapioWorker();

    expect(deleted).toEqual(["sapio-static-a", "sapio-fonts-v1"]);
    expect(unregister).toHaveBeenCalledOnce();
    expect(navigate).toHaveBeenCalledWith("https://sapioai.ir/m");
  });
});
