# Tasks

- Define trace, metric, error-category, redaction, retention, and provider-attempt telemetry contracts.
- Define OpenTelemetry-compatible activity/span naming and correlation propagation across facade, workflow, tool, provider, worker, cache, and Billing boundaries.
- Integrate `IAiExecutionTelemetrySink` outputs from `014-ai-model-provider-abstraction` with workflow-level telemetry.
- Define protected operational read/reporting requirements for latency, cost, errors, retries, fallbacks, and provider comparisons.
- Define privacy and tenant-isolation controls for prompt/tool/answer trace retention.
- Add verification requirements for correlation continuity, telemetry redaction, retry visibility, and reconciliation with Billing usage outcomes.
