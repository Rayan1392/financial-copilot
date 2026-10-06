import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { useMutation } from "@tanstack/react-query";
import { useServerFn } from "@tanstack/react-start";
import { sendChatMessage } from "@/lib/chat.functions";
import { PromptInput } from "@/components/app/prompt-input";
import { MessageList } from "@/components/app/message-list";
import { useState } from "react";
import { Activity, ArrowLeft, BarChart3, FileText, Package, PieChart, TrendingUp } from "lucide-react";

export const Route = createFileRoute("/_app/chat")({
  component: NewChatPage,
});

function chatErrorMessage(error: Error): string {
  if (error.message.toLowerCase().includes("insufficient"))
    return "اعتبار کافی برای پردازش درخواست وجود ندارد. لطفاً حساب خود را شارژ کنید.";
  return "متأسفیم، خطایی در پردازش درخواست رخ داد. لطفاً دوباره امتحان کنید.";
}

function NewChatPage() {
  const navigate = useNavigate();
  const send = useServerFn(sendChatMessage);
  const [queryError, setQueryError] = useState<string | null>(null);
  const [pendingMessage, setPendingMessage] = useState<string | null>(null);

  const starterPrompts = [
    { text: "ترکیب فروش محصولات کگهر را بررسی کن", icon: PieChart },
    { text: "روند فروش ماهانه کگهر را نشان بده", icon: TrendingUp },
    { text: "روند فروش گندله کگهر در ۱۲ ماهه اخیر چطور بوده؟", icon: BarChart3 },
    { text: "آخرین فروش فخاس چقدر بوده است؟", icon: Package },
    { text: "رتبه بندی گزارش ماهانه؟", icon: FileText },
    { text: "آخرین قیمت اخابر چقدر بوده؟", icon: Activity },
    { text: "کگهر را با صنعت خودش مقایسه کن", icon: BarChart3 },
  ];

  const startChat = useMutation({
    mutationFn: async ({ message, suggestedActionId }: { message: string; suggestedActionId?: string }) => {
      const result = await send({ data: { message, scannerPage: 1, suggestedActionId } });
      return result.threadId;
    },
    onSuccess: (id) => navigate({ to: "/c/$threadId", params: { threadId: id } }),
    onError: (error: Error) => {
      setPendingMessage(null);
      setQueryError(chatErrorMessage(error));
    },
  });

  const submit = (text: string, suggestedActionId?: string) => {
    if (startChat.isPending) return;
    setQueryError(null);
    setPendingMessage(text);
    startChat.mutate({ message: text, suggestedActionId });
  };

  return (
    <>
      <div className={`flex-1 overflow-y-auto scrollbar-thin ${pendingMessage ? "" : "flex items-center justify-center p-8"}`}>
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
            فاز ۱ · تحلیل تولید و فروش
          </div>
          <p className="text-base md:text-lg font-medium text-foreground mb-2 max-w-2xl mx-auto text-pretty">
            گزارش‌های تولید و فروش شرکت‌ها را با زبان طبیعی تحلیل کنید.
          </p>
          <p className="text-sm leading-7 text-muted-foreground mb-7 max-w-2xl mx-auto text-pretty">
            روند فروش ماهانه، ترکیب فروش محصولات و عملکرد یک محصول مشخص را بررسی کنید یا سؤال خود را درباره گزارش‌های تولید و فروش بپرسید.
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
