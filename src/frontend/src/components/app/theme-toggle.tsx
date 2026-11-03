import { Moon, Sun } from "lucide-react";
import { useEffect, useState } from "react";

export type Theme = "dark" | "light";

const themeStorageKey = "financial-copilot-theme";

/** Browser UI colors matching `--background` in each theme (the dark one is also in the PWA manifest). */
const THEME_COLORS: Record<Theme, string> = { dark: "#09090b", light: "#f7f7f9" };

function storedTheme(): Theme {
  if (typeof window === "undefined") return "dark";
  try {
    return window.localStorage.getItem(themeStorageKey) === "light" ? "light" : "dark";
  } catch {
    return "dark";
  }
}

function applyTheme(theme: Theme) {
  const root = document.documentElement;
  root.classList.toggle("light", theme === "light");
  root.classList.toggle("dark", theme === "dark");
  root.style.colorScheme = theme;
  document.querySelector('meta[name="theme-color"]')?.setAttribute("content", THEME_COLORS[theme]);
}

/** Applies the persisted preference after hydration, including on public/auth pages. */
export function ThemeInitializer() {
  useEffect(() => applyTheme(storedTheme()), []);
  return null;
}

/** The current theme and a toggle that persists it; shared by the desktop toggle and the mobile account row. */
export function useTheme() {
  const [theme, setTheme] = useState<Theme>("dark");

  useEffect(() => setTheme(storedTheme()), []);

  function toggleTheme() {
    const nextTheme = theme === "dark" ? "light" : "dark";
    setTheme(nextTheme);
    try {
      window.localStorage.setItem(themeStorageKey, nextTheme);
    } catch {
      // Storage unavailable: the theme still applies for this page view.
    }
    applyTheme(nextTheme);
  }

  return { theme, toggleTheme };
}

export function ThemeToggle() {
  const { theme, toggleTheme } = useTheme();
  const nextLabel = theme === "dark" ? "تغییر به تم روشن" : "تغییر به تم تیره";
  return (
    <button
      type="button"
      onClick={toggleTheme}
      aria-label={nextLabel}
      title={nextLabel}
      className="inline-flex items-center gap-1.5 rounded-md border border-hairline bg-surface px-2.5 py-1.5 text-xs text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
    >
      {theme === "dark" ? <Sun className="size-3.5" /> : <Moon className="size-3.5" />}
      <span>{theme === "dark" ? "تم تیره" : "تم روشن"}</span>
    </button>
  );
}
