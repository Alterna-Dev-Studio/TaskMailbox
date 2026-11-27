namespace TaskMailbox.Benchmarks

open BenchmarkDotNet.Columns
open BenchmarkDotNet.Configs
open BenchmarkDotNet.Diagnosers
open BenchmarkDotNet.Environments
open BenchmarkDotNet.Exporters
open BenchmarkDotNet.Exporters.Json
open BenchmarkDotNet.Jobs
open BenchmarkDotNet.Reports
open BenchmarkDotNet.Running
open Perfolizer.Horology

/// BenchmarkDotNet ships no P99 column.
type P99Column() =
    interface IColumn with
        member _.Id = "P99"
        member _.ColumnName = "P99"
        member _.Legend = "99th percentile"
        member _.Category = ColumnCategory.Statistics
        member _.PriorityInCategory = 0
        member _.AlwaysShow = true
        member _.IsNumeric = true
        member _.UnitType = UnitType.Time
        member _.IsDefault(_, _) = false
        member _.IsAvailable _ = true

        member x.GetValue(summary: Summary, benchmarkCase: BenchmarkCase) =
            (x :> IColumn).GetValue(summary, benchmarkCase, summary.Style)

        member _.GetValue(summary: Summary, benchmarkCase: BenchmarkCase, style: SummaryStyle) =
            match summary[benchmarkCase] with
            | null -> "NA"
            | report when isNull report.ResultStatistics -> "NA"
            | report ->
                let ns = report.ResultStatistics.Percentiles.Percentile 99

                let value =
                    TimeUnit
                        .Convert(ns, TimeUnit.Nanosecond, style.TimeUnit)
                        .ToString("N4", style.CultureInfo)

                if style.PrintUnitsInContent then
                    value
                    + " "
                    + style.TimeUnit.AbbreviationAscii
                else
                    value

/// The shared configuration: net8.0 and net10.0 jobs, or with <c>quick</c> a single dry run on the host
/// runtime. Both run one invocation per iteration so <c>IterationSetup</c> works.
type MailboxConfig(quick: bool) as this =
    inherit ManualConfig()

    do
        this.Add(DefaultConfig.Instance)
        |> ignore

        if quick then
            this.AddJob(Job.Dry)
            |> ignore
        else
            let job (runtime: Runtime) =
                Job.Default
                    .WithRuntime(runtime)
                    .WithInvocationCount(1)
                    .WithUnrollFactor(1)
                    .WithWarmupCount(3)
                    .WithIterationCount(20)

            this.AddJob(job CoreRuntime.Core80, job CoreRuntime.Core10_0)
            |> ignore

        this
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddColumn(
                StatisticColumn.OperationsPerSecond,
                StatisticColumn.P50,
                StatisticColumn.P95,
                P99Column(),
                BaselineRatioColumn.RatioMean,
                CategoriesColumn.Default
            )
            .HideColumns(StatisticColumn.Median)
            .AddExporter(JsonExporter.FullCompressed)
            .AddLogicalGroupRules(BenchmarkLogicalGroupRule.ByCategory)
        |> ignore
