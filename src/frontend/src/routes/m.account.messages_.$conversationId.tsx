import { createFileRoute } from "@tanstack/react-router";
import { MessageConversation } from "@/components/app/in-app-messages";
import { MobileScreen } from "@/components/mobile/mobile-screen";

export const Route = createFileRoute("/m/account/messages_/$conversationId")({
  component: ConversationPage,
});

function ConversationPage() {
  const { conversationId } = Route.useParams();
  return (
    <MobileScreen
      title="گفتگوی پشتیبانی"
      backFallback="/m/account/messages"
      showTabBar={false}
      hideNewChat
    >
      <div className="px-4 py-5">
        <MessageConversation id={conversationId} />
      </div>
    </MobileScreen>
  );
}
