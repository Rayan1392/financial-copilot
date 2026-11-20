import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { AnswerDensityContext } from "../answer-density";
import { CashFlowReport } from "../cash-flow-report/cash-flow-report";
import type { CashFlowReportResult } from "../cash-flow-report/cash-flow-report-types";
import cashFlowReportSource from "../cash-flow-report/cash-flow-report.tsx?raw";
import fixtures from "./fixtures/cash-flow-161.json";

vi.mock("recharts", () => ({
  Bar: () => null,
  CartesianGrid: () => null,
  ComposedChart: () => null,
  Legend: () => null,
  Line: () => null,
  ResponsiveContainer: () => null,
  Tooltip: () => null,
  XAxis: () => null,
  YAxis: () => null,
}));

const periods = ["Q2 1404", "Q3 1404", "Q4 1404", "Q1 1405", "Q2 1405"];
const result: CashFlowReportResult = {
  contractVersion: "161.2",
  outcome: "available",
  templateId: "General",
  company: { symbol: "فولاد", providerName: "NoavaranCurrentApi" },
  request: { view: "default", focus: "report" },
  anchorStatementId: "statement-q4",
  metadata: {
    viewLabel: "فصل‌های مستقل",
    basisLabel: "غیرتلفیقی",
    jalaliPeriodEnd: "۱۴۰۵/۰۶/۳۱",
    fiscalYear: 1405,
    isAudited: false,
    isRepresented: false,
    jalaliPublishedAt: "۱۴۰۵/۰۷/۳۰",
    providerName: "نوآوران امین",
    unit: "میلیون ریال",
  },
  columns: periods.map((label, index) => ({
    columnId: `q${index}`,
    statementIds: [`statement-q${index}`],
    kind: "quarter",
    label,
    periodMonths: 3,
    derivation: "independent_quarter",
    state: "available",
  })),
  sections: [
    {
      sectionId: "operating",
      label: "فعالیت‌های عملیاتی",
      rows: [
        {
          rowId: "operating_cash_flow",
          label: "جریان نقد عملیاتی",
          kind: "primary-total",
          sourceItemIds: [122, 121],
          cells: Object.fromEntries(
            periods.map((_, index) => [
              `q${index}`,
              { value: null, state: "unavailable", reason: "missing_predecessor" },
            ]),
          ),
        },
      ],
    },
  ],
  changes: {
    baseColumnId: "q3",
    compareColumnId: "q4",
    rows: {
      operating_cash_flow: {
        amount: null,
        percent: null,
        state: "unavailable",
        reason: "missing_predecessor",
      },
    },
  },
  kpis: [],
  verification: {
    status: "unverified",
    mainTotals: { status: "verified", checkIds: ["R1"] },
    cashBalance: { status: "unverified", checkIds: ["R2"] },
    detailCoverage: [
      {
        columnId: "q4",
        sectionId: "investing",
        status: "documented_difference",
        reportedSubtotal: -100,
        verifiedDetailTotal: -90,
        difference: -10,
        verifiedDetailCount: 1,
        unclassifiedItemCount: 0,
        unclassifiedItems: [],
        sourceReadComplete: true,
        statementIds: ["statement-q4"],
      },
    ],
    checks: [],
  },
  warnings: [],
  notices: [],
};

describe("CashFlowReport", () => {
  it("round-trips the A, B, quarterly-gap and failed 161.1 UI fixtures", () => {
    const cases = fixtures as unknown as Record<string, CashFlowReportResult>;
    for (const item of Object.values(cases)) {
      const restored = JSON.parse(JSON.stringify(item)) as CashFlowReportResult;
      expect(restored.contractVersion).toBe("161.1");
    }
    const a = cases.fixtureA.sections.flatMap((section) => section.rows);
    const b = cases.fixtureB.sections.flatMap((section) => section.rows);
    expect(a.find((row) => row.rowId === "item_128")?.cells.a.value).toBe(476771599);
    expect(b.find((row) => row.rowId === "item_125")?.cells.b.value).toBe(3536996227);
    expect(cases.quarterly.columns).toHaveLength(5);
    expect(cases.failed.outcome).toBe("failed");
  });

  it("renders dynamic five-quarter headers and does not invent unavailable amounts", () => {
    render(<CashFlowReport result={result} />);
    for (const period of periods) expect(screen.getAllByText(period).length).toBeGreaterThan(0);
    expect(screen.getAllByText("—").length).toBeGreaterThan(0);
    expect(screen.getByText("تغییر نسبت به Q1 1405")).toBeInTheDocument();
    expect(screen.getByLabelText("اعتبارسنجی حسابداری").textContent).toContain(
      "پوشش ریزاقلام: اختلاف تخصیص‌نیافته",
    );
    expect(screen.getByText(/واحد مبالغ: میلیون ریال/)).toBeInTheDocument();
    expect(screen.getByTestId("cash-flow-wide-table")).toBeInTheDocument();
    expect(screen.queryByText(/پیمایش افقی/)).not.toBeInTheDocument();
  });

  it("shows five latest-quarter headline values without KPI cards", () => {
    const withHeadlines: CashFlowReportResult = {
      ...result,
      kpis: [
        ["operating_cash_flow", "جریان نقد عملیاتی", 476771599],
        ["investing_cash_flow", "جریان نقد سرمایه‌گذاری", -386693808],
        ["financing_cash_flow", "جریان نقد تأمین مالی", -152999407],
        ["net_change", "تغییر خالص وجه نقد", -62921616],
        ["closing_cash", "مانده پایان دوره", 325830147],
      ].map(([metricId, label, value]) => ({
        metricId: String(metricId),
        label: String(label),
        columnId: "q4",
        value: Number(value),
        unit: "million_irr",
        state: "available",
        formula: "server-provided",
      })),
    };
    render(<CashFlowReport result={withHeadlines} />);
    const summary = screen.getByTestId("cash-flow-headline-summary");
    expect(summary.children).toHaveLength(5);
    expect(summary.querySelectorAll("article, button")).toHaveLength(0);
    expect(screen.queryByTestId("cash-flow-kpi-grid")).not.toBeInTheDocument();
  });

  it("preserves decimal precision and units for secondary ratios", () => {
    const withRatio: CashFlowReportResult = {
      ...result,
      kpis: [
        {
          metricId: "capex_coverage",
          label: "پوشش مخارج سرمایه‌ای",
          columnId: "q4",
          value: 1.36,
          unit: "multiple",
          state: "available",
          formula: "visual fixture",
        },
      ],
    };
    render(<CashFlowReport result={withRatio} />);
    fireEvent.click(screen.getByText(/شاخص‌های تکمیلی/));
    expect(screen.getByText("۱٫۳۶ برابر")).toBeInTheDocument();
  });

  it("translates mixed-sign reasons and never exposes internal English codes", () => {
    const bankResult: CashFlowReportResult = {
      ...result,
      kpis: [
        {
          metricId: "fx_share_of_cash_change",
          label: "سهم اثر نرخ ارز از تغییر موجودی نقد",
          columnId: "q4",
          value: null,
          unit: "percent",
          state: "unavailable",
          reason: "mixed_sign_change",
          formula: "internal",
        },
      ],
    };
    render(<CashFlowReport result={bankResult} />);
    expect(screen.getByText(/تغییرات اثر نرخ ارز و وجه نقد هم‌جهت نیست/)).toBeInTheDocument();
    expect(screen.queryByText(/mixed_sign_change/)).not.toBeInTheDocument();
  });

  it("deduplicates repeated warning entries and duplicate notices", () => {
    const warned: CashFlowReportResult = {
      ...result,
      warnings: [
        { code: "gap", severity: "warning", columnId: "q3", message: "داده تطبیقی ناقص است." },
        { code: "gap", severity: "warning", columnId: "q4", message: "داده تطبیقی ناقص است." },
      ],
      notices: ["یک نکته تکراری", "یک نکته تکراری"],
    };
    render(<CashFlowReport result={warned} />);
    expect(screen.getByText("داده تطبیقی ناقص است. (Q1 1405، Q2 1405)")).toBeInTheDocument();
    expect(screen.getAllByText("یک نکته تکراری")).toHaveLength(1);
  });

  it("shows every mobile quarter in newest-first vertical rows", () => {
    render(
      <AnswerDensityContext.Provider value="compact">
        <CashFlowReport result={result} />
      </AnswerDensityContext.Provider>,
    );
    const mobile = within(screen.getByTestId("cash-flow-mobile-sections"));
    const row = mobile.getByText("جریان نقد عملیاتی").parentElement;
    expect(row?.textContent?.indexOf("Q2 1405")).toBeLessThan(
      row?.textContent?.indexOf("Q1 1405") ?? -1,
    );
    for (const period of periods) expect(mobile.getByText(period)).toBeInTheDocument();
  });

  it("renders compact report sections as keyboard-operable accordions", () => {
    render(
      <AnswerDensityContext.Provider value="compact">
        <CashFlowReport result={result} />
      </AnswerDensityContext.Provider>,
    );
    const toggle = screen.getByRole("button", { name: /فعالیت‌های عملیاتی/ });
    expect(toggle).toHaveAttribute("aria-expanded", "true");
    fireEvent.click(toggle);
    expect(toggle).toHaveAttribute("aria-expanded", "false");
  });

  // BUG-038 / AC-U07: like Features 144 and 154 the report is a fixed light "paper" surface. Tailwind
  // `dark:` follows the operating system here (no class-based dark variant), and theme tokens follow
  // the site theme, so neither may appear in the report; the PNG export is light as well.
  it("uses a fixed light surface with no dark variants or theme tokens", () => {
    const themeDependent =
      /(^|\s)dark:|muted-foreground|bg-background|text-foreground|bg-card|bg-muted/;
    expect(cashFlowReportSource).not.toMatch(themeDependent);

    const warned: CashFlowReportResult = {
      ...result,
      warnings: [{ code: "gap", severity: "warning", columnId: "q3", message: "داده ناقص است." }],
      notices: ["یک نکته"],
    };
    const views = [
      render(<CashFlowReport result={warned} />),
      render(
        <AnswerDensityContext.Provider value="compact">
          <CashFlowReport result={warned} />
        </AnswerDensityContext.Provider>,
      ),
      render(<CashFlowReport result={{ ...result, outcome: "no_data", message: "ناموجود" }} />),
    ];
    for (const { container } of views) {
      for (const element of container.querySelectorAll("[class]")) {
        expect(element.getAttribute("class")).not.toMatch(themeDependent);
      }
    }
  });

  it("renders empty outcomes without presenting a fabricated report", () => {
    render(
      <CashFlowReport
        result={{ ...result, outcome: "no_data", message: "اطلاعات دوره در دسترس نیست" }}
      />,
    );
    expect(screen.getByTestId("cash-flow-report-empty")).toHaveTextContent(
      "اطلاعات دوره در دسترس نیست",
    );
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });
});
