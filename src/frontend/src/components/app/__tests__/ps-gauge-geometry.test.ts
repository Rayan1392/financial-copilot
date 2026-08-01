import { describe, expect, it } from "vitest";
import { render } from "@testing-library/react";
import { createElement } from "react";
import type { PsVisualizationResult } from "@/lib/chat.functions";
import { PsGauge } from "../ps-gauge";
import { getPsGaugeMarkerAngles } from "../ps-gauge-geometry";

function result(ttm: number, forward: number): Pick<PsVisualizationResult, "gaugeBands" | "ttmPs" | "forwardPs"> {
  return {
    gaugeBands: [
      { order: 0, role: "VeryLow", exactPercentage: 10, displayPercentage: 10, lowerBoundary: 2, upperBoundary: 2.5, startAngleDegrees: 0, endAngleDegrees: 30 },
      { order: 1, role: "Low", exactPercentage: 10, displayPercentage: 10, lowerBoundary: 2.5, upperBoundary: 3, startAngleDegrees: 30, endAngleDegrees: 60 },
      { order: 2, role: "LowerMiddle", exactPercentage: 20, displayPercentage: 20, lowerBoundary: 3, upperBoundary: 3.5, startAngleDegrees: 60, endAngleDegrees: 90 },
      { order: 3, role: "UpperMiddle", exactPercentage: 20, displayPercentage: 20, lowerBoundary: 3.5, upperBoundary: 4, startAngleDegrees: 90, endAngleDegrees: 120 },
      { order: 4, role: "High", exactPercentage: 20, displayPercentage: 20, lowerBoundary: 4, upperBoundary: 4.5, startAngleDegrees: 120, endAngleDegrees: 150 },
      { order: 5, role: "VeryHigh", exactPercentage: 20, displayPercentage: 20, lowerBoundary: 4.5, upperBoundary: 5, startAngleDegrees: 150, endAngleDegrees: 180 },
    ],
    ttmPs: { value: ttm, state: "Present" },
    forwardPs: { value: forward, state: "Present" },
  };
}

describe("P/S gauge marker geometry", () => {
  it("renders TTM as a solid marker and Forward as a separate clamped marker", () => {
    const angles = getPsGaugeMarkerAngles(result(2.74, 1.99));

    expect(angles.ttm).toBeCloseTo(44.4, 5);
    expect(angles.forward).toBe(0);
  });

  it("keeps both markers within the semicircle", () => {
    const angles = getPsGaugeMarkerAngles(result(10, -1));

    expect(angles.ttm).toBe(180);
    expect(angles.forward).toBe(0);
  });

  it("keeps the solid TTM needle visible when Forward clamps to the same edge", () => {
    const { container } = render(
      createElement(PsGauge, {
        data: {
          ...result(1.53, 0),
          companySymbol: "کگل",
          companyName: "گل گهر",
          status: "Fresh",
          gaugeRenderabilityStatus: "Renderable",
          gaugeClose: { value: 1.53, state: "Present" },
          needle: { sourceValue: 1.53, normalizedPosition: 0, angleDegrees: 0, bandOrder: 0 },
        } as PsVisualizationResult
      }),
    );
    const lines = Array.from(container.querySelectorAll("svg line"));

    expect(lines[0]).toHaveAttribute("stroke", "black");
    expect(lines[1]).toHaveAttribute("stroke", "#2563eb");
    expect(lines[1]).toHaveAttribute("stroke-dasharray", "6 4");
  });
});
