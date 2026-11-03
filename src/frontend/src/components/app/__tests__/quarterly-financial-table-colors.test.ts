import { describe, expect, it } from "vitest";
import {
  isQuarterlyExpenseRow,
  resolveQuarterlyChangeColors,
} from "../quarterly-financial-table-colors";

describe("quarterly QoQ semantic colors", () => {
  it.each([1, 5, 12, 334])("classifies approved SourceItemId %i as an expense", (sourceItemId) => {
    expect(isQuarterlyExpenseRow(sourceItemId)).toBe(true);
  });

  it.each([null, undefined, 15, 139, 153, 999])(
    "does not classify SourceItemId %s as an expense",
    (sourceItemId) => {
      expect(isQuarterlyExpenseRow(sourceItemId)).toBe(false);
    },
  );

  it("colors expense increases red and decreases green", () => {
    expect(resolveQuarterlyChangeColors(1, 30.1).tone).toBe("negative");
    expect(resolveQuarterlyChangeColors(12, -15).tone).toBe("positive");
  });

  it("keeps the usual colors for other rows", () => {
    expect(resolveQuarterlyChangeColors(15, 27.5).tone).toBe("positive");
    expect(resolveQuarterlyChangeColors(143, -10).tone).toBe("negative");
  });

  it("uses neutral colors for zero and unavailable values", () => {
    expect(resolveQuarterlyChangeColors(5, 0).tone).toBe("neutral");
    expect(resolveQuarterlyChangeColors(5, null).tone).toBe("neutral");
    expect(resolveQuarterlyChangeColors(5, Number.NaN).tone).toBe("neutral");
  });

  it("preserves the numeric percentage input", () => {
    const percentage = 27.5;
    resolveQuarterlyChangeColors(15, percentage);
    expect(percentage).toBe(27.5);
  });
});
