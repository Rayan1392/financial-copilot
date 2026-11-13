import { OfflineIcon } from "@/components/mobile/icons";

export function OfflineBanner({ online }: { online: boolean }) {
  if (online) return null;
  return (
    <div
      role="status"
      className="flex shrink-0 items-center justify-center gap-2 bg-gold/15 px-4 py-2 text-m-title font-medium text-gold"
    >
      <OfflineIcon weight="bold" className="size-5" aria-hidden="true" />
      اتصال اینترنت برقرار نیست
    </div>
  );
}
