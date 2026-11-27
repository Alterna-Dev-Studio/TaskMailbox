```

BenchmarkDotNet v0.15.8, Linux Ubuntu 26.04.1 LTS (Resolute Raccoon)
12th Gen Intel Core i5-12450H 2.50GHz, 1 CPU, 12 logical and 12 physical cores
.NET SDK 10.0.404
  [Host]     : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3 DEBUG
  Job-COFKQO : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3
  Job-DHKZFF : .NET 8.0.34 (8.0.34, 8.0.3426.52217), X64 RyuJIT x86-64-v3

InvocationCount=1  IterationCount=20  UnrollFactor=1  
WarmupCount=3  Categories=PostAndAsyncReply  

```
| Method           | Runtime   | Mean      | Error     | StdDev    | P99        | P50       | P95       | Op/s      | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|----------------- |---------- |----------:|----------:|----------:|-----------:|----------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| MailboxProcessor | .NET 10.0 | 17.031 μs | 0.1746 μs | 0.1940 μs | 17.4690 us | 16.986 μs | 17.381 μs |  58,717.6 |  1.00 |    0.02 | 0.4400 | 0.2133 |    3672 B |        1.00 |
| TaskMailbox      | .NET 10.0 |  2.700 μs | 0.0740 μs | 0.0822 μs |  2.8737 us |  2.713 μs |  2.813 μs | 370,386.6 |  0.16 |    0.01 | 0.3200 |      - |    2714 B |        0.74 |
| Channel          | .NET 10.0 |  1.182 μs | 0.0556 μs | 0.0595 μs |  1.3273 us |  1.167 μs |  1.297 μs | 846,165.8 |  0.07 |    0.00 | 0.0267 |      - |     237 B |        0.06 |
|                  |           |           |           |           |            |           |           |           |       |         |        |        |           |             |
| MailboxProcessor | .NET 8.0  | 18.190 μs | 0.1675 μs | 0.1861 μs | 18.5576 us | 18.222 μs | 18.472 μs |  54,974.4 |  1.00 |    0.01 | 0.4400 | 0.2133 |    3696 B |        1.00 |
| TaskMailbox      | .NET 8.0  |  2.846 μs | 0.0477 μs | 0.0530 μs |  2.9602 us |  2.829 μs |  2.942 μs | 351,427.7 |  0.16 |    0.00 | 0.3200 |      - |    2745 B |        0.74 |
| Channel          | .NET 8.0  |  1.223 μs | 0.0309 μs | 0.0317 μs |  1.2879 us |  1.217 μs |  1.279 μs | 817,862.1 |  0.07 |    0.00 | 0.0267 |      - |     252 B |        0.07 |
