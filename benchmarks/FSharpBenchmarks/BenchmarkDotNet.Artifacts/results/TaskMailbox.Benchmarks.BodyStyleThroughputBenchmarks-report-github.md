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
| Method                    | Runtime   | Mean      | Error     | StdDev    | P99         | P50       | P95      | Op/s         | Ratio | RatioSD | Gen0   | Gen1   | Gen2   | Allocated | Alloc Ratio |
|-------------------------- |---------- |----------:|----------:|----------:|------------:|----------:|---------:|-------------:|------:|--------:|-------:|-------:|-------:|----------:|------------:|
| MailboxProcessorRecursive | .NET 10.0 | 295.05 ns | 54.151 ns | 62.360 ns | 364.6430 ns | 326.88 ns | 357.8 ns |  3,389,211.0 |  1.05 |    0.34 | 0.0670 | 0.0020 | 0.0020 |     553 B |        1.00 |
| MailboxProcessorWhile     | .NET 10.0 | 298.60 ns | 37.954 ns | 43.708 ns | 348.9072 ns | 320.13 ns | 342.2 ns |  3,349,014.2 |  1.06 |    0.30 | 0.0570 | 0.0020 | 0.0020 |     482 B |        0.87 |
| TaskMailboxWhile          | .NET 10.0 |  98.19 ns |  6.818 ns |  7.578 ns | 111.1461 ns |  98.68 ns | 107.1 ns | 10,184,651.3 |  0.35 |    0.09 | 0.0150 | 0.0010 | 0.0010 |     132 B |        0.24 |
| TaskMailboxRecursive      | .NET 10.0 | 120.39 ns |  8.438 ns |  9.717 ns | 136.5937 ns | 120.48 ns | 134.2 ns |  8,306,474.9 |  0.43 |    0.11 | 0.0370 | 0.0010 | 0.0010 |     316 B |        0.57 |
|                           |           |           |           |           |             |           |          |              |       |         |        |        |        |           |             |
| MailboxProcessorRecursive | .NET 8.0  | 330.81 ns | 54.323 ns | 62.558 ns | 405.1782 ns | 355.75 ns | 404.6 ns |  3,022,918.9 |  1.04 |    0.29 | 0.0690 | 0.0020 | 0.0020 |     577 B |        1.00 |
| MailboxProcessorWhile     | .NET 8.0  | 300.80 ns | 47.096 ns | 54.236 ns | 360.9917 ns | 332.73 ns | 359.4 ns |  3,324,415.3 |  0.94 |    0.26 | 0.0600 | 0.0020 | 0.0020 |     506 B |        0.88 |
| TaskMailboxWhile          | .NET 8.0  | 115.64 ns | 14.210 ns | 16.365 ns | 135.8808 ns | 119.46 ns | 134.9 ns |  8,647,301.4 |  0.36 |    0.09 | 0.0180 |      - |      - |     154 B |        0.27 |
| TaskMailboxRecursive      | .NET 8.0  | 158.11 ns | 10.495 ns | 12.086 ns | 177.5717 ns | 159.18 ns | 171.5 ns |  6,324,909.7 |  0.50 |    0.11 | 0.0400 | 0.0010 | 0.0010 |     340 B |        0.59 |
