import { WifiOff } from "lucide-react";

export function OfflineBanner({ online }: { online: boolean }) {
  if (online) return null;
  return (
    <div
      role="status"
      className="flex shrink-0 items-center justify-center gap-2 bg-gold/15 px-4 py-2 text-sm font-medium text-gold"
    >
      <WifiOff className="size-4" aria-hidden="true" />
      اتصال اینترنت برقرار نیست
    </div>
  );
}
