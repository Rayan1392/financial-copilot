import { createFileRoute, Link } from "@tanstack/react-router";
import { MessageConversation } from "@/components/app/in-app-messages";

export const Route = createFileRoute("/_app/messages_/$conversationId")({
  component: ConversationPage,
});

function ConversationPage() {
  const { conversationId } = Route.useParams();
  return (
    <div className="mx-auto w-full max-w-3xl space-y-4 px-4 py-6">
      <Link to="/messages" className="text-sm font-medium underline underline-offset-4">
        بازگشت به پیام‌ها
      </Link>
      <MessageConversation id={conversationId} />
    </div>
  );
}
