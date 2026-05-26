import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { useMutation } from "@tanstack/react-query";
import { useServerFn } from "@tanstack/react-start";
import { createThread, sendChatMessage } from "@/lib/chat.functions";
import { PromptInput } from "@/components/app/prompt-input";
import { useState } from "react";

export const Route = createFileRoute("/_app/chat")({
  component: NewChatPage,
});

const SUGGESTIONS = [
  "توسن ارزنده است؟",
  "خلاصه بازار امروز را بگو",
  "بین اخابر و آسیاتک کدام بهتر است؟",
  "سهم‌هایی با P/E زیر ۶ و رشد بالا",
  "تحلیل پرتفوی من",
];

function NewChatPage() {
  const navigate = useNavigate();
  const create = useServerFn(createThread);
  const send = useServerFn(sendChatMessage);
  const [deepResearch, setDeepResearch] = useState(false);

  const startChat = useMutation({
    mutationFn: async (message: string) => {
      const thread = await create();
      await send({ data: { threadId: thread.id, message, deepResearch } });
      return thread.id;
    },
    onSuccess: (id) => navigate({ to: "/c/$threadId", params: { threadId: id } }),
  });

  return (
    <>
      <div className="flex-1 overflow-y-auto scrollbar-thin flex items-center justify-center p-8">
        <div className="max-w-2xl w-full text-center animate-fade-up">
          <div className="size-14 mx-auto rounded-2xl bg-emerald-soft ring-1 ring-emerald/30 flex items-center justify-center mb-6">
            <div className="size-5 rounded-full bg-emerald" />
          </div>
          <h1 className="text-2xl font-bold text-foreground mb-2 text-balance">
            دستیار هوشمند تحلیل بازار
          </h1>
          <p className="text-sm text-muted-foreground mb-8 max-w-md mx-auto text-pretty">
            یک سوال درباره نمادها، شاخص، یا فیلتر بازار بپرسید. تحلیل بنیادی، مقایسه، اسکرینر و خلاصه بازار را در یک گفتگو دریافت کنید.
          </p>
          <div className="grid grid-cols-1 md:grid-cols-2 gap-2 text-right">
            {SUGGESTIONS.map((s) => (
              <button
                key={s}
                onClick={() => startChat.mutate(s)}
                disabled={startChat.isPending}
                className="text-sm px-4 py-3 rounded-xl border border-border bg-surface hover:bg-surface-2 hover:border-emerald/30 transition disabled:opacity-50"
              >
                {s}
              </button>
            ))}
          </div>
        </div>
      </div>
      <PromptInput
        deepResearch={deepResearch}
        onToggleDeep={() => setDeepResearch((v) => !v)}
        onSubmit={(text) => startChat.mutate(text)}
        loading={startChat.isPending}
      />
    </>
  );
}
