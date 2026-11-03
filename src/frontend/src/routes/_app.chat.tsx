import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { PromptInput } from "@/components/app/prompt-input";
import { MessageList } from "@/components/app/message-list";
import { ArrowLeft, TrendingUp } from "lucide-react";
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
      <div className={`flex-1 ${pendingMessage ? "" : "flex items-center justify-center p-8"}`}>
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
        <div dir="rtl" className="max-w-3xl w-full py-6 md:py-10 text-center animate-fade-up">
          <div className="size-14 mx-auto rounded-2xl bg-emerald-soft ring-1 ring-emerald/30 flex items-center justify-center mb-5 text-emerald">
            <TrendingUp className="size-6" aria-hidden="true" />
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
            روند فروش ماهانه، ترکیب فروش محصولات، عملکرد یک محصول، رتبه‌بندی گزارش‌های ماهانه و سود و زیان فصلی نمادها را بررسی کنید. گزارش ترازنامه به‌زودی اضافه می‌شود.
          </p>
          <div className="grid grid-cols-1 md:grid-cols-2 gap-3 text-right">
            {starterPrompts.map((prompt) => (
              <button
                key={prompt.text}
                onClick={() => submit(prompt.text)}
                disabled={startChat.isPending}
                className="group flex min-h-16 items-center gap-3 rounded-xl border border-border bg-surface px-4 py-3 text-sm leading-6 text-foreground transition hover:border-emerald/30 hover:bg-surface-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40 disabled:opacity-50"
              >
                <span className="flex size-9 shrink-0 items-center justify-center rounded-lg bg-emerald-soft/60 text-emerald">
                  <prompt.icon className="size-4" aria-hidden="true" />
                </span>
                <span className="flex-1">{prompt.text}</span>
                <ArrowLeft className="size-4 shrink-0 text-muted-foreground/50 transition group-hover:text-emerald" aria-hidden="true" />
              </button>
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
      <PromptInput
        onSubmit={submit}
        loading={startChat.isPending}
        showAssistedQuery
      />
    </>
  );
}
