import { createRoot } from "react-dom/client";
import type { OptionComparison, OptionComparisonRow } from "@/lib/chat/option-comparison";
import { OptionComparisonCard } from "../option-comparison-card";
import "../../../styles.css";

// Feature 168 browser fixture: ?fixture=calls|stale&theme=dark|light. The figures are modelled on the اخابر
// order book of 2026-11-28 with an assumed underlying price of 1,489.
const call = (
  symbol: string,
  strike: number,
  ask: number | null,
  volume: number,
  extra: Partial<OptionComparisonRow> = {},
): OptionComparisonRow => {
  const fillable = ask !== null;
  const cost = fillable ? strike * 1.0005 + ask * 1.001 : null;
  const intrinsic = Math.max(1489 - strike, 0);
  return {
    symbol,
    strikePrice: strike,
    contractSize: 1000,
    openInterest: 109,
    state: fillable ? "Fillable" : volume > 0 ? "NotFillable" : "NoOrder",
    fillPrice: ask,
    availableVolume: volume,
    lastPrice: ask ?? 1,
    isNearestToMoney: strike === 1500,
    isRequestedContract: false,
    highlights: [],
    effectivePurchaseCost: cost,
    breakEvenDistance: cost === null ? null : (cost - 1489) / 1489,
    intrinsicValue: fillable ? intrinsic : null,
    timeValue: fillable ? ask - intrinsic : null,
    impliedAnnualRate: fillable && strike < 1489 ? ((ask - intrinsic) / strike) * (365 / 11) : null,
    leverage: fillable ? 1489 / ask : null,
    cashNow: fillable ? ask * 1.001 * 1000 : null,
    cashAtExpiry: fillable ? strike * 1.0005 * 1000 : null,
    theoreticalPrice: fillable ? Math.round(ask * 0.93) : null,
    impliedVolatility: fillable ? 0.62 : null,
    ...extra,
  };
};

const calls: OptionComparison = {
  version: 1,
  underlyingSymbol: "اخابر",
  underlyingClass: "Share",
  underlyingPrice: 1489,
  underlyingTradingDate: "2026-11-28",
  underlyingObservedAt: "2026-11-28T08:20:00Z",
  quantityInContracts: 1,
  requestedView: "Call",
  availableExpiryDates: ["2026-12-09", "2027-01-13", "2027-02-10"],
  fees: { trade: 0.001, exercise: 0.0005, shareTransferTax: 0.005 },
  orderBookIsCurrent: true,
  orderBookObservedAt: "2026-11-28T08:20:00Z",
  openInterestObservedAt: "2026-11-28T08:20:00Z",
  theoreticalInputs: {
    riskFreeAnnualRate: 0.415,
    historicalVolatility: 0.38,
    volatilityWindowDays: 90,
  },
  views: [
    {
      view: "Call",
      expiries: [
        {
          expiryDate: "2026-12-09",
          daysToExpiry: 11,
          rows: [
            call("ضمخا9013", 500, 1024, 248, { highlights: ["lowest_effective_purchase_cost"] }),
            call("ضمخا9019", 800, 730, 5),
            call("ضمخا9020", 900, 648, 2000),
            call("ضمخا9021", 1000, 544, 10, { highlights: ["lowest_implied_annual_rate"] }),
            call("ضمخا9022", 1100, 445, 637),
            call("ضمخا9024", 1300, 261, 120),
            call("ضمخا9025", 1400, 178, 10),
            call("ضمخا9026", 1500, 116, 39),
            call("ضمخا9027", 1600, 65, 107),
            call("ضمخا9028", 1700, null, 0),
            call("ضمخا9029", 1800, null, 3),
          ],
        },
        {
          expiryDate: "2027-01-13",
          daysToExpiry: 46,
          rows: [call("ضمخا1051", 1200, 454, 75), call("ضمخا1054", 1500, 250, 1000)],
        },
      ],
    },
    {
      view: "Put",
      expiries: [
        {
          expiryDate: "2026-12-09",
          daysToExpiry: 11,
          rows: [
            {
              symbol: "طمخا9025",
              strikePrice: 1400,
              contractSize: 1000,
              openInterest: 8,
              state: "Fillable",
              fillPrice: 20,
              availableVolume: 30,
              lastPrice: 1,
              isNearestToMoney: true,
              isRequestedContract: false,
              highlights: ["highest_effective_sale_price"],
              effectiveSalePrice: 1372.28,
              distanceFromMarket: -0.0784,
              protectionCost: 0.0134,
              protectionCostAnnualized: 0.446,
            },
            {
              symbol: "طمخا9026",
              strikePrice: 1500,
              contractSize: 1000,
              openInterest: 39,
              state: "Fillable",
              fillPrice: 60,
              availableVolume: 12,
              lastPrice: 55,
              isNearestToMoney: false,
              isRequestedContract: false,
              highlights: [],
              effectiveSalePrice: 1431.69,
              distanceFromMarket: -0.0385,
              protectionCost: 0.0403,
              protectionCostAnnualized: 1.338,
            },
          ],
        },
      ],
    },
    {
      view: "CoveredCall",
      expiries: [
        {
          expiryDate: "2026-12-09",
          daysToExpiry: 11,
          rows: [
            {
              symbol: "ضمخا9026",
              strikePrice: 1500,
              contractSize: 1000,
              openInterest: 39,
              state: "Fillable",
              fillPrice: 110,
              availableVolume: 41,
              lastPrice: 116,
              isNearestToMoney: true,
              isRequestedContract: false,
              highlights: ["highest_annualized_yield_if_exercised"],
              premiumIncome: 109.89,
              yieldIfExercised: 0.0756,
              yieldIfExercisedAnnualized: 2.51,
              yieldIfNotExercised: 0.0738,
              yieldIfNotExercisedAnnualized: 2.45,
              downsideCushion: 0.0738,
            },
            {
              symbol: "ضمخا9027",
              strikePrice: 1600,
              contractSize: 1000,
              openInterest: 107,
              state: "Fillable",
              fillPrice: 56,
              availableVolume: 10,
              lastPrice: 65,
              isNearestToMoney: false,
              isRequestedContract: false,
              highlights: [],
              premiumIncome: 55.94,
              yieldIfExercised: 0.1062,
              yieldIfExercisedAnnualized: 3.52,
              yieldIfNotExercised: 0.0376,
              yieldIfNotExercisedAnnualized: 1.25,
              downsideCushion: 0.0376,
            },
          ],
        },
      ],
    },
  ],
};

const stale: OptionComparison = {
  ...calls,
  orderBookIsCurrent: false,
  orderBookObservedAt: "2026-11-25T08:55:00Z",
  theoreticalInputs: null,
};

const params = new URLSearchParams(window.location.search);
const data = params.get("fixture") === "stale" ? stale : calls;
document.documentElement.classList.toggle("light", params.get("theme") === "light");

createRoot(document.getElementById("root")!).render(
  <main className="mx-auto min-h-screen max-w-3xl bg-background p-3 sm:p-6">
    <OptionComparisonCard data={data} />
  </main>,
);
