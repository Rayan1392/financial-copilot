/** Feature 154 typed balance-sheet report (backend contract 154.3). Amounts are million IRR; null is "missing". */
export interface BalanceSheetCell {
  value: number | null;
  state: "available" | "missing" | "ambiguous" | string;
  reason?: string | null;
  selectedSourceItemId?: number | null;
  lineItemId?: string | null;
  statementId: string;
  aliasDiscrepancy?: {
    otherSourceItemId: number;
    otherValue: number | null;
    difference: number | null;
  } | null;
  candidates: Array<{
    sourceItemId: number;
    role: string;
    lineItemId?: string | null;
    value: number | null;
    verified: boolean;
    sourceTitle?: string | null;
  }>;
}

export interface BalanceSheetRow {
  rowId: string;
  label: string;
  kind: "detail" | "subtotal" | "total" | "final_total" | "analytical" | string;
  unit: string;
  cell: BalanceSheetCell;
}

export interface BalanceSheetGroup {
  groupId: "none" | "current" | "non_current" | string;
  label?: string | null;
  rows: BalanceSheetRow[];
  subtotal?: BalanceSheetRow | null;
}

export interface BalanceSheetSection {
  sectionId: "assets" | "liabilities" | "equity" | string;
  label: string;
  caption: string;
  groups: BalanceSheetGroup[];
  total?: BalanceSheetRow | null;
}

export interface BalanceSheetStatementInfo {
  statementId: string;
  externalStatementId: string;
  periodEnd: string;
  jalaliPeriodEnd: string;
  periodType: string;
  periodMonths: number;
  isAudited: boolean;
  isRepresented: boolean;
  isComposing: boolean;
  publishedAt?: string | null;
  jalaliPublishedAt?: string | null;
  jalaliFiscalYearEnd?: string | null;
  unit: string;
  assumedScale: string;
}

export interface BalanceSheetCheck {
  state: "reconciled" | "discrepancy" | "unavailable" | string;
  left?: number | null;
  right?: number | null;
  difference?: number | null;
}

export interface BalanceSheetSnapshot {
  statement: BalanceSheetStatementInfo;
  sections: BalanceSheetSection[];
  finalTotals: { totalAssets: BalanceSheetRow; totalLiabilitiesAndEquity: BalanceSheetRow };
  ratio: {
    value: number | null;
    state: string;
    reason?: string | null;
    unit: string;
    displayDecimals: number;
    rounding: string;
  };
  reconciliation: { r1: BalanceSheetCheck; r2: BalanceSheetCheck };
  analyticalItems: BalanceSheetRow[];
}

export interface BalanceSheetChange {
  amount: number | null;
  percent: number | null;
  state: "available" | "percent_unavailable" | "unavailable" | string;
  reason?: string | null;
}

export interface BalanceSheetBaselineCandidate {
  kind: string;
  label: string;
  state: "available" | "unavailable" | string;
  reason?: string | null;
  baselineDate?: string | null;
  jalaliBaselineDate?: string | null;
  statementId?: string | null;
  source: string;
  anchorLinkVerified: boolean;
  rowChanges: Record<string, BalanceSheetChange>;
  ratioChange?: {
    anchor: number | null;
    baseline: number | null;
    ppDifference: number | null;
    state: string;
    reason?: string | null;
  } | null;
  alternatives: string[];
}

export interface BalanceSheetChartPoint {
  periodEnd: string;
  jalaliPeriodEnd: string;
  statementId?: string | null;
  r2State: string;
  r2Difference?: number | null;
  stackRepresentsAssets: boolean;
  negativeEquity: boolean;
  gapReason?: string | null;
}

export interface BalanceSheetReportResult {
  contractVersion: string;
  outcome: string;
  outcomeReason?: string | null;
  outcomeMessage?: string | null;
  requestedDate?: string | null;
  requestedJalaliDate?: string | null;
  unsupportedFilter?: string | null;
  consolidatedAlternativeAvailable: boolean;
  templateId?: string | null;
  title: string;
  company: {
    symbol: string;
    name?: string | null;
    providerName: string;
    externalCompanyId?: string | null;
  };
  snapshots: Record<string, BalanceSheetSnapshot>;
  anchorStatementId?: string | null;
  requestedView: "default" | "single_date" | "comparison" | string;
  comparison?: { selectedBaselineKind: string; candidates: BalanceSheetBaselineCandidate[] } | null;
  chart: { maxDates: number; points: BalanceSheetChartPoint[] };
  generatedAtUtc: string;
}
