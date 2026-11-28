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
| Method           | Runtime   | Mean      | Error     | StdDev    | P99        | P50       | P95       | Op/s      | Ratio | Gen0   | Gen1   | Allocated | Alloc Ratio |
|----------------- |---------- |----------:|----------:|----------:|-----------:|----------:|----------:|----------:|------:|-------:|-------:|----------:|------------:|
| MailboxProcessor | .NET 10.0 | 17.096 μs | 0.0924 μs | 0.0988 μs | 17.2351 us | 17.086 μs | 17.229 μs |  58,493.6 |  1.00 | 0.4400 | 0.2133 |    3672 B |        1.00 |
| TaskMailbox      | .NET 10.0 |  2.640 μs | 0.0460 μs | 0.0472 μs |  2.7181 us |  2.647 μs |  2.709 μs | 378,805.7 |  0.15 | 0.2800 |      - |    2374 B |        0.65 |
| Channel          | .NET 10.0 |  1.163 μs | 0.0199 μs | 0.0186 μs |  1.1854 us |  1.164 μs |  1.183 μs | 859,910.1 |  0.07 | 0.0267 |      - |     241 B |        0.07 |
|                  |           |           |           |           |            |           |           |           |       |        |        |           |             |
| MailboxProcessor | .NET 8.0  | 18.190 μs | 0.1245 μs | 0.1384 μs | 18.3894 us | 18.190 μs | 18.356 μs |  54,976.1 |  1.00 | 0.4400 | 0.2133 |    3696 B |        1.00 |
| TaskMailbox      | .NET 8.0  |  2.702 μs | 0.0619 μs | 0.0688 μs |  2.8030 us |  2.702 μs |  2.793 μs | 370,113.1 |  0.15 | 0.2933 |      - |    2440 B |        0.66 |
| Channel          | .NET 8.0  |  1.251 μs | 0.0847 μs | 0.0832 μs |  1.5001 us |  1.232 μs |  1.355 μs | 799,337.0 |  0.07 | 0.0267 |      - |     250 B |        0.07 |
