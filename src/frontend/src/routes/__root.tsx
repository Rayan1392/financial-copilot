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
        content:
          "https://storage.googleapis.com/gpt-engineer-file-uploads/bwtrv7ryw1gzeXPKAE6bV9W35wY2/social-images/social-1779702784036-a92e9a13-c65b-47ce-add4-40e79fb35ddd.webp",
      },
      {
        name: "twitter:image",
        content:
          "https://storage.googleapis.com/gpt-engineer-file-uploads/bwtrv7ryw1gzeXPKAE6bV9W35wY2/social-images/social-1779702784036-a92e9a13-c65b-47ce-add4-40e79fb35ddd.webp",
      },
      { name: "twitter:card", content: "summary_large_image" },
      { property: "og:type", content: "website" },
    ],
    links: [
      { rel: "stylesheet", href: appCss },
      { rel: "preconnect", href: "https://fonts.googleapis.com" },
      { rel: "preconnect", href: "https://fonts.gstatic.com", crossOrigin: "" },
      {
        rel: "stylesheet",
        href: "https://fonts.googleapis.com/css2?family=Vazirmatn:wght@300;400;500;600;700;800&family=JetBrains+Mono:wght@400;500;600&display=swap",
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
      <Outlet />
    </QueryClientProvider>
  );
}
