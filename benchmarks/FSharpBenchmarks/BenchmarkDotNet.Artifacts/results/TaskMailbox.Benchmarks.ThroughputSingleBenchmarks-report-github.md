```

BenchmarkDotNet v0.15.8, Linux Ubuntu 26.04.1 LTS (Resolute Raccoon)
12th Gen Intel Core i5-12450H 2.50GHz, 1 CPU, 12 logical and 12 physical cores
.NET SDK 10.0.404
  [Host]     : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3 DEBUG
  Job-COFKQO : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3
  Job-DHKZFF : .NET 8.0.34 (8.0.34, 8.0.3426.52217), X64 RyuJIT x86-64-v3

InvocationCount=1  IterationCount=20  UnrollFactor=1  
WarmupCount=3  Categories=Throughput.Single  

```
| Method           | Runtime   | Mean     | Error    | StdDev   | P99         | P50      | P95      | Op/s        | Ratio | RatioSD | Gen0   | Gen1   | Gen2   | Allocated | Alloc Ratio |
|----------------- |---------- |---------:|---------:|---------:|------------:|---------:|---------:|------------:|------:|--------:|-------:|-------:|-------:|----------:|------------:|
| MailboxProcessor | .NET 10.0 | 276.3 ns | 43.90 ns | 50.55 ns | 346.1415 ns | 272.8 ns | 341.3 ns | 3,618,849.7 |  1.03 |    0.26 | 0.0570 | 0.0020 | 0.0020 |     482 B |        1.00 |
| TaskMailbox      | .NET 10.0 | 102.7 ns |  5.37 ns |  5.75 ns | 115.6481 ns | 101.9 ns | 113.9 ns | 9,735,668.0 |  0.38 |    0.07 | 0.0150 | 0.0010 | 0.0010 |     132 B |        0.27 |
| Channel          | .NET 10.0 | 122.8 ns | 12.83 ns | 14.77 ns | 136.7910 ns | 127.3 ns | 135.1 ns | 8,141,537.0 |  0.46 |    0.10 | 0.0010 |      - |      - |      12 B |        0.02 |
|                  |           |          |          |          |             |          |          |             |       |         |        |        |        |           |             |
| MailboxProcessor | .NET 8.0  | 288.9 ns | 62.19 ns | 71.62 ns | 358.7527 ns | 345.7 ns | 358.2 ns | 3,461,992.1 |  1.07 |    0.38 | 0.0600 | 0.0020 | 0.0020 |     506 B |        1.00 |
| TaskMailbox      | .NET 8.0  | 102.2 ns |  3.69 ns |  4.25 ns | 107.5533 ns | 103.3 ns | 107.1 ns | 9,785,353.0 |  0.38 |    0.10 | 0.0180 | 0.0010 | 0.0010 |     156 B |        0.31 |
| Channel          | .NET 8.0  | 137.5 ns |  4.10 ns |  4.39 ns | 149.0886 ns | 136.5 ns | 144.5 ns | 7,272,055.9 |  0.51 |    0.13 | 0.0010 |      - |      - |      14 B |        0.03 |
