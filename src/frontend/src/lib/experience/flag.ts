/**
 * Rollout flag for automatic phone redirects (Feature 153 spec §7.8). Build-time, because the Worker runtime does
 * not reliably expose environment variables. Off unless the build sets `VITE_MOBILE_AUTO_REDIRECT=true`; it must
 * stay off in production until the D-Q5 rollout gate (verified origins and refresh cookie) passes.
 */
export const MOBILE_AUTO_REDIRECT_ENABLED = import.meta.env.VITE_MOBILE_AUTO_REDIRECT === "true";
