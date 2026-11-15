import {
  drawBrandFooter,
  drawBrandHeader,
  downloadCanvasPng,
  exportDate,
  PNG_BRAND_HEIGHT,
  PNG_FOOTER_HEIGHT,
  PNG_HEADER_COLOR,
  PNG_FONT,
  safeFileSymbol,
  toPersianDigits,
  wrap,
} from "../png-brand";
import type { CashFlowReportResult } from "./cash-flow-report-types";
import {
  cashFlowReasonText,
  cashFlowStateLabel,
  formatCashFlowAmount,
  formatCashFlowPercent,
} from "./cash-flow-report-model";

const number = new Intl.NumberFormat("fa-IR-u-nu-arabext", { maximumFractionDigits: 0 });
const multiple = new Intl.NumberFormat("fa-IR-u-nu-arabext", {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});
const headlineMetricIds = [
  "operating_cash_flow",
  "investing_cash_flow",
  "financing_cash_flow",
  "net_change",
  "closing_cash",
];

function metricUnit(unit: string) {
  if (unit === "million_irr") return "";
  if (unit === "multiple") return "برابر";
  if (unit === "percent") return "٪";
  return "واحد دیگر";
}

function metricValue(value: number | null | undefined, state: string, unit: string) {
  if (value == null || ["unavailable", "not_reported", "not_applicable"].includes(state))
    return "—";
  if (unit === "percent") return formatCashFlowPercent(value);
  if (unit === "multiple") return multiple.format(value);
  return formatCashFlowAmount(value, state);
}

function distinctWarnings(result: CashFlowReportResult) {
  const groups = new Map<string, { message: string; periods: Set<string> }>();
  for (const warning of result.warnings) {
    const key = JSON.stringify([warning.code, warning.severity, warning.message]);
    const group = groups.get(key) ?? { message: warning.message, periods: new Set<string>() };
    const period = warning.columnId
      ? result.columns.find((column) => column.columnId === warning.columnId)?.label
      : undefined;
    if (period) group.periods.add(period);
    groups.set(key, group);
  }
  return [...groups.values()].map(({ message, periods }) =>
    periods.size ? `${message} (${[...periods].join("، ")})` : message,
  );
}

function metadataLine(result: CashFlowReportResult) {
  const metadata = result.metadata;
  if (!metadata) return "واحد مبالغ: میلیون ریال";
  return [
    metadata.viewLabel,
    metadata.basisLabel,
    metadata.jalaliPeriodEnd ? `پایان دوره ${metadata.jalaliPeriodEnd}` : undefined,
    metadata.fiscalYear ? `سال مالی ${number.format(metadata.fiscalYear)}` : undefined,
    metadata.isAudited == null ? undefined : metadata.isAudited ? "حسابرسی‌شده" : "حسابرسی‌نشده",
    metadata.isRepresented ? "ارائهٔ مجدد" : undefined,
    metadata.jalaliPublishedAt ? `انتشار ${metadata.jalaliPublishedAt}` : undefined,
    metadata.providerName,
    `واحد مبالغ: ${metadata.unit}`,
  ]
    .filter(Boolean)
    .join(" · ");
}

function comparisonTitle(result: CashFlowReportResult) {
  const baseline = result.columns.find(
    (column) => column.columnId === result.changes?.baseColumnId,
  );
  return baseline ? `تغییر نسبت به ${baseline.label}` : "تغییر نسبت به دورهٔ مبنا";
}

function anchorColumnId(result: CashFlowReportResult) {
  return (
    result.columns.find((column) => column.statementIds?.includes(result.anchorStatementId ?? ""))
      ?.columnId ?? result.columns.at(-1)?.columnId
  );
}

function validationLines(result: CashFlowReportResult) {
  const anchor = anchorColumnId(result);
  const coverage = (result.verification.detailCoverage ?? []).filter(
    (item) => item.columnId === anchor,
  );
  const overallCoverage = coverage.some((item) => item.status === "inconsistent_source_data")
    ? "inconsistent_source_data"
    : coverage.some((item) => item.status === "insufficient_detail")
      ? "insufficient_detail"
      : coverage.some((item) => item.status === "documented_difference")
        ? "documented_difference"
        : coverage.length > 0 && coverage.every((item) => item.status === "complete")
          ? "complete"
          : "unverified";
  const lines = [
    `تطبیق جمع جریان‌ها: ${cashFlowStateLabel(result.verification.mainTotals?.status ?? result.verification.status)}`,
    `تطبیق ماندهٔ وجه نقد: ${cashFlowStateLabel(result.verification.cashBalance?.status ?? "unverified")}`,
    `پوشش ریزاقلام: ${cashFlowStateLabel(overallCoverage)}`,
  ];
  for (const item of coverage) {
    const section =
      item.sectionId === "operating"
        ? "عملیاتی"
        : item.sectionId === "investing"
          ? "سرمایه‌گذاری"
          : "تأمین مالی";
    lines.push(
      `${section}: ${cashFlowStateLabel(item.status)} · جمع گزارش‌شده ${formatCashFlowAmount(item.reportedSubtotal)} · ریزاقلام تأییدشده ${formatCashFlowAmount(item.verifiedDetailTotal)} · اختلاف ${formatCashFlowAmount(item.difference)}`,
    );
    for (const source of item.unclassifiedItems.slice(0, 20)) {
      lines.push(
        `قلم تخصیص‌نیافته ${source.label ?? `شناسهٔ ${number.format(source.sourceItemId)}`} · ${formatCashFlowAmount(source.value)}`,
      );
    }
    if (item.unclassifiedItemCount > 20) {
      lines.push(
        `${number.format(item.unclassifiedItemCount - 20)} قلم دیگر در جزئیات گزارش در دسترس است.`,
      );
    }
  }
  return lines;
}

function drawFlowChart(
  context: CanvasRenderingContext2D,
  width: number,
  pad: number,
  result: CashFlowReportResult,
  y: number,
) {
  if (result.columns.length === 0) return y;
  const series = [
    { rowId: "item_128", label: "عملیاتی", color: "#0f766e" },
    { rowId: "item_121", label: "سرمایه‌گذاری", color: "#f59e0b" },
    { rowId: "item_123", label: "تأمین مالی", color: "#0e7490" },
  ];
  const allRows = result.sections.flatMap((section) => section.rows);
  const data = result.columns.map((column) => ({
    column,
    values: series.map(
      (item) =>
        allRows.find((row) => row.rowId === item.rowId)?.cells?.[column.columnId]?.value ?? null,
    ),
  }));
  const values = data.flatMap((point) =>
    point.values.filter((value): value is number => value != null),
  );
  if (values.length === 0) return y;
  const minimum = Math.min(0, ...values);
  const maximum = Math.max(0, ...values);
  const span = maximum - minimum || 1;
  const left = pad + 70;
  const right = width - pad;
  const top = y + 42;
  const bottom = top + 102;
  const plotHeight = bottom - top;

  context.direction = "rtl";
  context.textAlign = "right";
  context.fillStyle = "#164e63";
  context.font = `700 12px ${PNG_FONT}`;
  context.fillText("روند جریان‌های نقدی", right, y + 15);
  context.font = `10px ${PNG_FONT}`;
  let legendX = right;
  for (const item of series) {
    context.fillStyle = item.color;
    context.fillRect(legendX - 72, y + 27, 8, 8);
    context.fillStyle = "#334155";
    context.textAlign = "right";
    context.fillText(item.label, legendX - 78, y + 35);
    legendX -= 112;
  }

  const yFor = (value: number) => top + ((maximum - value) / span) * plotHeight;
  for (let tick = 0; tick <= 4; tick++) {
    const value = maximum - (span * tick) / 4;
    const tickY = yFor(value);
    context.strokeStyle = tick === 4 || value === 0 ? "#94a3b8" : "#e2e8f0";
    context.beginPath();
    context.moveTo(left, tickY);
    context.lineTo(right, tickY);
    context.stroke();
    context.fillStyle = "#64748b";
    context.textAlign = "left";
    context.fillText(number.format(value), pad, tickY + 3, 62);
  }

  const groupWidth = (right - left) / data.length;
  const barWidth = Math.min(16, Math.max(7, groupWidth / 8));
  data.forEach((point, index) => {
    const center = left + groupWidth * (index + 0.5);
    point.values.forEach((value, seriesIndex) => {
      if (value == null) return;
      const x = center + (seriesIndex - 1) * (barWidth + 4) - barWidth / 2;
      const zeroY = yFor(0);
      const valueY = yFor(value);
      context.fillStyle = series[seriesIndex].color;
      context.fillRect(x, Math.min(zeroY, valueY), barWidth, Math.max(1, Math.abs(zeroY - valueY)));
    });
    context.fillStyle = "#334155";
    context.font = `9px ${PNG_FONT}`;
    context.textAlign = "center";
    context.fillText(point.column.label, center, bottom + 15, Math.max(50, groupWidth - 4));
  });
  return bottom + 32;
}

function hasFlowChart(result: CashFlowReportResult) {
  const rows = result.sections.flatMap((section) => section.rows);
  return result.columns.some((column) =>
    ["item_128", "item_121", "item_123"].some(
      (rowId) => rows.find((row) => row.rowId === rowId)?.cells?.[column.columnId]?.value != null,
    ),
  );
}

export async function downloadCashFlowReportImage(result: CashFlowReportResult) {
  await document.fonts?.ready;
  const pad = 28;
  const labelWidth = 250;
  const periodWidth = 116;
  const comparisonWidth = result.changes ? 160 : 0;
  const width = pad * 2 + labelWidth + periodWidth * result.columns.length + comparisonWidth;
  const latestColumnId = anchorColumnId(result);
  const headlineKpis = headlineMetricIds.flatMap((metricId) => {
    const kpi = result.kpis.find(
      (item) => item.metricId === metricId && item.columnId === latestColumnId,
    );
    return kpi?.value != null &&
      !["unavailable", "not_reported", "not_applicable"].includes(kpi.state)
      ? [kpi]
      : [];
  });
  const secondaryKpis = result.kpis.filter(
    (kpi) =>
      kpi.columnId === latestColumnId &&
      !headlineMetricIds.includes(kpi.metricId) &&
      kpi.value != null &&
      !["unavailable", "not_reported", "not_applicable"].includes(kpi.state),
  );
  const warnings = distinctWarnings(result);
  const warningMessages = new Set(result.warnings.map((warning) => warning.message));
  const notices = [...new Set(result.notices)].filter((notice) => !warningMessages.has(notice));
  const rowHeight = 46;
  const rowHeightTotal = result.sections.reduce(
    (total, section) => total + 28 + section.rows.length * rowHeight,
    0,
  );
  const waterfallSteps = result.waterfall?.steps ?? [];
  const includeFlowChart = hasFlowChart(result);
  const validation = validationLines(result);
  const residualNotes = result.sections.flatMap((section) =>
    section.rows
      .filter((row) => row.kind === "residual" && row.note)
      .flatMap((row) => {
        const traceColumn = row.cells[anchorColumnId(result) ?? ""]?.provenance;
        return [
          row.note!,
          `شناسهٔ اقلام منبع: ${row.sourceItemIds.map((id) => number.format(id)).join("، ")}`,
          `شناسهٔ ردیف‌های منبع: ${traceColumn?.lineItemIds?.join("، ") || "در دسترس نیست"}`,
          `شناسهٔ صورت‌های منبع: ${traceColumn?.statementIds?.join("، ") || "در دسترس نیست"}`,
        ];
      }),
  );
  const unavailableNotes = [
    ...new Set(
      result.kpis
        .filter(
          (kpi) =>
            kpi.columnId === latestColumnId &&
            kpi.value == null &&
            [
              "capex_mapping_unverified",
              "capex_component_missing",
              "unexpected_capex_sign",
              "mixed_sign_change",
            ].includes(kpi.reason ?? ""),
        )
        .map((kpi) => cashFlowReasonText(kpi.reason)),
    ),
  ];
  const afterTableLines =
    warnings.reduce((n, item) => n + Math.max(1, Math.ceil(item.length / 170)), 0) +
    notices.reduce((n, item) => n + Math.max(1, Math.ceil(item.length / 170)), 0) +
    residualNotes.reduce((n, item) => n + Math.max(1, Math.ceil(item.length / 170)), 0) +
    unavailableNotes.length +
    (result.analysis?.facts ?? []).reduce(
      (n, item) => n + Math.max(1, Math.ceil(item.text.length / 170)),
      0,
    );
  const summaryHeight =
    64 +
    (headlineKpis.length ? 68 : 0) +
    secondaryKpis.length * 20 +
    26 +
    validation.length * 21 +
    (includeFlowChart ? 180 : 0) +
    (waterfallSteps.length ? 22 + waterfallSteps.length * 24 : 0);
  const height =
    PNG_BRAND_HEIGHT +
    144 +
    summaryHeight +
    rowHeightTotal +
    afterTableLines * 22 +
    (afterTableLines ? 34 : 0) +
    PNG_FOOTER_HEIGHT;
  const scale = Math.min(2, Math.max(1, window.devicePixelRatio || 1));
  const canvas = document.createElement("canvas");
  canvas.width = width * scale;
  canvas.height = height * scale;
  const context = canvas.getContext("2d");
  if (!context) throw new Error("Canvas is unavailable.");
  context.scale(scale, scale);
  context.fillStyle = "#fff";
  context.fillRect(0, 0, width, height);
  drawBrandHeader(context, width, pad, exportDate());
  context.fillStyle = PNG_HEADER_COLOR;
  context.fillRect(pad, PNG_BRAND_HEIGHT, width - pad * 2, 34);
  context.fillStyle = "#fff";
  context.font = `700 15px ${PNG_FONT}`;
  context.direction = "rtl";
  context.textAlign = "right";
  context.fillText(
    `صورت جریان وجوه نقد ${result.company.symbol ?? ""}`,
    width - pad - 10,
    PNG_BRAND_HEIGHT + 22,
  );
  let y = PNG_BRAND_HEIGHT + 54;
  context.fillStyle = "#475569";
  context.font = `10px ${PNG_FONT}`;
  context.textAlign = "right";
  const metadata = metadataLine(result);
  for (const line of wrap(context, metadata, width - pad * 2)) {
    context.fillText(line, width - pad, y + 13);
    y += 17;
  }
  const companyName = result.company.name?.trim();
  if (companyName && companyName !== result.company.symbol) {
    context.fillStyle = "#64748b";
    context.fillText(companyName, width - pad, y + 12);
    y += 16;
  }
  context.fillStyle = "#164e63";
  context.font = `700 11px ${PNG_FONT}`;
  context.fillText("وضعیت کنترل‌های گزارش", width - pad, y + 13);
  y += 19;
  context.fillStyle = "#334155";
  context.font = `10px ${PNG_FONT}`;
  for (const line of validation) {
    for (const part of wrap(context, line, width - pad * 2)) {
      context.fillText(part, width - pad, y + 12);
      y += 17;
    }
  }
  y += 5;
  context.fillStyle = "#0f172a";
  context.font = `700 12px ${PNG_FONT}`;
  context.textAlign = "right";
  if (headlineKpis.length > 0) {
    context.fillStyle = "#164e63";
    context.font = `10px ${PNG_FONT}`;
    headlineKpis.forEach((kpi, index) => {
      const center = width - pad - ((width - pad * 2) * (index + 0.5)) / headlineKpis.length;
      context.textAlign = "center";
      context.fillStyle = "#475569";
      context.fillText(kpi.label, center, y + 15, (width - pad * 2) / headlineKpis.length - 8);
      context.fillStyle = kpi.value != null && kpi.value < 0 ? "#be123c" : "#0f172a";
      context.font = `700 12px ${PNG_FONT}`;
      context.fillText(
        formatCashFlowAmount(kpi.value, kpi.state),
        center,
        y + 38,
        (width - pad * 2) / headlineKpis.length - 8,
      );
      context.font = `10px ${PNG_FONT}`;
    });
    y += 56;
  }
  if (includeFlowChart) y = drawFlowChart(context, width, pad, result, y + 4);
  if (waterfallSteps.length > 0) {
    context.fillStyle = "#164e63";
    context.font = `700 12px ${PNG_FONT}`;
    context.textAlign = "right";
    context.fillText("تراکم جریان وجه نقد", width - pad, y + 14);
    y += 22;
    context.font = `11px ${PNG_FONT}`;
    for (const step of waterfallSteps) {
      context.fillStyle = "#0f172a";
      context.textAlign = "right";
      context.fillText(step.label, width - pad - 140, y + 15, Math.max(80, width - pad * 2 - 230));
      context.textAlign = "left";
      context.fillText(formatCashFlowAmount(step.value), pad + 8, y + 15, 130);
      y += 24;
    }
  }
  const labelX = width - pad;
  context.fillStyle = "#d9efed";
  context.fillRect(pad, y, width - pad * 2, 42);
  context.fillStyle = "#164e63";
  context.font = `700 11px ${PNG_FONT}`;
  context.textAlign = "right";
  context.fillText("شرح", labelX - 8, y + 23);
  context.textAlign = "center";
  if (result.changes) {
    context.fillText(
      comparisonTitle(result),
      pad + comparisonWidth / 2,
      y + 25,
      comparisonWidth - 8,
    );
  }
  result.columns.forEach((column, index) => {
    const center = width - pad - labelWidth - periodWidth * index - periodWidth / 2;
    context.fillText(column.label, center, y + 25, periodWidth - 8);
  });
  y += 42;
  for (const section of result.sections) {
    context.fillStyle = "#d9efed";
    context.fillRect(pad, y, width - pad * 2, 28);
    context.fillStyle = "#0f3c4c";
    context.font = `700 13px ${PNG_FONT}`;
    context.textAlign = "right";
    context.fillText(section.label, labelX - 8, y + 19);
    y += 28;
    for (const row of section.rows) {
      context.fillStyle =
        row.kind === "total" || row.kind === "primary-total" || row.kind === "reconciliation"
          ? "#fff7e8"
          : "#fff";
      context.fillRect(pad, y, width - pad * 2, rowHeight);
      context.strokeStyle = "#e2e8f0";
      context.beginPath();
      context.moveTo(pad, y + rowHeight);
      context.lineTo(width - pad, y + rowHeight);
      context.stroke();
      context.fillStyle = "#0f172a";
      context.font = `${row.kind === "total" || row.kind === "primary-total" ? "700" : "400"} 12px ${PNG_FONT}`;
      context.textAlign = "right";
      const labelLines = wrap(context, row.label, labelWidth - 16);
      context.fillText(
        labelLines[0] ?? row.label,
        labelX - 8,
        y + (labelLines.length > 1 ? 17 : 24),
        labelWidth - 16,
      );
      if (labelLines.length > 1)
        context.fillText(labelLines[1], labelX - 8, y + 33, labelWidth - 16);
      result.columns.forEach((column, index) => {
        const cell = row.cells?.[column.columnId];
        const value =
          cell?.value == null
            ? "—"
            : cell.value < 0
              ? `(${number.format(Math.abs(cell.value))})`
              : number.format(cell.value);
        context.textAlign = "center";
        context.fillStyle = cell?.value != null && cell.value < 0 ? "#9f1239" : "#0f172a";
        context.fillText(
          value,
          width - pad - labelWidth - periodWidth * index - periodWidth / 2,
          y + 28,
          periodWidth - 8,
        );
      });
      if (result.changes) {
        const change = result.changes.rows?.[row.rowId];
        const comparisonValue = change?.amount == null ? "—" : formatCashFlowAmount(change.amount);
        const comparisonPercent =
          change?.percent == null ? undefined : formatCashFlowPercent(change.percent);
        const center = pad + comparisonWidth / 2;
        context.textAlign = "center";
        context.fillStyle = change?.amount != null && change.amount < 0 ? "#9f1239" : "#0f172a";
        context.fillText(comparisonValue, center, y + 19, comparisonWidth - 8);
        context.fillStyle = "#64748b";
        context.font = `9px ${PNG_FONT}`;
        context.fillText(
          comparisonPercent ?? "درصد معنادار نیست",
          center,
          y + 35,
          comparisonWidth - 8,
        );
        context.font = `12px ${PNG_FONT}`;
      }
      y += rowHeight;
    }
  }
  if (secondaryKpis.length > 0) {
    context.fillStyle = "#164e63";
    context.font = `700 12px ${PNG_FONT}`;
    context.textAlign = "right";
    context.fillText("شاخص‌های تکمیلی", width - pad, y + 15);
    y += 22;
    context.font = `10px ${PNG_FONT}`;
    context.fillStyle = "#334155";
    for (const kpi of secondaryKpis) {
      context.fillText(
        `${kpi.label}: ${metricValue(kpi.value, kpi.state, kpi.unit)} ${metricUnit(kpi.unit)}`,
        width - pad - 4,
        y + 14,
        width - pad * 2,
      );
      y += 20;
    }
  }
  const afterTable = [
    ...warnings,
    ...notices,
    ...unavailableNotes,
    ...residualNotes,
    ...(result.analysis?.facts.map((fact) => fact.text) ?? []),
  ];
  if (afterTable.length > 0) {
    context.fillStyle = "#164e63";
    context.font = `700 12px ${PNG_FONT}`;
    context.textAlign = "right";
    context.fillText("هشدار و نکته", width - pad, y + 15);
    y += 24;
    context.font = `10px ${PNG_FONT}`;
    context.fillStyle = "#334155";
    for (const line of afterTable) {
      for (const part of wrap(context, line, width - pad * 2 - 10)) {
        context.textAlign = "right";
        context.fillText(part, width - pad - 4, y + 14);
        y += 22;
      }
    }
  }
  const footerTop = y + 18;
  drawBrandFooter(context, width, pad, footerTop, "صورت جریان وجوه نقد");
  const outputHeight = footerTop + 52;
  const outputCanvas = document.createElement("canvas");
  outputCanvas.width = width * scale;
  outputCanvas.height = outputHeight * scale;
  const outputContext = outputCanvas.getContext("2d");
  if (!outputContext) throw new Error("Canvas is unavailable.");
  outputContext.scale(scale, scale);
  outputContext.drawImage(
    canvas,
    0,
    0,
    width * scale,
    outputHeight * scale,
    0,
    0,
    width,
    outputHeight,
  );
  const symbol = safeFileSymbol(result.company.symbol ?? "report") || "report";
  await downloadCanvasPng(outputCanvas, `cash-flow-${toPersianDigits(symbol)}.png`);
}
