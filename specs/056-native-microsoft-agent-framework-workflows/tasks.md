# Tasks - 048-native-microsoft-agent-framework-workflows

## 1. Workflow Architecture Assessment

### 1.1 Review Current Runner

* Analyze FinancialCopilotAgentWorkflowRunner
* Identify manually orchestrated stages
* Document current execution graph

### 1.2 Define Workflow Model

* Define workflow state model
* Define workflow execution context
* Define workflow metadata structure

---

## 2. Create Native Workflow

### 2.1 Create Query Analysis Step

Inputs:

* User query
* Conversation context

Outputs:

* Normalized query
* Analysis metadata

---

### 2.2 Create Intent Classification Step

Outputs:

* Intent
* Confidence
* Query category

---

### 2.3 Create Tool Selection Step

Outputs:

* Selected tool(s)
* Execution strategy

---

### 2.4 Create Tool Execution Step

Supported tools:

* Scanner
* Symbol Metrics
* Explainability
* Memory
* Future tools

---

### 2.5 Create Result Validation Step

Validate:

* Missing data
* Empty responses
* Tool failures
* Hallucination safeguards

---

### 2.6 Create Explainability Step

Generate:

* Confidence
* Data provenance
* Missing-data explanation

---

### 2.7 Create Billing Step

Responsibilities:

* Token accounting
* Credit consumption
* Usage tracking

---

### 2.8 Create Persistence Step

Persist:

* Conversation
* Tool traces
* Workflow execution metadata

---

### 2.9 Create Final Response Step

Generate final structured response.

---

## 3. Workflow State Management

### 3.1 Workflow Context

Implement:

* WorkflowId
* SessionId
* CorrelationId

---

### 3.2 Workflow State

Persist:

* Intent
* Tool selections
* Intermediate outputs
* Final output

---

## 4. Telemetry

### 4.1 Workflow Metrics

Capture:

* Workflow duration
* Step duration
* Step retries
* Failures

---

### 4.2 OpenTelemetry Integration

Add workflow spans for:

* Workflow
* Agent execution
* Tool execution

---

## 5. Testing

### 5.1 Unit Tests

Coverage for:

* Workflow steps
* State transitions
* Error handling

---

### 5.2 Integration Tests

Scenarios:

* PE query
* Scanner query
* Missing-answer query
* Explainability query

---

### 5.3 Regression Tests

Verify parity with existing V2 behavior.

---

## 6. Documentation

### 6.1 Architecture Diagram

Document:

* Workflow graph
* Step dependencies
* State transitions

---

### 6.2 Developer Guide

Document:

* Adding new workflow steps
* Adding new tools
* Workflow debugging

---

## Definition of Done

* Native Microsoft Agent Framework Workflow APIs are used
* Manual orchestration is removed from the runner
* Workflow state is centralized
* OpenTelemetry is available
* Existing V2 scenarios pass regression tests
* Feature flag compatibility preserved
* Architecture approved for future Deep Research and Multi-Agent stories
