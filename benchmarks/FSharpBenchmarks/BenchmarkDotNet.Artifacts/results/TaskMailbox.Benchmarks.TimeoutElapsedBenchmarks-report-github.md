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
| MailboxProcessor | .NET 10.0 | 1.153 ms | 0.0247 ms | 0.0284 ms | 1.2075 ms | 1.151 ms | 1.195 ms | 867.1 |  1.00 |    0.03 |   1.61 KB |        1.00 |
| TaskMailbox      | .NET 10.0 | 1.164 ms | 0.0186 ms | 0.0214 ms | 1.2011 ms | 1.160 ms | 1.197 ms | 859.1 |  1.01 |    0.03 |   1.27 KB |        0.79 |
| Channel          | .NET 10.0 | 1.180 ms | 0.0225 ms | 0.0260 ms | 1.2320 ms | 1.175 ms | 1.224 ms | 847.1 |  1.02 |    0.03 |   1.75 KB |        1.09 |
|                  |           |          |           |           |           |          |          |       |       |         |           |             |
| MailboxProcessor | .NET 8.0  | 1.153 ms | 0.0218 ms | 0.0214 ms | 1.1836 ms | 1.146 ms | 1.180 ms | 867.3 |  1.00 |    0.03 |   1.63 KB |        1.00 |
| TaskMailbox      | .NET 8.0  | 1.142 ms | 0.0159 ms | 0.0170 ms | 1.1715 ms | 1.137 ms | 1.170 ms | 875.8 |  0.99 |    0.02 |   1.37 KB |        0.84 |
| Channel          | .NET 8.0  | 1.193 ms | 0.0281 ms | 0.0324 ms | 1.2526 ms | 1.183 ms | 1.243 ms | 838.1 |  1.04 |    0.03 |   2.23 KB |        1.37 |
