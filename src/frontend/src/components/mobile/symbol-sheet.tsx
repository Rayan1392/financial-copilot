import {
  AddIcon,
  BalanceSheetIcon,
  ChevronIcon,
  DeleteIcon,
  MonthlySalesIcon,
  NavChatIcon,
  QuarterlyReportIcon,
  type MobileIcon,
} from "@/components/mobile/icons";
import { BottomSheet, SheetAction } from "@/components/mobile/bottom-sheet";
import { SignedChange } from "@/components/mobile/signed-change";
import { formatNumber, relativeTime } from "@/lib/format/persian";
import { symbolAnalysisPrompts, type SymbolPrompt } from "@/lib/chat/symbol-prompts";
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

const PROMPT_ICONS: Record<SymbolPrompt["id"], MobileIcon> = {
  "monthly-sales": MonthlySalesIcon,
  "quarterly-report": QuarterlyReportIcon,
  "balance-sheet": BalanceSheetIcon,
};
const chevron = <ChevronIcon className="size-4 text-muted-foreground" aria-hidden="true" />;

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
          <p className="flex flex-wrap items-center gap-x-3 gap-y-1 px-3 pb-3">
            {subject.price != null && (
              <span className="text-m-hero font-bold tabular-nums text-foreground">
                {formatNumber(subject.price)}
              </span>
            )}
            <SignedChange value={subject.changePercent} variant="badge" className="text-m-title" />
            {subject.asOf && (
              <span className="text-m-sub text-muted-foreground">{relativeTime(subject.asOf)}</span>
            )}
          </p>
          <h3 className="px-3 pt-1 text-m-title font-bold text-muted-foreground">
            تحلیل با دستیار
          </h3>
          <div className="py-1">
            {symbolAnalysisPrompts(symbol).map((prompt) => (
              <SheetAction
                key={prompt.id}
                icon={PROMPT_ICONS[prompt.id]}
                iconTone={prompt.id === "monthly-sales" ? "emerald" : "gold"}
                onClick={() => {
                  close();
                  onAsk(prompt.text);
                }}
                trailing={chevron}
              >
                {prompt.text}
              </SheetAction>
            ))}
            <SheetAction
              icon={NavChatIcon}
              onClick={() => {
                close();
                onAskOther(symbol);
              }}
              trailing={chevron}
            >
              پرسش دیگری درباره {symbol}…
            </SheetAction>
          </div>
          <div className="border-t border-hairline pt-1">
            {subject.followed ? (
              <>
                <SheetAction
                  tone="danger"
                  icon={DeleteIcon}
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
                  <p className="px-3 pb-2 text-m-sub text-muted-foreground">
                    شناسه شرکت این نماد در دسترس نیست؛ حذف آن از صفحه دیده‌بان نسخه دسکتاپ ممکن است.
                  </p>
                )}
              </>
            ) : (
              <SheetAction
                icon={AddIcon}
                iconTone="emerald"
                disabled={followBySymbol.isPending}
                onClick={() => followBySymbol.mutate({ symbol }, { onSuccess: close })}
              >
                افزودن به دیده‌بان
              </SheetAction>
            )}
          </div>
        </>
      )}
    </BottomSheet>
  );
}
