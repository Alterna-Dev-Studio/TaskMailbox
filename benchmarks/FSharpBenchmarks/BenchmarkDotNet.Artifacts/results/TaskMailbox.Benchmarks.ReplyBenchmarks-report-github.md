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
| MailboxProcessor | .NET 10.0 | 17.115 μs | 0.1834 μs | 0.2112 μs | 17.5582 us | 17.079 μs | 17.477 μs |  58,428.5 |  1.00 |    0.02 | 0.4400 | 0.2133 |    3672 B |        1.00 |
| TaskMailbox      | .NET 10.0 |  2.679 μs | 0.0710 μs | 0.0759 μs |  2.8276 us |  2.686 μs |  2.790 μs | 373,246.1 |  0.16 |    0.00 | 0.2933 |      - |    2512 B |        0.68 |
| Channel          | .NET 10.0 |  1.163 μs | 0.0790 μs | 0.0811 μs |  1.2245 us |  1.177 μs |  1.221 μs | 859,825.8 |  0.07 |    0.00 | 0.0267 |      - |     242 B |        0.07 |
|                  |           |           |           |           |            |           |           |           |       |         |        |        |           |             |
| MailboxProcessor | .NET 8.0  | 18.452 μs | 0.2046 μs | 0.2356 μs | 18.8258 us | 18.453 μs | 18.816 μs |  54,193.6 |  1.00 |    0.02 | 0.4400 | 0.2133 |    3696 B |        1.00 |
| TaskMailbox      | .NET 8.0  |  2.903 μs | 0.0600 μs | 0.0691 μs |  3.0470 us |  2.907 μs |  3.004 μs | 344,429.6 |  0.16 |    0.00 | 0.3067 |      - |    2584 B |        0.70 |
| Channel          | .NET 8.0  |  1.214 μs | 0.0464 μs | 0.0456 μs |  1.2749 us |  1.217 μs |  1.273 μs | 824,025.0 |  0.07 |    0.00 | 0.0267 |      - |     249 B |        0.07 |
