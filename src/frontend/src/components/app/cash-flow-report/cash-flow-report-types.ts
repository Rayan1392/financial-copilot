export interface CashFlowCell {
  value?: number | null;
  state: string;
  reason?: string | null;
  provenance?: {
    statementIds: string[];
    lineItemIds: string[];
    sourceItemIds: number[];
    formula?: string | null;
  };
}

export interface CashFlowReportColumn {
  columnId: string;
  kind: string;
  label: string;
  fiscalYear?: number | null;
  quarter?: number | null;
  periodMonths: number;
  periodEnd?: string | null;
  jalaliPeriodEnd?: string | null;
  derivation: string;
  statementIds?: string[];
  state: string;
  reason?: string | null;
}

export interface CashFlowReportRow {
  rowId: string;
  label: string;
  kind: string;
  sourceItemIds: number[];
  note?: string | null;
  cells: Record<string, CashFlowCell>;
}

export interface CashFlowReportSection {
  sectionId: string;
  label: string;
  rows: CashFlowReportRow[];
}

export interface CashFlowReportResult {
  contractVersion: string;
  outcome: string;
  outcomeReason?: string | null;
  message?: string | null;
  templateId?: string | null;
  company: { symbol?: string | null; name?: string | null; providerName: string };
  request: { view: string; focus: string; metricId?: string | null; analysisTopic?: string | null };
  anchorStatementId?: string | null;
  metadata?: {
    viewLabel: string;
    basisLabel: string;
    jalaliPeriodEnd?: string | null;
    fiscalYear?: number | null;
    isAudited?: boolean | null;
    isRepresented?: boolean | null;
    jalaliPublishedAt?: string | null;
    providerName: string;
    unit: string;
  } | null;
  columns: CashFlowReportColumn[];
  sections: CashFlowReportSection[];
  changes?: {
    baseColumnId: string;
    compareColumnId: string;
    rows: Record<
      string,
      { amount?: number | null; percent?: number | null; state: string; reason?: string | null }
    >;
  } | null;
  kpis: Array<{
    metricId: string;
    label: string;
    columnId: string;
    value?: number | null;
    unit: string;
    state: string;
    reason?: string | null;
    formula: string;
    components?: Array<{ sourceType: string; sourceItemId: number; value?: number | null; state: string }>;
  }>;
  waterfall?: {
    columnId: string;
    steps: Array<{
      stepId: string;
      label: string;
      kind: string;
      value: number;
      from: number;
      to: number;
    }>;
    flowSteps: Array<{
      stepId: string;
      label: string;
      kind: string;
      value: number;
      from: number;
      to: number;
    }>;
  } | null;
  verification: {
    status: string;
    mainTotals?: ValidationDimension | null;
    cashBalance?: ValidationDimension | null;
    detailCoverage?: DetailCoverage[] | null;
    checks: Array<{
      checkId: string;
      columnId: string;
      state: string;
      expected?: number | null;
      actual?: number | null;
      difference?: number | null;
    }>;
  };
  warnings: Array<{ code: string; severity: string; columnId?: string | null; message: string }>;
  notices: string[];
  analysis?: {
    topic: string;
    facts: Array<{ factId: string; text: string; metricIds: string[]; columnId: string; relatedColumnIds?: string[] | null }>;
  } | null;
}

export interface ValidationDimension {
  status: string;
  expected?: number | null;
  actual?: number | null;
  difference?: number | null;
  checkIds: string[];
}

export interface DetailCoverage {
  columnId: string;
  sectionId: string;
  status: string;
  reportedSubtotal?: number | null;
  verifiedDetailTotal?: number | null;
  difference?: number | null;
  verifiedDetailCount: number;
  unclassifiedItemCount: number;
  unclassifiedItems: Array<{ sourceItemId: number; label?: string | null; value?: number | null }>;
  sourceReadComplete: boolean;
  statementIds: string[];
}
