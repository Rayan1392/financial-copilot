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

/**
 * Icon-only toggle. The icon and the label both name the action (the theme you switch to), never the
 * current state, so the sun in dark mode reads unambiguously as "switch to light".
 */
export function ThemeToggle() {
  const { theme, toggleTheme } = useTheme();
  const nextLabel = theme === "dark" ? "تغییر به تم روشن" : "تغییر به تم تیره";
  return (
    <button
      type="button"
      onClick={toggleTheme}
      aria-label={nextLabel}
      title={nextLabel}
      className="group flex size-10 items-center justify-center rounded-xl text-muted-foreground transition hover:bg-emerald-soft/60 hover:text-emerald focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
    >
      {theme === "dark" ? (
        <Sun className="size-5 transition-transform group-hover:rotate-12" aria-hidden="true" />
      ) : (
        <Moon className="size-5 transition-transform group-hover:-rotate-12" aria-hidden="true" />
      )}
    </button>
  );
}
