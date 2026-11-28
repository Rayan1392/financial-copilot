import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { GetCompanyIdPage } from "../get-company-id-page";
import { financialCopilotApi } from "@/integrations/financial-copilot/api-client";

vi.mock("@/integrations/financial-copilot/api-client", () => ({
  FinancialCopilotApiError: class FinancialCopilotApiError extends Error {
    constructor(message: string, public status: number) {
      super(message);
    }
  },
  financialCopilotApi: vi.fn(),
}));

function openMonthlyForm() {
  fireEvent.click(screen.getByRole("tab", { name: /تولید و فروش ماهانه/ }));
}

function fillRequiredFields(month = "") {
  fireEvent.change(screen.getByLabelText("نماد", { selector: "#backfill-symbol" }), {
    target: { value: "فماک" },
  });
  fireEvent.change(screen.getByLabelText("سال شمسی"), { target: { value: "1405" } });
  if (month) fireEvent.change(screen.getByLabelText("ماه شمسی"), { target: { value: month } });
}

function response(month: number) {
  return {
    requestId: `run-${month}`,
    companyId: 19,
    shamsiYear: 1405,
    shamsiMonth: month,
    status: "Completed",
    alreadyProcessed: false,
    processedRecords: 1,
    errorCount: 0,
    errorMessage: null,
    completedAt: null,
  };
}

describe("GetCompanyIdPage monthly backfill", () => {
  beforeEach(() => vi.clearAllMocks());

  it("fetches the completed months in order and continues after a month failure", async () => {
    vi.mocked(financialCopilotApi)
      .mockResolvedValueOnce({ shamsiYear: 1405, months: [1, 2, 3] })
      .mockResolvedValueOnce(response(1))
      .mockRejectedValueOnce(new Error("provider unavailable"))
      .mockResolvedValueOnce(response(3));

    render(<GetCompanyIdPage />);
    openMonthlyForm();
    fillRequiredFields();
    fireEvent.click(screen.getByRole("button", { name: "اجرا" }));

    expect(await screen.findByText("پایان عملیات: 3 ماه بررسی شد.")).toBeTruthy();
    const ingestionCalls = vi.mocked(financialCopilotApi).mock.calls.filter(([path]) =>
      path.endsWith("/single-company-month"),
    );
    expect(ingestionCalls.map(([, init]) => JSON.parse(String(init?.body)).shamsiMonth)).toEqual([1, 2, 3]);
    expect(screen.getByText("provider unavailable")).toBeTruthy();
  });

  it("preserves one-month submission and displays an empty-year result", async () => {
    vi.mocked(financialCopilotApi)
      .mockResolvedValueOnce(response(5))
      .mockResolvedValueOnce({ shamsiYear: 1405, months: [] });

    const { rerender } = render(<GetCompanyIdPage />);
    openMonthlyForm();
    fillRequiredFields("5");
    fireEvent.click(screen.getByRole("button", { name: "اجرا" }));
    await screen.findByText("run-5");
    expect(financialCopilotApi).toHaveBeenCalledTimes(1);

    rerender(<GetCompanyIdPage />);
    fireEvent.change(screen.getByLabelText("ماه شمسی"), { target: { value: "" } });
    fireEvent.click(screen.getByRole("button", { name: "اجرا" }));
    await waitFor(() =>
      expect(screen.getByText("برای سال انتخاب‌شده هنوز ماه کامل‌شده‌ای وجود ندارد.")).toBeTruthy(),
    );
    expect(financialCopilotApi).toHaveBeenCalledTimes(2);
  });

  it("aborts the active request when the form unmounts", async () => {
    let resolveMonthList: ((value: { shamsiYear: number; months: number[] }) => void) | undefined;
    vi.mocked(financialCopilotApi).mockImplementationOnce(
      () => new Promise((resolve) => { resolveMonthList = resolve; }),
    );

    const view = render(<GetCompanyIdPage />);
    openMonthlyForm();
    fillRequiredFields();
    fireEvent.click(screen.getByRole("button", { name: "اجرا" }));
    await waitFor(() => expect(financialCopilotApi).toHaveBeenCalledTimes(1));
    view.unmount();
    expect(vi.mocked(financialCopilotApi).mock.calls[0][1]?.signal?.aborted).toBe(true);
    resolveMonthList?.({ shamsiYear: 1405, months: [1] });
  });
});
