import { useRef, useState } from "react";
import { MessageSquare } from "lucide-react";
import { MessageConversation, MessageInbox } from "@/components/app/in-app-messages";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import type { AdminUserDirectoryItem } from "@/integrations/financial-copilot/admin-client";
import { messageApi } from "@/integrations/financial-copilot/in-app-messages";
import { formatNumber } from "@/lib/format/persian";

export function AdminMessageComposer({ recipient }: { recipient: AdminUserDirectoryItem }) {
  const [open, setOpen] = useState(false);
  const [subject, setSubject] = useState("");
  const [body, setBody] = useState("");
  const [repliesEnabled, setRepliesEnabled] = useState(false);
  const [reviewKey, setReviewKey] = useState<string | null>(null);
  const attempt = useRef<{ draft: string; key: string }>(null);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState(false);
  const [success, setSuccess] = useState(false);

  function reset() {
    setSubject("");
    setBody("");
    setRepliesEnabled(false);
    setReviewKey(null);
    attempt.current = null;
    setError(false);
  }

  async function send() {
    if (!reviewKey || sending) return;
    setSending(true);
    setError(false);
    try {
      await messageApi.start(recipient.userId, {
        subject,
        body,
        repliesEnabled,
        idempotencyKey: reviewKey,
      });
      setSuccess(true);
      setOpen(false);
      reset();
    } catch {
      setError(true);
    } finally {
      setSending(false);
    }
  }

  return (
    <div className="my-3 flex flex-wrap items-center gap-2">
      <Dialog
        open={open}
        onOpenChange={(next) => {
          if (sending) return;
          setOpen(next);
          if (next) setSuccess(false);
        }}
      >
        <DialogTrigger asChild>
          <Button variant="outline" size="sm">
            <MessageSquare className="size-4" aria-hidden="true" /> ارسال پیام
          </Button>
        </DialogTrigger>
        <DialogContent dir="rtl" className="light scheme-light max-h-[90vh] overflow-y-auto">
          <DialogHeader className="text-right">
            <DialogTitle>{reviewKey ? "پیش‌نمایش پیام" : "ارسال پیام"}</DialogTitle>
            <DialogDescription className="text-right">
              گیرنده: {recipient.displayName && <strong>{recipient.displayName} · </strong>}
              <span dir="ltr" className="break-all">
                {recipient.email ?? recipient.userId}
              </span>
            </DialogDescription>
          </DialogHeader>
          {reviewKey ? (
            <div className="min-w-0 space-y-3">
              <p className="text-sm">{repliesEnabled ? "امکان پاسخ فعال است." : "اعلان یک‌طرفه"}</p>
              <div className="rounded-xl border border-border p-3">
                <h3 className="break-words font-bold">{subject}</h3>
                <p dir="auto" className="mt-2 whitespace-pre-wrap break-words text-sm">
                  {body}
                </p>
              </div>
              {error && (
                <p role="alert" className="text-sm text-destructive">
                  ارسال تأیید نشد. می‌توانید دوباره تلاش کنید.
                </p>
              )}
              <div className="flex flex-wrap gap-2">
                <Button onClick={() => void send()} disabled={sending}>
                  {error ? "تلاش دوباره" : "تأیید و ارسال"}
                </Button>
                <Button
                  variant="outline"
                  disabled={sending}
                  onClick={() => {
                    setReviewKey(null);
                    setError(false);
                  }}
                >
                  ویرایش
                </Button>
              </div>
            </div>
          ) : (
            <form
              className="space-y-3"
              onSubmit={(event) => {
                event.preventDefault();
                if (!subject.trim() || !body.trim()) return;
                const draft = JSON.stringify([subject.trim(), body.trim(), repliesEnabled]);
                if (attempt.current?.draft !== draft)
                  attempt.current = { draft, key: crypto.randomUUID() };
                setReviewKey(attempt.current.key);
              }}
            >
              <div className="space-y-1">
                <label htmlFor="message-subject" className="text-sm font-medium">
                  عنوان
                </label>
                <Input
                  id="message-subject"
                  maxLength={120}
                  required
                  value={subject}
                  onChange={(event) => setSubject(event.target.value)}
                />
                <p className="text-xs text-muted-foreground">
                  {formatNumber(120 - subject.length)} نویسه باقی‌مانده
                </p>
              </div>
              <div className="space-y-1">
                <label htmlFor="message-body" className="text-sm font-medium">
                  متن پیام
                </label>
                <Textarea
                  id="message-body"
                  maxLength={2000}
                  required
                  value={body}
                  onChange={(event) => setBody(event.target.value)}
                />
                <p className="text-xs text-muted-foreground">
                  {formatNumber(2000 - body.length)} نویسه باقی‌مانده
                </p>
              </div>
              <label className="flex items-center gap-2 text-sm">
                <Checkbox
                  checked={repliesEnabled}
                  onCheckedChange={(checked) => setRepliesEnabled(checked === true)}
                />
                امکان پاسخ
              </label>
              <Button type="submit" disabled={!subject.trim() || !body.trim()}>
                پیش‌نمایش
              </Button>
            </form>
          )}
        </DialogContent>
      </Dialog>
      {success && (
        <span role="status" className="text-sm text-emerald-700">
          پیام ارسال شد.
        </span>
      )}
    </div>
  );
}

export function AdminMessageQueue() {
  const [selected, setSelected] = useState<string | null>(null);
  return selected ? (
    <div className="space-y-4">
      <Button variant="outline" onClick={() => setSelected(null)}>
        بازگشت به صف
      </Button>
      <MessageConversation id={selected} staff />
    </div>
  ) : (
    <MessageInbox staff onOpen={setSelected} />
  );
}
