// Moved unchanged out of admin-page.tsx: the error text shown by admin panels, with the correlation id.

export const showError = (error: unknown) => {
  if (!(error instanceof Error)) return "درخواست ناموفق بود.";
  const details = error as Error & { type?: string; correlationId?: string };
  return [
    details.message,
    details.type,
    details.correlationId && `شناسه پیگیری: ${details.correlationId}`,
  ]
    .filter(Boolean)
    .join(" | ");
};
