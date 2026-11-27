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
| MailboxProcessor | .NET 10.0 | 414.86 ns | 3.441 ns | 3.824 ns | 422.6203 ns | 415.26 ns | 420.20 ns |  2,410,462.4 |  1.00 |    0.01 | 0.1176 |     986 B |        1.00 |
| TaskMailbox      | .NET 10.0 | 146.88 ns | 1.326 ns | 1.474 ns | 149.3931 ns | 146.88 ns | 149.05 ns |  6,808,414.9 |  0.35 |    0.00 | 0.0312 |     264 B |        0.27 |
| Channel          | .NET 10.0 |  43.36 ns | 1.014 ns | 1.168 ns |  45.6451 ns |  43.18 ns |  45.20 ns | 23,061,317.4 |  0.10 |    0.00 |      - |         - |        0.00 |
|                  |           |           |          |          |             |           |           |              |       |         |        |           |             |
| MailboxProcessor | .NET 8.0  | 468.33 ns | 5.708 ns | 6.573 ns | 480.8249 ns | 469.23 ns | 477.65 ns |  2,135,224.3 |  1.00 |    0.02 | 0.1204 |    1010 B |        1.00 |
| TaskMailbox      | .NET 8.0  | 203.74 ns | 2.227 ns | 2.475 ns | 208.4598 ns | 203.51 ns | 208.04 ns |  4,908,199.0 |  0.44 |    0.01 | 0.0428 |     360 B |        0.36 |
| Channel          | .NET 8.0  |  42.88 ns | 0.579 ns | 0.619 ns |  43.6957 ns |  43.01 ns |  43.69 ns | 23,318,640.1 |  0.09 |    0.00 |      - |         - |        0.00 |
