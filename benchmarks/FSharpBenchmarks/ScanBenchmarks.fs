namespace TaskMailbox.Benchmarks

open BenchmarkDotNet.Attributes
open IcedTasks
open TaskMailbox
open TaskMailbox.Benchmarks.Agents

/// N <c>Scan</c>s that each take the next matching message after walking <c>Skipped</c> rejected ones.
/// The rejected messages stay queued, so every scan skips all of them. A bare channel has no selective
/// receive, so there is no Channel row.
[<BenchmarkCategory("Scan")>]
type ScanBenchmarks() =
    let n = 400000
    let mutable scope = new Scope()
    let mutable gate = newGate ()
    let mutable finished = newDone ()

    /// Number of non-matching messages queued ahead of the matching ones.
    [<Params(0, 10, 100, 1000)>]
    member val Skipped = 0 with get, set

    member private x.Reset() =
        scope <- new Scope()
        gate <- newGate ()
        finished <- newDone ()

    member private x.Run() =
        task {
            gate.SetResult()
            let! sum = bounded finished.Task
            return checkSum n sum
        }

    [<IterationSetup(Target = "MailboxProcessor")>]
    member x.SetupMailboxProcessor() =
        x.Reset()
        let gate, finished = gate, finished

        let agent =
            MailboxProcessor.Start(
                (fun inbox ->
                    async {
                        do! Async.AwaitTask gate.Task
                        let mutable sum = 0L

                        for _ in 1..n do
                            let! m =
                                inbox.Scan(fun m ->
                                    if m > 0 then Some(async { return m }) else None
                                )

                            sum <-
                                sum
                                + int64 m

                        finished.SetResult sum
                    }
                ),
                scope.Token
            )

        scope.Add agent

        for _ in 1 .. x.Skipped do
            agent.Post -1

        for i in 1..n do
            agent.Post i

    [<IterationSetup(Target = "TaskMailbox")>]
    member x.SetupTaskMailbox() =
        x.Reset()
        let gate, finished = gate, finished

        let agent =
            TaskMailboxProcessor.Start(
                (fun inbox ->
                    cancellableTask {
                        do! gate.Task
                        let mutable sum = 0L

                        for _ in 1..n do
                            let! m =
                                inbox.Scan(fun m ->
                                    if m > 0 then Some(cancellableTask { return m }) else None
                                )

                            sum <-
                                sum
                                + int64 m

                        finished.SetResult sum
                    }
                ),
                scope.Token
            )

        scope.Add agent

        for _ in 1 .. x.Skipped do
            agent.Post -1

        for i in 1..n do
            agent.Post i

    [<IterationCleanup>]
    member _.Cleanup() = (scope :> System.IDisposable).Dispose()

    [<Benchmark(Baseline = true, OperationsPerInvoke = 400000)>]
    member x.MailboxProcessor() = x.Run()

    [<Benchmark(OperationsPerInvoke = 400000)>]
    member x.TaskMailbox() = x.Run()
