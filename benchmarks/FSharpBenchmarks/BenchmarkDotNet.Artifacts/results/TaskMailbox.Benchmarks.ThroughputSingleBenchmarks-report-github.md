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
| MailboxProcessor | .NET 10.0 | 306.8 ns | 37.68 ns | 43.39 ns | 351.2483 ns | 325.9 ns | 349.8 ns | 3,259,607.4 |  1.02 |    0.21 | 0.0570 | 0.0020 | 0.0020 |     482 B |        1.00 |
| TaskMailbox      | .NET 10.0 | 242.8 ns |  4.39 ns |  4.88 ns | 249.9377 ns | 244.5 ns | 248.4 ns | 4,118,883.2 |  0.81 |    0.12 | 0.0410 | 0.0010 | 0.0010 |     348 B |        0.72 |
| Channel          | .NET 10.0 | 135.3 ns |  2.77 ns |  3.19 ns | 142.4641 ns | 134.9 ns | 140.7 ns | 7,392,176.2 |  0.45 |    0.07 | 0.0010 |      - |      - |      13 B |        0.03 |
|                  |           |          |          |          |             |          |          |             |       |         |        |        |        |           |             |
| MailboxProcessor | .NET 8.0  | 298.4 ns | 59.11 ns | 68.07 ns | 359.3770 ns | 343.1 ns | 353.7 ns | 3,351,553.9 |  1.06 |    0.37 | 0.0600 | 0.0020 | 0.0020 |     506 B |        1.00 |
| TaskMailbox      | .NET 8.0  | 184.1 ns |  4.96 ns |  5.71 ns | 196.2002 ns | 182.9 ns | 193.9 ns | 5,431,100.1 |  0.65 |    0.17 | 0.0460 | 0.0010 | 0.0010 |     396 B |        0.78 |
| Channel          | .NET 8.0  | 131.7 ns |  3.75 ns |  4.32 ns | 140.2399 ns | 131.4 ns | 140.1 ns | 7,593,312.9 |  0.47 |    0.12 | 0.0010 |      - |      - |      12 B |        0.02 |
