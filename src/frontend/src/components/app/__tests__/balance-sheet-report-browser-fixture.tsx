import { createRoot } from "react-dom/client";
import fixtures from "./fixtures/balance-sheet-154.json";
import { AnswerDensityContext } from "../answer-density";
import { BalanceSheetReport } from "../balance-sheet-report/balance-sheet-report";
import type { BalanceSheetReportResult } from "../balance-sheet-report/balance-sheet-report-types";
import "../../../styles.css";

// Feature 154 browser fixture: ?fixture=webMellat|fazar|negative|single|gaps&density=compact&restated=baseline|anchor
const params = new URLSearchParams(location.search);
const name = (params.get("fixture") ?? "webMellat") as keyof typeof fixtures;
const result = structuredClone(fixtures[name]) as unknown as BalanceSheetReportResult;
// ?restated=baseline|anchor marks that statement as restated (F154-V-06 badge evidence).
const restated = params.get("restated");
if (restated && result.comparison) {
  const kind = result.comparison.selectedBaselineKind;
  const id =
    restated === "anchor"
      ? result.anchorStatementId
      : result.comparison.candidates.find((candidate) => candidate.kind === kind)?.statementId;
  if (id && result.snapshots[id]) result.snapshots[id].statement.isRepresented = true;
}
const density = params.get("density") === "compact" ? "compact" : "default";

createRoot(document.getElementById("root")!).render(
  <AnswerDensityContext.Provider value={density}>
    <main className={density === "compact" ? "px-2 py-3" : "mx-auto max-w-[1600px] p-4"}>
      <BalanceSheetReport result={result} />
    </main>
  </AnswerDensityContext.Provider>,
);
