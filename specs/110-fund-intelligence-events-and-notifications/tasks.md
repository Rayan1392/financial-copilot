# Tasks — Fund Intelligence Events and Notifications

## 1. Event Taxonomy and Ownership

- [ ] Extend Feature 084 insight taxonomy with fund-specific types without changing existing event semantics.
- [ ] Define subject types for Fund, Company/Security, Industry, and ConsensusSnapshot.
- [ ] Make Feature 110 owner only of detectors/evidence adapters; keep scoring framework, personalization, notification decisions, delivery, and history in existing features.
- [ ] Document delayed-disclosure and correction semantics.

## 2. Detector Inputs and Eligibility

- [ ] Consume terminal persisted snapshots/signals from Features 105–107.
- [ ] Require accepted non-superseded report revisions and minimum confidence/coverage.
- [ ] Define detector-specific thresholds for weight change, amount, fund breadth, new-entry/exit count, score change, streak, sector rotation, risk posture, and adjusted exposure.
- [ ] Version all thresholds and eligibility policies.
- [ ] Suppress or downgrade events with unresolved identity, material reconciliation issue, or insufficient reporting-fund coverage.

## 3. Detector Implementations

- [ ] Implement `FundNewPositionDetector` and `FundFullExitDetector` for material positions.
- [ ] Implement material increase/reduction detector using disclosed activity plus weight/quantity evidence.
- [ ] Implement cross-fund accumulation/distribution spike detectors using Feature 106 snapshots and prior comparable period.
- [ ] Implement multi-period institutional streak detector.
- [ ] Implement sector rotation and fund risk-posture change detectors.
- [ ] Implement material valuation-adjustment risk detector.
- [ ] Implement optional quality-weighted participation detector only when Feature 107 minimum sample/confidence is satisfied.

## 4. Evidence and Deduplication

- [ ] Build immutable evidence bundles containing source report ids, fund ids, company/industry, exact period dates, metrics, thresholds, baseline, coverage, confidence, calculation versions, and delayed-disclosure warning.
- [ ] Create stable deduplication keys by detector version, accepted report/snapshot, subject, and threshold policy.
- [ ] Link corrected/superseded source reports to prior events without mutating historical evidence.
- [ ] Ensure one reprocess/recalculation cannot generate duplicate events.

## 5. Personalization and Trackers

- [ ] Add relevance adapters for Feature 085 followed symbols and Feature 086 personalized feed.
- [ ] Extend Feature 091 condition registry with governed conditions such as fund buyer count, new-entry count, accumulation score, streak, and full-exit count.
- [ ] Add Feature 093 radar categories for fund activity and sector rotation.
- [ ] Enforce actor/tenant isolation and entitlement checks.
- [ ] Do not infer that following a symbol means owning it.

## 6. Notification and Telegram Rendering

- [ ] Add notification templates for each event type with concise Persian title, evidence summary, period, delayed-disclosure label, confidence, and actions.
- [ ] Reuse Feature 097 throttling, grouping, quiet hours, digesting, and delivery attempts.
- [ ] Reuse Feature 089 Telegram adapter and Feature 099 immutable history/why explanation.
- [ ] Add actions: OpenSymbol, OpenFund, ViewContributingFunds, OpenSourceReport, AskWhy, CreateOrEditTracker, MuteCategory.
- [ ] Ensure low-confidence events are not promoted as urgent.

## 7. Billing, Security, and Observability

- [ ] Reuse existing entitlement and usage policy; detection itself must not consume user AI credits.
- [ ] Charge only for explicit AI follow-up according to existing Billing rules.
- [ ] Protect raw report/source links with authorization.
- [ ] Emit detector candidate/created/suppressed/deduplicated/corrected counts, coverage, confidence, notification decisions, and delivery outcomes.
- [ ] Trace report -> analytics/consensus -> insight event -> notification intent -> delivery -> alert history.

## 8. Tests and Acceptance Scenarios

- [ ] Unit-test every detector boundary, evidence value, confidence gate, deduplication key, correction link, and Persian template.
- [ ] Integration-test Feature 084 event persistence, followed-symbol relevance, tracker evaluation, Feature 097 noise control, Telegram delivery, and Feature 099 history/why.
- [ ] Replay-test report reprocess and corrected revision for duplicate prevention.
- [ ] Given a high accumulation score with insufficient reporting-fund coverage, suppress or downgrade according to policy and show the reason.
- [ ] Given a followed symbol with a new cross-fund accumulation event, deliver at most one notification decision per actor/event and preserve exact evidence.
- [ ] Given a superseded report, keep old alert history immutable and attach correction status rather than rewriting the original alert.

## Completion Gate

- [ ] Keep tasks unchecked until detector evidence, delayed-data wording, correction/deduplication, personalization, noise control, Telegram delivery, and immutable history all pass end-to-end tests.
