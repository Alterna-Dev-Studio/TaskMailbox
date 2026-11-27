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
| Method           | Runtime   | Producers | Mean     | Error    | StdDev   | P99         | P50      | P95      | Op/s        | Ratio | RatioSD | Gen0   | Gen1   | Gen2   | Allocated | Alloc Ratio |
|----------------- |---------- |---------- |---------:|---------:|---------:|------------:|---------:|---------:|------------:|------:|--------:|-------:|-------:|-------:|----------:|------------:|
| MailboxProcessor | .NET 10.0 | 4         | 327.5 ns | 27.86 ns | 29.81 ns | 354.7208 ns | 335.4 ns | 353.1 ns | 3,053,248.8 |  1.01 |    0.14 | 0.0580 | 0.0020 | 0.0020 |     482 B |       1.000 |
| TaskMailbox      | .NET 10.0 | 4         | 280.7 ns |  5.95 ns |  6.86 ns | 293.9927 ns | 279.4 ns | 291.4 ns | 3,562,425.0 |  0.87 |    0.10 | 0.0410 | 0.0010 | 0.0010 |     348 B |       0.722 |
| Channel          | .NET 10.0 | 4         | 136.7 ns | 15.76 ns | 18.14 ns | 161.7331 ns | 139.0 ns | 157.7 ns | 7,315,615.3 |  0.42 |    0.07 |      - |      - |      - |       2 B |       0.004 |
|                  |           |           |          |          |          |             |          |          |             |       |         |        |        |        |           |             |
| MailboxProcessor | .NET 8.0  | 4         | 376.2 ns | 41.27 ns | 47.53 ns | 410.2907 ns | 394.6 ns | 407.3 ns | 2,657,862.7 |  1.02 |    0.21 | 0.0610 | 0.0020 | 0.0020 |     506 B |       1.000 |
| TaskMailbox      | .NET 8.0  | 4         | 225.8 ns |  6.35 ns |  7.06 ns | 238.2545 ns | 226.4 ns | 237.6 ns | 4,427,731.6 |  0.61 |    0.10 | 0.0460 | 0.0010 | 0.0010 |     396 B |       0.783 |
| Channel          | .NET 8.0  | 4         | 167.8 ns | 13.64 ns | 15.71 ns | 193.8782 ns | 166.2 ns | 192.2 ns | 5,959,839.1 |  0.45 |    0.08 |      - |      - |      - |       4 B |       0.008 |
