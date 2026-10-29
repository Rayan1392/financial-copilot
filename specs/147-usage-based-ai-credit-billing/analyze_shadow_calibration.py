#!/usr/bin/env python3
"""Join Feature 147 structured usage logs with billing exports and summarize calibration."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import re
import statistics
from collections import Counter, defaultdict
from decimal import Decimal, ROUND_CEILING, ROUND_HALF_UP
from pathlib import Path
from typing import Any


CALL_RE = re.compile(
    r"(?P<provider>[^/;:]+)/(?P<model>[^:;]+):calls=(?P<calls>\d+),"
    r"input=(?P<input>\?|\d+),cached=(?P<cached>\?|\d+)"
    r"(?:,write=(?P<cache_write>\?|\d+))?"
    r"(?:,write30m=(?P<write30m>\?|\d+))?"
    r"(?:,write12h=(?P<write12h>\?|\d+))?"
    r",output=(?P<output>\?|\d+)"
    r"(?:,workload=(?P<workload>\?|[A-Za-z0-9_]+))?"
    r"(?:,attempt=(?P<attempt>\d+))?"
)

CONFIDENCE_RANK = {
    "VERIFIED_CONTRACT": 0,
    "VERIFIED_PUBLIC_RATE": 1,
    "UNVERIFIED": 2,
    "INCOMPLETE_USAGE": 3,
}
REAL_PROVENANCE = {"PRODUCTION_SHADOW", "STAGING_LIVE", "HISTORICAL_REPLAY"}
DEFAULT_COSTS = "0.0050,0.0075,0.0100,0.0125,0.0150,0.0200"
STAGE_LABELS = {
    "ScannerParsing": "Intent extraction",
    "SchemaNavigation": "Navigation and schema/context injection",
    "ResearchTool": "Planning and tool selection",
    "ExplanationGeneration": "Response generation",
    "Embeddings": "Schema/context injection",
    "Summarization": "Response generation",
    "SuggestedQuestions": "Response generation",
}


def flatten(event: dict[str, Any]) -> dict[str, Any]:
    result = dict(event)
    for key in ("Attributes", "attributes", "Properties", "properties", "State", "state"):
        nested = result.pop(key, None)
        if isinstance(nested, dict):
            result.update(nested)
    return result


def event_name(event: dict[str, Any]) -> str:
    for key in ("EventName", "event.name", "MessageTemplate", "messageTemplate", "Message", "message", "RenderedMessage"):
        value = event.get(key)
        if isinstance(value, str) and value:
            return value
    return ""


def read_jsonl(path: Path) -> list[dict[str, Any]]:
    events = []
    with path.open(encoding="utf-8") as source:
        for line_number, line in enumerate(source, 1):
            if not line.strip():
                continue
            try:
                item = json.loads(line)
            except json.JSONDecodeError as exc:
                raise ValueError(f"Invalid JSON on {path}:{line_number}: {exc}") from exc
            if isinstance(item, dict):
                events.append(flatten(item))
    return events


def read_csv(path: Path | None) -> list[dict[str, str]]:
    if path is None:
        return []
    with path.open(encoding="utf-8-sig", newline="") as source:
        return list(csv.DictReader(source))


def decimal(value: Any) -> Decimal | None:
    if value is None or value == "":
        return None
    try:
        return Decimal(str(value))
    except Exception:
        return None


def integer_or_none(value: Any) -> int | None:
    if value in (None, "", "?"):
        return None
    try:
        return int(value)
    except (TypeError, ValueError):
        return None


def confidence(value: Any) -> str:
    normalized = str(value or "UNVERIFIED").upper().replace("-", "_").replace(" ", "_")
    return normalized if normalized in CONFIDENCE_RANK else "UNVERIFIED"


def worse_confidence(left: str, right: str) -> str:
    return max((left, right), key=lambda item: CONFIDENCE_RANK[item])


def read_prices(path: Path) -> tuple[dict[tuple[str, str], dict[str, Any]], dict[str, Any]]:
    payload = json.loads(path.read_text(encoding="utf-8"))
    if payload.get("currency", "USD").upper() != "USD":
        raise ValueError("Calibration cost inputs must be normalized to USD.")
    if not payload.get("asOfUtc"):
        raise ValueError("The price manifest must include asOfUtc so the snapshot is reproducible.")

    rates: dict[tuple[str, str], dict[str, Any]] = {}
    for original in payload.get("models", []):
        item = dict(original)
        if item.get("currency", "USD").upper() != "USD":
            continue
        rate_confidence = confidence(item.get("pricingConfidence", item.get("confidence")))
        if "pricingConfidence" not in item and "confidence" not in item and item.get("verified", False):
            rate_confidence = "VERIFIED_CONTRACT"
        item["pricingConfidence"] = rate_confidence
        if rate_confidence not in {"VERIFIED_CONTRACT", "VERIFIED_PUBLIC_RATE"}:
            continue
        key = (item["providerKey"].casefold(), item["modelKey"].casefold())
        if key in rates:
            raise ValueError(
                f"The manifest has multiple rates for {item['providerKey']}/{item['modelKey']}; "
                "run each effective-price window with one applicable rate per model."
            )
        rates[key] = item
    return rates, payload


def parse_calls(value: Any) -> list[dict[str, Any]]:
    if not isinstance(value, str):
        return []
    parsed = []
    for match in CALL_RE.finditer(value):
        item = match.groupdict()
        parsed.append({
            "provider": item["provider"],
            "model": item["model"],
            "calls": int(item["calls"]),
            "input": integer_or_none(item["input"]),
            "cached": integer_or_none(item["cached"]),
            "cache_write": integer_or_none(item.get("cache_write")),
            "write30m": integer_or_none(item.get("write30m")),
            "write12h": integer_or_none(item.get("write12h")),
            "output": integer_or_none(item["output"]),
            "workload": item.get("workload") or "Unattributed",
            "attempt": int(item["attempt"]) if item.get("attempt") else 0,
        })
    return parsed


def price_call(call: dict[str, Any], prices: dict[tuple[str, str], dict[str, Any]]) -> tuple[Decimal | None, bool, str]:
    rate = prices.get((call["provider"].casefold(), call["model"].casefold()))
    if rate is None:
        return None, False, "UNVERIFIED"
    if any(call[key] is None for key in ("input", "cached", "output")):
        return None, False, "INCOMPLETE_USAGE"

    input_tokens = call["input"]
    cached_tokens = call["cached"]
    output_tokens = call["output"]
    write_total = call["cache_write"]
    write30m = call["write30m"]
    write12h = call["write12h"]
    input_price = decimal(rate.get("inputPerMillionTokens"))
    cached_price = decimal(rate.get("cachedInputPerMillionTokens"))
    output_price = decimal(rate.get("outputPerMillionTokens"))
    multiplier30m = decimal(rate.get("cacheWrite30mInputMultiplier", rate.get("cacheWriteInputMultiplier", 1)))
    multiplier12h = decimal(rate.get("cacheWrite12hInputMultiplier", rate.get("cacheWriteInputMultiplier", 1)))
    if any(value is None or value < 0 for value in (input_price, cached_price, output_price)) or multiplier30m is None or multiplier12h is None:
        return None, False, "UNVERIFIED"

    write_required = bool(rate.get("cacheWriteUsageRequired", False)) or multiplier30m != 1 or multiplier12h != 1
    if all(value is None for value in (write_total, write30m, write12h)):
        if write_required:
            return None, False, "INCOMPLETE_USAGE"
        write_total = write30m = write12h = 0
    elif write30m is not None and write12h is not None:
        if write30m < 0 or write12h < 0 or (write_total is not None and write_total != write30m + write12h):
            return None, False, "INCOMPLETE_USAGE"
        write_total = write30m + write12h
    elif write_total is not None:
        if write_total < 0:
            return None, False, "INCOMPLETE_USAGE"
        if write_total == 0:
            write30m = write12h = 0
        elif multiplier30m == multiplier12h:
            write30m, write12h = write_total, 0
        else:
            return None, False, "INCOMPLETE_USAGE"
    else:
        return None, False, "INCOMPLETE_USAGE"

    if min(input_tokens, cached_tokens, output_tokens, write_total, write30m, write12h) < 0:
        return None, False, "INCOMPLETE_USAGE"
    if cached_tokens + write_total > input_tokens:
        return None, False, "INCOMPLETE_USAGE"

    ordinary = input_tokens - cached_tokens - write_total
    long_threshold = integer_or_none(rate.get("longContextThresholdInputTokens"))
    long_input_multiplier = decimal(rate.get("longContextInputMultiplier", 1)) or Decimal(1)
    long_output_multiplier = decimal(rate.get("longContextOutputMultiplier", 1)) or Decimal(1)
    input_multiplier = long_input_multiplier if long_threshold is not None and input_tokens > long_threshold else Decimal(1)
    output_multiplier = long_output_multiplier if long_threshold is not None and input_tokens > long_threshold else Decimal(1)
    input_cost = (
        Decimal(ordinary) * input_price
        + Decimal(cached_tokens) * cached_price
        + Decimal(write30m) * input_price * multiplier30m
        + Decimal(write12h) * input_price * multiplier12h
    ) * input_multiplier
    output_cost = Decimal(output_tokens) * output_price * output_multiplier
    return Decimal(call["calls"]) * (input_cost + output_cost) / Decimal(1_000_000), True, rate["pricingConfidence"]


def percentile(values: list[Decimal], quantile: Decimal) -> Decimal | None:
    if not values:
        return None
    ordered = sorted(values)
    position = Decimal(len(ordered) - 1) * quantile
    lower, upper = int(position), int(position.to_integral_value(rounding=ROUND_CEILING))
    if lower == upper:
        return ordered[lower]
    fraction = position - lower
    return ordered[lower] + (ordered[upper] - ordered[lower]) * fraction


def distribution(values: list[Decimal], total_count: int | None = None) -> dict[str, Any]:
    return {
        "observed_count": len(values),
        "missing_count": max(0, (total_count or len(values)) - len(values)),
        "min": min(values) if values else None,
        "max": max(values) if values else None,
        "p50": percentile(values, Decimal("0.50")),
        "p90": percentile(values, Decimal("0.90")),
        "p95": percentile(values, Decimal("0.95")),
        "p99": percentile(values, Decimal("0.99")),
    }


def sum_known(values: list[int | None]) -> int | None:
    return sum(value for value in values if value is not None) if all(value is not None for value in values) else None


def charge_for(cost: Decimal, cost_per_credit: Decimal, multiplier: Decimal, minimum: int, maximum: int) -> tuple[int, bool, Decimal, Decimal]:
    raw = (cost * multiplier / cost_per_credit).quantize(Decimal("0.000001"), rounding=ROUND_HALF_UP)
    rounded = max(minimum, int(raw.to_integral_value(rounding=ROUND_CEILING)))
    capped = rounded > maximum
    credits = min(rounded, maximum)
    overage = max(Decimal(0), Decimal(credits) * cost_per_credit - cost * multiplier)
    uncovered_cost = max(Decimal(0), cost * multiplier - Decimal(maximum) * cost_per_credit)
    return credits, capped, overage, uncovered_cost


def json_default(value: Any) -> Any:
    if isinstance(value, Decimal):
        return str(value)
    raise TypeError(f"Cannot JSON encode {type(value).__name__}")


def summarize(rows: list[dict[str, Any]], scenarios: list[dict[str, Any]], call_rows: list[dict[str, Any]], provenance: str) -> dict[str, Any]:
    modeled_costs = [row["modeled_cost_usd"] for row in rows if row["modeled_cost_usd"] is not None]
    definitive_costs = [row["modeled_cost_usd"] for row in rows if row["definitive_cost_usd"]]
    calls_per_question = [Decimal(row["model_calls"]) for row in rows]
    confidence_counts = Counter(row["pricing_confidence"] for row in rows)
    input_values: dict[str, list[Decimal]] = defaultdict(list)
    input_missing: Counter[str] = Counter()
    call_observation_count = 0
    for call in call_rows:
        repeats = call["calls"]
        call_observation_count += repeats
        for key in ("input", "cached", "cache_write", "write30m", "write12h", "output"):
            value = call[key]
            if value is None and key == "cache_write" and call["write30m"] is not None and call["write12h"] is not None:
                value = call["write30m"] + call["write12h"]
            if value is None:
                input_missing[key] += repeats
            else:
                input_values[key].extend([Decimal(value)] * repeats)

    aggregates: dict[str, Any] = {
        "sample_provenance": provenance,
        "logical_question_count": len(rows),
        "llm_calls_per_question": distribution(calls_per_question),
        "input_token_distributions_per_call": {
            key: distribution(values, call_observation_count)
            for key, values in input_values.items()
        },
        "input_token_missing_call_counts": dict(input_missing),
        "modeled_cost_question_count": len(modeled_costs),
        "definitive_cost_question_count": len(definitive_costs),
        "unpriced_question_count": sum(row["modeled_cost_usd"] is None for row in rows),
        "unverified_or_incomplete_pricing_question_count": sum(
            row["pricing_confidence"] in {"UNVERIFIED", "INCOMPLETE_USAGE"} for row in rows
        ),
        "unverified_or_incomplete_pricing_question_percent": (
            Decimal(100 * sum(row["pricing_confidence"] in {"UNVERIFIED", "INCOMPLETE_USAGE"} for row in rows)) / len(rows)
            if rows else None
        ),
        "non_contract_rate_question_count": sum(row["pricing_confidence"] == "VERIFIED_PUBLIC_RATE" for row in rows),
        "non_contract_rate_question_percent": (
            Decimal(100 * sum(row["pricing_confidence"] == "VERIFIED_PUBLIC_RATE" for row in rows)) / len(rows)
            if rows else None
        ),
        "pricing_confidence_distribution": dict(confidence_counts),
        "modeled_cost_usd_per_question": distribution(modeled_costs),
        "definitive_cost_usd_per_question": distribution(definitive_costs),
        "scenarios": {},
        "feature141_stage_breakdown": {},
        "financial_note": "Provider-cost coverage ratios are modeled comparisons only; they are not revenue, margin, or profitability.",
    }

    # Stage attribution is based on provider-reported workload labels. It remains unattributed
    # when older log lines do not contain a workload field.
    feature141_operations = {row["operation_id"] for row in rows if "feature141" in row["execution_route"].casefold()}
    stage_costs: dict[str, Decimal] = defaultdict(Decimal)
    stage_calls: Counter[str] = Counter()
    stage_unknown_costs: Counter[str] = Counter()
    stage_tokens: dict[str, Counter[str]] = defaultdict(Counter)
    stage_missing_tokens: dict[str, Counter[str]] = defaultdict(Counter)
    stage_retries: Counter[str] = Counter()
    for call in call_rows:
        operation = call["operation_id"]
        if operation not in feature141_operations:
            continue
        stage = STAGE_LABELS.get(call["workload"], "Unattributed workload")
        stage_calls[stage] += call["calls"]
        stage_retries[stage] += call["calls"] if call["attempt"] > 0 else 0
        for source_key, output_key in (
            ("input", "input_tokens"), ("cached", "cache_read_tokens"),
            ("cache_write", "cache_write_tokens"), ("write30m", "cache_write_30m_tokens"),
            ("write12h", "cache_write_12h_tokens"), ("output", "output_tokens"),
        ):
            value = call[source_key]
            if value is None and source_key == "cache_write" and call["write30m"] is not None and call["write12h"] is not None:
                value = call["write30m"] + call["write12h"]
            if value is None:
                stage_missing_tokens[stage][output_key] += call["calls"]
            else:
                stage_tokens[stage][output_key] += value * call["calls"]
        if call["modeled_cost_usd"] is None:
            stage_unknown_costs[stage] += call["calls"]
        else:
            stage_costs[stage] += call["modeled_cost_usd"]
    total_stage_cost = sum(stage_costs.values(), Decimal(0))
    for stage in sorted(set(stage_calls) | set(stage_costs)):
        stage_cost = stage_costs.get(stage, Decimal(0))
        aggregates["feature141_stage_breakdown"][stage] = {
            "llm_calls": stage_calls[stage],
            "modeled_cost_usd": stage_cost,
            "share_of_attributed_modeled_cost_percent": Decimal(100) * stage_cost / total_stage_cost if total_stage_cost else None,
            "calls_without_priceable_usage": stage_unknown_costs[stage],
            "retry_attempt_calls": stage_retries[stage],
            "token_totals": dict(stage_tokens[stage]),
            "missing_token_call_counts": dict(stage_missing_tokens[stage]),
        }

    feature141_route_costs: dict[str, Decimal] = defaultdict(Decimal)
    feature141_route_calls: Counter[str] = Counter()
    for row in rows:
        if row["operation_id"] in feature141_operations:
            route = row["execution_route"]
            feature141_route_calls[route] += row["model_calls"]
            if row["modeled_cost_usd"] is not None:
                feature141_route_costs[route] += row["modeled_cost_usd"]
    aggregates["feature141_route_breakdown"] = {
        route: {
            "llm_calls": feature141_route_calls[route],
            "modeled_cost_usd": feature141_route_costs.get(route, Decimal(0)),
        }
        for route in sorted(feature141_route_calls)
    }

    for scenario in scenarios:
        label = format(scenario["cost_per_credit"], "f")
        eligible = [row for row in rows if row["modeled_cost_usd"] is not None]
        calculated = [charge_for(row["modeled_cost_usd"], **scenario) for row in eligible]
        credits = [item[0] for item in calculated]
        total_modeled_cost = sum((row["modeled_cost_usd"] for row in eligible), Decimal(0))
        total_credit_value = sum((Decimal(item[0]) * scenario["cost_per_credit"] for item in calculated), Decimal(0))
        legacy_pairs = [
            (row["legacy_credits_charged"], item[0])
            for row, item in zip(eligible, calculated)
            if row["legacy_credits_charged"] is not None
        ]
        aggregates["scenarios"][label] = {
            "modeled_question_count": len(eligible),
            "definitive_question_count": sum(row["definitive_cost_usd"] for row in eligible),
            "mean_credits": (Decimal(sum(credits)) / len(credits)) if credits else None,
            "median_credits": Decimal(str(statistics.median(credits))) if credits else None,
            "credit_distribution_1_to_5": {str(i): credits.count(i) for i in range(1, 6)},
            "credits_above_5_count": sum(value > 5 for value in credits),
            "cap_hit_percent": Decimal(100 * sum(item[1] for item in calculated)) / len(calculated) if calculated else None,
            "modeled_credit_value_to_provider_cost_ratio": total_credit_value / total_modeled_cost if total_modeled_cost else None,
            "modeled_cap_credit_value_usd_per_question": Decimal(scenario["maximum"]) * scenario["cost_per_credit"],
            "modeled_cap_credit_value_usd_for_sample": Decimal(len(eligible) * scenario["maximum"]) * scenario["cost_per_credit"],
            "sampled_provider_cost_above_cap_usd": sum((item[3] for item in calculated), Decimal(0)),
            "maximum_sampled_provider_cost_above_cap_usd_per_question": max((item[3] for item in calculated), default=None),
            "legacy_vs_hypothetical_enforced": {
                "questions_with_both_values": len(legacy_pairs),
                "legacy_charged_credits": sum(pair[0] for pair in legacy_pairs),
                "hypothetical_enforced_credits": sum(pair[1] for pair in legacy_pairs),
                "credit_delta_enforced_minus_legacy": sum(pair[1] - pair[0] for pair in legacy_pairs),
                "higher_than_legacy_count": sum(pair[1] > pair[0] for pair in legacy_pairs),
                "lower_than_legacy_count": sum(pair[1] < pair[0] for pair in legacy_pairs),
                "equal_to_legacy_count": sum(pair[1] == pair[0] for pair in legacy_pairs),
            },
            "by_category": {},
        }
        for category in sorted({row["category"] for row in eligible}):
            category_rows = [row for row in eligible if row["category"] == category]
            category_credits = [charge_for(row["modeled_cost_usd"], **scenario)[0] for row in category_rows]
            aggregates["scenarios"][label]["by_category"][category] = {
                "logical_question_count": len(category_rows),
                "mean_credits": Decimal(sum(category_credits)) / len(category_credits) if category_credits else None,
                "median_credits": Decimal(str(statistics.median(category_credits))) if category_credits else None,
            }
    return aggregates


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--logs", required=True, type=Path, help="Normalized structured billing logs in JSONL format.")
    parser.add_argument("--prices", required=True, type=Path, help="Dated USD rate snapshot; only contract or public rates are modeled.")
    parser.add_argument("--ledger", type=Path, help="CSV export of billing_usage_ledger_entries.")
    parser.add_argument("--reservations", type=Path, help="CSV export of billing_usage_reservations.")
    parser.add_argument("--categories", type=Path, help="Optional JSON map from ExecutionRoute values to analysis categories.")
    parser.add_argument("--sample-provenance", choices=sorted(REAL_PROVENANCE | {"SYNTHETIC_TEST"}), default="SYNTHETIC_TEST")
    parser.add_argument("--cost-per-credit", default=DEFAULT_COSTS, help="Comma-separated USD sensitivity values.")
    parser.add_argument("--multiplier", type=Decimal, default=Decimal("1.0"))
    parser.add_argument("--minimum-credits", type=int, default=1)
    parser.add_argument("--maximum-credits", type=int, default=5)
    parser.add_argument("--out", required=True, type=Path, help="Output directory; CSV and JSON summaries are written here.")
    args = parser.parse_args()

    events = read_jsonl(args.logs)
    prices, price_manifest = read_prices(args.prices)
    ledger_rows = read_csv(args.ledger)
    reservation_rows = read_csv(args.reservations)
    category_map = json.loads(args.categories.read_text(encoding="utf-8")) if args.categories else {}
    costs = [Decimal(value.strip()) for value in args.cost_per_credit.split(",") if value.strip()]
    if any(value <= 0 for value in costs) or args.multiplier <= 0 or args.minimum_credits < 0 or args.maximum_credits < args.minimum_credits:
        raise ValueError("Credit sensitivity values and limits must be positive and ordered.")

    operation_for_correlation: dict[str, str] = {}
    calls_by_operation: dict[str, list[dict[str, Any]]] = defaultdict(list)
    route_by_operation: dict[str, str] = {}
    evaluation_by_correlation: dict[str, dict[str, Any]] = {}
    settlement_by_operation: dict[str, str] = {}
    seen_call_events: set[tuple[str, str]] = set()

    for event in events:
        name = event_name(event)
        correlation = str(event.get("CorrelationId", ""))
        operation = str(event.get("OperationId", ""))
        if "Usage-based model-call accounting" in name:
            if operation and correlation:
                operation_for_correlation[correlation] = operation
            if operation:
                event_key = (operation, str(event.get("Calls", "")))
                if event_key not in seen_call_events:
                    calls_by_operation[operation].extend(parse_calls(event.get("Calls")))
                    seen_call_events.add(event_key)
                route_by_operation[operation] = str(event.get("ExecutionRoute") or route_by_operation.get(operation, "Unknown"))
        elif "Usage-based credit charge evaluated" in name and correlation:
            if operation:
                operation_for_correlation[correlation] = operation
            evaluation_by_correlation[correlation] = event
        elif "Usage-based billing settlement completed" in name and operation:
            operation_for_correlation[correlation] = operation
            settlement_by_operation[operation] = str(event.get("SettlementOutcome", "Unknown"))
            route_by_operation[operation] = str(event.get("ExecutionRoute") or route_by_operation.get(operation, "Unknown"))

    ledger_by_operation: dict[str, dict[str, str]] = {}
    for row in ledger_rows:
        operation = row.get("IdempotencyKey", "").removesuffix(":charge")
        if operation:
            ledger_by_operation[operation] = row
    reservations_by_operation: dict[str, dict[str, str]] = {}
    for row in reservation_rows:
        operation = row.get("IdempotencyKey", "").removesuffix(":reservation")
        if operation:
            reservations_by_operation[operation] = row

    evaluations_by_operation: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for correlation, event in evaluation_by_correlation.items():
        operation = str(event.get("OperationId") or operation_for_correlation.get(correlation) or f"unmatched:{correlation}")
        evaluations_by_operation[operation].append(event)
    operation_ids = set(calls_by_operation) | set(evaluations_by_operation) | set(ledger_by_operation) | set(reservations_by_operation)
    rows: list[dict[str, Any]] = []
    flattened_call_rows: list[dict[str, Any]] = []

    for operation in sorted(operation_ids):
        evaluations = evaluations_by_operation.get(operation, [])
        evaluation = evaluations[-1] if evaluations else {}
        calls = calls_by_operation.get(operation, [])
        call_costs = [price_call(call, prices) for call in calls]
        modeled_cost = None if any(cost is None or not complete for cost, complete, _ in call_costs) else sum((cost for cost, _, _ in call_costs if cost is not None), Decimal(0))
        if not calls and evaluations and int(decimal(evaluation.get("ModelCalls")) or 0) == 0:
            modeled_cost = Decimal(0)
        confidence_value = "VERIFIED_CONTRACT"
        for _, _, call_confidence in call_costs:
            confidence_value = worse_confidence(confidence_value, call_confidence)
        if confidence(evaluation.get("PricingConfidence")) == "INCOMPLETE_USAGE":
            confidence_value = "INCOMPLETE_USAGE"
        if any(not complete for _, complete, _ in call_costs):
            confidence_value = "INCOMPLETE_USAGE"
        if not calls:
            confidence_value = confidence(evaluation.get("PricingConfidence"))

        definitive = (
            modeled_cost is not None
            and args.sample_provenance in REAL_PROVENANCE
            and confidence_value == "VERIFIED_CONTRACT"
        )
        ledger = ledger_by_operation.get(operation, {})
        reservation = reservations_by_operation.get(operation, {})
        route = route_by_operation.get(operation, str(evaluation.get("ExecutionRoute", "Unknown")))
        legacy_credits = decimal(ledger.get("CreditsCharged"))
        operation_call_list = calls
        total_calls = sum(call["calls"] for call in calls)
        input_total = sum_known([call["input"] for call in operation_call_list])
        cached_total = sum_known([call["cached"] for call in operation_call_list])
        write_total = sum_known([call["cache_write"] for call in operation_call_list])
        write30m_total = sum_known([call["write30m"] for call in operation_call_list])
        write12h_total = sum_known([call["write12h"] for call in operation_call_list])
        output_total = sum_known([call["output"] for call in operation_call_list])

        rows.append({
            "operation_id": operation,
            "execution_route": route,
            "category": category_map.get(route, route or "Unclassified"),
            "operation_code": evaluation.get("OperationCode", ledger.get("OperationCode", "")),
            "completion_status": evaluation.get("CompletionStatus", ledger.get("CompletionStatus", "")),
            "model_calls": total_calls,
            "models": ";".join(sorted({f"{call['provider']}/{call['model']}" for call in calls})),
            "input_tokens": input_total,
            "cached_input_tokens": cached_total,
            "cache_write_input_tokens": write_total,
            "cache_write_30m_input_tokens": write30m_total,
            "cache_write_12h_input_tokens": write12h_total,
            "output_tokens": output_total,
            "modeled_cost_usd": modeled_cost,
            "definitive_cost_usd": definitive,
            "pricing_confidence": confidence_value,
            "sample_provenance": args.sample_provenance,
            "legacy_credits_charged": legacy_credits,
            "hypothetical_credits_logged": decimal(evaluation.get("ProposedCredits")),
            "policy_version": evaluation.get("PolicyVersion", ""),
            "cost_per_credit_configured": decimal(evaluation.get("CostPerCredit")),
            "pricing_multiplier_configured": decimal(evaluation.get("PricingMultiplier")),
            "maximum_credit_cap_configured": decimal(evaluation.get("MaximumCreditsPerQuestion")),
            "missing_usage_fallback_credits": decimal(evaluation.get("MissingUsageFallbackCredits")),
            "reservation_status": reservation.get("Status", "Unknown"),
            "reserved_credits": decimal(reservation.get("ReservedCredits")),
            "settlement_outcome": settlement_by_operation.get(operation, "Unknown"),
            "settlement_status": (
                "Committed" if ledger else "Released" if reservation.get("Status") == "Released" else
                "Pending" if reservation.get("Status") == "Reserved" else "Unknown"
            ),
            "cap_reached_logged": evaluation.get("CappedAtMaximum", "Unknown"),
            "unmeasured_model_calls": decimal(evaluation.get("UnmeasuredModelCalls")),
            "currency": "USD" if modeled_cost is not None else "Unpriced",
        })

        for call, (cost, complete, rate_confidence) in zip(calls, call_costs):
            flattened_call_rows.append({
                **call,
                "operation_id": operation,
                "execution_route": route,
                "category": category_map.get(route, route or "Unclassified"),
                "modeled_cost_usd": cost if complete else None,
                "pricing_confidence": rate_confidence,
            })

    scenarios = [
        {"cost_per_credit": value, "multiplier": args.multiplier, "minimum": args.minimum_credits, "maximum": args.maximum_credits}
        for value in costs
    ]
    for row in rows:
        cost = row["modeled_cost_usd"]
        for scenario in scenarios:
            suffix = format(scenario["cost_per_credit"], "f")
            if cost is None:
                row[f"credits_at_{suffix}"] = None
                row[f"cap_at_{suffix}"] = None
                row[f"rounding_overage_usd_at_{suffix}"] = None
            else:
                credits, capped, overage, _ = charge_for(cost, **scenario)
                row[f"credits_at_{suffix}"] = credits
                row[f"cap_at_{suffix}"] = capped
                row[f"rounding_overage_usd_at_{suffix}"] = overage

    args.out.mkdir(parents=True, exist_ok=True)
    with (args.out / "questions.csv").open("w", encoding="utf-8", newline="") as target:
        writer = csv.DictWriter(target, fieldnames=list(rows[0].keys()) if rows else ["operation_id"])
        writer.writeheader()
        writer.writerows(rows)
    summary = summarize(rows, scenarios, flattened_call_rows, args.sample_provenance)
    summary["price_manifest"] = {
        "version": price_manifest.get("version"),
        "as_of_utc": price_manifest.get("asOfUtc"),
        "sha256": hashlib.sha256(args.prices.read_bytes()).hexdigest(),
        "accepted_model_rates": len(prices),
        "unverified_model_rate_count": sum(
            confidence(item.get("pricingConfidence", item.get("confidence"))) == "UNVERIFIED"
            for item in price_manifest.get("models", [])
        ),
    }
    (args.out / "summary.json").write_text(json.dumps(summary, indent=2, default=json_default), encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
