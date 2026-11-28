```

BenchmarkDotNet v0.15.8, Linux Ubuntu 26.04.1 LTS (Resolute Raccoon)
12th Gen Intel Core i5-12450H 2.50GHz, 1 CPU, 12 logical and 12 physical cores
.NET SDK 10.0.404
  [Host]     : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3 DEBUG
  Job-COFKQO : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3
  Job-DHKZFF : .NET 8.0.34 (8.0.34, 8.0.3426.52217), X64 RyuJIT x86-64-v3

InvocationCount=1  IterationCount=20  UnrollFactor=1  
WarmupCount=3  Categories=Cancellation  

```
| Method           | Runtime   | Mean      | Error     | StdDev    | P99        | P50       | P95       | Op/s      | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------- |---------- |----------:|----------:|----------:|-----------:|----------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| MailboxProcessor | .NET 10.0 | 18.557 μs | 0.6374 μs | 0.7340 μs | 19.7566 us | 18.649 μs | 19.715 μs |  53,887.4 |  1.00 |    0.05 | 0.3333 |   3.06 KB |        1.00 |
| TaskMailbox      | .NET 10.0 |  3.853 μs | 0.3009 μs | 0.3090 μs |  4.5560 us |  3.912 μs |  4.221 μs | 259,538.4 |  0.21 |    0.02 | 0.1667 |    1.6 KB |        0.52 |
| Channel          | .NET 10.0 |  6.037 μs | 0.3542 μs | 0.3937 μs |  6.8542 us |  5.942 μs |  6.827 μs | 165,656.5 |  0.33 |    0.02 | 0.1667 |   1.65 KB |        0.54 |
|                  |           |           |           |           |            |           |           |           |       |         |        |           |             |
| MailboxProcessor | .NET 8.0  | 19.043 μs | 0.7791 μs | 0.8336 μs | 20.9048 us | 18.831 μs | 20.887 μs |  52,512.3 |  1.00 |    0.06 | 0.3333 |   3.15 KB |        1.00 |
| TaskMailbox      | .NET 8.0  | 11.558 μs | 0.6843 μs | 0.7606 μs | 12.9691 us | 11.507 μs | 12.757 μs |  86,522.7 |  0.61 |    0.05 | 0.1667 |   1.67 KB |        0.53 |
| Channel          | .NET 8.0  | 17.390 μs | 0.7028 μs | 0.7811 μs | 18.7387 us | 17.362 μs | 18.577 μs |  57,505.3 |  0.91 |    0.05 | 0.2222 |   2.13 KB |        0.68 |
