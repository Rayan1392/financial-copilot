import { useEffect, useState } from "react";

const HISTORY_STORAGE_KEY = "sapio-history-sidebar-collapsed";
const MARKET_STORAGE_KEY = "sapio-market-sidebar-collapsed";

export function useSidebarPreferences() {
  const [historyCollapsed, setHistoryCollapsed] = useState(false);
  const [marketCollapsed, setMarketCollapsed] = useState(false);
  const [preferencesLoaded, setPreferencesLoaded] = useState(false);

  useEffect(() => {
    try {
      setHistoryCollapsed(window.localStorage.getItem(HISTORY_STORAGE_KEY) === "true");
      setMarketCollapsed(window.localStorage.getItem(MARKET_STORAGE_KEY) === "true");
    } catch {
      // Keep the default expanded layout when browser storage is unavailable.
    } finally {
      setPreferencesLoaded(true);
    }
  }, []);

  useEffect(() => {
    if (!preferencesLoaded) return;
    try {
      window.localStorage.setItem(HISTORY_STORAGE_KEY, String(historyCollapsed));
    } catch {
      // The controls remain usable for this session if storage is unavailable.
    }
  }, [historyCollapsed, preferencesLoaded]);

  useEffect(() => {
    if (!preferencesLoaded) return;
    try {
      window.localStorage.setItem(MARKET_STORAGE_KEY, String(marketCollapsed));
    } catch {
      // The controls remain usable for this session if storage is unavailable.
    }
  }, [marketCollapsed, preferencesLoaded]);

  return {
    historyCollapsed,
    marketCollapsed,
    toggleHistory: () => setHistoryCollapsed((value) => !value),
    toggleMarket: () => setMarketCollapsed((value) => !value),
  };
}
