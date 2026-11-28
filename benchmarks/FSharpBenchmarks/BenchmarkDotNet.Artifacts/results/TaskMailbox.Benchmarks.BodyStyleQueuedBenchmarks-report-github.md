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
| MailboxProcessorRecursive | .NET 10.0 | 129.79 ns | 2.433 ns | 2.603 ns | 135.2593 ns | 128.77 ns | 134.45 ns |  7,705,048.3 |  1.00 |    0.03 | 0.0650 |     545 B |        1.00 |
| MailboxProcessorWhile     | .NET 10.0 | 131.07 ns | 2.114 ns | 2.434 ns | 135.4149 ns | 130.82 ns | 133.95 ns |  7,629,462.9 |  1.01 |    0.03 | 0.0560 |     473 B |        0.87 |
| TaskMailboxWhile          | .NET 10.0 |  49.75 ns | 1.042 ns | 1.158 ns |  52.2407 ns |  49.69 ns |  51.32 ns | 20,100,804.2 |  0.38 |    0.01 | 0.0150 |     128 B |        0.23 |
| TaskMailboxRecursive      | .NET 10.0 |  87.99 ns | 1.616 ns | 1.861 ns |  92.1237 ns |  87.42 ns |  92.04 ns | 11,364,446.1 |  0.68 |    0.02 | 0.0370 |     312 B |        0.57 |
|                           |           |           |          |          |             |           |           |              |       |         |        |           |             |
| MailboxProcessorRecursive | .NET 8.0  | 160.31 ns | 3.075 ns | 3.290 ns | 167.4026 ns | 160.47 ns | 167.07 ns |  6,237,829.7 |  1.00 |    0.03 | 0.0680 |     569 B |        1.00 |
| MailboxProcessorWhile     | .NET 8.0  | 152.28 ns | 2.349 ns | 2.412 ns | 156.6020 ns | 151.14 ns | 155.86 ns |  6,566,891.0 |  0.95 |    0.02 | 0.0590 |     497 B |        0.87 |
| TaskMailboxWhile          | .NET 8.0  |  60.07 ns | 1.016 ns | 1.043 ns |  61.7228 ns |  60.34 ns |  61.70 ns | 16,648,083.8 |  0.37 |    0.01 | 0.0180 |     152 B |        0.27 |
| TaskMailboxRecursive      | .NET 8.0  | 125.91 ns | 1.282 ns | 1.317 ns | 128.0778 ns | 126.00 ns | 127.54 ns |  7,942,100.4 |  0.79 |    0.02 | 0.0400 |     336 B |        0.59 |
