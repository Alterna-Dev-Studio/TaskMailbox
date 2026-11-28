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
| MailboxProcessor | .NET 10.0 |  9.918 μs | 0.2640 μs | 0.2935 μs | 10.4039 us |  9.953 μs | 10.377 μs | 100,823.1 |  1.00 |    0.04 | 0.2000 | 0.0833 |    1709 B |        1.00 |
| TaskMailbox      | .NET 10.0 |  2.064 μs | 0.0478 μs | 0.0490 μs |  2.1734 us |  2.055 μs |  2.153 μs | 484,514.2 |  0.21 |    0.01 | 0.1333 |      - |    1205 B |        0.71 |
| Channel          | .NET 10.0 |  1.626 μs | 0.0345 μs | 0.0338 μs |  1.7110 us |  1.617 μs |  1.670 μs | 614,819.2 |  0.16 |    0.01 | 0.0500 |      - |     429 B |        0.25 |
|                  |           |           |           |           |            |           |           |           |       |         |        |        |           |             |
| MailboxProcessor | .NET 8.0  | 10.541 μs | 0.1830 μs | 0.1879 μs | 10.8478 us | 10.564 μs | 10.803 μs |  94,871.2 |  1.00 |    0.02 | 0.2000 | 0.0833 |    1676 B |        1.00 |
| TaskMailbox      | .NET 8.0  |  2.121 μs | 0.0894 μs | 0.0957 μs |  2.3250 us |  2.111 μs |  2.301 μs | 471,417.6 |  0.20 |    0.01 | 0.1500 |      - |    1273 B |        0.76 |
| Channel          | .NET 8.0  |  1.751 μs | 0.1045 μs | 0.1026 μs |  2.0482 us |  1.734 μs |  1.865 μs | 570,992.7 |  0.17 |    0.01 | 0.0500 |      - |     439 B |        0.26 |
