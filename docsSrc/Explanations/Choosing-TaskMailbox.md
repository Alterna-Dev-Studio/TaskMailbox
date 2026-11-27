---
title: Choosing TaskMailbox
category: Explanations
categoryindex: 3
index: 2
---

<!-- Claims checked against each project's docs/source on 2026-11-27. -->

# Choosing TaskMailbox

TaskMailbox keeps `MailboxProcessor`'s shape and semantics but makes the body task-native. The body awaits `CancellableTask`, `Task`, or `ValueTask` directly. The caller's token is always honoured, and `PostAnd*Reply` is cold. It is a single in-process agent, not a framework: it doesn't supervise, restart, distribute, persist messages, or apply backpressure.

## Lightweight options

| Option | Model | Async type | Hot/Cold<sup>1</sup> | Default scheduler<sup>2</sup> | Selective receive (Scan) | Request/reply | Dependencies | Maintenance |
|---|---|---|---|---|---|---|---|---|
| TaskMailbox | Single-reader mailbox, `MailboxProcessor` semantics; body uses `mailbox { }` | `CancellableTask<'T>` | Cold (hot by applying a token) | Thread pool via `Task.Run` (`Start`); caller's thread until the first await, then the pool (`StartImmediate`) | Yes, `Scan`/`TryScan` | Yes, `PostAnd*Reply` | IcedTasks | Pre-release 0.0.1-alpha.1; unreleased |
| `MailboxProcessor` | Single-reader mailbox | `Async<'T>` | Per op: `Receive`/`Scan` cold; `PostAndAsyncReply` hot | The `Async` trampoline on the thread pool | Yes, `Scan`/`TryScan` | Yes, `PostAnd*Reply` | None (FSharp.Core) | FSharp.Core 11.0.101 (2026) |
| TPL Dataflow `ActionBlock` | Push block; takes an `Action<T>` or `Func<T,Task>` delegate | `Task` | Hot (`Task`) | A `TaskScheduler` (configurable); `MaxDegreeOfParallelism` 1 by default, so one item at a time unless configured | No | No; only `Post`/`Completion`/`InputCount` | None on .NET; NuGet package on .NET Framework | 11.0.0 (2026-11-10) |
| Raw `System.Threading.Channels` loop | Queue plus a hand-written read loop | `ValueTask<T>` (reads); `Task` (your loop) | Hot (`ValueTask`) | Whatever the loop uses; usually the thread pool | No; `ChannelReader<T>` has `TryRead`/`ReadAsync`/`WaitToReadAsync` only | No; build with `TaskCompletionSource` | None (BCL) | Ships with .NET; 11.0.0 |
| Hopac `Mailbox` | Unbounded buffered mailbox; `Ch`/`Alt` for richer rendezvous | `Job<'T>` (and `Alt<'T>`) | Cold (hot via `Job.start`/`Promise.start`) | Hopac's own cooperative worker scheduler, not the .NET thread pool | Only via `Alt.choose` across channels, not a predicate scan | Manual, with `Ch`/`IVar` | Hopac | Last release 0.5.1 (2021); last commit 2021 |
| Actress (`kthompson/Actress`) | C# port of `MailboxProcessor` | `Task<T>` | Hot (`Task`) | Thread pool via `Task.Run` | Yes, `Scan`/`TryScan` | Yes, `PostAndReply`/`TryPostAndReply` | Actress | Last release 0.2.1 (2018); last commit 2020 |
| WalletWasabi `MailboxProcessor` | Single-reader, `Channel`-backed mailbox; body is a `Process<TMsg>` delegate | `Task<T>` | Hot (`Task`) | Thread pool via `Task.Run` | No | Yes, `PostAndReplyAsync` | None; vendored in `WalletWasabi.Services`, not a package | Last commit to file 2026-09 |
| Fable `MailboxProcessor` (fable-library) | Single-reader mailbox on the JS event loop | `Async<'T>` | Per op: `Receive` cold `async`; `PostAndAsyncReply` hot | The JavaScript event loop | No; no `Scan`/`TryScan`/`TryReceive` | `PostAndAsyncReply` only; no sync `PostAndReply` | None; ships with the Fable compiler | Fable 5.28.0 (2026); file last changed 2026-10 |

- <sup>1</sup> Hot/Cold: whether the operations a caller awaits (receive/scan and request/reply) start when called (hot) or only when run (cold). A cold operation can always be started eagerly by the caller; a hot one can only be made lazy by wrapping it (e.g. in `coldTask` or `Async`). See [Compatibility with MailboxProcessor](Compatibility.html).
- <sup>2</sup> Default scheduler: what runs the body and its continuations by default. Most options let the body or its configuration switch schedulers — `Async.SwitchToContext`/`Async.SwitchToThreadPool` for `MailboxProcessor`, `ConfigureAwait` or a custom `TaskScheduler` for Task-based options, the `TaskScheduler` option for Dataflow, `Job.awaitTask`/`Job.toAsync`-style interop for Hopac — except Fable, which only has the JS event loop.

Fable's body runs on a single-threaded JS event loop, not real concurrency.

## Heavier frameworks

Akka.NET/Akkling, Proto.Actor, and Orleans solve supervision, clustering, and cross-process distribution — a different problem than a single in-process mailbox.

## Choose TaskMailbox when…

- Your body is naturally a `Task`, `ValueTask`, or already a `CancellableTask`, and you don't want `Async.AwaitTask` glue.
- You want the caller's `CancellationToken` honoured on every wait, including an infinite one.
- You already know `MailboxProcessor` and want the same members and semantics — see [Compatibility with MailboxProcessor](Compatibility.html) for exactly where they differ.

## Choose something else when…

- You need supervision, clustering, persistence, or backpressure — pick a framework (Akka.NET/Akkling, Proto.Actor, Orleans), not a mailbox.
- You don't need selective receive, request/reply, or `MailboxProcessor` semantics — a plain loop over `System.Threading.Channels`, or TPL Dataflow for a parallel push pipeline, is less to pull in.
