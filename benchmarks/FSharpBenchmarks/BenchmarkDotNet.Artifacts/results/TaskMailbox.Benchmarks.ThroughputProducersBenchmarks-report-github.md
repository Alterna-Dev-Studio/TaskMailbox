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
| MailboxProcessor | .NET 10.0 | 4         | 352.1 ns | 16.37 ns | 18.85 ns | 385.0949 ns | 353.1 ns | 382.0 ns | 2,839,899.4 |  1.00 |    0.08 | 0.0580 | 0.0020 | 0.0020 |     482 B |       1.000 |
| TaskMailbox      | .NET 10.0 | 4         | 188.4 ns |  8.44 ns |  9.72 ns | 203.7845 ns | 189.2 ns | 199.8 ns | 5,308,441.5 |  0.54 |    0.04 | 0.0150 |      - |      - |     129 B |       0.268 |
| Channel          | .NET 10.0 | 4         | 111.8 ns | 23.37 ns | 26.92 ns | 143.1398 ns | 115.3 ns | 142.7 ns | 8,941,037.6 |  0.32 |    0.08 |      - |      - |      - |       2 B |       0.004 |
|                  |           |           |          |          |          |             |          |          |             |       |         |        |        |        |           |             |
| MailboxProcessor | .NET 8.0  | 4         | 383.7 ns | 38.56 ns | 44.41 ns | 426.4875 ns | 394.4 ns | 419.1 ns | 2,606,030.7 |  1.02 |    0.20 | 0.0600 | 0.0020 | 0.0020 |     506 B |       1.000 |
| TaskMailbox      | .NET 8.0  | 4         | 188.9 ns |  8.38 ns |  9.65 ns | 210.8200 ns | 187.6 ns | 202.5 ns | 5,294,777.2 |  0.50 |    0.08 | 0.0180 |      - |      - |     158 B |       0.312 |
| Channel          | .NET 8.0  | 4         | 130.9 ns |  8.75 ns |  9.36 ns | 150.1705 ns | 132.2 ns | 147.0 ns | 7,641,710.2 |  0.35 |    0.06 |      - |      - |      - |       2 B |       0.004 |
