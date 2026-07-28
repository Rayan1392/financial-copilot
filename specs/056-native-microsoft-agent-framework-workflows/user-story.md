# 048-native-microsoft-agent-framework-workflows

## Title

Replace Manual V2 Orchestration with Native Microsoft Agent Framework Workflows

## User Story

As a Product Owner,

I want the V2 AI orchestration layer to use native Microsoft Agent Framework Workflow primitives instead of manually orchestrated C# execution chains,

So that complex AI workflows become observable, durable, extensible, testable, and ready for future multi-agent capabilities such as Deep Research, Portfolio Analysis, Autonomous Research Agents, and Human-in-the-Loop approval flows.

---

## Business Value

The current V2 implementation successfully uses Microsoft Agent Framework agents and tools, but workflow execution is still implemented as imperative C# orchestration.

Migrating to native Workflow primitives provides:

* Durable workflow execution
* Explicit step orchestration
* Workflow state persistence
* Better observability
* Easier future multi-agent expansion
* Human approval checkpoints
* Long-running workflow support
* Reduced orchestration complexity

---

## Acceptance Criteria

### AC-1 Native Workflow Definition

Given the AI orchestration layer

When a user submits a query

Then execution shall be performed through Microsoft Agent Framework Workflow definitions rather than manually ordered service calls.

---

### AC-2 Workflow State

Given a workflow execution

When multiple steps are executed

Then workflow state shall be represented through workflow context/state objects rather than temporary local variables.

---

### AC-3 Workflow Step Isolation

Given workflow execution

Then each business stage shall be represented by an independent workflow step.

Minimum required steps:

1. Query Analysis
2. Intent Classification
3. Tool Selection
4. Tool Execution
5. Result Validation
6. Explainability Generation
7. Billing Calculation
8. Response Persistence
9. Final Answer Generation

---

### AC-4 Fault Handling

Given a workflow step failure

When an exception occurs

Then workflow failure information shall be captured through workflow execution context and surfaced through telemetry.

---

### AC-5 Observability

Given workflow execution

Then telemetry shall expose:

* Workflow Id
* Execution Duration
* Step Duration
* Failed Step
* Tool Usage
* Token Consumption

---

### AC-6 Feature Flag Compatibility

Given existing deployments

When AiOrchestration:Mode is set to:

* V1
* MicrosoftAgentFrameworkV2

Then both modes shall continue to operate without breaking changes.

---

### AC-7 Deep Research Readiness

Given future Deep Research features

Then workflow architecture shall support:

* branching
* fan-out execution
* parallel tool execution
* future multi-agent workflows

without architectural refactoring.

---

### AC-8 Regression Safety

Existing user scenarios must continue to function unchanged:

* PE queries
* Scanner queries
* Symbol metric queries
* Explainability responses
* Memory-enabled conversations
* Billing
* Persistence

---

## Out of Scope

* Multi-agent implementation
* Deep Research implementation
* Human approval implementation
* LangGraph migration
* Additional AI providers

These capabilities will be implemented in future stories.
