---
title: Compatibility with MailboxProcessor
category: Explanations
categoryindex: 3
index: 1
---

# Compatibility with MailboxProcessor

FSharp.Core's `MailboxProcessor` is the specification. `tests/TaskMailbox.Tests/CompatibilityTests.fs` runs each scenario against both.

## Same as MailboxProcessor

- `Receive` returns messages in arrival order. A negative timeout waits indefinitely, `0` checks the queue once, and an elapsed timeout throws `TimeoutException` without losing a later message.
- `Start` runs the body on the thread pool. `StartImmediate` runs it on the calling thread until its first suspension. Starting twice throws `InvalidOperationException`.
- A body with a cancelled token does not run.
- `Error` gets the body's exception. Cancellation of the agent's token is not an error; an `OperationCanceledException` from any other token is.
- `Dispose` drops queued and later messages and sets `CurrentQueueLength` to `0`. It does not cancel the body. A waiting `Receive` waits until its timeout or token.
- `Post` after `Dispose` throws `ObjectDisposedException` only with `isThrowExceptionAfterDisposed`.

## Differences

| | MailboxProcessor | TaskMailboxProcessor |
|---|---|---|
| Body | `Async<unit>` | `CancellableTask<unit>` |
| Token when none is given | `Async.DefaultCancellationToken` | `CancellationToken.None` |
| A second `Receive` while one waits | Undefined | Throws `InvalidOperationException` |
| Exception from an `Error` handler | Thrown from `StartImmediate` | Faults the agent's internal task |
| `Receive` | Hot `Async` | Cold `CancellableTask`; nothing is taken until it runs |
