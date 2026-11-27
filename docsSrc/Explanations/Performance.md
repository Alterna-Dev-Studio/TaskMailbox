---
title: Performance baseline
category: Explanations
categoryindex: 3
index: 3
---

# Performance baseline

A baseline of `TaskMailboxProcessor` against FSharp.Core's `MailboxProcessor` and a bare `Channel`, to compare against after later changes. It is one run on one machine, not a claim about either library.

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
- N is sized so that one iteration takes at least about 100 ms, BenchmarkDotNet's guidance. The shortest is the bare `Channel` reply row at about 89 ms. The full run takes about 12 minutes.
- P50, P95 and P99 are over iterations (each the mean of N operations), not per-message latency.

## Results

Mean per operation, ratio to `MailboxProcessor` in brackets, and allocated bytes per operation. N is the operations per invocation.

| Scenario | N | Runtime | MailboxProcessor | TaskMailbox | Channel |
|---|---|---|---|---|---|
| Post, wake idle agent, receive | 60000 | .NET 10.0 | 9.754 μs, 1733 B | 2.198 μs (0.23), 1378 B | 1.641 μs (0.17), 430 B |
| | | .NET 8.0 | 10.492 μs, 1691 B | 2.103 μs (0.20), 1429 B | 1.654 μs (0.16), 439 B |
| Drain queued messages | 6000000 | .NET 10.0 | 132.80 ns, 473 B | 209.31 ns (1.58), 344 B | 16.66 ns (0.13), 0 B |
| | | .NET 8.0 | 155.15 ns, 497 B | 147.99 ns (0.95), 392 B | 17.10 ns (0.11), 0 B |
| Throughput, 1 producer | 1000000 | .NET 10.0 | 306.8 ns, 482 B | 242.8 ns (0.81), 348 B | 135.3 ns (0.45), 13 B |
| | | .NET 8.0 | 298.4 ns, 506 B | 184.1 ns (0.65), 396 B | 131.7 ns (0.47), 12 B |
| Throughput, 4 producers | 1000000 | .NET 10.0 | 327.5 ns, 482 B | 280.7 ns (0.87), 348 B | 136.7 ns (0.42), 2 B |
| | | .NET 8.0 | 376.2 ns, 506 B | 225.8 ns (0.61), 396 B | 167.8 ns (0.45), 4 B |
| `PostAndAsyncReply` round trip | 75000 | .NET 10.0 | 17.031 μs, 3672 B | 2.700 μs (0.16), 2714 B | 1.182 μs (0.07), 237 B |
| | | .NET 8.0 | 18.190 μs, 3696 B | 2.846 μs (0.16), 2745 B | 1.223 μs (0.07), 252 B |
| `TryReceive(0)` on empty | 2500000 | .NET 10.0 | 414.86 ns, 986 B | 146.88 ns (0.35), 264 B | 43.36 ns (0.10), 0 B |
| | | .NET 8.0 | 468.33 ns, 1010 B | 203.74 ns (0.44), 360 B | 42.88 ns (0.09), 0 B |
| `TryReceive(1)` timing out | 100 | .NET 10.0 | 1.145 ms, 1.61 KB | 1.168 ms (1.02), 3.8 KB | 1.148 ms (1.00), 1.75 KB |
| | | .NET 8.0 | 1.132 ms, 1.63 KB | 1.171 ms (1.03), 4.36 KB | 1.170 ms (1.03), 2.23 KB |
| Pending `Receive` cancelled | 18000 | .NET 10.0 | 17.448 μs, 3.06 KB | 18.497 μs (1.06), 4.9 KB | 6.054 μs (0.35), 1.65 KB |
| | | .NET 8.0 | 18.896 μs, 3.15 KB | 56.997 μs (3.03), 7.77 KB | 19.609 μs (1.04), 2.13 KB |

`Scan` takes the next matching message after walking `Skipped` queued messages that it rejects (N is 400000):

| Skipped | Runtime | MailboxProcessor | TaskMailbox |
|---|---|---|---|
| 0 | .NET 10.0 | 299.9 ns, 1066 B | 311.4 ns (1.04), 704 B |
| | .NET 8.0 | 320.0 ns, 1066 B | 281.8 ns (0.88), 752 B |
| 10 | .NET 10.0 | 302.6 ns, 1066 B | 316.9 ns (1.05), 704 B |
| | .NET 8.0 | 341.7 ns, 1066 B | 299.4 ns (0.88), 752 B |
| 100 | .NET 10.0 | 422.7 ns, 1066 B | 398.3 ns (0.94), 704 B |
| | .NET 8.0 | 486.6 ns, 1066 B | 376.4 ns (0.77), 752 B |
| 1000 | .NET 10.0 | 1,520.1 ns, 1066 B | 1,032.1 ns (0.68), 704 B |
| | .NET 8.0 | 1,781.9 ns, 1066 B | 1,029.0 ns (0.58), 752 B |

## Body style

The body-style benchmarks compare a recursive body (`mailbox { }` for `TaskMailbox`, a recursive `async` for `MailboxProcessor`) with a `while` loop, queued (N = 1000000) and with one concurrent producer (N = 1000000). Ratios are to `MailboxProcessorRecursive`.

| Scenario | Runtime | MailboxProcessor recursive | MailboxProcessor while | TaskMailbox while | TaskMailbox recursive |
|---|---|---|---|---|---|
| Queued | .NET 10.0 | 138.3 ns, 545 B | 131.1 ns (0.95), 473 B | 210.8 ns (1.53), 344 B | 145.5 ns (1.05), 528 B |
| | .NET 8.0 | 163.2 ns, 569 B | 154.1 ns (0.94), 497 B | 150.2 ns (0.92), 392 B | 221.8 ns (1.36), 576 B |
| Throughput | .NET 10.0 | 279.7 ns, 553 B | 290.8 ns (1.09), 482 B | 242.0 ns (0.90), 348 B | 184.7 ns (0.69), 532 B |
| | .NET 8.0 | 275.4 ns, 577 B | 302.1 ns (1.15), 506 B | 179.9 ns (0.68), 396 B | 244.6 ns (0.93), 580 B |

The `MailboxProcessor` styles are within 1.06 of each other in the queued rows and within 1.15 in the throughput rows, where the `RatioSD` is 0.30 or more. The `TaskMailbox` styles differ by more, and in different directions on the two runtimes: `while` is faster on .NET 8.0 and recursive is faster on .NET 10.0, in both the queued and the throughput row.

## Caveats

- One run on one machine on a laptop. BenchmarkDotNet reported `Failed to set up high priority`.
- The `MailboxProcessor` throughput rows are noisy: Throughput 1 and 4 producers and the body-style throughput rows have Errors of roughly 10 to 20 percent of the mean (for example 59.11 ns on 298.4 ns, and 53.50 ns on 275.4 ns) and ratio standard deviations up to 0.37. The `Channel` row for 4 producers has Errors of 15.76 ns on 136.7 ns and 13.64 ns on 167.8 ns. The other rows have small Errors, at most about 6 percent of the mean (`Scan` at most 12.76 ns on 299.9 ns, the queued rows 1 to 3 percent). Compare ratios with their `RatioSD` and the full reports, not the means alone.
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
