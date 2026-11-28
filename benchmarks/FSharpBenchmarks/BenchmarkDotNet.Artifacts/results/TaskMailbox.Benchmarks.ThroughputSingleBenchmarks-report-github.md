```

BenchmarkDotNet v0.15.8, Linux Ubuntu 26.04.1 LTS (Resolute Raccoon)
12th Gen Intel Core i5-12450H 2.50GHz, 1 CPU, 12 logical and 12 physical cores
.NET SDK 10.0.404
  [Host]     : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3 DEBUG
  Job-COFKQO : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3
  Job-DHKZFF : .NET 8.0.34 (8.0.34, 8.0.3426.52217), X64 RyuJIT x86-64-v3

InvocationCount=1  IterationCount=20  UnrollFactor=1  
WarmupCount=3  Categories=Throughput.Single  

```
| Method           | Runtime   | Mean     | Error    | StdDev   | P99         | P50      | P95      | Op/s        | Ratio | RatioSD | Gen0   | Gen1   | Gen2   | Allocated | Alloc Ratio |
|----------------- |---------- |---------:|---------:|---------:|------------:|---------:|---------:|------------:|------:|--------:|-------:|-------:|-------:|----------:|------------:|
| MailboxProcessor | .NET 10.0 | 282.9 ns | 42.36 ns | 48.78 ns | 339.9698 ns | 314.5 ns | 333.3 ns | 3,535,376.6 |  1.03 |    0.25 | 0.0570 | 0.0020 | 0.0020 |     482 B |        1.00 |
| TaskMailbox      | .NET 10.0 | 109.6 ns |  5.37 ns |  6.19 ns | 119.6510 ns | 110.4 ns | 118.0 ns | 9,122,919.6 |  0.40 |    0.07 | 0.0150 | 0.0010 | 0.0010 |     132 B |        0.27 |
| Channel          | .NET 10.0 | 133.5 ns |  7.11 ns |  8.19 ns | 141.6086 ns | 135.9 ns | 140.0 ns | 7,493,339.3 |  0.49 |    0.09 | 0.0010 |      - |      - |      13 B |        0.03 |
|                  |           |          |          |          |             |          |          |             |       |         |        |        |        |           |             |
| MailboxProcessor | .NET 8.0  | 280.9 ns | 58.61 ns | 67.49 ns | 359.4687 ns | 330.2 ns | 358.0 ns | 3,559,846.5 |  1.06 |    0.37 | 0.0600 | 0.0020 | 0.0020 |     506 B |        1.00 |
| TaskMailbox      | .NET 8.0  | 108.0 ns |  8.72 ns |  9.33 ns | 130.9968 ns | 106.6 ns | 123.9 ns | 9,260,352.9 |  0.41 |    0.11 | 0.0180 | 0.0010 | 0.0010 |     156 B |        0.31 |
| Channel          | .NET 8.0  | 130.7 ns |  7.73 ns |  8.90 ns | 141.2411 ns | 132.1 ns | 139.8 ns | 7,653,415.7 |  0.49 |    0.13 | 0.0010 |      - |      - |      14 B |        0.03 |
