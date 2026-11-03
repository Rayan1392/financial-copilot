/*
 * SapioAI service worker (Feature 153, spec §9.3 and §9.7).
 *
 * Caches static, user-independent assets only. It never caches HTML documents, server-function (`/_serverFn/`)
 * or API responses, auth endpoints, or anything cross-origin except the public Google Fonts files, and it never
 * stores a response marked `no-store` or `private`.
 *
 * The page registers `/sw.js?v=<build id>`; the version names the static cache, and activation removes caches of
 * other versions. Emergency rollback: deploy public/sw-kill.js as /sw.js (docs/deployment/pwa-service-worker.md).
 */
"use strict";

const VERSION = new URL(self.location.href).searchParams.get("v") || "dev";
const STATIC_CACHE = "sapio-static-" + VERSION;
const FONT_CACHE = "sapio-fonts-v1";
const MAX_STATIC_ENTRIES = 200;
const MAX_FONT_ENTRIES = 30;
const OFFLINE_PAGE = "/offline.html";
const PRECACHE_PATHS = [
  OFFLINE_PAGE,
  "/manifest.webmanifest",
  "/favicon.ico",
  "/icons/icon-192.png",
  "/icons/icon-512.png",
  "/icons/icon-maskable-512.png",
  "/icons/apple-touch-icon.png",
];
const FONT_ORIGINS = ["https://fonts.googleapis.com", "https://fonts.gstatic.com"];

/**
 * Decides how a request is handled. Pure, so it can be unit-tested outside a worker.
 * @returns {"passthrough" | "navigation" | "static" | "font"}
 */
function routeRequest(request, scopeOrigin) {
  if (request.method !== "GET") return "passthrough";
  const url = new URL(request.url);
  if (url.origin === scopeOrigin) {
    if (url.pathname.startsWith("/_serverFn") || url.pathname.startsWith("/api/")) return "passthrough";
    if (request.mode === "navigate") return "navigation";
    if (url.pathname.startsWith("/assets/")) return "static";
    if (PRECACHE_PATHS.includes(url.pathname) && url.search === "") return "static";
    return "passthrough";
  }
  if (FONT_ORIGINS.includes(url.origin)) return "font";
  // Every other origin, including the API origin, goes straight to the network.
  return "passthrough";
}

/** A response may be stored only when it is a complete success and the server did not forbid storing it. */
function isCacheable(response) {
  if (!response || !response.ok || response.status !== 200) return false;
  if (response.type !== "basic" && response.type !== "cors") return false;
  const cacheControl = (response.headers.get("cache-control") || "").toLowerCase();
  return !cacheControl.includes("no-store") && !cacheControl.includes("private");
}

async function trimCache(cacheName, maxEntries) {
  const cache = await caches.open(cacheName);
  const keys = await cache.keys();
  for (let index = 0; index < keys.length - maxEntries; index += 1) await cache.delete(keys[index]);
}

async function cacheFirst(request) {
  const cache = await caches.open(STATIC_CACHE);
  const cached = await cache.match(request);
  if (cached) return cached;
  const response = await fetch(request);
  if (isCacheable(response)) {
    await cache.put(request, response.clone());
    await trimCache(STATIC_CACHE, MAX_STATIC_ENTRIES);
  }
  return response;
}

async function staleWhileRevalidate(request) {
  const cache = await caches.open(FONT_CACHE);
  const cached = await cache.match(request);
  const network = fetch(request)
    .then(async (response) => {
      if (isCacheable(response)) {
        await cache.put(request, response.clone());
        await trimCache(FONT_CACHE, MAX_FONT_ENTRIES);
      }
      return response;
    })
    .catch(() => cached);
  return cached || network;
}

async function networkOnlyNavigation(request) {
  try {
    return await fetch(request);
  } catch (error) {
    const offline = await caches.match(OFFLINE_PAGE);
    if (offline) return offline;
    throw error;
  }
}

/**
 * Precaches each path as a fresh, non-redirected response. The Workers asset server answers `/offline.html` with a
 * 307 to `/offline`, and a redirected response cannot be served for a navigation, so the body is copied.
 */
async function precache() {
  const cache = await caches.open(STATIC_CACHE);
  await Promise.all(
    PRECACHE_PATHS.map(async (path) => {
      const response = await fetch(path, { cache: "no-cache" });
      if (!response.ok) throw new Error("Precache failed for " + path + " (" + response.status + ")");
      await cache.put(
        path,
        new Response(await response.blob(), { status: 200, headers: response.headers }),
      );
    }),
  );
}

if (typeof module !== "undefined" && module.exports) {
  // Unit tests load this file in a plain JavaScript context.
  module.exports = { routeRequest, isCacheable, STATIC_CACHE, PRECACHE_PATHS };
} else {
  self.addEventListener("install", (event) => {
    event.waitUntil(precache());
  });

  self.addEventListener("activate", (event) => {
    event.waitUntil(
      caches
        .keys()
        .then((names) =>
          Promise.all(
            names
              .filter((name) => name.startsWith("sapio-static-") && name !== STATIC_CACHE)
              .map((name) => caches.delete(name)),
          ),
        )
        .then(() => self.clients.claim()),
    );
  });

  // The page asks for activation only after the user confirms the update prompt.
  self.addEventListener("message", (event) => {
    if (event.data && event.data.type === "SKIP_WAITING") self.skipWaiting();
  });

  self.addEventListener("fetch", (event) => {
    const route = routeRequest(event.request, self.location.origin);
    if (route === "navigation") event.respondWith(networkOnlyNavigation(event.request));
    else if (route === "static") event.respondWith(cacheFirst(event.request));
    else if (route === "font") event.respondWith(staleWhileRevalidate(event.request));
    // "passthrough": no respondWith, so the browser handles the request as if no worker existed.
  });
}
