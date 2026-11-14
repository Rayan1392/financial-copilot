import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  createMemoryHistory,
  createRootRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router";
import { render, screen, waitFor, within } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { OwnProfile } from "@/integrations/financial-copilot/auth";

const getOwnProfile = vi.fn<() => Promise<OwnProfile>>();

vi.mock("@/integrations/financial-copilot/auth", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/integrations/financial-copilot/auth")>()),
  getOwnProfile: () => getOwnProfile(),
}));

const { HeaderAccount, OWN_PROFILE_QUERY_KEY } = await import("../header-account");
const { ChatHeader } = await import("../chat-header");

const PERSIAN_NAME = "کامران عبدی";

function expectNoAccountChip() {
  expect(screen.queryByRole("link", { name: /پروفایل حساب/ })).not.toBeInTheDocument();
  expect(screen.queryByTestId("header-account-name")).not.toBeInTheDocument();
}

function profile(displayName: string | null, overrides: Partial<OwnProfile> = {}): OwnProfile {
  return {
    displayName,
    firstName: null,
    lastName: null,
    nationalIdMasked: null,
    mobileMasked: null,
    profileSyncedAtUtc: null,
    contactMobileMasked: null,
    avatarAvailable: false,
    ...overrides,
  };
}

/** Renders inside a real in-memory router (Link needs router context) and the given query client. */
function inRouter(ui: ReactNode, client: QueryClient) {
  const router = createRouter({
    routeTree: createRootRoute({
      component: () => (
        <>
          <span data-testid="router-ready" />
          {ui}
        </>
      ),
    }),
    history: createMemoryHistory({ initialEntries: ["/chat"] }),
  });
  return (
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  );
}

function renderWithClient(ui: ReactNode, client = new QueryClient()) {
  const view = render(inRouter(ui, client));
  return { ...view, client };
}

async function settled(client: QueryClient) {
  await screen.findByTestId("router-ready");
  await waitFor(() =>
    expect(client.getQueryState(OWN_PROFILE_QUERY_KEY)?.fetchStatus).toBe("idle"),
  );
  await waitFor(() =>
    expect(client.getQueryState(OWN_PROFILE_QUERY_KEY)?.status).not.toBe("pending"),
  );
}

describe("HeaderAccount", () => {
  afterEach(() => {
    getOwnProfile.mockReset();
  });

  it("shows a valid Persian display name as a link to the profile page", async () => {
    getOwnProfile.mockResolvedValue(profile(PERSIAN_NAME));
    renderWithClient(<HeaderAccount />);

    const link = await screen.findByRole("link", { name: `پروفایل حساب: ${PERSIAN_NAME}` });
    expect(link).toHaveAttribute("href", "/profile");
    expect(within(link).getByTestId("header-account-name")).toHaveTextContent(PERSIAN_NAME);
    // Avatar initial is decorative; the accessible name comes from the link label.
    expect(within(link).getByText("ک")).toHaveAttribute("aria-hidden", "true");
  });

  it("trims surrounding whitespace", async () => {
    getOwnProfile.mockResolvedValue(profile(`  ${PERSIAN_NAME} `));
    renderWithClient(<HeaderAccount />);

    expect(await screen.findByTestId("header-account-name")).toHaveTextContent(
      new RegExp(`^${PERSIAN_NAME}$`),
    );
  });

  it.each([
    ["null", null],
    ["empty", ""],
    ["whitespace-only", "   "],
  ])("renders nothing for a %s display name", async (_label, displayName) => {
    getOwnProfile.mockResolvedValue(profile(displayName));
    const { client } = renderWithClient(<HeaderAccount />);

    await settled(client);
    expectNoAccountChip();
    expect(screen.queryByText(/null|undefined/)).not.toBeInTheDocument();
  });

  it("renders nothing when displayName is missing from the response", async () => {
    getOwnProfile.mockResolvedValue({
      ...profile(null),
      displayName: undefined,
    } as unknown as OwnProfile);
    const { client } = renderWithClient(<HeaderAccount />);

    await settled(client);
    expectNoAccountChip();
  });

  it("does not fall back to first/last name or expose other profile fields", async () => {
    getOwnProfile.mockResolvedValue(
      profile(null, { firstName: "Ali", lastName: "Rezaei", mobileMasked: "0912•••4567" }),
    );
    const { client } = renderWithClient(<HeaderAccount />);

    await settled(client);
    expectNoAccountChip();
  });

  it("renders nothing and does not retry when the profile request fails", async () => {
    getOwnProfile.mockRejectedValue(new Error("404"));
    const { client } = renderWithClient(<HeaderAccount />);

    await settled(client);
    expectNoAccountChip();
    expect(getOwnProfile).toHaveBeenCalledTimes(1);
  });

  it("truncates a long name with an ellipsis and keeps the full name available", async () => {
    const longName = "سید محمدحسین عبدالرحمن‌زاده اصفهانی طباطبایی نژاد".repeat(2);
    getOwnProfile.mockResolvedValue(profile(longName));
    renderWithClient(<HeaderAccount />);

    const name = await screen.findByTestId("header-account-name");
    expect(name).toHaveTextContent(longName);
    expect(name).toHaveClass("truncate", "max-w-40", "lg:max-w-56");
    const link = screen.getByRole("link", { name: `پروفایل حساب: ${longName}` });
    expect(link).toHaveAttribute("title", longName);
    expect(link).toHaveClass("min-w-0");
  });

  it("hides only the name text on mobile and keeps the avatar link for account navigation", async () => {
    getOwnProfile.mockResolvedValue(profile(PERSIAN_NAME));
    renderWithClient(<HeaderAccount />);

    const name = await screen.findByTestId("header-account-name");
    expect(name).toHaveClass("hidden", "sm:block");
    const avatar = screen.getByText("ک");
    expect(avatar).not.toHaveClass("hidden");
    expect(avatar).toHaveClass("shrink-0");
    expect(screen.getByRole("link", { name: `پروفایل حساب: ${PERSIAN_NAME}` })).toBeVisible();
  });

  it("requests the profile once for multiple consumers, re-renders and remounts", async () => {
    getOwnProfile.mockResolvedValue(profile(PERSIAN_NAME));
    const client = new QueryClient();
    const { rerender, unmount } = renderWithClient(
      <>
        <HeaderAccount />
        <HeaderAccount />
      </>,
      client,
    );
    expect(await screen.findAllByTestId("header-account-name")).toHaveLength(2);

    rerender(
      inRouter(
        <>
          <HeaderAccount />
          <HeaderAccount />
        </>,
        client,
      ),
    );
    await screen.findAllByTestId("header-account-name");
    unmount();
    renderWithClient(<HeaderAccount />, client);
    await screen.findByTestId("header-account-name");

    expect(getOwnProfile).toHaveBeenCalledTimes(1);
  });
});

describe("ChatHeader account placement", () => {
  afterEach(() => {
    getOwnProfile.mockReset();
  });

  it("puts the account chip first in the right-side (RTL start) group and keeps existing navigation", async () => {
    getOwnProfile.mockResolvedValue(profile(PERSIAN_NAME));
    renderWithClient(<ChatHeader />);

    const account = await screen.findByRole("link", { name: `پروفایل حساب: ${PERSIAN_NAME}` });
    const header = screen.getByRole("banner");
    const startGroup = header.firstElementChild as HTMLElement;
    expect(startGroup).toContainElement(account);
    expect(startGroup.firstElementChild).toContainElement(account);
    // The marketing tagline was removed: it was truncated at 1440px and crowded the header.
    expect(screen.queryByText("تحلیل بازار، ساده‌تر از همیشه")).not.toBeInTheDocument();

    const nav = screen.getByRole("navigation", { name: "ناوبری اصلی" });
    expect(within(nav).getByRole("link", { name: "خانه" })).toHaveAttribute("href", "/chat");
    expect(within(nav).getByRole("link", { name: "دیده‌بان" })).toHaveAttribute(
      "href",
      "/followed-symbols",
    );
    expect(within(nav).getByRole("link", { name: "اطلاعیه‌های کدال" })).toHaveAttribute(
      "href",
      "/disclosures",
    );
    expect(screen.getAllByRole("link", { name: /پروفایل حساب/ })).toHaveLength(1);
    expect(getOwnProfile).toHaveBeenCalledTimes(1);
  });

  it("keeps the header intact without an account chip when no display name exists", async () => {
    getOwnProfile.mockResolvedValue(profile(null));
    const { client } = renderWithClient(<ChatHeader />);

    await settled(client);
    expect(screen.queryByRole("link", { name: /پروفایل حساب/ })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "خانه" })).toBeInTheDocument();
  });

  it("marks only the current page's link as active", async () => {
    getOwnProfile.mockResolvedValue(profile(null));
    const { client } = renderWithClient(<ChatHeader />);

    await settled(client);
    const home = screen.getByRole("link", { name: "خانه" });
    expect(home).toHaveAttribute("aria-current", "page");
    expect(home).toHaveClass("text-emerald");
    for (const name of ["دیده‌بان", "اطلاعیه‌های کدال"]) {
      const link = screen.getByRole("link", { name });
      expect(link).not.toHaveAttribute("aria-current");
      expect(link).toHaveClass("text-muted-foreground");
    }
  });

  it("shows the same visible text as the accessible name on the main links", async () => {
    getOwnProfile.mockResolvedValue(profile(null));
    const { client } = renderWithClient(<ChatHeader />);

    await settled(client);
    for (const name of ["خانه", "دیده‌بان", "اطلاعیه‌های کدال"]) {
      expect(within(screen.getByRole("link", { name })).getByText(name)).toHaveClass(
        "hidden",
        "@3xl:inline",
      );
    }
  });

  it("shows the brand only where no sidebar carries it", async () => {
    getOwnProfile.mockResolvedValue(profile(null));
    const { client, unmount } = renderWithClient(<ChatHeader />);

    await settled(client);
    expect(screen.getByTestId("header-brand")).not.toHaveClass("md:hidden");
    unmount();

    const second = renderWithClient(<ChatHeader withSidebar />);
    await settled(second.client);
    // Below md the sidebar is hidden, so the header keeps the brand there only.
    expect(screen.getByTestId("header-brand")).toHaveClass("md:hidden");
  });

  it("renders the theme toggle as a single icon button named by its action", async () => {
    getOwnProfile.mockResolvedValue(profile(null));
    const { client } = renderWithClient(<ChatHeader />);

    await settled(client);
    const toggle = screen.getByRole("button", { name: "تغییر به تم روشن" });
    expect(toggle).toHaveAttribute("title", "تغییر به تم روشن");
    expect(toggle).toHaveTextContent("");
  });
});
