# User Story - Frontend AI Orchestration V2 Awareness

## Story

As a FinancialCopilot user and platform operator,

I want the frontend to understand and optionally display AI orchestration metadata,

so that Microsoft Agent Framework V2 can be rolled out, monitored, evaluated, and compared against V1 without changing the existing chat experience.

## Business Context

Story `047-microsoft-agent-framework-orchestration-v2` introduces a new orchestration path based on Microsoft Agent Framework while preserving the public AI facade and existing response contracts.

The frontend must remain fully compatible with both orchestration modes.

This story introduces optional frontend awareness of orchestration metadata, diagnostics, evaluation support, and rollout controls while keeping the existing user experience unchanged.

## Acceptance Criteria

### Compatibility

* Existing chat functionality continues to work without modification.
* Existing conversations remain fully loadable.
* Existing users experience no breaking UI changes.
* Existing scanner and symbol lookup rendering continues to function.

### Response Metadata Support

* The frontend supports the following optional response metadata:

  * `aiOrchestrationMode`
  * `workflowVersion`
  * `providerSelection`
  * `providerFallbackOccurred`
  * `workflowCorrelationId`

* Missing metadata must not break rendering.

* Existing V1 responses remain fully supported.

### Diagnostics Support

* Add an optional diagnostics panel.
* Diagnostics are visible only to authorized administrators or development environments.
* Diagnostics display:

  * orchestration mode
  * workflow version
  * selected provider
  * provider fallback status
  * execution duration
  * correlation identifier

### Rollout Support

* Frontend supports backend-driven orchestration rollout.

* The backend remains authoritative for orchestration selection.

* Optional administrator-only controls may request:

  * V1
  * MicrosoftAgentFrameworkV2
  * Auto

* Normal users cannot override orchestration mode.

### Evaluation Support

* Evaluation views can compare:

  * V1 responses
  * V2 responses
  * citations
  * confidence metadata
  * follow-up suggestions

* Evaluation features are hidden from normal users.

### Export Support

* Conversation export includes orchestration metadata when available.
* Export remains backward compatible with older conversations.

### Security

* Diagnostic metadata must not expose:

  * prompts
  * secrets
  * credentials
  * billing internals
  * provider tokens

* Orchestration selection remains controlled by backend authorization policies.

## Dependencies

* `032-frontend-chat-conversation-cutover`
* `037-frontend-admin-panel`
* `047-microsoft-agent-framework-orchestration-v2`

## Out of Scope

* Replacing existing chat UI.
* Creating new public AI endpoints.
* Exposing prompts or workflow internals.
* Allowing users to directly control orchestration behavior.
* Implementing new agent capabilities beyond visibility and diagnostics.
