import { useEffect, useState } from "react";

/** Tracks the app theme (`.light` on <html>) so chart colors follow the theme toggle. */
export function useInteractiveChartTheme(): "light" | "dark" {
  const [theme, setTheme] = useState<"light" | "dark">("dark");

  useEffect(() => {
    const root = document.documentElement;
    const update = () => setTheme(root.classList.contains("light") ? "light" : "dark");
    update();
    const observer = new MutationObserver(update);
    observer.observe(root, { attributes: true, attributeFilter: ["class"] });
    return () => observer.disconnect();
  }, []);

  return theme;
}
