using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Scanner;

namespace FinancialCopilot.Application.AI.Evaluation;

// Curated Phase 1 golden datasets covering bilingual parsing, semantic metric resolution,
// ambiguity/clarification routing, evidence completeness, confidence protection, and
// billing-metadata protection. These are the regression baseline for the scanner MVP.
public static class GoldenDatasets
{
    private static readonly Guid Phase1DatasetId =
        new("d0000001-0000-0000-0000-000000000001");

    public static readonly EvaluationDataset Phase1ScannerEvaluation = new(
        DatasetId: Phase1DatasetId,
        Name: "phase1-scanner-evaluation",
        Version: "1.0.0",
        Questions: BuildPhase1Questions(Phase1DatasetId),
        CreatedAt: new DateTimeOffset(2026, 5, 27, 0, 0, 0, TimeSpan.Zero));

    private static IReadOnlyCollection<GoldenQuestion> BuildPhase1Questions(Guid datasetId) =>
    [
        // ── Bilingual Parsing ──────────────────────────────────────────────────────
        new(
            QuestionId: new Guid("b1000001-0000-0000-0000-000000000001"),
            DatasetId: datasetId,
            Query: "Show me stocks with PE ratio below 15",
            Language: "en",
            Category: EvaluationCategory.BilingualParsing,
            ExpectedIntent: DetectedIntent.Scanner,
            ExpectedClarification: false,
            ExpectedConditions: [new ExpectedCondition("PE_TTM", ConditionOperator.LessThan, 15m)],
            EvidenceRequirements: [],
            ProseRequirements: [],
            Notes: "English PE alias must resolve to PE_TTM"),

        new(
            QuestionId: new Guid("b1000002-0000-0000-0000-000000000001"),
            DatasetId: datasetId,
            Query: "سهام‌هایی با نسبت قیمت به درآمد کمتر از ۱۵ نشان بده",
            Language: "fa",
            Category: EvaluationCategory.BilingualParsing,
            ExpectedIntent: DetectedIntent.Scanner,
            ExpectedClarification: false,
            ExpectedConditions: [new ExpectedCondition("PE_TTM", ConditionOperator.LessThan, 15m)],
            EvidenceRequirements: [],
            ProseRequirements: [],
            Notes: "Persian 'نسبت قیمت به درآمد' must resolve to PE_TTM"),

        // ── Semantic Metric Resolution ─────────────────────────────────────────────
        new(
            QuestionId: new Guid("b2000001-0000-0000-0000-000000000001"),
            DatasetId: datasetId,
            Query: "Companies with price-to-earnings ratio under 20",
            Language: "en",
            Category: EvaluationCategory.SemanticMetricResolution,
            ExpectedIntent: DetectedIntent.Scanner,
            ExpectedClarification: false,
            ExpectedConditions: [new ExpectedCondition("PE_TTM", ConditionOperator.LessThan, 20m)],
            EvidenceRequirements: [],
            ProseRequirements: [],
            Notes: "Hyphenated alias 'price-to-earnings' must resolve to PE_TTM"),

        new(
            QuestionId: new Guid("b2000002-0000-0000-0000-000000000001"),
            DatasetId: datasetId,
            Query: "Stocks with P/S below 2",
            Language: "en",
            Category: EvaluationCategory.SemanticMetricResolution,
            ExpectedIntent: DetectedIntent.Scanner,
            ExpectedClarification: false,
            ExpectedConditions: [new ExpectedCondition("PS_TTM", ConditionOperator.LessThan, 2m)],
            EvidenceRequirements: [],
            ProseRequirements: [],
            Notes: "Abbreviation 'P/S' must resolve to PS_TTM"),

        new(
            QuestionId: new Guid("b2000003-0000-0000-0000-000000000001"),
            DatasetId: datasetId,
            Query: "سهام با نسبت قیمت به فروش زیر ۳",
            Language: "fa",
            Category: EvaluationCategory.SemanticMetricResolution,
            ExpectedIntent: DetectedIntent.Scanner,
            ExpectedClarification: false,
            ExpectedConditions: [new ExpectedCondition("PS_TTM", ConditionOperator.LessThan, 3m)],
            EvidenceRequirements: [],
            ProseRequirements: [],
            Notes: "Persian 'نسبت قیمت به فروش' must resolve to PS_TTM"),

        // ── Ambiguity and Clarification ───────────────────────────────────────────
        new(
            QuestionId: new Guid("b3000001-0000-0000-0000-000000000001"),
            DatasetId: datasetId,
            Query: "Show me good stocks",
            Language: "en",
            Category: EvaluationCategory.AmbiguityClarification,
            ExpectedIntent: DetectedIntent.Clarification,
            ExpectedClarification: true,
            ExpectedConditions: [],
            EvidenceRequirements: [],
            ProseRequirements: [],
            Notes: "Vague query must trigger clarification; no scanner plan must be produced"),

        new(
            QuestionId: new Guid("b3000002-0000-0000-0000-000000000001"),
            DatasetId: datasetId,
            Query: "سهام خوب نشان بده",
            Language: "fa",
            Category: EvaluationCategory.AmbiguityClarification,
            ExpectedIntent: DetectedIntent.Clarification,
            ExpectedClarification: true,
            ExpectedConditions: [],
            EvidenceRequirements: [],
            ProseRequirements: [],
            Notes: "Persian vague query must trigger clarification"),

        new(
            QuestionId: new Guid("b3000003-0000-0000-0000-000000000001"),
            DatasetId: datasetId,
            Query: "What is the weather today?",
            Language: "en",
            Category: EvaluationCategory.AmbiguityClarification,
            ExpectedIntent: DetectedIntent.Unknown,
            ExpectedClarification: false,
            ExpectedConditions: [],
            EvidenceRequirements: [],
            ProseRequirements: [],
            Notes: "Off-topic request must route to Unknown intent; scanner must not activate"),

        // Financial statement full-table routing.
        new(
            QuestionId: new Guid("b3500001-0000-0000-0000-000000000001"),
            DatasetId: datasetId,
            Query: "آخرین صورت سود و زیان کگل را نشان بده",
            Language: "fa",
            Category: EvaluationCategory.TableSchema,
            ExpectedIntent: DetectedIntent.FinancialStatementTableLookup,
            ExpectedClarification: false,
            ExpectedConditions: [],
            EvidenceRequirements: [],
            ProseRequirements: [],
            Notes: "Full income-statement display request must not route to period analysis"),

        new(
            QuestionId: new Guid("b3500002-0000-0000-0000-000000000001"),
            DatasetId: datasetId,
            Query: "آخرین ترازنامه کگل",
            Language: "fa",
            Category: EvaluationCategory.TableSchema,
            ExpectedIntent: DetectedIntent.FinancialStatementTableLookup,
            ExpectedClarification: false,
            ExpectedConditions: [],
            EvidenceRequirements: [],
            ProseRequirements: [],
            Notes: "Full balance-sheet display request must route to table lookup"),

        new(
            QuestionId: new Guid("b3500003-0000-0000-0000-000000000001"),
            DatasetId: datasetId,
            Query: "جریان وجه نقد ۹ ماهه کگل را نمایش بده",
            Language: "fa",
            Category: EvaluationCategory.TableSchema,
            ExpectedIntent: DetectedIntent.FinancialStatementTableLookup,
            ExpectedClarification: false,
            ExpectedConditions: [],
            EvidenceRequirements: [],
            ProseRequirements: [],
            Notes: "Cash-flow table request with period filter must route deterministically"),

        // ── Evidence Completeness ─────────────────────────────────────────────────
        new(
            QuestionId: new Guid("b4000001-0000-0000-0000-000000000001"),
            DatasetId: datasetId,
            Query: "Stocks with PE below 6 and PS below 1.5",
            Language: "en",
            Category: EvaluationCategory.EvidenceCompleteness,
            ExpectedIntent: DetectedIntent.Scanner,
            ExpectedClarification: false,
            ExpectedConditions:
            [
                new ExpectedCondition("PE_TTM", ConditionOperator.LessThan, 6m),
                new ExpectedCondition("PS_TTM", ConditionOperator.LessThan, 1.5m)
            ],
            EvidenceRequirements:
            [
                new EvidenceRequirement("PE_TTM"),
                new EvidenceRequirement("PS_TTM")
            ],
            ProseRequirements: [],
            Notes: "Both conditions must appear as metric evidence in the explainable answer"),

        new(
            QuestionId: new Guid("b4000002-0000-0000-0000-000000000001"),
            DatasetId: datasetId,
            Query: "سهام‌هایی که رشد سود خالص سالانه آن‌ها بیشتر از ۲۰ درصد است",
            Language: "fa",
            Category: EvaluationCategory.EvidenceCompleteness,
            ExpectedIntent: DetectedIntent.Scanner,
            ExpectedClarification: false,
            ExpectedConditions:
            [
                new ExpectedCondition("NET_PROFIT_GROWTH_YOY", ConditionOperator.GreaterThan, 20m)
            ],
            EvidenceRequirements:
            [
                new EvidenceRequirement("NET_PROFIT_GROWTH_YOY")
            ],
            ProseRequirements: [],
            Notes: "Persian annual net profit growth query must produce evidence for NET_PROFIT_GROWTH_YOY"),

        // ── Confidence Protection ─────────────────────────────────────────────────
        new(
            QuestionId: new Guid("b5000001-0000-0000-0000-000000000001"),
            DatasetId: datasetId,
            Query: "Find undervalued stocks with PE below 8",
            Language: "en",
            Category: EvaluationCategory.ConfidenceProtection,
            ExpectedIntent: DetectedIntent.Scanner,
            ExpectedClarification: false,
            ExpectedConditions: [new ExpectedCondition("PE_TTM", ConditionOperator.LessThan, 8m)],
            EvidenceRequirements: [],
            ProseRequirements: [],
            Notes: "Confidence score must carry policy version 'v1' and be within [0, 1]; LLM must not fabricate or override it"),

        // ── Billing Metadata Protection ───────────────────────────────────────────
        new(
            QuestionId: new Guid("b6000001-0000-0000-0000-000000000001"),
            DatasetId: datasetId,
            Query: "Stocks with EPS above 3",
            Language: "en",
            Category: EvaluationCategory.BillingMetadataProtection,
            ExpectedIntent: DetectedIntent.Scanner,
            ExpectedClarification: false,
            ExpectedConditions: [new ExpectedCondition("EPS_TTM", ConditionOperator.GreaterThan, 3m)],
            EvidenceRequirements: [],
            ProseRequirements: [],
            Notes: "Usage metadata must come exclusively from the billing backend; LLM must not produce or alter billing fields"),
    ];
}

// In-memory seed repository backed by the hard-coded golden datasets.
// Replace at the composition root with a durable store when dataset management is needed.
public sealed class SeedEvaluationDatasetRepository : IEvaluationDatasetRepository
{
    private static readonly IReadOnlyCollection<EvaluationDataset> All =
    [
        GoldenDatasets.Phase1ScannerEvaluation
    ];

    public IReadOnlyCollection<EvaluationDataset> GetAll() => All;

    public EvaluationDataset? GetById(Guid datasetId) =>
        All.FirstOrDefault(d => d.DatasetId == datasetId);

    public EvaluationDataset? GetByName(string name) =>
        All.FirstOrDefault(d => d.Name == name);
}
