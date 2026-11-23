import { useState } from "react";
import { Copy, Search } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import type { AdminConversationLogUser } from "@/integrations/financial-copilot/admin-client";

export type ActorSelection = { user: AdminConversationLogUser; trigger: HTMLButtonElement };

/**
 * Feature 164 change «unresolved-actor-identity»: an actor without a user account in the tenant (an API client or an
 * unknown ID). It has no Customer 360, so this dialog shows only the ID and makes no account request.
 */
export function ConversationLogActorDialog({
  selection: { user, trigger },
  onShowQuestions,
  onClose,
}: {
  selection: ActorSelection;
  onShowQuestions: (actorId: string) => void;
  onClose: () => void;
}) {
  const [copyState, setCopyState] = useState<"idle" | "copied" | "failed">("idle");
  const isApiClient = user.actorKind === "ApiClient";

  async function copy() {
    try {
      await navigator.clipboard.writeText(user.userId);
      setCopyState("copied");
    } catch {
      setCopyState("failed");
    }
  }

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent
        dir="rtl"
        onCloseAutoFocus={(event) => {
          event.preventDefault();
          if (trigger.isConnected) trigger.focus();
        }}
        className="light scheme-light rounded-2xl bg-white sm:max-w-lg"
      >
        <DialogHeader className="text-right">
          <DialogTitle>شناسهٔ پرسش‌کننده</DialogTitle>
          <DialogDescription>
            این شناسه به حساب کاربری در این فضا متصل نیست؛ پروفایل مشتری برای آن وجود ندارد.
          </DialogDescription>
        </DialogHeader>

        <dl className="grid gap-3 rounded-xl border border-slate-200 bg-slate-50/70 p-4 text-sm">
          <div>
            <dt className="text-xs text-slate-500">نوع</dt>
            <dd className="mt-1 font-medium text-slate-900">
              {isApiClient ? "کلاینت API" : "شناسهٔ ناشناخته"}
            </dd>
          </div>
          {isApiClient && (
            <div>
              <dt className="text-xs text-slate-500">نام کلاینت</dt>
              <dd className="mt-1 text-slate-900">
                <bdi>{user.apiClientName || "ثبت نشده"}</bdi>
              </dd>
            </div>
          )}
          <div>
            <dt className="text-xs text-slate-500">شناسه</dt>
            <dd className="mt-1 flex items-center gap-2">
              <bdi dir="ltr" className="min-w-0 flex-1 break-all font-mono text-xs text-slate-800">
                {user.userId}
              </bdi>
              <Button
                type="button"
                size="sm"
                variant="outline"
                onClick={copy}
                aria-label="کپی شناسه"
              >
                <Copy className="ml-1 size-3.5" />
                کپی شناسه
              </Button>
            </dd>
            <p role="status" className="mt-1 min-h-4 text-xs text-slate-500">
              {copyState === "copied" && "شناسه کپی شد."}
              {copyState === "failed" && "کپی انجام نشد؛ شناسه را دستی انتخاب کنید."}
            </p>
          </div>
        </dl>

        <DialogFooter className="gap-2 sm:justify-start">
          <Button type="button" onClick={() => onShowQuestions(user.userId)}>
            <Search className="ml-2 size-4" />
            نمایش همهٔ پرسش‌های این شناسه
          </Button>
          <Button type="button" variant="outline" onClick={onClose}>
            بستن
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
