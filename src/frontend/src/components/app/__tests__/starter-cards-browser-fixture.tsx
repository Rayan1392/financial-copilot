import { useState } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { NewChatIntroduction } from "../new-chat-introduction";
import { StarterCardGrid } from "../starter-card-grid";
import { StarterSymbolPicker } from "../starter-symbol-picker";
import { StarterSymbolPickerDialog } from "../starter-symbol-picker-dialog";
import { BottomSheet } from "@/components/mobile/bottom-sheet";
import { MobileStarterList } from "@/components/mobile/mobile-starter-list";
import { homeStarterPrompts } from "@/lib/chat/starter-prompts";
import { useStarterSelection } from "@/lib/chat/use-starter-selection";
import "../../../styles.css";

// Browser fixture of the starter cards (changes «valuation-gauge-starter-card» and «starter-cards-symbol-picker»):
// ?view=desktop|mobile&gauge=1|0&watch=1|0. The desktop view renders the `/chat` introduction and starter cards
// with their picker dialog; the mobile view repeats `/m` with its bottom sheet. What a
// card or the picker sends is written to the «sent» line.
const params = new URLSearchParams(location.search);
const prompts = homeStarterPrompts({ valuationGaugeCards: params.get("gauge") !== "0" });
const watchlistSymbols = params.get("watch") === "1" ? ["شپنا", "فملی"] : [];
const mobile = params.get("view") === "mobile";

export function Fixture() {
  const [sent, setSent] = useState<string[]>([]);
  const starter = useStarterSelection((text) => setSent((current) => [...current, text]));

  return (
    <>
      <output className="sr-only" data-testid="sent">
        {sent.join("|")}
      </output>
      {mobile ? (
        <div className="mx-auto max-w-xl px-4 py-4" data-testid="starter-root">
          <h2 className="mb-3 text-m-head font-bold text-foreground">چه چیزی را بررسی کنیم؟</h2>
          <MobileStarterList prompts={prompts} disabled={false} onSelect={starter.select} />
        </div>
      ) : (
        <div className="flex p-8">
          <div
            className="m-auto w-full max-w-3xl py-6 text-center md:py-10"
            data-testid="starter-root"
          >
            <NewChatIntroduction />
            <StarterCardGrid prompts={prompts} disabled={false} onSelect={starter.select} />
          </div>
        </div>
      )}
      {mobile ? (
        // Same sheet as the picker on `/m`.
        <BottomSheet
          open={starter.open}
          onOpenChange={(open) => !open && starter.close()}
          title={starter.prompt?.topic ?? ""}
          onCloseAutoFocus={starter.restoreFocus}
          fullHeight
        >
          {starter.prompt && (
            <StarterSymbolPicker
              key={starter.prompt.id}
              prompt={starter.prompt}
              watchlistSymbols={watchlistSymbols}
              onPick={starter.pick}
            />
          )}
        </BottomSheet>
      ) : (
        <StarterSymbolPickerDialog
          prompt={starter.prompt}
          open={starter.open}
          watchlistSymbols={watchlistSymbols}
          onPick={starter.pick}
          onClose={starter.close}
          onCloseAutoFocus={starter.restoreFocus}
        />
      )}
    </>
  );
}

createRoot(document.getElementById("root")!).render(
  <QueryClientProvider client={new QueryClient()}>
    <div className="min-h-dvh bg-background text-foreground">
      <Fixture />
    </div>
  </QueryClientProvider>,
);
