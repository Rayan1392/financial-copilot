# User Story - Derived Feature Foundation

## Story

As a financial intelligence platform owner,
I want a lightweight, reproducible derived-feature foundation,
so that future ranking, scoring, anomaly detection, recommendations, and portfolio intelligence can build on stable backend evidence without introducing a full ML platform in Phase 1.

## Acceptance Criteria

- The domain can represent `DerivedFeature`, `FeatureDefinition`, `FeatureSnapshot`, `FeatureVersion`, `FeatureComputationJob`, and `FeatureDependency`.
- Example future features include `MomentumScore`, `EarningsQualityScore`, `RelativeStrength`, `VolatilityScore`, `LiquidityScore`, `GrowthConsistency`, and `SmartMoneySignal`.
- Feature definitions identify input metric/feature dependencies, policy version, required observation window, output unit/range, and reproducibility metadata.
- Feature computation is deterministic for the same versioned inputs and policy.
- Historical feature snapshots can be persisted for audit, backtesting preparation, evaluation, and later portfolio intelligence.
- Feature recalculation can be scheduled or triggered asynchronously through RabbitMQ worker workflows.
- AI orchestration and future ranking tools consume feature results through stable Application interfaces rather than embedding formula logic in prompts.
- The initial Scanner MVP is not blocked on implementing advanced feature scores; only architecture/contracts and explicitly promoted feature implementations are required.
- This capability is not described or implemented as a general ML training, model registry, or online feature-store platform.

## Technical Notes

- Feature calculations build on normalized data, semantic metric versions from `015-financial-semantic-layer`, and derived metrics from `006-derived-metrics-engine`.
- A future model-based score may be added through a separately governed prediction capability; deterministic feature evidence remains separately traceable.
- Keep feature storage and computation inside the modular monolith and worker boundary initially.
