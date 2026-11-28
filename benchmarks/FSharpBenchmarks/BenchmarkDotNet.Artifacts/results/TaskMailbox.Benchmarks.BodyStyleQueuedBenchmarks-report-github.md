```

BenchmarkDotNet v0.15.8, Linux Ubuntu 26.04.1 LTS (Resolute Raccoon)
12th Gen Intel Core i5-12450H 2.50GHz, 1 CPU, 12 logical and 12 physical cores
.NET SDK 10.0.404
  [Host]     : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3 DEBUG
  Job-COFKQO : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3
  Job-DHKZFF : .NET 8.0.34 (8.0.34, 8.0.3426.52217), X64 RyuJIT x86-64-v3

InvocationCount=1  IterationCount=20  UnrollFactor=1  
WarmupCount=3  Categories=BodyStyle.Queued  

```
| Method                    | Runtime   | Mean      | Error    | StdDev   | P99         | P50       | P95       | Op/s         | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------------- |---------- |----------:|---------:|---------:|------------:|----------:|----------:|-------------:|------:|--------:|-------:|----------:|------------:|
| MailboxProcessorRecursive | .NET 10.0 | 135.23 ns | 5.783 ns | 6.659 ns | 148.8349 ns | 133.22 ns | 147.99 ns |  7,394,828.3 |  1.00 |    0.07 | 0.0650 |     545 B |        1.00 |
| MailboxProcessorWhile     | .NET 10.0 | 131.75 ns | 1.983 ns | 2.204 ns | 135.9585 ns | 131.78 ns | 135.43 ns |  7,590,293.9 |  0.98 |    0.05 | 0.0560 |     473 B |        0.87 |
| TaskMailboxWhile          | .NET 10.0 |  44.22 ns | 0.644 ns | 0.689 ns |  45.4957 ns |  44.20 ns |  45.32 ns | 22,614,506.5 |  0.33 |    0.02 | 0.0150 |     128 B |        0.23 |
| TaskMailboxRecursive      | .NET 10.0 |  85.60 ns | 1.788 ns | 1.987 ns |  88.8981 ns |  85.69 ns |  88.53 ns | 11,681,752.2 |  0.63 |    0.03 | 0.0370 |     312 B |        0.57 |
|                           |           |           |          |          |             |           |           |              |       |         |        |           |             |
| MailboxProcessorRecursive | .NET 8.0  | 158.47 ns | 1.857 ns | 1.824 ns | 162.0344 ns | 158.52 ns | 160.85 ns |  6,310,458.3 |  1.00 |    0.02 | 0.0680 |     569 B |        1.00 |
| MailboxProcessorWhile     | .NET 8.0  | 156.74 ns | 2.361 ns | 2.719 ns | 161.5049 ns | 156.27 ns | 160.89 ns |  6,380,083.9 |  0.99 |    0.02 | 0.0590 |     497 B |        0.87 |
| TaskMailboxWhile          | .NET 8.0  |  53.63 ns | 2.323 ns | 2.675 ns |  59.0562 ns |  52.80 ns |  58.71 ns | 18,644,898.2 |  0.34 |    0.02 | 0.0180 |     152 B |        0.27 |
| TaskMailboxRecursive      | .NET 8.0  | 125.08 ns | 1.731 ns | 1.993 ns | 128.4901 ns | 124.96 ns | 128.27 ns |  7,994,569.8 |  0.79 |    0.02 | 0.0400 |     336 B |        0.59 |
