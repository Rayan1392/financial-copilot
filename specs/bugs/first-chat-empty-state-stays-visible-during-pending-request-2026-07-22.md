# First chat request keeps empty-state suggestions visible while AI is processing

Date: 2026-07-22

## Symptom

On the main chat page, when a user submits the first question in a new session, the welcome
empty-state and suggested question buttons remain visible while the AI request is processing.
After a few seconds, the response appears abruptly. The normal `در حال تحلیل...` loading state is
only visible on second and later messages inside an existing thread.

## Root Cause

The new-chat route (`/_app/chat`) submitted the first message directly through `startChat.mutate`
and waited for the backend to create a thread before navigating to `/c/$threadId`.

Unlike the thread route, it did not keep any optimistic user message or render `MessageList` while
the mutation was pending. Therefore the route kept rendering the empty-state suggestions until the
server response completed.

## Fix

Track the submitted first message locally as `pendingMessage`. While it exists, hide the welcome
empty-state and render `MessageList` with:

- one optimistic user message
- `streaming={startChat.isPending}`

This reuses the same streaming placeholder shown in existing chat threads.

## Expected Behavior

Immediately after submitting the first question, the suggested questions disappear and the user sees
their submitted message plus the `در حال تحلیل...` assistant loading state.
