# Tasks

## Frontend Contracts

* Extend frontend AI response DTOs with nullable fields:

  * `aiOrchestrationMode`
  * `workflowVersion`
  * `providerSelection`
  * `providerFallbackOccurred`
  * `workflowCorrelationId`

* Preserve full backward compatibility with existing V1 responses.

* Ensure serialization and deserialization tolerate missing fields.

## Chat Rendering Compatibility

* Verify scanner results render correctly for both V1 and V2.
* Verify symbol lookup results render correctly for both V1 and V2.
* Verify explainable-answer rendering remains unchanged.
* Verify usage metadata rendering remains unchanged.

## Diagnostics Panel

* Create an admin-only diagnostics component.

* Display:

  * orchestration mode
  * workflow version
  * provider selection
  * provider fallback status
  * workflow duration
  * correlation identifier

* Hide diagnostics by default.

* Ensure diagnostics are not visible to non-admin users.

## Conversation Viewer

* Persist orchestration metadata with conversation messages.
* Display orchestration metadata in conversation debug mode.
* Ensure older conversations without metadata continue to load correctly.

## Rollout Controls

* Implement optional administrator-only orchestration selector:

  * Auto
  * V1
  * MicrosoftAgentFrameworkV2

* Backend remains authoritative.

* Frontend selection acts only as a request hint.

* Unauthorized users must not see orchestration controls.

## Evaluation Support

* Create an internal evaluation comparison view.

* Allow side-by-side comparison of:

  * V1 response
  * V2 response
  * citations
  * confidence metadata
  * follow-up suggestions
  * usage metadata

* Restrict evaluation views to authorized administrators.

## Export Support

* Extend conversation export models with orchestration metadata.
* Include workflow version and orchestration mode when available.
* Maintain compatibility with older exports.

## Security

* Ensure orchestration metadata never exposes:

  * prompts
  * provider credentials
  * secrets
  * billing internals

* Verify orchestration controls follow existing permission policies.

## Testing

### Unit Tests

* DTO compatibility tests.
* Diagnostics component rendering tests.
* Permission visibility tests.
* Export serialization tests.

### Integration Tests

* V1 response rendering.
* V2 response rendering.
* Missing metadata handling.
* Conversation reload compatibility.
* Evaluation view authorization.
* Admin orchestration selector authorization.

### End-to-End Tests

* Chat flow using V1 mode.
* Chat flow using MicrosoftAgentFrameworkV2 mode.
* Conversation reload with orchestration metadata.
* Export and re-import compatibility.

## Documentation

* Update frontend architecture documentation.
* Document orchestration metadata fields.
* Document rollout and rollback procedures.
* Document evaluation workflow for V1 versus V2 comparisons.

## Completion Criteria

* Existing chat functionality remains unchanged.
* Both V1 and V2 responses render correctly.
* Diagnostics are available for administrators.
* Evaluation tools support V1/V2 comparison.
* Export and reload functionality remain backward compatible.
* All tests pass.
