import { useRef, type ReactNode, type RefObject } from "react";
import { TopAppBar, type TopAppBarProps } from "@/components/mobile/top-app-bar";
import { BottomNav } from "@/components/mobile/bottom-nav";
import { OfflineBanner } from "@/components/mobile/offline-banner";
import { DesktopSuggestion } from "@/components/mobile/desktop-suggestion";
import { useKeyboardInset } from "@/hooks/use-keyboard-inset";
import { useOnlineStatus } from "@/hooks/use-online-status";

interface MobileScreenProps extends TopAppBarProps {
  children: ReactNode;
  /** Docked content above the tab bar, such as the composer. */
  footer?: ReactNode;
  /** Hidden on pushed screens and while a question is pending (design §2). */
  showTabBar?: boolean;
  mainRef?: RefObject<HTMLElement | null>;
}

/**
 * The frame of every `/m` screen: top app bar, offline banner, one scrolling main region, optional docked
 * footer, and the bottom tab bar. Uses dynamic viewport height and safe-area insets; the tab bar hides while the
 * on-screen keyboard is open.
 */
export function MobileScreen({
  children,
  footer,
  showTabBar = true,
  mainRef,
  ...bar
}: MobileScreenProps) {
  const rootRef = useRef<HTMLDivElement>(null);
  const ownMainRef = useRef<HTMLElement>(null);
  const scrollRef = mainRef ?? ownMainRef;
  const keyboardInset = useKeyboardInset(rootRef);
  const online = useOnlineStatus();

  return (
    <div
      ref={rootRef}
      dir="rtl"
      // iOS does not shrink dvh for the keyboard; subtracting the measured inset keeps the composer above it.
      style={{ height: "calc(100dvh - var(--keyboard-inset, 0px))" }}
      className="flex h-screen w-full flex-col overflow-hidden bg-background text-m-body text-foreground"
    >
      <TopAppBar {...bar} />
      <OfflineBanner online={online} />
      <DesktopSuggestion />
      {/* `relative` makes main the containing block of absolute descendants (sr-only labels in the watchlist strip),
          so they are clipped here. Escaping them widened the document; Android Chrome then grew the layout viewport
          and the keyboard check collapsed the screen to its app bar (hotfix 1.12.2). */}
      <main
        ref={scrollRef}
        className="relative min-h-0 flex-1 overflow-y-auto overflow-x-hidden overscroll-contain px-[env(safe-area-inset-left)] scrollbar-thin"
      >
        {children}
      </main>
      {footer}
      {showTabBar && keyboardInset === 0 && (
        <BottomNav onReselect={() => scrollRef.current?.scrollTo({ top: 0, behavior: "smooth" })} />
      )}
    </div>
  );
}
