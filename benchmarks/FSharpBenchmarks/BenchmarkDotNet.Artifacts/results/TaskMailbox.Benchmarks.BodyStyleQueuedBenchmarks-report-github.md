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
| Method                    | Runtime   | Mean     | Error   | StdDev  | P99         | P50      | P95      | Op/s        | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------------- |---------- |---------:|--------:|--------:|------------:|---------:|---------:|------------:|------:|--------:|-------:|----------:|------------:|
| MailboxProcessorRecursive | .NET 10.0 | 138.3 ns | 2.82 ns | 3.02 ns | 144.7011 ns | 137.6 ns | 144.6 ns | 7,232,589.9 |  1.00 |    0.03 | 0.0650 |     545 B |        1.00 |
| MailboxProcessorWhile     | .NET 10.0 | 131.1 ns | 2.06 ns | 2.38 ns | 135.9848 ns | 130.6 ns | 135.2 ns | 7,626,388.2 |  0.95 |    0.03 | 0.0560 |     473 B |        0.87 |
| TaskMailboxWhile          | .NET 10.0 | 210.8 ns | 2.17 ns | 2.23 ns | 215.8281 ns | 210.3 ns | 214.9 ns | 4,742,788.1 |  1.53 |    0.04 | 0.0410 |     344 B |        0.63 |
| TaskMailboxRecursive      | .NET 10.0 | 145.5 ns | 1.35 ns | 1.33 ns | 147.3545 ns | 145.6 ns | 147.2 ns | 6,871,024.4 |  1.05 |    0.02 | 0.0630 |     528 B |        0.97 |
|                           |           |          |         |         |             |          |          |             |       |         |        |           |             |
| MailboxProcessorRecursive | .NET 8.0  | 163.2 ns | 3.72 ns | 4.14 ns | 171.8461 ns | 162.5 ns | 170.9 ns | 6,127,851.4 |  1.00 |    0.03 | 0.0680 |     569 B |        1.00 |
| MailboxProcessorWhile     | .NET 8.0  | 154.1 ns | 2.35 ns | 2.71 ns | 159.9393 ns | 153.0 ns | 159.3 ns | 6,489,961.3 |  0.94 |    0.03 | 0.0590 |     497 B |        0.87 |
| TaskMailboxWhile          | .NET 8.0  | 150.2 ns | 1.62 ns | 1.66 ns | 152.6554 ns | 150.1 ns | 152.1 ns | 6,657,266.9 |  0.92 |    0.02 | 0.0460 |     392 B |        0.69 |
| TaskMailboxRecursive      | .NET 8.0  | 221.8 ns | 2.97 ns | 3.42 ns | 227.9874 ns | 220.6 ns | 227.1 ns | 4,509,108.5 |  1.36 |    0.04 | 0.0680 |     576 B |        1.01 |
