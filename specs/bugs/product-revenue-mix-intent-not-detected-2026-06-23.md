# Bug: ProductRevenueMix Intent Not Detected — Falls Through to SymbolLookup

**Date:** 2026-06-23  
**Feature:** 075 — Company Product Revenue Mix  
**Symptom:** Queries like "مهم‌ترین محصول کچاد چیست؟" return "Metric term 'مهم‌ترین محصول' is not recognized" instead of the product revenue mix table.

---

## Reproduction

Send any of these queries to the AI endpoint:

- مهم‌ترین محصول کچاد چیست؟
- کگل بیشتر از چه محصولی درآمد دارد؟
- ترکیب فروش محصولات فملی را نشان بده

**Expected:** ProductRevenueMix intent detected → product revenue table returned.  
**Actual:** SymbolLookup intent detected → "Metric term '...' is not recognized in the supported catalog."

---

## Root Cause

Two independent bugs combine to cause the failure.

### Bug 1 — Phrase matching never normalizes the phrase list itself

**File:** `src/backend/FinancialCopilot.Application/AI/Orchestration/LlmAiIntentDetector.cs`

`NormalizeLookupText` replaces the ZWNJ character (U+200C) with a space before matching:

```csharp
private static string NormalizeLookupText(string text) =>
    text.Trim()
        .Replace('ك', 'ک')
        .Replace('ي', 'ی')
        .Replace('‌', ' ');   // ZWNJ → space
```

After normalization the user query becomes `"مهم ترین محصول کچاد چیست؟"` (space between مهم and ترین).  
The phrase list contains `"مهم‌ترین محصول"` (with ZWNJ) and `"مهم ترین محصول"` (with space) — so this specific
phrase **should** match.

However, normalization is only applied to the input query, **not** to each phrase before the `Contains` check.
When `enrichedMessage` is the input (see Bug 2 below), the phrase matching runs over a longer string that has
already gone through different whitespace handling, making the match unreliable.

The safe fix is to normalize both sides: normalize each phrase at startup the same way the query is normalized,
so the comparison is always between two normalized strings.

### Bug 2 — Intent detector receives `enrichedMessage`, not the raw user query

**File:** `src/backend/FinancialCopilot.Application/AI/Orchestration/AiQueryOrchestrationService.cs`, line 91

```csharp
var enrichedMessage = BuildEnrichedMessage(request.Message, memoryContext);  // line 61

var intentResult = await intentDetector.DetectAsync(
    new IntentDetectionInput(
        enrichedMessage,      // ← memory-prepended string, not the raw query
        ...),
    cancellationToken);
```

`BuildEnrichedMessage` prepends stored conversation context and memory items:

```
[Recent conversation]
<summary lines>
---
[Stored context]
- ShortTermConversationMemory: ...
---
مهم‌ترین محصول کچاد چیست؟      ← actual user query is at the end
```

The deterministic phrase check (`LooksLikeProductRevenueMixQuery`) calls `NormalizeLookupText` on this entire
prepended string. The `Trim()` inside `NormalizeLookupText` only removes leading/trailing whitespace — it does
not strip the `[Recent conversation]` header — so the phrase search runs correctly over the full string.

**However**, the enriched prefix can itself contain words that confuse the LLM fallback path. More critically,
when `LooksLikeProductRevenueMixQuery` returns `false` for any reason (see Bug 1), the LLM sees a long
context-prepended prompt and classifies the intent as `SymbolLookup` because the phrase "مهم‌ترین محصول"
looks like a metric name request to the LLM without knowing about the ProductRevenueMix intent category.

The LLM's system prompt (`SystemPrompt` constant in `LlmAiIntentDetector`) does **not** mention the
`ProductRevenueMix` intent — it only lists Scanner, SymbolLookup, ComprehensiveAnalysis, Unknown, and
Clarification. So the LLM can never classify a query as ProductRevenueMix; that classification is 100%
dependent on the deterministic phrase pre-check succeeding.

---

## Full Failure Chain

1. User sends: `"مهم‌ترین محصول کچاد چیست؟"`
2. `BuildEnrichedMessage` prepends memory context → `enrichedMessage` is a multi-line string
3. `intentDetector.DetectAsync(enrichedMessage, ...)` is called
4. `LooksLikeProductRevenueMixQuery(enrichedMessage)` — phrase matching runs but is fragile due to un-normalized phrase list
5. If phrase check returns `false`, falls through to LLM
6. LLM system prompt has no `ProductRevenueMix` intent → classifies as `SymbolLookup`
7. `SymbolLookupParser` receives enriched message, tries to parse `"مهم‌ترین محصول"` as a metric code
8. Metric catalog lookup fails → **"Metric term 'مهم‌ترین محصول' is not recognized"**

---

## Fix Options

### Option A — Pass raw `request.Message` to intent detector (recommended)

Intent classification should be based on what the user actually typed, not on the memory-enriched prompt.
The memory context is useful for the LLM answering step, not for routing.

```csharp
// AiQueryOrchestrationService.cs line 91
var intentResult = await intentDetector.DetectAsync(
    new IntentDetectionInput(
        request.Message,    // raw query, not enrichedMessage
        ...),
    cancellationToken);
```

### Option B — Add ProductRevenueMix to the LLM system prompt

As a safety net, add `ProductRevenueMix` as a recognized intent in `LlmAiIntentDetector.SystemPrompt` so
the LLM can classify it when the deterministic pre-check misses.

### Option C — Normalize both sides of the phrase match

Normalize each phrase in `ProductRevenuePhrases` at static initialization time using the same
`NormalizeLookupText` transformation, so the match is always between two normalized strings regardless
of what is prepended.

---

## Recommended Fix

Apply **Option A** as the primary fix (correct the input passed to intent detection) and **Option C**
as a defensive secondary fix (normalize phrase list). Option B is also worth adding as a fallback layer.

---

## Resolution

**Fixed 2026-06-23.**

- **Option A** applied in `AiQueryOrchestrationService.cs` line 91: `intentDetector.DetectAsync` now receives
  `request.Message` instead of `enrichedMessage`.
- **Option C** applied in `LlmAiIntentDetector.cs`: added `NormalizedProductRevenuePhrases` static array
  (phrases pre-normalized via `NormalizeLookupText` at startup); `LooksLikeProductRevenueMixQuery` now
  compares normalized query against normalized phrases on both sides.
