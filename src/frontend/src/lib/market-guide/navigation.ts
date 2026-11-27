import { marketGuideGroups, type GuideAction } from "./catalog";

export type GuideSource = "desktop" | "mobile";

export function parseGuideSearch(search: Record<string, unknown>): {
  source?: GuideSource;
  example?: string;
} {
  const { source, example } = search;
  return {
    source: source === "desktop" || source === "mobile" ? source : undefined,
    example:
      typeof example === "string" &&
      marketGuideGroups.some((group) => group.examples.some((item) => item.id === example))
        ? example
        : undefined,
  };
}

export function guideReturnPath(example: string, source: GuideSource) {
  return `/market-guide?${new URLSearchParams({ example, source })}`;
}

export function guideDestination(action: GuideAction, source: GuideSource) {
  const mobile = source === "mobile";
  switch (action) {
    case "chat":
      return { to: mobile ? "/m" : "/chat" } as const;
    case "watchlist":
      return { to: mobile ? "/m/watchlist" : "/followed-symbols" } as const;
    case "events":
      return { to: "/followed-symbols", search: { tab: "events" } } as const;
    case "telegram":
      return { to: "/telegram-link" } as const;
    case "support":
      return { to: mobile ? "/m/account/messages" : "/messages" } as const;
  }
}
