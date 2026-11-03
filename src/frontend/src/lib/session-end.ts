/**
 * Distinguishes a deliberate sign-out from a lost session, so the mobile shell shows «نشست شما به پایان رسید»
 * only when the session really expired.
 */
let explicitSignOut = false;

export function markExplicitSignOut() {
  explicitSignOut = true;
}

export function consumeExplicitSignOut(): boolean {
  const value = explicitSignOut;
  explicitSignOut = false;
  return value;
}
