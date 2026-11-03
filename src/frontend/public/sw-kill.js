/*
 * Kill-switch service worker (Feature 153 spec §9.3). NOT active by default.
 *
 * To remove the SapioAI service worker from every browser after a defect, deploy this file's contents as
 * `/sw.js` (see docs/deployment/pwa-service-worker.md). Browsers check `/sw.js` on navigation, install this
 * version, delete every SapioAI cache, unregister, and reload open pages without a worker.
 */
"use strict";

async function removeSapioWorker() {
  const names = await caches.keys();
  await Promise.all(names.filter((name) => name.startsWith("sapio-")).map((name) => caches.delete(name)));
  await self.registration.unregister();
  const windows = await self.clients.matchAll({ type: "window" });
  for (const client of windows) client.navigate(client.url);
}

if (typeof module !== "undefined" && module.exports) {
  module.exports = { removeSapioWorker };
} else {
  self.addEventListener("install", () => self.skipWaiting());
  self.addEventListener("activate", (event) => event.waitUntil(removeSapioWorker()));
}
