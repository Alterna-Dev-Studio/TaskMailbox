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
| MailboxProcessorRecursive | .NET 10.0 | 279.7 ns | 47.53 ns | 54.74 ns | 320.4455 ns | 312.0 ns | 318.8 ns | 3,574,922.5 |  1.05 |    0.32 | 0.0660 | 0.0020 | 0.0020 |     553 B |        1.00 |
| MailboxProcessorWhile     | .NET 10.0 | 290.8 ns | 39.87 ns | 45.91 ns | 350.7649 ns | 320.4 ns | 346.4 ns | 3,438,207.1 |  1.09 |    0.30 | 0.0570 | 0.0020 | 0.0020 |     482 B |        0.87 |
| TaskMailboxWhile          | .NET 10.0 | 242.0 ns |  9.04 ns | 10.41 ns | 263.0024 ns | 243.4 ns | 258.9 ns | 4,131,504.0 |  0.90 |    0.21 | 0.0410 | 0.0010 | 0.0010 |     348 B |        0.63 |
| TaskMailboxRecursive      | .NET 10.0 | 184.7 ns |  2.79 ns |  2.99 ns | 189.4579 ns | 185.1 ns | 188.1 ns | 5,412,755.0 |  0.69 |    0.16 | 0.0630 | 0.0010 | 0.0010 |     532 B |        0.96 |
|                           |           |          |          |          |             |          |          |             |       |         |        |        |        |           |             |
| MailboxProcessorRecursive | .NET 8.0  | 275.4 ns | 53.50 ns | 61.61 ns | 368.1415 ns | 238.2 ns | 367.5 ns | 3,630,910.5 |  1.04 |    0.31 | 0.0690 | 0.0020 | 0.0020 |     577 B |        1.00 |
| MailboxProcessorWhile     | .NET 8.0  | 302.1 ns | 48.02 ns | 55.30 ns | 352.2999 ns | 339.7 ns | 351.1 ns | 3,310,623.5 |  1.15 |    0.30 | 0.0600 | 0.0020 | 0.0020 |     506 B |        0.88 |
| TaskMailboxWhile          | .NET 8.0  | 179.9 ns |  4.85 ns |  5.59 ns | 188.7569 ns | 180.7 ns | 186.1 ns | 5,560,168.8 |  0.68 |    0.13 | 0.0460 | 0.0010 | 0.0010 |     396 B |        0.69 |
| TaskMailboxRecursive      | .NET 8.0  | 244.6 ns |  5.69 ns |  6.32 ns | 257.6654 ns | 244.8 ns | 252.0 ns | 4,087,555.6 |  0.93 |    0.18 | 0.0680 | 0.0010 | 0.0010 |     580 B |        1.01 |
