# User Story — Fund Intelligence Events and Notifications

## Status
`[ ]` Proposed

## Feature
Publish significant fund portfolio changes and cross-fund institutional patterns into the existing insight, personalization, tracker, Telegram, notification, and alert-history infrastructure.

## Story

As a FinancialCopilot user,

I want to receive controlled alerts when a followed symbol or market segment shows meaningful fund accumulation, exits, sector rotation, or valuation-quality risk,

so that I can review new institutional disclosure evidence without manually checking every monthly fund report.

## Business Context

Features 084–099 already provide persisted insight events, personalized feeds, followed symbols, conditional trackers, notification orchestration, Telegram delivery, noise control, and immutable alert history. Feature 110 must add fund-intelligence detectors and evidence adapters only. It must not create a second alert or transport subsystem.

Monthly fund disclosures are delayed and can be corrected. Alerts must state the report period and publication/import time, deduplicate corrected/reprocessed reports, and preserve immutable evidence.

## Dependencies

- Features `105`, `106`, `107`, and `108`.
- Feature `084-proactive-market-event-intelligence`.
- Features `085`, `086`, `089`, `091`, `093`, `097`, and `099`.

## In Scope

- Significant new fund position.
- Significant full fund exit.
- Material fund position increase/reduction.
- Cross-fund accumulation/distribution spike.
- Multi-period accumulation/distribution streak.
- Sector rotation breadth change.
- Fund risk-posture shift.
- Material valuation-adjustment exposure.
- Optional high-quality-fund participation evidence when Feature 107 has sufficient samples.
- Personalized relevance for followed symbols and saved tracker conditions.
- Telegram/web notification rendering and immutable history through existing infrastructure.

## Out of Scope

- Real-time order-flow alerts.
- Automatic trading or recommendations.
- Rebuilding notification preferences, delivery queues, payment, or alert history.
- Alerting from low-coverage or unreconciled data without explicit degraded policy.

## Acceptance Criteria

1. Detectors consume persisted Feature 105–107 outputs, not raw Excel.
2. Every event includes fund/report period, import/publication freshness, source reports, detector version, thresholds, coverage, and confidence.
3. Alerts explicitly state that fund disclosures are monthly and delayed.
4. Reprocessing the same accepted source revision does not create duplicate insight events.
5. A corrected/superseded report creates a correction/supersession relationship according to Feature 084/099 immutability rules.
6. Low coverage, unresolved securities, reconciliation failures, or valuation-quality issues reduce confidence or suppress alerts according to versioned policy.
7. Followed-symbol and tracker relevance use canonical actor/company identity.
8. Notification orchestration, entitlements, throttling, digesting, Telegram delivery, and history reuse Features 097–099.
9. No event or alert uses buy/sell recommendation language.
10. User mute/dismiss/feedback behavior remains governed by existing alert features.

## Detector Proposal

```text
FundNewPositionDetector
FundFullExitDetector
FundMaterialPositionChangeDetector
CrossFundAccumulationSpikeDetector
CrossFundDistributionSpikeDetector
InstitutionalStreakDetector
FundSectorRotationDetector
FundRiskPostureChangeDetector
FundValuationAdjustmentRiskDetector
QualityWeightedFundParticipationDetector
```
