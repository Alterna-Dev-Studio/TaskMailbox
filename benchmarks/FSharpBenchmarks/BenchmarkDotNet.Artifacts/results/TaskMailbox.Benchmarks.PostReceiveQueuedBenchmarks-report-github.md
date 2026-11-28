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
| Method           | Runtime   | Mean      | Error    | StdDev   | P99         | P50       | P95       | Op/s         | Ratio | Gen0   | Allocated | Alloc Ratio |
|----------------- |---------- |----------:|---------:|---------:|------------:|----------:|----------:|-------------:|------:|-------:|----------:|------------:|
| MailboxProcessor | .NET 10.0 | 133.14 ns | 0.839 ns | 0.861 ns | 134.5311 ns | 133.19 ns | 134.37 ns |  7,510,624.0 |  1.00 | 0.0565 |     473 B |        1.00 |
| TaskMailbox      | .NET 10.0 |  45.51 ns | 0.401 ns | 0.429 ns |  46.1842 ns |  45.45 ns |  46.17 ns | 21,971,616.8 |  0.34 | 0.0152 |     128 B |        0.27 |
| Channel          | .NET 10.0 |  16.61 ns | 0.109 ns | 0.112 ns |  16.7829 ns |  16.62 ns |  16.78 ns | 60,195,895.5 |  0.12 |      - |         - |        0.00 |
|                  |           |           |          |          |             |           |           |              |       |        |           |             |
| MailboxProcessor | .NET 8.0  | 156.46 ns | 1.142 ns | 1.173 ns | 158.7050 ns | 156.45 ns | 158.23 ns |  6,391,362.5 |  1.00 | 0.0593 |     497 B |        1.00 |
| TaskMailbox      | .NET 8.0  |  54.55 ns | 0.526 ns | 0.563 ns |  55.9156 ns |  54.35 ns |  55.34 ns | 18,332,660.6 |  0.35 | 0.0182 |     152 B |        0.31 |
| Channel          | .NET 8.0  |  17.31 ns | 0.248 ns | 0.286 ns |  17.8182 ns |  17.20 ns |  17.79 ns | 57,761,794.5 |  0.11 |      - |         - |        0.00 |
