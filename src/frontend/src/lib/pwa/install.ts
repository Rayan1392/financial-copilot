import { useEffect, useState } from "react";
import { dismiss, isDismissed } from "@/lib/dismissal";

/** Install entry points (Feature 153 T20, spec §9.4). Nothing here is shown on first paint or in standalone mode. */

interface BeforeInstallPromptEvent extends Event {
  prompt: () => Promise<void>;
  userChoice: Promise<{ outcome: "accepted" | "dismissed" }>;
}

const ANSWERED_KEY = "sapio-has-answer";
const INSTALL_SNOOZE_KEY = "sapio-install-snoozed";
const THIRTY_DAYS_MS = 30 * 24 * 60 * 60 * 1000;

let deferredPrompt: BeforeInstallPromptEvent | null = null;
const listeners = new Set<() => void>();
let captureStarted = false;

function notify() {
  for (const listener of listeners) listener();
}

/** Call once in the browser, as early as possible, so the Chromium prompt event is not missed. */
export function captureInstallPrompt() {
  if (captureStarted || typeof window === "undefined") return;
  captureStarted = true;
  window.addEventListener("beforeinstallprompt", (event) => {
    event.preventDefault();
    deferredPrompt = event as BeforeInstallPromptEvent;
    notify();
  });
  window.addEventListener("appinstalled", () => {
    deferredPrompt = null;
    notify();
  });
}

export function isStandaloneDisplay(): boolean {
  if (typeof window === "undefined") return false;
  const navigatorStandalone =
    (navigator as Navigator & { standalone?: boolean }).standalone === true;
  const media =
    typeof window.matchMedia === "function" &&
    window.matchMedia("(display-mode: standalone)").matches;
  return navigatorStandalone || media;
}

/** iOS and iPadOS Safari, which has no install prompt and needs the Share → Add to Home Screen steps. */
export function isIosSafari(userAgent: string, maxTouchPoints = 0): boolean {
  const ios =
    /iPhone|iPad|iPod/.test(userAgent) || (/Macintosh/.test(userAgent) && maxTouchPoints > 1);
  return ios && /Safari/.test(userAgent) && !/CriOS|FxiOS|EdgiOS|OPiOS/.test(userAgent);
}

export function markAnswered() {
  try {
    window.localStorage.setItem(ANSWERED_KEY, "1");
  } catch {
    // Storage unavailable: the post-answer install card simply does not appear.
  }
}

function hasAnswered(): boolean {
  try {
    return window.localStorage.getItem(ANSWERED_KEY) === "1";
  } catch {
    return false;
  }
}

export interface InstallState {
  /** «نصب»: Chromium prompt available, or iOS Safari instructions. */
  installable: boolean;
  mode: "prompt" | "ios" | null;
  /** The post-answer card on `/m`: installable, a first answer seen, and not snoozed. */
  showCard: boolean;
  promptInstall: () => Promise<void>;
  snooze: () => void;
}

export function useInstallState(): InstallState {
  const [, setVersion] = useState(0);
  const [snoozed, setSnoozed] = useState(true);
  const [environment, setEnvironment] = useState({ standalone: true, ios: false, answered: false });

  useEffect(() => {
    captureInstallPrompt();
    const listener = () => setVersion((value) => value + 1);
    listeners.add(listener);
    setSnoozed(isDismissed(INSTALL_SNOOZE_KEY));
    setEnvironment({
      standalone: isStandaloneDisplay(),
      ios: isIosSafari(navigator.userAgent, navigator.maxTouchPoints),
      answered: hasAnswered(),
    });
    return () => {
      listeners.delete(listener);
    };
  }, []);

  const mode = environment.standalone
    ? null
    : deferredPrompt
      ? "prompt"
      : environment.ios
        ? "ios"
        : null;
  return {
    installable: mode !== null,
    mode,
    showCard: mode !== null && environment.answered && !snoozed,
    async promptInstall() {
      const event = deferredPrompt;
      if (!event) return;
      deferredPrompt = null;
      await event.prompt();
      await event.userChoice.catch(() => undefined);
      notify();
    },
    snooze() {
      dismiss(INSTALL_SNOOZE_KEY, THIRTY_DAYS_MS);
      setSnoozed(true);
    },
  };
}
