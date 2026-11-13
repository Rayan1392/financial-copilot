import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  Outlet,
  Link,
  createRootRouteWithContext,
  HeadContent,
  Scripts,
  useRouter,
} from "@tanstack/react-router";
import { useEffect } from "react";
import { subscribeToAuthChanges } from "@/integrations/financial-copilot/auth";
import { useQueryClient } from "@tanstack/react-query";
import { ThemeInitializer } from "@/components/app/theme-toggle";
import { PwaRuntime } from "@/lib/pwa/update-toast";
import { applyExperienceOverride } from "@/lib/experience/client";

import appCss from "../styles.css?url";

function NotFoundComponent() {
  return (
    <div dir="rtl" className="flex min-h-screen items-center justify-center bg-background px-4">
      <div className="max-w-md text-center">
        <h1 className="text-7xl font-bold text-foreground">۴۰۴</h1>
        <p className="mt-4 text-sm text-muted-foreground">صفحه‌ای که می‌خواهید پیدا نشد.</p>
        <Link
          to="/"
          className="mt-6 inline-flex rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground"
        >
          بازگشت به خانه
        </Link>
      </div>
    </div>
  );
}

function ErrorComponent({ error, reset }: { error: Error; reset: () => void }) {
  const router = useRouter();
  return (
    <div dir="rtl" className="flex min-h-screen items-center justify-center bg-background px-4">
      <div className="max-w-md text-center">
        <h1 className="text-xl font-semibold text-foreground">خطایی رخ داد</h1>
        <p className="mt-2 text-sm text-muted-foreground">{error.message}</p>
        <button
          onClick={() => {
            router.invalidate();
            reset();
          }}
          className="mt-6 rounded-md bg-primary px-4 py-2 text-sm font-medium text-primary-foreground"
        >
          تلاش مجدد
        </button>
      </div>
    </div>
  );
}

export const Route = createRootRouteWithContext<{ queryClient: QueryClient }>()({
  head: () => ({
    meta: [
      { charSet: "utf-8" },
      { name: "viewport", content: "width=device-width, initial-scale=1" },
      { title: "ساپیو - دستیار هوشمند بازار" },
      { name: "description", content: "کوپایلوت هوش مصنوعی بازار سرمایه ایران" },
      { name: "theme-color", content: "#09090b" },
      { property: "og:title", content: "ساپیو - دستیار هوشمند بازار" },
      { name: "twitter:title", content: "ساپیو - دستیار هوشمند بازار" },
      { property: "og:description", content: "کوپایلوت هوش مصنوعی بازار سرمایه ایران" },
      { name: "twitter:description", content: "کوپایلوت هوش مصنوعی بازار سرمایه ایران" },
      {
        property: "og:image",
        content: "https://sapioai.ir/sapio-social-preview.png",
      },
      {
        name: "twitter:image",
        content: "https://sapioai.ir/sapio-social-preview.png",
      },
      { name: "twitter:card", content: "summary_large_image" },
      { property: "og:type", content: "website" },
      // Feature 153 PWA: installed-app metadata for iOS (Android reads the manifest).
      { name: "apple-mobile-web-app-capable", content: "yes" },
      { name: "mobile-web-app-capable", content: "yes" },
      { name: "apple-mobile-web-app-title", content: "ساپیو" },
      { name: "apple-mobile-web-app-status-bar-style", content: "black-translucent" },
    ],
    links: [
      { rel: "stylesheet", href: appCss },
      { rel: "manifest", href: "/manifest.webmanifest" },
      { rel: "icon", type: "image/x-icon", href: "/favicon.ico" },
      { rel: "icon", type: "image/png", sizes: "32x32", href: "/favicon-32x32.png" },
      { rel: "icon", type: "image/png", sizes: "16x16", href: "/favicon-16x16.png" },
      { rel: "apple-touch-icon", sizes: "180x180", href: "/apple-touch-icon.png" },
      // Fonts are self-hosted (public/fonts, declared in styles.css); the text face is preloaded.
      {
        rel: "preload",
        as: "font",
        type: "font/woff2",
        href: "/fonts/vazirmatn-variable.woff2",
        crossOrigin: "anonymous",
      },
    ],
  }),
  shellComponent: RootShell,
  component: RootComponent,
  notFoundComponent: NotFoundComponent,
  errorComponent: ErrorComponent,
});

function RootShell({ children }: { children: React.ReactNode }) {
  return (
    <html lang="fa" dir="rtl" className="dark" suppressHydrationWarning>
      <head>
        <HeadContent />
      </head>
      <body>
        {children}
        <Scripts />
      </body>
    </html>
  );
}

/** Applies a one-shot `?experience=mobile|desktop` choice on any page (spec §7.5). */
function ExperienceOverride() {
  useEffect(() => applyExperienceOverride(), []);
  return null;
}

function AuthSync() {
  const router = useRouter();
  const qc = useQueryClient();
  useEffect(() => {
    return subscribeToAuthChanges(() => {
      qc.clear();
      router.invalidate();
    });
  }, [router, qc]);

  return null;
}

function RootComponent() {
  const { queryClient } = Route.useRouteContext();
  return (
    <QueryClientProvider client={queryClient}>
      <ThemeInitializer />
      <AuthSync />
      <ExperienceOverride />
      <Outlet />
      <PwaRuntime />
    </QueryClientProvider>
  );
}
