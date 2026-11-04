import { StrictMode } from "react";
import { act, renderHook, waitFor } from "@testing-library/react";
import { useSidebarPreferences } from "@/components/app/use-sidebar-preferences";

const HISTORY_KEY = "sapio-history-sidebar-collapsed";
const MARKET_KEY = "sapio-market-sidebar-collapsed";

function StrictModeWrapper({ children }: { children: React.ReactNode }) {
  return <StrictMode>{children}</StrictMode>;
}

describe("useSidebarPreferences", () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  it("restores independent choices and persists the last choice across remounts", async () => {
    window.localStorage.setItem(HISTORY_KEY, "true");
    window.localStorage.setItem(MARKET_KEY, "false");

    const firstMount = renderHook(() => useSidebarPreferences(), { wrapper: StrictModeWrapper });
    await waitFor(() => {
      expect(firstMount.result.current.historyCollapsed).toBe(true);
      expect(firstMount.result.current.marketCollapsed).toBe(false);
    });

    act(() => firstMount.result.current.toggleMarket());
    expect(window.localStorage.getItem(HISTORY_KEY)).toBe("true");
    expect(window.localStorage.getItem(MARKET_KEY)).toBe("true");

    firstMount.unmount();
    const secondMount = renderHook(() => useSidebarPreferences(), { wrapper: StrictModeWrapper });
    await waitFor(() => {
      expect(secondMount.result.current.historyCollapsed).toBe(true);
      expect(secondMount.result.current.marketCollapsed).toBe(true);
    });

    act(() => secondMount.result.current.toggleHistory());
    expect(window.localStorage.getItem(HISTORY_KEY)).toBe("false");
    expect(window.localStorage.getItem(MARKET_KEY)).toBe("true");
  });
});
