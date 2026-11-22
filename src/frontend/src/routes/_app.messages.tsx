import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { MessageInbox } from "@/components/app/in-app-messages";

export const Route = createFileRoute("/_app/messages")({ component: InboxPage });

function InboxPage() {
  const navigate = useNavigate();
  return (
    <div className="mx-auto w-full max-w-3xl space-y-4 px-4 py-6">
      <h1 className="text-xl font-bold">پیام‌ها</h1>
      <MessageInbox
        onOpen={(conversationId) =>
          void navigate({ to: "/messages/$conversationId", params: { conversationId } })
        }
      />
    </div>
  );
}
