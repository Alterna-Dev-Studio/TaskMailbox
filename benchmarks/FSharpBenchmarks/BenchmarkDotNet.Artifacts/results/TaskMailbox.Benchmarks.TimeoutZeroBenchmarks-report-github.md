```

BenchmarkDotNet v0.15.8, Linux Ubuntu 26.04.1 LTS (Resolute Raccoon)
12th Gen Intel Core i5-12450H 2.50GHz, 1 CPU, 12 logical and 12 physical cores
.NET SDK 10.0.404
  [Host]     : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3 DEBUG
  Job-COFKQO : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3
  Job-DHKZFF : .NET 8.0.34 (8.0.34, 8.0.3426.52217), X64 RyuJIT x86-64-v3

InvocationCount=1  IterationCount=20  UnrollFactor=1  
WarmupCount=3  Categories=Timeout.Zero  

```
| Method           | Runtime   | Mean      | Error    | StdDev   | P99         | P50       | P95       | Op/s         | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------- |---------- |----------:|---------:|---------:|------------:|----------:|----------:|-------------:|------:|--------:|-------:|----------:|------------:|
| MailboxProcessor | .NET 10.0 | 411.90 ns | 3.300 ns | 3.668 ns | 418.7830 ns | 412.51 ns | 416.78 ns |  2,427,783.8 |  1.00 |    0.01 | 0.1176 |     986 B |        1.00 |
| TaskMailbox      | .NET 10.0 |  91.65 ns | 1.108 ns | 1.185 ns |  93.7082 ns |  91.64 ns |  93.44 ns | 10,910,773.2 |  0.22 |    0.00 | 0.0140 |     120 B |        0.12 |
| Channel          | .NET 10.0 |  41.03 ns | 1.020 ns | 1.175 ns |  43.5105 ns |  41.14 ns |  42.98 ns | 24,373,088.5 |  0.10 |    0.00 |      - |         - |        0.00 |
|                  |           |           |          |          |             |           |           |              |       |         |        |           |             |
| MailboxProcessor | .NET 8.0  | 471.81 ns | 5.559 ns | 6.402 ns | 485.7921 ns | 470.07 ns | 482.59 ns |  2,119,505.5 |  1.00 |    0.02 | 0.1204 |    1010 B |        1.00 |
| TaskMailbox      | .NET 8.0  | 115.51 ns | 1.328 ns | 1.363 ns | 118.1659 ns | 115.58 ns | 117.58 ns |  8,657,425.4 |  0.24 |    0.00 | 0.0228 |     192 B |        0.19 |
| Channel          | .NET 8.0  |  39.84 ns | 1.081 ns | 1.156 ns |  41.4899 ns |  40.10 ns |  41.06 ns | 25,101,970.8 |  0.08 |    0.00 |      - |         - |        0.00 |
