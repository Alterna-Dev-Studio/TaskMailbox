```

BenchmarkDotNet v0.15.8, Linux Ubuntu 26.04.1 LTS (Resolute Raccoon)
12th Gen Intel Core i5-12450H 2.50GHz, 1 CPU, 12 logical and 12 physical cores
.NET SDK 10.0.404
  [Host]     : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3 DEBUG
  Job-COFKQO : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3
  Job-DHKZFF : .NET 8.0.34 (8.0.34, 8.0.3426.52217), X64 RyuJIT x86-64-v3

InvocationCount=1  IterationCount=20  UnrollFactor=1  
WarmupCount=3  Categories=BodyStyle.Throughput  

```
| Method                    | Runtime   | Mean     | Error    | StdDev   | P99         | P50      | P95      | Op/s        | Ratio | RatioSD | Gen0   | Gen1   | Gen2   | Allocated | Alloc Ratio |
|-------------------------- |---------- |---------:|---------:|---------:|------------:|---------:|---------:|------------:|------:|--------:|-------:|-------:|-------:|----------:|------------:|
| MailboxProcessorRecursive | .NET 10.0 | 277.8 ns | 47.12 ns | 54.26 ns | 336.2505 ns | 312.1 ns | 329.8 ns | 3,599,306.6 |  1.04 |    0.30 | 0.0660 | 0.0020 | 0.0020 |     553 B |        1.00 |
| MailboxProcessorWhile     | .NET 10.0 | 276.8 ns | 38.97 ns | 44.88 ns | 315.7176 ns | 305.1 ns | 315.6 ns | 3,612,836.7 |  1.04 |    0.28 | 0.0580 | 0.0020 | 0.0020 |     482 B |        0.87 |
| TaskMailboxWhile          | .NET 10.0 | 109.5 ns |  7.13 ns |  8.21 ns | 124.0588 ns | 108.4 ns | 121.0 ns | 9,129,766.3 |  0.41 |    0.09 | 0.0150 | 0.0010 | 0.0010 |     132 B |        0.24 |
| TaskMailboxRecursive      | .NET 10.0 | 129.2 ns |  9.16 ns | 10.55 ns | 142.2920 ns | 133.2 ns | 141.3 ns | 7,741,102.6 |  0.48 |    0.11 | 0.0370 | 0.0010 | 0.0010 |     316 B |        0.57 |
|                           |           |          |          |          |             |          |          |             |       |         |        |        |        |           |             |
| MailboxProcessorRecursive | .NET 8.0  | 317.4 ns | 53.86 ns | 62.02 ns | 376.6783 ns | 356.7 ns | 372.0 ns | 3,151,030.6 |  1.04 |    0.30 | 0.0690 | 0.0020 | 0.0020 |     577 B |        1.00 |
| MailboxProcessorWhile     | .NET 8.0  | 283.2 ns | 54.10 ns | 62.30 ns | 370.5850 ns | 284.9 ns | 346.5 ns | 3,530,532.0 |  0.93 |    0.29 | 0.0600 | 0.0020 | 0.0020 |     506 B |        0.88 |
| TaskMailboxWhile          | .NET 8.0  | 106.2 ns |  3.88 ns |  4.31 ns | 112.8972 ns | 106.4 ns | 111.7 ns | 9,415,328.8 |  0.35 |    0.08 | 0.0180 | 0.0010 | 0.0010 |     156 B |        0.27 |
| TaskMailboxRecursive      | .NET 8.0  | 157.1 ns |  5.64 ns |  6.49 ns | 168.9880 ns | 156.9 ns | 165.0 ns | 6,363,968.0 |  0.52 |    0.11 | 0.0400 | 0.0010 | 0.0010 |     340 B |        0.59 |
