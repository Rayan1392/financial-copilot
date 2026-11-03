/**
 * Service-worker registration (Feature 153 T18). Production builds and secure contexts only; scope `/` (D-Q3).
 * A new worker installs and waits; it activates only after the user confirms the update prompt.
 */
const BUILD_ID: string = import.meta.env.VITE_APP_BUILD_ID ?? "dev";

type UpdateListener = (activate: () => void) => void;

export function serviceWorkerUrl(buildId = BUILD_ID): string {
  return `/sw.js?v=${encodeURIComponent(buildId)}`;
}

export function canRegisterServiceWorker(): boolean {
  return (
    import.meta.env.PROD &&
    typeof navigator !== "undefined" &&
    "serviceWorker" in navigator &&
    window.isSecureContext
  );
}

function activateWaiting(registration: ServiceWorkerRegistration) {
  const waiting = registration.waiting;
  if (!waiting) {
    window.location.reload();
    return;
  }
  navigator.serviceWorker.addEventListener("controllerchange", () => window.location.reload(), {
    once: true,
  });
  waiting.postMessage({ type: "SKIP_WAITING" });
}

export async function registerServiceWorker(onUpdateReady: UpdateListener): Promise<void> {
  if (!canRegisterServiceWorker()) return;
  try {
    const registration = await navigator.serviceWorker.register(serviceWorkerUrl(), {
      scope: "/",
      updateViaCache: "none",
    });
    const announce = () => onUpdateReady(() => activateWaiting(registration));
    // A waiting worker plus an existing controller means an update, not the first install.
    if (registration.waiting && navigator.serviceWorker.controller) announce();
    registration.addEventListener("updatefound", () => {
      const installing = registration.installing;
      installing?.addEventListener("statechange", () => {
        if (installing.state === "installed" && navigator.serviceWorker.controller) announce();
      });
    });
  } catch (error) {
    // The app works without a worker; registration failures must never break the page.
    console.warn("Service worker registration failed.", error);
  }
}
