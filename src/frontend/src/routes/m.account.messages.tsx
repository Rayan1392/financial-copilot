import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { MessageInbox } from "@/components/app/in-app-messages";
import { MobileScreen } from "@/components/mobile/mobile-screen";

export const Route = createFileRoute("/m/account/messages")({ component: InboxPage });

function InboxPage() {
  const navigate = useNavigate();
  return (
    <MobileScreen title="پیام‌ها" backFallback="/m/account" showTabBar={false} hideNewChat>
      <div className="px-4 py-5">
        <MessageInbox
          onOpen={(conversationId) =>
            void navigate({ to: "/m/account/messages/$conversationId", params: { conversationId } })
          }
        />
      </div>
    </MobileScreen>
  );
}
