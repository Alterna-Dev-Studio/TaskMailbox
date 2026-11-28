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
| MailboxProcessor | .NET 10.0 | 412.29 ns | 3.455 ns | 3.696 ns | 420.0995 ns | 411.79 ns | 419.42 ns |  2,425,461.4 |  1.00 |    0.01 | 0.1176 |     986 B |        1.00 |
| TaskMailbox      | .NET 10.0 |  90.36 ns | 1.233 ns | 1.419 ns |  92.6352 ns |  90.27 ns |  92.63 ns | 11,067,374.5 |  0.22 |    0.00 | 0.0140 |     120 B |        0.12 |
| Channel          | .NET 10.0 |  43.41 ns | 0.684 ns | 0.788 ns |  44.8570 ns |  43.43 ns |  44.75 ns | 23,037,338.8 |  0.11 |    0.00 |      - |         - |        0.00 |
|                  |           |           |          |          |             |           |           |              |       |         |        |           |             |
| MailboxProcessor | .NET 8.0  | 468.67 ns | 5.607 ns | 6.457 ns | 480.3750 ns | 467.14 ns | 480.15 ns |  2,133,704.4 |  1.00 |    0.02 | 0.1204 |    1010 B |        1.00 |
| TaskMailbox      | .NET 8.0  | 120.86 ns | 1.890 ns | 2.022 ns | 125.7375 ns | 120.41 ns | 124.20 ns |  8,274,231.4 |  0.26 |    0.01 | 0.0228 |     192 B |        0.19 |
| Channel          | .NET 8.0  |  42.40 ns | 0.642 ns | 0.687 ns |  43.2535 ns |  42.63 ns |  43.10 ns | 23,585,938.2 |  0.09 |    0.00 |      - |         - |        0.00 |
