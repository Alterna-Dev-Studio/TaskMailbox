```

BenchmarkDotNet v0.15.8, Linux Ubuntu 26.04.1 LTS (Resolute Raccoon)
12th Gen Intel Core i5-12450H 2.50GHz, 1 CPU, 12 logical and 12 physical cores
.NET SDK 10.0.404
  [Host]     : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3 DEBUG
  Job-COFKQO : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v3
  Job-DHKZFF : .NET 8.0.34 (8.0.34, 8.0.3426.52217), X64 RyuJIT x86-64-v3

InvocationCount=1  IterationCount=20  UnrollFactor=1  
WarmupCount=3  Categories=Cancellation  

```
| Method           | Runtime   | Mean      | Error     | StdDev    | P99        | P50       | P95       | Op/s      | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------- |---------- |----------:|----------:|----------:|-----------:|----------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| MailboxProcessor | .NET 10.0 | 18.105 μs | 0.5175 μs | 0.5315 μs | 19.3661 us | 17.988 μs | 19.238 μs |  55,234.2 |  1.00 |    0.04 | 0.3333 |   3.06 KB |        1.00 |
| TaskMailbox      | .NET 10.0 | 10.773 μs | 0.3824 μs | 0.4251 μs | 11.5818 us | 10.787 μs | 11.398 μs |  92,828.1 |  0.60 |    0.03 | 0.3333 |   3.04 KB |        0.99 |
| Channel          | .NET 10.0 |  5.724 μs | 0.3917 μs | 0.4191 μs |  6.7905 us |  5.528 μs |  6.366 μs | 174,700.6 |  0.32 |    0.02 | 0.1667 |   1.65 KB |        0.54 |
|                  |           |           |           |           |            |           |           |           |       |         |        |           |             |
| MailboxProcessor | .NET 8.0  | 19.456 μs | 0.6512 μs | 0.7500 μs | 20.7009 us | 19.541 μs | 20.449 μs |  51,398.2 |  1.00 |    0.05 | 0.3333 |   3.15 KB |        1.00 |
| TaskMailbox      | .NET 8.0  | 39.238 μs | 0.5901 μs | 0.6795 μs | 40.8084 us | 38.945 μs | 40.453 μs |  25,485.3 |  2.02 |    0.08 | 0.3889 |   3.44 KB |        1.09 |
| Channel          | .NET 8.0  | 19.079 μs | 0.9238 μs | 1.0639 μs | 20.9310 us | 19.057 μs | 20.581 μs |  52,413.4 |  0.98 |    0.07 | 0.2222 |   2.13 KB |        0.68 |
