```

BenchmarkDotNet v0.15.8, Linux Ubuntu 26.04.1 LTS (Resolute Raccoon)
12th Gen Intel Core i5-12450H 2.50GHz, 1 CPU, 12 logical and 12 physical cores
.NET SDK 10.0.404
  [Host]     : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3 DEBUG
  Job-COFKQO : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3
  Job-DHKZFF : .NET 8.0.34 (8.0.34, 8.0.3426.52217), X64 RyuJIT x86-64-v3

InvocationCount=1  IterationCount=20  UnrollFactor=1  
WarmupCount=3  Categories=Throughput.Producers  

```
| Method           | Runtime   | Producers | Mean      | Error     | StdDev    | P99         | P50       | P95       | Op/s         | Ratio | RatioSD | Gen0   | Gen1   | Gen2   | Allocated | Alloc Ratio |
|----------------- |---------- |---------- |----------:|----------:|----------:|------------:|----------:|----------:|-------------:|------:|--------:|-------:|-------:|-------:|----------:|------------:|
| MailboxProcessor | .NET 10.0 | 4         | 334.68 ns | 11.938 ns | 13.269 ns | 359.7439 ns | 337.55 ns | 354.71 ns |  2,987,889.5 |  1.00 |    0.05 | 0.0580 | 0.0020 | 0.0020 |     482 B |       1.000 |
| TaskMailbox      | .NET 10.0 | 4         | 213.90 ns | 15.002 ns | 17.276 ns | 248.2690 ns | 209.49 ns | 234.58 ns |  4,675,013.6 |  0.64 |    0.06 | 0.0170 |      - |      - |     146 B |       0.303 |
| Channel          | .NET 10.0 | 4         |  74.25 ns |  8.111 ns |  9.341 ns |  91.6698 ns |  72.48 ns |  87.83 ns | 13,467,548.3 |  0.22 |    0.03 |      - |      - |      - |       1 B |       0.002 |
|                  |           |           |           |           |           |             |           |           |              |       |         |        |        |        |           |             |
| MailboxProcessor | .NET 8.0  | 4         | 375.70 ns |  9.748 ns | 11.225 ns | 398.8280 ns | 371.90 ns | 397.85 ns |  2,661,712.7 |  1.00 |    0.04 | 0.0600 | 0.0020 | 0.0020 |     506 B |       1.000 |
| TaskMailbox      | .NET 8.0  | 4         | 174.21 ns |  4.611 ns |  4.934 ns | 186.9257 ns | 173.30 ns | 181.62 ns |  5,740,299.6 |  0.46 |    0.02 | 0.0180 |      - |      - |     157 B |       0.310 |
| Channel          | .NET 8.0  | 4         |  87.21 ns | 13.662 ns | 15.734 ns | 117.5281 ns |  84.31 ns | 114.60 ns | 11,466,488.5 |  0.23 |    0.04 |      - |      - |      - |       2 B |       0.004 |
