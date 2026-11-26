---
title: Compatibility with MailboxProcessor
category: Explanations
categoryindex: 3
index: 1
---

# Compatibility with MailboxProcessor

FSharp.Core's `MailboxProcessor` is the specification. `tests/TaskMailbox.Tests/CompatibilityTests.fs` runs each scenario against both.

## Same as MailboxProcessor

- `Receive` returns messages in arrival order. `-1` waits indefinitely, `0` checks the queue once, and an elapsed timeout throws `TimeoutException` without losing a later message. `TryReceive` returns `None` instead of throwing.
- A timeout below `-1` also waits indefinitely, except on an agent created with a token, where an empty queue throws `ArgumentOutOfRangeException`.
- `DefaultTimeout` is read when `Receive`, `TryReceive`, `Scan`, `TryScan`, or a `PostAnd*Reply` method is called, not when it runs.
- Cancelling the caller's token throws `OperationCanceledException`, not a timeout or `None`, and takes no message. See Differences for an infinite wait on an agent created without a token, and for a `Scan`/`TryScan` with a finite timeout.
- `Scan` and `TryScan` test skipped messages, then arrivals, in that order. A rejected message stays skipped for the next reader; `Receive` and `TryReceive` take skipped messages first.
- A `Scan`/`TryScan` timeout bounds the search, not the matched computation: `TimeoutException` from `Scan`, `None` from `TryScan`, `0` is a single pass, and negative waits indefinitely even with a token, unlike `Receive`.
- `CurrentQueueLength` counts skipped messages; `Dispose` drops them. The matched message is consumed before its computation runs, which may call `Receive`. A scanner exception on a skipped message leaves it queued.
- The agent's token does not cancel a `Receive` run on another token.
- `Start` runs the body on the thread pool. `StartImmediate` runs it on the calling thread until its first suspension, then on the thread pool. It never resumes on the caller's `SynchronizationContext`, so a blocking `PostAndReply` from that thread does not deadlock. Starting twice throws `InvalidOperationException`.
- A body with a cancelled token does not run.
- `Error` gets the body's exception. Cancellation of the agent's token is not an error; an `OperationCanceledException` from any other token is.
- `Dispose` drops queued and later messages and sets `CurrentQueueLength` to `0`. It does not cancel the body. A waiting `Receive` waits until its timeout or token.
- `Post` after `Dispose` throws `ObjectDisposedException` only with `isThrowExceptionAfterDisposed`.
- `PostAndReply`, `TryPostAndReply`, `PostAndAsyncReply`, and `PostAndTryAsyncReply` round-trip a message through the agent. The first `Reply` wins; later replies, and a reply after the caller stopped waiting, are dropped silently; `Reply` does not throw. `TryPostAndReply` and `PostAndTryAsyncReply` return `None` on timeout; `PostAndReply` and `PostAndAsyncReply` throw `TimeoutException`.
- A reply timeout below `-1` throws `ArgumentOutOfRangeException`.
- The caller's token is honoured on a reply wait; the agent's token never ends one.
- A reply to a request the body already took is still delivered after `Dispose`; a queued request waits for its timeout or token.
- `PostAndReply` and `TryPostAndReply` block the calling thread. Calling them from the agent's own body deadlocks.

## Recursive loops

Write a recursive body with `mailbox { }`. Its `return!` is a tail call, like `async`'s. In `cancellableTask { }` it is not: each message adds stack and keeps memory until the loop ends.

As in `async`, `return!` is not a tail call inside `try`, `use`, a loop, or when more code follows. Keep the recursion outside them:

```fsharp
let rec loop () =
    mailbox {
        let! msg = inbox.Receive()

        try
            handle msg
        with e ->
            log e

        return! loop ()
    }
```

`mailbox { }` needs a statically compiled state machine. Where F# cannot compile one (warning FS3511), it throws `NotSupportedException`.

## Differences

| | MailboxProcessor | TaskMailboxProcessor |
|---|---|---|
| Body | `Async<unit>` | `CancellableTask<unit>`, written with `mailbox { }` |
| Token when none is given | `Async.DefaultCancellationToken` | `CancellationToken.None` |
| A second `Receive` or `Scan` while one waits | Undefined | Throws `InvalidOperationException` |
| Exception from an `Error` handler | Thrown from `StartImmediate` | Faults the agent's internal task |
| `Receive` | Hot `Async` | Cold `CancellableTask`; nothing is taken until it runs |
| Scanner | `'Msg -> Async<'T> option` | `'Msg -> CancellableTask<'T> option` |
| Caller's token on an infinite `Receive`, `Scan`, `TryScan`, or reply wait, agent created without a token | Ignored | Throws `OperationCanceledException` |
| Caller cancels a `Scan`/`TryScan` with a finite timeout | Never completes | Throws `OperationCanceledException` |
| Scanner throws on a newly arrived message | Message lost | Message kept, in order |
| Reply channel | `AsyncReplyChannel<'Reply>`, no public constructor | `TaskReplyChannel<'Reply>`, public constructor so a body can be unit-tested without an agent |
| `PostAndAsyncReply`/`PostAndTryAsyncReply` | Hot: posts at the call; running the `Async` again returns a cached reply or throws `ObjectDisposedException` | Cold: posts when run; each run is a new request; a `buildMessage` exception surfaces when run; a `Post` between creating and running arrives first |
| Already-cancelled caller token on the async reply variants | Posted, then cancelled | Not posted, throws `OperationCanceledException` |
| A reply timeout below `-1` | Posted, then throws | Throws before posting |
| A request after `Dispose`, without `isThrowExceptionAfterDisposed` | Dropped; the caller waits for its timeout (forever if infinite) | Throws `ObjectDisposedException` |

A `TaskReplyChannel<'Reply>` you construct yourself is a pass-through: it calls its function on every `Reply`. Reply-once belongs to the request, not the channel.

`PostAndAsyncReply`/`PostAndTryAsyncReply` are cold: `counter.PostAndAsyncReply Get |> ignore` sends nothing. Use `Post` to send without waiting for a reply. Applying a token runs it and returns a running `Task`, for eager posting.
