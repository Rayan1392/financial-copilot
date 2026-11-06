import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { PromptInput } from "@/components/app/prompt-input";
import { SapioMark } from "@/components/app/sapio-mark";
import { MessageList } from "@/components/app/message-list";
import { StarterCard } from "@/components/app/starter-card";
import { STARTER_PROMPTS } from "@/lib/chat/starter-prompts";
import { chatErrorMessage } from "@/lib/chat/chat-errors";
import { useStartChat } from "@/lib/chat/use-start-chat";

export const Route = createFileRoute("/_app/chat")({
  component: NewChatPage,
});

function NewChatPage() {
  const navigate = useNavigate();
  const startChat = useStartChat({
    navigateToThread: (threadId) => navigate({ to: "/c/$threadId", params: { threadId } }),
  });
  const { submit, pendingMessage } = startChat;
  const queryError = startChat.failure ? chatErrorMessage(startChat.failure) : null;
  const starterPrompts = STARTER_PROMPTS;

  return (
    <>
      {/* The intro scrolls in its own region so the composer below stays pinned on short viewports
          (same structure as the thread page). m-auto centers without clipping overflowing content. */}
      <div
        className={`min-h-0 flex-1 overflow-y-auto scrollbar-thin ${pendingMessage ? "" : "flex p-8"}`}
      >
        {pendingMessage ? (
          <MessageList
            messages={[
              {
                id: "pending-user-message",
                role: "user",
                created_at: new Date().toISOString(),
                content: { text: pendingMessage },
              },
            ]}
            loading={false}
            streaming={startChat.isPending}
            onSuggested={submit}
          />
        ) : (
          <div dir="rtl" className="m-auto max-w-3xl w-full py-6 md:py-10 text-center animate-fade-up">
            <div className="mb-5 flex justify-center">
              <SapioMark className="size-14 text-emerald shadow-sm" />
            </div>
            <h1 className="text-2xl md:text-3xl font-bold text-foreground mb-3 text-balance">
              دستیار هوشمند بازار سرمایه
            </h1>
            <div className="inline-flex items-center rounded-full border border-emerald/20 bg-emerald-soft/60 px-3 py-1 text-xs font-medium text-emerald mb-5">
              فاز ۱ · تولید، فروش و گزارش‌های مالی
            </div>
            <p className="text-base md:text-lg font-medium text-foreground mb-2 max-w-2xl mx-auto text-pretty">
              گزارش‌های تولید و فروش و صورت‌های مالی شرکت‌ها را با زبان طبیعی بررسی کنید.
            </p>
            <p className="text-sm leading-7 text-muted-foreground mb-7 max-w-2xl mx-auto text-pretty">
              روند فروش ماهانه، ترکیب فروش محصولات، عملکرد یک محصول، رتبه‌بندی گزارش‌های ماهانه و
              سود و زیان فصلی و ترازنامه (صورت وضعیت مالی) نمادها را بررسی کنید.
            </p>
            <div className="grid grid-cols-1 md:grid-cols-2 gap-3 text-right">
              {starterPrompts.map((prompt) => (
                <StarterCard
                  key={prompt.text}
                  prompt={prompt}
                  disabled={startChat.isPending}
                  onSelect={submit}
                />
              ))}
            </div>
          </div>
        )}
      </div>
      {queryError && (
        <div className="mx-4 mb-2 rounded-lg border border-destructive/30 bg-destructive/10 px-4 py-2.5 text-sm text-destructive text-right">
          {queryError}
        </div>
      )}
      <PromptInput onSubmit={submit} loading={startChat.isPending} showAssistedQuery />
    </>
  );
}
