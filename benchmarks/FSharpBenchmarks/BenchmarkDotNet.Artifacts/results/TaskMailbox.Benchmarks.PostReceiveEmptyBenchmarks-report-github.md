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
| MailboxProcessor | .NET 10.0 |  9.754 μs | 0.3063 μs | 0.3404 μs | 10.3246 us |  9.812 μs | 10.222 μs | 102,525.7 |  1.00 |    0.05 | 0.2000 | 0.0833 |    1733 B |        1.00 |
| TaskMailbox      | .NET 10.0 |  2.198 μs | 0.1341 μs | 0.1377 μs |  2.5367 us |  2.164 μs |  2.525 μs | 455,017.2 |  0.23 |    0.02 | 0.1500 |      - |    1378 B |        0.80 |
| Channel          | .NET 10.0 |  1.641 μs | 0.0568 μs | 0.0558 μs |  1.7994 us |  1.627 μs |  1.714 μs | 609,302.6 |  0.17 |    0.01 | 0.0500 |      - |     430 B |        0.25 |
|                  |           |           |           |           |            |           |           |           |       |         |        |        |           |             |
| MailboxProcessor | .NET 8.0  | 10.492 μs | 0.3397 μs | 0.3776 μs | 11.1876 us | 10.520 μs | 10.937 μs |  95,314.7 |  1.00 |    0.05 | 0.2000 | 0.0833 |    1691 B |        1.00 |
| TaskMailbox      | .NET 8.0  |  2.103 μs | 0.0690 μs | 0.0678 μs |  2.2235 us |  2.086 μs |  2.212 μs | 475,603.5 |  0.20 |    0.01 | 0.1667 |      - |    1429 B |        0.85 |
| Channel          | .NET 8.0  |  1.654 μs | 0.0300 μs | 0.0308 μs |  1.7039 us |  1.655 μs |  1.694 μs | 604,775.5 |  0.16 |    0.01 | 0.0500 |      - |     439 B |        0.26 |
