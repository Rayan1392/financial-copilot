import type { ReactNode } from "react";
import { MessageSquarePlus, Sparkles } from "lucide-react";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { symbolAnalysisPrompts } from "@/lib/chat/symbol-prompts";
import { useOpenNewChat } from "@/lib/chat/new-chat-intent";

/**
 * Desktop counterpart of the mobile symbol sheet: the same analysis prompts (one owner, `symbol-prompts.ts`) and a
 * free question, each starting a new conversation on `/chat`. `trigger` must be a single focusable element.
 */
export function SymbolActionsMenu({
  symbol,
  companyName,
  trigger,
}: {
  symbol: string;
  companyName?: string | null;
  trigger: ReactNode;
}) {
  const { ask, askAbout } = useOpenNewChat("/chat");
  return (
    <DropdownMenu dir="rtl">
      <DropdownMenuTrigger asChild>{trigger}</DropdownMenuTrigger>
      <DropdownMenuContent side="right" align="start" className="w-64">
        <DropdownMenuLabel className="flex flex-col gap-0.5">
          <bdi className="text-sm">{symbol}</bdi>
          {companyName && (
            <span className="truncate text-[11px] font-normal text-muted-foreground">
              {companyName}
            </span>
          )}
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        {symbolAnalysisPrompts(symbol).map((prompt) => (
          <DropdownMenuItem key={prompt.id} onSelect={() => ask(prompt.text)}>
            <Sparkles className="size-3.5 text-emerald" aria-hidden="true" />
            {prompt.text}
          </DropdownMenuItem>
        ))}
        <DropdownMenuItem onSelect={() => askAbout(symbol)}>
          <MessageSquarePlus className="size-3.5 text-muted-foreground" aria-hidden="true" />
          پرسش دیگری درباره <bdi>{symbol}</bdi>…
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
