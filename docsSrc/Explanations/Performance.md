---
title: Performance baseline
category: Explanations
categoryindex: 3
index: 3
---

# Performance baseline

A baseline of `TaskMailboxProcessor` against FSharp.Core's `MailboxProcessor` and a bare `Channel`, to compare against after later changes. It was re-recorded after throw-free cancellation; the earlier baselines are in the Phase 7 and Throw-free cancellation sections. It is one run on one machine, not a claim about either library.

## Running

```sh
dotnet run -c Release --project benchmarks/FSharpBenchmarks -f net10.0 -- --filter '*'
```

This runs every benchmark on .NET 8.0 and .NET 10.0. `--quick` runs each case once as `Job.Dry` on the host runtime, as a smoke check. Results land in `benchmarks/FSharpBenchmarks/BenchmarkDotNet.Artifacts/results/`.

## Machine

BenchmarkDotNet v0.15.8, Linux Ubuntu 26.04.1 LTS, 12th Gen Intel Core i5-12450H (12 logical cores), .NET SDK 10.0.404, .NET 10.0.15 and .NET 8.0.34. A development laptop running Linux.

## Method

- Layout follows IcedTasks' `benchmarks/`. The bare `Channel` consumer is Bent Rasmussen's Channel actor drain loop (`WaitToReadAsync` + `TryRead`).
- Each scenario has three methods: `MailboxProcessor` (FSharp.Core 10.0.103, the baseline), `TaskMailbox`, and a bare `Channel` (unbounded, `SingleReader`, drained with `WaitToReadAsync` + `TryRead`). Scan has no `Channel` row.
- Agent bodies are `while` loops (`async`, `cancellableTask`, `task`), except in the body-style benchmarks.
- A fresh agent is created and started in `IterationSetup` with a cancellation token, and cancelled and disposed in `IterationCleanup`.
- `InvocationCount` 1, `UnrollFactor` 1, 3 warmups, 20 iterations. `OperationsPerInvoke` is N, so times and allocations are per message or operation.
- Every method checks the sum or count of what the agent processed, and fails if it is wrong.
- N is sized so that one iteration of the PR #22 baseline took at least about 100 ms, BenchmarkDotNet's guidance. Faster rows are now shorter; the shortest is the `TaskMailbox` cancelled `Receive` row on .NET 10.0, at about 69 ms. The full run takes about 10 minutes.
- P50, P95 and P99 are over iterations (each the mean of N operations), not per-message latency.

## Results

Mean per operation, ratio to `MailboxProcessor` in brackets, and allocated bytes per operation. N is the operations per invocation.

| Scenario | N | Runtime | MailboxProcessor | TaskMailbox | Channel |
|---|---|---|---|---|---|
| Post, wake idle agent, receive | 60000 | .NET 10.0 | 10.074 μs, 1716 B | 2.122 μs (0.21), 1085 B | 1.686 μs (0.17), 430 B |
| | | .NET 8.0 | 10.836 μs, 1693 B | 2.035 μs (0.19), 1127 B | 1.667 μs (0.15), 436 B |
| Drain queued messages | 6000000 | .NET 10.0 | 132.39 ns, 473 B | 47.08 ns (0.36), 128 B | 16.86 ns (0.13), 0 B |
| | | .NET 8.0 | 157.25 ns, 497 B | 50.92 ns (0.32), 152 B | 17.77 ns (0.11), 0 B |
| Throughput, 1 producer | 1000000 | .NET 10.0 | 276.3 ns, 482 B | 102.7 ns (0.38), 132 B | 122.8 ns (0.46), 12 B |
| | | .NET 8.0 | 288.9 ns, 506 B | 102.2 ns (0.38), 156 B | 137.5 ns (0.51), 14 B |
| Throughput, 4 producers | 1000000 | .NET 10.0 | 352.1 ns, 482 B | 188.4 ns (0.54), 129 B | 111.8 ns (0.32), 2 B |
| | | .NET 8.0 | 383.7 ns, 506 B | 188.9 ns (0.50), 158 B | 130.9 ns (0.35), 2 B |
| `PostAndAsyncReply` round trip | 75000 | .NET 10.0 | 17.096 μs, 3672 B | 2.640 μs (0.15), 2374 B | 1.163 μs (0.07), 241 B |
| | | .NET 8.0 | 18.190 μs, 3696 B | 2.702 μs (0.15), 2440 B | 1.251 μs (0.07), 250 B |
| `TryReceive(0)` on empty | 2500000 | .NET 10.0 | 411.90 ns, 986 B | 91.65 ns (0.22), 120 B | 41.03 ns (0.10), 0 B |
| | | .NET 8.0 | 471.81 ns, 1010 B | 115.51 ns (0.24), 192 B | 39.84 ns (0.08), 0 B |
| `TryReceive(1)` timing out | 100 | .NET 10.0 | 1.153 ms, 1.61 KB | 1.164 ms (1.01), 1.27 KB | 1.180 ms (1.02), 1.75 KB |
| | | .NET 8.0 | 1.153 ms, 1.63 KB | 1.142 ms (0.99), 1.37 KB | 1.193 ms (1.04), 2.23 KB |
| Pending `Receive` cancelled | 18000 | .NET 10.0 | 18.557 μs, 3.06 KB | 3.853 μs (0.21), 1.6 KB | 6.037 μs (0.33), 1.65 KB |
| | | .NET 8.0 | 19.043 μs, 3.15 KB | 11.558 μs (0.61), 1.67 KB | 17.390 μs (0.91), 2.13 KB |

`Scan` takes the next matching message after walking `Skipped` queued messages that it rejects (N is 400000):

| Skipped | Runtime | MailboxProcessor | TaskMailbox |
|---|---|---|---|
| 0 | .NET 10.0 | 287.0 ns, 1066 B | 265.3 ns (0.92), 512 B |
| | .NET 8.0 | 331.1 ns, 1066 B | 208.7 ns (0.63), 512 B |
| 10 | .NET 10.0 | 301.1 ns, 1066 B | 271.4 ns (0.90), 512 B |
| | .NET 8.0 | 348.3 ns, 1066 B | 209.3 ns (0.60), 512 B |
| 100 | .NET 10.0 | 427.5 ns, 1066 B | 346.9 ns (0.81), 512 B |
| | .NET 8.0 | 475.1 ns, 1066 B | 290.2 ns (0.61), 512 B |
| 1000 | .NET 10.0 | 1,508.6 ns, 1066 B | 987.0 ns (0.65), 512 B |
| | .NET 8.0 | 1,629.9 ns, 1066 B | 932.4 ns (0.57), 512 B |

## Body style

The body-style benchmarks compare a recursive body (`mailbox { }` for `TaskMailbox`, a recursive `async` for `MailboxProcessor`) with a `while` loop, queued (N = 1000000) and with one concurrent producer (N = 1000000). Ratios are to `MailboxProcessorRecursive`.

| Scenario | Runtime | MailboxProcessor recursive | MailboxProcessor while | TaskMailbox while | TaskMailbox recursive |
|---|---|---|---|---|---|
| Queued | .NET 10.0 | 135.23 ns, 545 B | 131.75 ns (0.98), 473 B | 44.22 ns (0.33), 128 B | 85.60 ns (0.63), 312 B |
| | .NET 8.0 | 158.47 ns, 569 B | 156.74 ns (0.99), 497 B | 53.63 ns (0.34), 152 B | 125.08 ns (0.79), 336 B |
| Throughput | .NET 10.0 | 295.05 ns, 553 B | 298.60 ns (1.06), 482 B | 98.19 ns (0.35), 132 B | 120.39 ns (0.43), 316 B |
| | .NET 8.0 | 330.81 ns, 577 B | 300.80 ns (0.94), 506 B | 115.64 ns (0.36), 154 B | 158.11 ns (0.50), 340 B |

The `MailboxProcessor` styles are within 1.03 of each other in the queued rows and within 1.10 in the throughput rows, where the `RatioSD` is 0.26 or more. The `TaskMailbox` styles differ by more: `while` is faster than recursive on both runtimes, in both the queued and the throughput row.

## Phase 7

Phase 7 flattened `Receive` and `TryReceive` into one `cancellableTask`, added a synchronous fast path for a non-empty queue, and made the internal arrival wait non-throwing. "Before" is the PR #22 baseline and "now" is the state after Phase 7 (the baseline before the next section); the ratio is to `MailboxProcessor` in the same run. Body-style ratios are to `MailboxProcessorRecursive`. Allocation is per operation.

| Scenario | Runtime | MailboxProcessor now | TaskMailbox before | TaskMailbox now | Allocated before to now |
|---|---|---|---|---|---|
| Drain queued | .NET 10.0 | 133.14 ns | 209.31 ns (1.58) | 45.51 ns (0.34) | 344 to 128 B |
| | .NET 8.0 | 156.46 ns | 147.99 ns (0.95) | 54.55 ns (0.35) | 392 to 152 B |
| Body style queued, `while` | .NET 10.0 | 129.79 ns | 210.8 ns (1.53) | 49.75 ns (0.38) | 344 to 128 B |
| | .NET 8.0 | 160.31 ns | 150.2 ns (0.92) | 60.07 ns (0.37) | 392 to 152 B |
| Body style queued, recursive | .NET 10.0 | 129.79 ns | 145.5 ns (1.05) | 87.99 ns (0.68) | 528 to 312 B |
| | .NET 8.0 | 160.31 ns | 221.8 ns (1.36) | 125.91 ns (0.79) | 576 to 336 B |
| `TryReceive(0)` on empty | .NET 10.0 | 412.29 ns | 146.88 ns (0.35) | 90.36 ns (0.22) | 264 to 120 B |
| | .NET 8.0 | 468.67 ns | 203.74 ns (0.44) | 120.86 ns (0.26) | 360 to 192 B |
| Pending `Receive` cancelled | .NET 10.0 | 18.105 μs | 18.497 μs (1.06) | 10.773 μs (0.60) | 4.9 to 3.04 KB |
| | .NET 8.0 | 19.456 μs | 56.997 μs (3.03) | 39.238 μs (2.02) | 7.77 to 3.44 KB |
| `TryReceive(1)` timing out | .NET 10.0 | 1.126 ms | 1.168 ms (1.02) | 1.141 ms (1.01) | 3.8 to 2.02 KB |
| | .NET 8.0 | 1.135 ms | 1.171 ms (1.03) | 1.146 ms (1.01) | 4.36 to 2.54 KB |
| Throughput, 1 producer | .NET 10.0 | 282.9 ns | 242.8 ns (0.81) | 109.6 ns (0.40) | 348 to 132 B |
| | .NET 8.0 | 280.9 ns | 184.1 ns (0.65) | 108.0 ns (0.41) | 396 to 156 B |
| Throughput, 4 producers | .NET 10.0 | 334.68 ns | 280.7 ns (0.87) | 213.90 ns (0.64) | 348 to 146 B |
| | .NET 8.0 | 375.70 ns | 225.8 ns (0.61) | 174.21 ns (0.46) | 396 to 157 B |
| Body style throughput, `while` | .NET 10.0 | 277.8 ns | 242.0 ns (0.90) | 109.5 ns (0.41) | 348 to 132 B |
| | .NET 8.0 | 317.4 ns | 179.9 ns (0.68) | 106.2 ns (0.35) | 396 to 156 B |
| Body style throughput, recursive | .NET 10.0 | 277.8 ns | 184.7 ns (0.69) | 129.2 ns (0.48) | 532 to 316 B |
| | .NET 8.0 | 317.4 ns | 244.6 ns (0.93) | 157.1 ns (0.52) | 580 to 340 B |

Draining a queue is now faster than `MailboxProcessor` on both runtimes. After Phase 7 a cancelled `Receive` was faster than `MailboxProcessor` on .NET 10.0 and still slower on .NET 8.0 (2.02); the next section changes that. Wake, reply and `Scan` times moved by under 7 percent (they are not in the table), and their allocation fell by 8 to 202 B. The `MailboxProcessor` rows moved by under 10 percent, except body-style throughput `recursive` on .NET 8.0 (275.4 to 317.4 ns).

## Throw-free cancellation

Cancelling a pending `Receive` or `Scan` no longer throws inside the library: the arrival wait cancels through `Task.WaitAsync`, `Receive` waits through a hand-written completion, and `Scan` splits the search from the run. A cancelled call still resumes on the thread pool. "Before" is the state after Phase 7 (the committed Results and `Scan` tables of the previous baseline); the ratio is to `MailboxProcessor` in the same run, and `MailboxProcessor` is the run's own mean. Allocation is per operation.

| Scenario | Runtime | MailboxProcessor | TaskMailbox before | TaskMailbox after | Allocated before to after |
|---|---|---|---|---|---|
| Pending `Receive` cancelled | .NET 10.0 | 18.557 μs | 10.773 μs (0.60) | 3.853 μs (0.21) | 3.04 to 1.6 KB |
| | .NET 8.0 | 19.043 μs | 39.238 μs (2.02) | 11.558 μs (0.61) | 3.44 to 1.67 KB |
| `TryReceive(1)` timing out | .NET 10.0 | 1.153 ms | 1.141 ms (1.01) | 1.164 ms (1.01) | 2.02 to 1.27 KB |
| | .NET 8.0 | 1.153 ms | 1.146 ms (1.01) | 1.142 ms (0.99) | 2.54 to 1.37 KB |
| `Scan`, Skipped 0 | .NET 10.0 | 287.0 ns | 304.0 ns (1.03) | 265.3 ns (0.92) | 696 to 512 B |
| | .NET 8.0 | 331.1 ns | 284.2 ns (0.88) | 208.7 ns (0.63) | 744 to 512 B |
| `Scan`, Skipped 1000 | .NET 10.0 | 1,508.6 ns | 1,038.2 ns (0.69) | 987.0 ns (0.65) | 696 to 512 B |
| | .NET 8.0 | 1,629.9 ns | 1,030.7 ns (0.64) | 932.4 ns (0.57) | 744 to 512 B |
| Post, wake idle agent | .NET 10.0 | 10.074 μs | 2.064 μs (0.21) | 2.122 μs (0.21) | 1205 to 1085 B |
| | .NET 8.0 | 10.836 μs | 2.121 μs (0.20) | 2.035 μs (0.19) | 1273 to 1127 B |
| `PostAndAsyncReply` round trip | .NET 10.0 | 17.096 μs | 2.679 μs (0.16) | 2.640 μs (0.15) | 2512 to 2374 B |
| | .NET 8.0 | 18.190 μs | 2.903 μs (0.16) | 2.702 μs (0.15) | 2584 to 2440 B |

Exceptions thrown and caught per cancelled operation, from a local `FirstChanceException` count over 5,000 cancelled operations (not a committed benchmark):

| Operation | Before | After |
|---|---|---|
| Cancelled `Receive` | 4 | 1 |
| Cancelled `Scan` | 6 | 1 |
| `Receive(1)` timing out | 5 | 2 |
| `TryReceive(1)` timing out | 1 | 0 |

The one remaining exception in the cancelled `Receive` and `Scan` cases is the caller's own `await`.

## Channel options

`TaskMailboxProcessor` keeps `AllowSynchronousContinuations = false` on the arrivals channel. A prototype measurement of `true` (2026-11-27, same machine and BenchmarkDotNet config as the baseline; a prototype, not a committed benchmark):

- Post and wake went from 2.19 to 0.78 μs, and reply from 2.75 to 0.91 μs.
- Throughput was not better: 1 producer 247 to 348 ns (SD 270) on .NET 10.0, 4 producers 239 to 332 ns on .NET 8.0. Allocation rose from 348 to 1,080 B per message.
- The tests `Receive Races with Post` (and on .NET 8.0 also `Receive Races with Post on timeout`) fail: `Post` runs the agent body inline on the producer's thread, and a blocking body deadlocks the producer.

`MailboxProcessor`'s `Post` always queues the reader to the thread pool, so `true` would be a new intentional difference.

## Caveats

- One run on one machine on a laptop. BenchmarkDotNet reported `Failed to set up high priority`.
- The `MailboxProcessor` throughput rows are noisy: Throughput 1 and 4 producers and the body-style throughput rows have Errors of roughly 10 to 22 percent of the mean (for example 62.19 ns on 288.9 ns, and 54.15 ns on 295.05 ns) and ratio standard deviations up to 0.38. The `Channel` row for 4 producers has Errors of 23.37 ns on 111.8 ns and 8.75 ns on 130.9 ns, and the `TaskMailbox` throughput rows up to 12 percent (14.21 ns on 115.64 ns). The other rows have small Errors, at most about 9 percent of the mean (the wake `Channel` row, 0.1469 μs on 1.686 μs), the queued rows 1 to 4 percent. Compare ratios with their `RatioSD` and the full reports, not the means alone.
- The `TaskMailbox` 4-producer throughput row on .NET 8.0 (174.21 ns in the previous baseline, 188.9 ns now) is session drift, not a regression: an interleaved same-session run of the previous baseline (beab757) and this change (two rounds each) measured 192.3 and 198.5 ns before against 166.3 and 167.7 ns after, and the bare `Channel` 4-producer row moved from 87.21 ns to 114 to 136 ns between sessions.
- The timing-out `TryReceive(1)` rows are dominated by the roughly 1 ms timer, so they say little about library overhead.
- The `Channel` rows are a floor, not an equivalent: a bare channel has no `Receive` timeout, `Scan` or reply channel of its own.

## Reports

The full reports, with Error, StdDev, percentiles, `Op/s` and `Gen0`, are committed next to the benchmarks (the JSON beside each is the full-compressed export):

- [Post, wake idle agent](https://github.com/Alterna-Dev-Studio/TaskMailbox/blob/main/benchmarks/FSharpBenchmarks/BenchmarkDotNet.Artifacts/results/TaskMailbox.Benchmarks.PostReceiveEmptyBenchmarks-report-github.md)
- [Drain queued messages](https://github.com/Alterna-Dev-Studio/TaskMailbox/blob/main/benchmarks/FSharpBenchmarks/BenchmarkDotNet.Artifacts/results/TaskMailbox.Benchmarks.PostReceiveQueuedBenchmarks-report-github.md)
- [Throughput, 1 producer](https://github.com/Alterna-Dev-Studio/TaskMailbox/blob/main/benchmarks/FSharpBenchmarks/BenchmarkDotNet.Artifacts/results/TaskMailbox.Benchmarks.ThroughputSingleBenchmarks-report-github.md)
- [Throughput, 4 producers](https://github.com/Alterna-Dev-Studio/TaskMailbox/blob/main/benchmarks/FSharpBenchmarks/BenchmarkDotNet.Artifacts/results/TaskMailbox.Benchmarks.ThroughputProducersBenchmarks-report-github.md)
- [PostAndAsyncReply](https://github.com/Alterna-Dev-Studio/TaskMailbox/blob/main/benchmarks/FSharpBenchmarks/BenchmarkDotNet.Artifacts/results/TaskMailbox.Benchmarks.ReplyBenchmarks-report-github.md)
- [Scan](https://github.com/Alterna-Dev-Studio/TaskMailbox/blob/main/benchmarks/FSharpBenchmarks/BenchmarkDotNet.Artifacts/results/TaskMailbox.Benchmarks.ScanBenchmarks-report-github.md)
- [TryReceive(0)](https://github.com/Alterna-Dev-Studio/TaskMailbox/blob/main/benchmarks/FSharpBenchmarks/BenchmarkDotNet.Artifacts/results/TaskMailbox.Benchmarks.TimeoutZeroBenchmarks-report-github.md)
- [TryReceive(1)](https://github.com/Alterna-Dev-Studio/TaskMailbox/blob/main/benchmarks/FSharpBenchmarks/BenchmarkDotNet.Artifacts/results/TaskMailbox.Benchmarks.TimeoutElapsedBenchmarks-report-github.md)
- [Cancelled Receive](https://github.com/Alterna-Dev-Studio/TaskMailbox/blob/main/benchmarks/FSharpBenchmarks/BenchmarkDotNet.Artifacts/results/TaskMailbox.Benchmarks.CancellationBenchmarks-report-github.md)
- [Body style, queued](https://github.com/Alterna-Dev-Studio/TaskMailbox/blob/main/benchmarks/FSharpBenchmarks/BenchmarkDotNet.Artifacts/results/TaskMailbox.Benchmarks.BodyStyleQueuedBenchmarks-report-github.md)
- [Body style, throughput](https://github.com/Alterna-Dev-Studio/TaskMailbox/blob/main/benchmarks/FSharpBenchmarks/BenchmarkDotNet.Artifacts/results/TaskMailbox.Benchmarks.BodyStyleThroughputBenchmarks-report-github.md)
