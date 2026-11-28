```

BenchmarkDotNet v0.15.8, Linux Ubuntu 26.04.1 LTS (Resolute Raccoon)
12th Gen Intel Core i5-12450H 2.50GHz, 1 CPU, 12 logical and 12 physical cores
.NET SDK 10.0.404
  [Host]     : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3 DEBUG
  Job-COFKQO : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3
  Job-DHKZFF : .NET 8.0.34 (8.0.34, 8.0.3426.52217), X64 RyuJIT x86-64-v3

InvocationCount=1  IterationCount=20  UnrollFactor=1  
WarmupCount=3  Categories=PostReceive.Empty  

```
| Method           | Runtime   | Mean      | Error     | StdDev    | P99        | P50       | P95       | Op/s      | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|----------------- |---------- |----------:|----------:|----------:|-----------:|----------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| MailboxProcessor | .NET 10.0 | 10.074 μs | 0.2343 μs | 0.2507 μs | 10.6329 us | 10.047 μs | 10.603 μs |  99,269.0 |  1.00 |    0.03 | 0.2000 | 0.0833 |    1716 B |        1.00 |
| TaskMailbox      | .NET 10.0 |  2.122 μs | 0.0745 μs | 0.0797 μs |  2.2899 us |  2.094 μs |  2.286 μs | 471,145.3 |  0.21 |    0.01 | 0.1167 |      - |    1085 B |        0.63 |
| Channel          | .NET 10.0 |  1.686 μs | 0.1469 μs | 0.1509 μs |  2.0281 us |  1.618 μs |  2.005 μs | 593,233.4 |  0.17 |    0.02 | 0.0500 |      - |     430 B |        0.25 |
|                  |           |           |           |           |            |           |           |           |       |         |        |        |           |             |
| MailboxProcessor | .NET 8.0  | 10.836 μs | 0.3093 μs | 0.3562 μs | 11.4988 us | 10.828 μs | 11.443 μs |  92,281.3 |  1.00 |    0.05 | 0.2000 | 0.0833 |    1693 B |        1.00 |
| TaskMailbox      | .NET 8.0  |  2.035 μs | 0.0486 μs | 0.0477 μs |  2.1041 us |  2.043 μs |  2.096 μs | 491,303.3 |  0.19 |    0.01 | 0.1333 |      - |    1127 B |        0.67 |
| Channel          | .NET 8.0  |  1.667 μs | 0.0237 μs | 0.0243 μs |  1.7195 us |  1.666 μs |  1.705 μs | 599,705.9 |  0.15 |    0.01 | 0.0500 |      - |     436 B |        0.26 |
