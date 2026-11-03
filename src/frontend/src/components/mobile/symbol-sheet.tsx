import { ChevronLeft } from "lucide-react";
import { BottomSheet, SheetAction } from "@/components/mobile/bottom-sheet";
import { SignedChange } from "@/components/mobile/signed-change";
import { formatNumber, relativeTime } from "@/lib/format/persian";
import { symbolAnalysisPrompts } from "@/lib/chat/symbol-prompts";
import { useFollowActions } from "@/lib/workspace/follow-actions";

export interface SymbolSheetSubject {
  symbol: string;
  companyName?: string | null;
  price?: number | null;
  changePercent?: number | null;
  asOf?: string | null;
  /** Present when the symbol is followed and its identity is known; null when followed but unidentified. */
  externalCompanyId?: string | null;
  followed: boolean;
}

/**
 * Symbol action sheet (design §6.3): analysis prompts from the shared module (D-Q1), a free question, and
 * follow or unfollow. Starting a chat is delegated, so every caller runs the same new-chat flow.
 */
export function SymbolSheet({
  subject,
  onOpenChange,
  onAsk,
  onAskOther,
}: {
  subject: SymbolSheetSubject | null;
  onOpenChange: (open: boolean) => void;
  onAsk: (prompt: string) => void;
  onAskOther: (symbol: string) => void;
}) {
  const { unfollow, followBySymbol } = useFollowActions();
  const symbol = subject?.symbol ?? "";
  const title = subject?.companyName ? `${symbol} · ${subject.companyName}` : symbol;
  const close = () => onOpenChange(false);

  return (
    <BottomSheet open={subject !== null} onOpenChange={onOpenChange} title={title}>
      {subject && (
        <>
          <p className="flex flex-wrap items-center gap-2 px-3 pb-2 text-sm text-muted-foreground">
            {subject.price != null && (
              <span className="tabular-nums text-foreground">{formatNumber(subject.price)}</span>
            )}
            <SignedChange value={subject.changePercent} />
            {subject.asOf && <span>· {relativeTime(subject.asOf)}</span>}
          </p>
          <h3 className="px-3 pt-2 text-[13px] font-semibold text-muted-foreground">
            تحلیل با دستیار
          </h3>
          <div className="py-1">
            {symbolAnalysisPrompts(symbol).map((prompt) => (
              <SheetAction
                key={prompt.id}
                onClick={() => {
                  close();
                  onAsk(prompt.text);
                }}
                trailing={
                  <ChevronLeft className="size-4 text-muted-foreground" aria-hidden="true" />
                }
              >
                {prompt.text}
              </SheetAction>
            ))}
            <SheetAction
              onClick={() => {
                close();
                onAskOther(symbol);
              }}
              trailing={<ChevronLeft className="size-4 text-muted-foreground" aria-hidden="true" />}
            >
              پرسش دیگری درباره {symbol}…
            </SheetAction>
          </div>
          <div className="border-t border-hairline pt-1">
            {subject.followed ? (
              <>
                <SheetAction
                  tone="danger"
                  disabled={!subject.externalCompanyId || unfollow.isPending}
                  onClick={() => {
                    if (!subject.externalCompanyId) return;
                    unfollow.mutate({ externalCompanyId: subject.externalCompanyId, symbol });
                    close();
                  }}
                >
                  حذف از دیده‌بان
                </SheetAction>
                {!subject.externalCompanyId && (
                  <p className="px-3 pb-2 text-xs leading-6 text-muted-foreground">
                    شناسه شرکت این نماد در دسترس نیست؛ حذف آن از صفحه دیده‌بان نسخه دسکتاپ ممکن است.
                  </p>
                )}
              </>
            ) : (
              <SheetAction
                disabled={followBySymbol.isPending}
                onClick={() => followBySymbol.mutate({ symbol }, { onSuccess: close })}
              >
                افزودن به دیده‌بان
              </SheetAction>
            )}
            <SheetAction onClick={close}>بستن</SheetAction>
          </div>
        </>
      )}
    </BottomSheet>
  );
}
