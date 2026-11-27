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
| MailboxProcessor | .NET 10.0 | 1.145 ms | 0.0154 ms | 0.0177 ms | 1.1768 ms | 1.145 ms | 1.172 ms | 873.3 |  1.00 |    0.02 |   1.61 KB |        1.00 |
| TaskMailbox      | .NET 10.0 | 1.168 ms | 0.0180 ms | 0.0207 ms | 1.1969 ms | 1.175 ms | 1.196 ms | 855.8 |  1.02 |    0.02 |    3.8 KB |        2.37 |
| Channel          | .NET 10.0 | 1.148 ms | 0.0162 ms | 0.0187 ms | 1.1850 ms | 1.143 ms | 1.174 ms | 871.3 |  1.00 |    0.02 |   1.75 KB |        1.09 |
|                  |           |          |           |           |           |          |          |       |       |         |           |             |
| MailboxProcessor | .NET 8.0  | 1.132 ms | 0.0119 ms | 0.0127 ms | 1.1516 ms | 1.136 ms | 1.151 ms | 883.2 |  1.00 |    0.02 |   1.63 KB |        1.00 |
| TaskMailbox      | .NET 8.0  | 1.171 ms | 0.0133 ms | 0.0153 ms | 1.1913 ms | 1.169 ms | 1.191 ms | 854.0 |  1.03 |    0.02 |   4.36 KB |        2.68 |
| Channel          | .NET 8.0  | 1.170 ms | 0.0298 ms | 0.0343 ms | 1.2334 ms | 1.160 ms | 1.229 ms | 854.4 |  1.03 |    0.03 |   2.23 KB |        1.37 |
