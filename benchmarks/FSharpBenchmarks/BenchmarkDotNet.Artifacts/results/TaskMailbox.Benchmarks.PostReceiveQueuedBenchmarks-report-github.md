```

BenchmarkDotNet v0.15.8, Linux Ubuntu 26.04.1 LTS (Resolute Raccoon)
12th Gen Intel Core i5-12450H 2.50GHz, 1 CPU, 12 logical and 12 physical cores
.NET SDK 10.0.404
  [Host]     : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3 DEBUG
  Job-COFKQO : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3
  Job-DHKZFF : .NET 8.0.34 (8.0.34, 8.0.3426.52217), X64 RyuJIT x86-64-v3

InvocationCount=1  IterationCount=20  UnrollFactor=1  
WarmupCount=3  Categories=PostReceive.Queued  

```
| Method           | Runtime   | Mean      | Error    | StdDev   | P99         | P50       | P95       | Op/s         | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------- |---------- |----------:|---------:|---------:|------------:|----------:|----------:|-------------:|------:|--------:|-------:|----------:|------------:|
| MailboxProcessor | .NET 10.0 | 132.80 ns | 1.324 ns | 1.416 ns | 135.9346 ns | 132.78 ns | 135.71 ns |  7,529,847.2 |  1.00 |    0.01 | 0.0565 |     473 B |        1.00 |
| TaskMailbox      | .NET 10.0 | 209.31 ns | 1.383 ns | 1.593 ns | 212.1774 ns | 209.51 ns | 211.38 ns |  4,777,688.9 |  1.58 |    0.02 | 0.0410 |     344 B |        0.73 |
| Channel          | .NET 10.0 |  16.66 ns | 0.227 ns | 0.261 ns |  17.1279 ns |  16.62 ns |  17.12 ns | 60,008,584.9 |  0.13 |    0.00 |      - |         - |        0.00 |
|                  |           |           |          |          |             |           |           |              |       |         |        |           |             |
| MailboxProcessor | .NET 8.0  | 155.15 ns | 2.214 ns | 2.550 ns | 160.3110 ns | 154.39 ns | 159.66 ns |  6,445,221.8 |  1.00 |    0.02 | 0.0593 |     497 B |        1.00 |
| TaskMailbox      | .NET 8.0  | 147.99 ns | 1.693 ns | 1.881 ns | 151.1812 ns | 148.05 ns | 150.85 ns |  6,757,183.0 |  0.95 |    0.02 | 0.0468 |     392 B |        0.79 |
| Channel          | .NET 8.0  |  17.10 ns | 0.266 ns | 0.285 ns |  17.7035 ns |  17.03 ns |  17.67 ns | 58,487,346.6 |  0.11 |    0.00 |      - |         - |        0.00 |
