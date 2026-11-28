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
| MailboxProcessor | .NET 10.0 | 132.39 ns | 1.625 ns | 1.807 ns | 136.4725 ns | 131.91 ns | 135.38 ns |  7,553,509.4 |  1.00 |    0.02 | 0.0565 |     473 B |        1.00 |
| TaskMailbox      | .NET 10.0 |  47.08 ns | 0.527 ns | 0.586 ns |  48.2525 ns |  46.94 ns |  48.04 ns | 21,242,078.8 |  0.36 |    0.01 | 0.0152 |     128 B |        0.27 |
| Channel          | .NET 10.0 |  16.86 ns | 0.129 ns | 0.138 ns |  17.1224 ns |  16.85 ns |  17.11 ns | 59,327,109.4 |  0.13 |    0.00 |      - |         - |        0.00 |
|                  |           |           |          |          |             |           |           |              |       |         |        |           |             |
| MailboxProcessor | .NET 8.0  | 157.25 ns | 2.366 ns | 2.630 ns | 162.6915 ns | 156.90 ns | 161.81 ns |  6,359,454.0 |  1.00 |    0.02 | 0.0593 |     497 B |        1.00 |
| TaskMailbox      | .NET 8.0  |  50.92 ns | 0.542 ns | 0.624 ns |  51.9211 ns |  50.97 ns |  51.69 ns | 19,637,867.8 |  0.32 |    0.01 | 0.0182 |     152 B |        0.31 |
| Channel          | .NET 8.0  |  17.77 ns | 0.221 ns | 0.237 ns |  18.1362 ns |  17.77 ns |  18.13 ns | 56,281,065.2 |  0.11 |    0.00 |      - |         - |        0.00 |
