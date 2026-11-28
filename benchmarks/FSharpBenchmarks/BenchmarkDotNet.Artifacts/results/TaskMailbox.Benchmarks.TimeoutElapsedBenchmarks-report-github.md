```

BenchmarkDotNet v0.15.8, Linux Ubuntu 26.04.1 LTS (Resolute Raccoon)
12th Gen Intel Core i5-12450H 2.50GHz, 1 CPU, 12 logical and 12 physical cores
.NET SDK 10.0.404
  [Host]     : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3 DEBUG
  Job-COFKQO : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3
  Job-DHKZFF : .NET 8.0.34 (8.0.34, 8.0.3426.52217), X64 RyuJIT x86-64-v3

InvocationCount=1  IterationCount=20  UnrollFactor=1  
WarmupCount=3  Categories=Timeout.Elapsed  

```
| Method           | Runtime   | Mean     | Error     | StdDev    | P99       | P50      | P95      | Op/s  | Ratio | RatioSD | Allocated | Alloc Ratio |
|----------------- |---------- |---------:|----------:|----------:|----------:|---------:|---------:|------:|------:|--------:|----------:|------------:|
| MailboxProcessor | .NET 10.0 | 1.126 ms | 0.0118 ms | 0.0136 ms | 1.1502 ms | 1.126 ms | 1.144 ms | 888.1 |  1.00 |    0.02 |   1.61 KB |        1.00 |
| TaskMailbox      | .NET 10.0 | 1.141 ms | 0.0151 ms | 0.0148 ms | 1.1629 ms | 1.142 ms | 1.161 ms | 876.6 |  1.01 |    0.02 |   2.02 KB |        1.25 |
| Channel          | .NET 10.0 | 1.143 ms | 0.0128 ms | 0.0147 ms | 1.1667 ms | 1.146 ms | 1.162 ms | 874.7 |  1.02 |    0.02 |   1.75 KB |        1.09 |
|                  |           |          |           |           |           |          |          |       |       |         |           |             |
| MailboxProcessor | .NET 8.0  | 1.135 ms | 0.0127 ms | 0.0146 ms | 1.1594 ms | 1.136 ms | 1.157 ms | 881.2 |  1.00 |    0.02 |   1.63 KB |        1.00 |
| TaskMailbox      | .NET 8.0  | 1.146 ms | 0.0124 ms | 0.0127 ms | 1.1717 ms | 1.144 ms | 1.164 ms | 872.3 |  1.01 |    0.02 |   2.54 KB |        1.56 |
| Channel          | .NET 8.0  | 1.163 ms | 0.0207 ms | 0.0238 ms | 1.1923 ms | 1.170 ms | 1.192 ms | 859.8 |  1.03 |    0.02 |   2.23 KB |        1.37 |
