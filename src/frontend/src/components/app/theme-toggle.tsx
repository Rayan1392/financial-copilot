import { Moon, Sun } from "lucide-react";
import { useEffect, useState } from "react";

type Theme = "dark" | "light";

const themeStorageKey = "financial-copilot-theme";

function storedTheme(): Theme {
  if (typeof window === "undefined") return "dark";
  return window.localStorage.getItem(themeStorageKey) === "light" ? "light" : "dark";
}

function applyTheme(theme: Theme) {
  const root = document.documentElement;
  root.classList.toggle("light", theme === "light");
  root.classList.toggle("dark", theme === "dark");
  root.style.colorScheme = theme;
}

/** Applies the persisted preference after hydration, including on public/auth pages. */
export function ThemeInitializer() {
  useEffect(() => applyTheme(storedTheme()), []);
  return null;
}

export function ThemeToggle() {
  const [theme, setTheme] = useState<Theme>("dark");

  useEffect(() => setTheme(storedTheme()), []);

  function toggleTheme() {
    const nextTheme = theme === "dark" ? "light" : "dark";
    setTheme(nextTheme);
    window.localStorage.setItem(themeStorageKey, nextTheme);
    applyTheme(nextTheme);
  }

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
