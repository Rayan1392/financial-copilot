/**
 * Per-browser dismissal flags for banners and install prompts. A convenience only: storage may be unavailable
 * (private mode, blocked site data), so every access is guarded and a failure just shows the item again.
 */
export function isDismissed(key: string, now = Date.now()): boolean {
  try {
    const value = window.localStorage.getItem(key);
    if (value === null) return false;
    const until = Number(value);
    return Number.isFinite(until) ? until > now : true;
  } catch {
    return false;
  }
}

/** Dismisses until `now + durationMs`, or permanently when no duration is given. */
export function dismiss(key: string, durationMs?: number, now = Date.now()) {
  try {
    window.localStorage.setItem(
      key,
      durationMs === undefined ? "permanent" : String(now + durationMs),
    );
  } catch {
    // Storage is unavailable; the item simply reappears next time.
  }
}
