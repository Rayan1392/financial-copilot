import { render, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type {
  CyclicalWavesV2Run,
  CyclicalWavesV2StatusResponse,
} from "@/integrations/financial-copilot/data-admin-client";

const getCyclicalWavesV2Status = vi.fn<() => Promise<CyclicalWavesV2StatusResponse>>();

vi.mock("@/integrations/financial-copilot/data-admin-client", () => ({
  getCyclicalWavesV2Status: () => getCyclicalWavesV2Status(),
  runCyclicalWavesV2CatalogSync: vi.fn(),
  runCyclicalWavesV2TickerSync: vi.fn(),
  queueCyclicalWavesV2ScreenerSync: vi.fn(),
  queueCyclicalWavesV2IncomeStatementsSync: vi.fn(),
  queueCyclicalWavesV2AnalysisSync: vi.fn(),
  queueCyclicalWavesV2FullSync: vi.fn(),
}));

const { CyclicalWavesV2Page } = await import("../cyclicalwaves-v2-page");

const ACTION_LABELS = [
  "کاتالوگ نمادها",
  "همگام‌سازی یک نماد",
  "اسکرینر مالی",
  "صورت‌های سود و زیان",
  "تحلیل جامع",
  "همگام‌سازی کامل",
];

function run(overrides: Partial<CyclicalWavesV2Run> = {}): CyclicalWavesV2Run {
  return {
    runId: "11111111-1111-1111-1111-111111111111",
    scope: "Screener",
    ticker: null,
    analysisMode: null,
    trigger: "Admin",
    status: "Succeeded",
    requestedAt: "2026-10-30T10:00:00Z",
    startedAt: "2026-10-30T10:00:05Z",
    completedAt: "2026-10-30T10:02:00Z",
    counts: { total: 10, processed: 10, updated: 4, unchanged: 6, failed: 0 },
    errorSummary: null,
    requestedBy: "User:admin",
    ...overrides,
  };
}

function status(overrides: Partial<CyclicalWavesV2StatusResponse> = {}): CyclicalWavesV2StatusResponse {
  return {
    ingestionEnabled: true,
    dailySyncEnabled: false,
    dailySyncCronUtc: "30 1 * * *",
    nextScheduledRunUtc: null,
    credentialsConfigured: true,
    chatProvider: "CyclicalWavesV2",
    activeRun: null,
    lastSuccessByScope: [],
    recentRuns: [run({ errorSummary: "ticker X: timeout" })],
    ...overrides,
  };
}

function actionButton(label: string) {
  return screen.getByRole("button", { name: label });
}

describe("CyclicalWavesV2Page", () => {
  afterEach(() => {
    getCyclicalWavesV2Status.mockReset();
  });

  it("renders the status header and enables actions when no run is active", async () => {
    getCyclicalWavesV2Status.mockResolvedValue(status());

    render(<CyclicalWavesV2Page />);

    const header = await screen.findByRole("region", { name: "وضعیت CyclicalWaves v2" });
    expect(within(header).getByText("30 1 * * *")).toBeInTheDocument();
    expect(within(header).getByText("اعتبارنامه پیکربندی شده")).toBeInTheDocument();
    expect(within(header).getByText("CyclicalWavesV2")).toBeInTheDocument();
    expect(screen.getByText("ticker X: timeout")).toBeInTheDocument();

    for (const label of ACTION_LABELS.filter((l) => l !== "همگام‌سازی یک نماد"))
      expect(actionButton(label)).toBeEnabled();
    // Single-ticker sync stays disabled until a ticker is typed.
    expect(actionButton("همگام‌سازی یک نماد")).toBeDisabled();
  });

  it("disables every action button while a run is active", async () => {
    const active = run({ runId: "22222222-2222-2222-2222-222222222222", status: "Running", completedAt: null });
    getCyclicalWavesV2Status.mockResolvedValue(status({ activeRun: active, recentRuns: [active] }));

    render(<CyclicalWavesV2Page />);

    await screen.findByRole("region", { name: "وضعیت CyclicalWaves v2" });
    for (const label of ACTION_LABELS) expect(actionButton(label)).toBeDisabled();
  });
});
