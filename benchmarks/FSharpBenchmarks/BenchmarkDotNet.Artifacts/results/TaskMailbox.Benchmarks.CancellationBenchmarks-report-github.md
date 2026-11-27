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
| MailboxProcessor | .NET 10.0 | 17.448 μs | 0.7434 μs | 0.8561 μs | 19.1357 us | 17.522 μs | 18.650 μs |  57,314.5 |  1.00 |    0.07 | 0.3333 |   3.06 KB |        1.00 |
| TaskMailbox      | .NET 10.0 | 18.497 μs | 0.4553 μs | 0.5243 μs | 19.2843 us | 18.461 μs | 19.146 μs |  54,061.6 |  1.06 |    0.06 | 0.6111 |    4.9 KB |        1.60 |
| Channel          | .NET 10.0 |  6.054 μs | 0.3751 μs | 0.4169 μs |  6.8121 us |  5.943 μs |  6.747 μs | 165,180.9 |  0.35 |    0.03 | 0.1667 |   1.65 KB |        0.54 |
|                  |           |           |           |           |            |           |           |           |       |         |        |           |             |
| MailboxProcessor | .NET 8.0  | 18.896 μs | 1.0577 μs | 1.2181 μs | 21.3564 us | 19.004 μs | 20.553 μs |  52,920.6 |  1.00 |    0.09 | 0.3333 |   3.15 KB |        1.00 |
| TaskMailbox      | .NET 8.0  | 56.997 μs | 0.4384 μs | 0.4690 μs | 57.8101 us | 56.900 μs | 57.779 μs |  17,544.7 |  3.03 |    0.19 | 0.9444 |   7.77 KB |        2.47 |
| Channel          | .NET 8.0  | 19.609 μs | 0.9562 μs | 1.1012 μs | 21.1583 us | 19.865 μs | 20.719 μs |  50,997.4 |  1.04 |    0.09 | 0.2222 |   2.13 KB |        0.68 |
