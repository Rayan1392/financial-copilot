import { describe, expect, it } from "vitest";
import { render } from "@testing-library/react";
import { createElement } from "react";
import type { PsVisualizationResult } from "@/lib/chat.functions";
import { PsGauge } from "../ps-gauge";
import { getPsGaugeMarkerAngles, mapPsGaugeValueToAngle } from "../ps-gauge-geometry";

function result(
  ttm: number,
  forward: number,
): Pick<PsVisualizationResult, "gaugeBands" | "ttmPs" | "forwardPs"> {
  return {
    gaugeBands: [
      {
        order: 0,
        role: "VeryLow",
        exactPercentage: 10,
        displayPercentage: 10,
        lowerBoundary: 2,
        upperBoundary: 2.5,
        startAngleDegrees: 0,
        endAngleDegrees: 30,
      },
      {
        order: 1,
        role: "Low",
        exactPercentage: 10,
        displayPercentage: 10,
        lowerBoundary: 2.5,
        upperBoundary: 3,
        startAngleDegrees: 30,
        endAngleDegrees: 60,
      },
      {
        order: 2,
        role: "LowerMiddle",
        exactPercentage: 20,
        displayPercentage: 20,
        lowerBoundary: 3,
        upperBoundary: 3.5,
        startAngleDegrees: 60,
        endAngleDegrees: 90,
      },
      {
        order: 3,
        role: "UpperMiddle",
        exactPercentage: 20,
        displayPercentage: 20,
        lowerBoundary: 3.5,
        upperBoundary: 4,
        startAngleDegrees: 90,
        endAngleDegrees: 120,
      },
      {
        order: 4,
        role: "High",
        exactPercentage: 20,
        displayPercentage: 20,
        lowerBoundary: 4,
        upperBoundary: 4.5,
        startAngleDegrees: 120,
        endAngleDegrees: 150,
      },
      {
        order: 5,
        role: "VeryHigh",
        exactPercentage: 20,
        displayPercentage: 20,
        lowerBoundary: 4.5,
        upperBoundary: 5,
        startAngleDegrees: 150,
        endAngleDegrees: 180,
      },
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
        } as PsVisualizationResult,
      }),
    );
    const lines = Array.from(container.querySelectorAll("svg line"));

    expect(lines[0]).toHaveAttribute("stroke", "black");
    expect(lines[1]).toHaveAttribute("stroke", "#2563eb");
    expect(lines[1]).toHaveAttribute("stroke-dasharray", "6 4");
  });

  it("maps a TTM value inside the start-to-min first band instead of clamping to zero", () => {
    const data = {
      gaugeBands: [
        {
          lowerBoundary: 0.29868693299183074,
          upperBoundary: 0.5107477311430351,
          startAngleDegrees: 0,
          endAngleDegrees: 30,
        },
        {
          lowerBoundary: 0.5107477311430351,
          upperBoundary: 0.878382594188526,
          startAngleDegrees: 30,
          endAngleDegrees: 60,
        },
        {
          lowerBoundary: 0.878382594188526,
          upperBoundary: 1.2460174572440175,
          startAngleDegrees: 60,
          endAngleDegrees: 90,
        },
        {
          lowerBoundary: 1.2460174572440175,
          upperBoundary: 1.6136523202995087,
          startAngleDegrees: 90,
          endAngleDegrees: 120,
        },
        {
          lowerBoundary: 1.6136523202995087,
          upperBoundary: 1.981287183325,
          startAngleDegrees: 120,
          endAngleDegrees: 150,
        },
        {
          lowerBoundary: 1.981287183325,
          upperBoundary: 5.05668737,
          startAngleDegrees: 150,
          endAngleDegrees: 180,
        },
      ],
    } as Parameters<typeof mapPsGaugeValueToAngle>[1];

    expect(mapPsGaugeValueToAngle(0.42, data)).toBeCloseTo(17.16, 2);
  });

  it("repairs legacy persisted bands using the authoritative outer boundaries", () => {
    const data = {
      gaugeBands: [
        { lowerBoundary: 0.5107, upperBoundary: 0.7559, startAngleDegrees: 0, endAngleDegrees: 30 },
        { lowerBoundary: 0.7559, upperBoundary: 1.001, startAngleDegrees: 30, endAngleDegrees: 60 },
        { lowerBoundary: 1.001, upperBoundary: 1.246, startAngleDegrees: 60, endAngleDegrees: 90 },
        { lowerBoundary: 1.246, upperBoundary: 1.491, startAngleDegrees: 90, endAngleDegrees: 120 },
        { lowerBoundary: 1.491, upperBoundary: 1.736, startAngleDegrees: 120, endAngleDegrees: 150 },
        { lowerBoundary: 1.736, upperBoundary: 1.9813, startAngleDegrees: 150, endAngleDegrees: 180 },
      ],
      providerBoundaryStart: 0.29868693299183074,
      providerBoundaryEnd: 5.05668737,
      gaugeAxisMin: 0.5107477311430351,
      gaugeAxisMax: 1.981287183325,
    } as Parameters<typeof mapPsGaugeValueToAngle>[1];

    expect(mapPsGaugeValueToAngle(0.42, data)).toBeCloseTo(17.16, 2);
  });
});
