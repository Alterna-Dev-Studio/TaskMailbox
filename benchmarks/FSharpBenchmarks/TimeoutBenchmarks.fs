namespace TaskMailbox.Benchmarks

open System
open System.Threading
open System.Threading.Tasks
open BenchmarkDotNet.Attributes
open IcedTasks
open TaskMailbox
open TaskMailbox.Benchmarks.Agents

/// An empty mailbox whose body waits on a gate, makes <c>N</c> attempts, and completes with how many
/// ended as expected (timed out, found nothing, or cancelled).
[<Sealed>]
type internal Driven(n: int) =
    let mutable scope = new Scope()
    let mutable gate = newGate ()
    let mutable finished = newDone ()

    member _.N = n
    member _.Scope = scope
    member _.Gate = gate
    member _.Finished = finished

    member _.Reset() =
        scope <- new Scope()
        gate <- newGate ()
        finished <- newDone ()

    /// Opens the gate and returns the count, checked against <c>N</c>.
    member _.Run() =
        task {
            gate.SetResult()
            let! count = bounded finished.Task
            return checkCount n count
        }

    member _.Dispose() = (scope :> IDisposable).Dispose()

module internal Driven =

    /// Each attempt returns whether it ended as expected.
    let mailboxProcessor (d: Driven) (attempt: MailboxProcessor<int> -> Async<bool>) =
        d.Reset()
        let gate, finished, n = d.Gate, d.Finished, d.N

        let agent =
            MailboxProcessor.Start(
                (fun inbox ->
                    async {
                        do! Async.AwaitTask gate.Task
                        let mutable count = 0L

                        for _ in 1..n do
                            let! ok = attempt inbox

                            if ok then
                                count <- count + 1L

                        finished.SetResult count
                    }
                ),
                d.Scope.Token
            )

        d.Scope.Add agent

    let taskMailbox (d: Driven) (attempt: TaskMailboxProcessor<int> -> CancellableTask<bool>) =
        d.Reset()
        let gate, finished, n = d.Gate, d.Finished, d.N

        let agent =
            TaskMailboxProcessor.Start(
                (fun inbox ->
                    cancellableTask {
                        do! gate.Task
                        let mutable count = 0L

                        for _ in 1..n do
                            let! ok = attempt inbox

                            if ok then
                                count <- count + 1L

                        finished.SetResult count
                    }
                ),
                d.Scope.Token
            )

        d.Scope.Add agent

    let channel (d: Driven) (attempt: Channels.ChannelReader<int> -> Task<bool>) =
        d.Reset()
        let gate, finished, n = d.Gate, d.Finished, d.N
        let reader = (newChannel ()).Reader

        d.Scope.Track(
            Task.Run(fun () ->
                task {
                    do! gate.Task
                    let mutable count = 0L

                    for _ in 1..n do
                        let! ok = attempt reader

                        if ok then
                            count <- count + 1L

                    finished.SetResult count
                }
                :> Task
            )
        )

/// N <c>TryReceive(1)</c>s on an empty mailbox, each ending at the timeout (the timer path). The bare
/// channel reads under a one millisecond cancellation timer.
[<BenchmarkCategory("Timeout.Elapsed")>]
type TimeoutElapsedBenchmarks() =
    let d = Driven 100

    [<IterationSetup(Target = "MailboxProcessor")>]
    member _.SetupMailboxProcessor() =
        Driven.mailboxProcessor
            d
            (fun inbox ->
                async {
                    let! r = inbox.TryReceive 1
                    return r.IsNone
                }
            )

    [<IterationSetup(Target = "TaskMailbox")>]
    member _.SetupTaskMailbox() =
        Driven.taskMailbox
            d
            (fun inbox ->
                cancellableTask {
                    let! r = inbox.TryReceive 1
                    return r.IsNone
                }
            )

    [<IterationSetup(Target = "Channel")>]
    member _.SetupChannel() =
        Driven.channel
            d
            (fun reader ->
                task {
                    use cts = new CancellationTokenSource(1)

                    try
                        let! _ = reader.ReadAsync(cts.Token).AsTask()
                        return false
                    with :? OperationCanceledException ->
                        return true
                }
            )

    [<IterationCleanup>]
    member _.Cleanup() = d.Dispose()

    [<Benchmark(Baseline = true, OperationsPerInvoke = 100)>]
    member _.MailboxProcessor() = d.Run()

    [<Benchmark(OperationsPerInvoke = 100)>]
    member _.TaskMailbox() = d.Run()

    [<Benchmark(OperationsPerInvoke = 100)>]
    member _.Channel() = d.Run()

/// N <c>TryReceive(0)</c>s on an empty mailbox: the immediate path, no timer. The bare channel uses
/// <c>TryRead</c>.
[<BenchmarkCategory("Timeout.Zero")>]
type TimeoutZeroBenchmarks() =
    let d = Driven 2500000

    [<IterationSetup(Target = "MailboxProcessor")>]
    member _.SetupMailboxProcessor() =
        Driven.mailboxProcessor
            d
            (fun inbox ->
                async {
                    let! r = inbox.TryReceive 0
                    return r.IsNone
                }
            )

    [<IterationSetup(Target = "TaskMailbox")>]
    member _.SetupTaskMailbox() =
        Driven.taskMailbox
            d
            (fun inbox ->
                cancellableTask {
                    let! r = inbox.TryReceive 0
                    return r.IsNone
                }
            )

    [<IterationSetup(Target = "Channel")>]
    member _.SetupChannel() =
        Driven.channel
            d
            (fun reader ->
                let mutable msg = 0
                Task.FromResult(not (reader.TryRead &msg))
            )

    [<IterationCleanup>]
    member _.Cleanup() = d.Dispose()

    [<Benchmark(Baseline = true, OperationsPerInvoke = 2500000)>]
    member _.MailboxProcessor() = d.Run()

    [<Benchmark(OperationsPerInvoke = 2500000)>]
    member _.TaskMailbox() = d.Run()

    [<Benchmark(OperationsPerInvoke = 2500000)>]
    member _.Channel() = d.Run()

/// N pending <c>Receive</c>s on an empty mailbox, each run on a fresh caller token that is then cancelled.
[<BenchmarkCategory("Cancellation")>]
type CancellationBenchmarks() =
    let d = Driven 18000

    [<IterationSetup(Target = "MailboxProcessor")>]
    member _.SetupMailboxProcessor() =
        Driven.mailboxProcessor
            d
            (fun inbox ->
                async {
                    use cts = new CancellationTokenSource()

                    let receive =
                        Async.StartImmediateAsTask(inbox.Receive(), cancellationToken = cts.Token)

                    cts.Cancel()
                    // Awaiting a cancelled task in async would cancel the body, so observe it from outside.
                    return!
                        receive.ContinueWith(fun (t: Task<int>) -> t.IsCanceled)
                        |> Async.AwaitTask
                }
            )

    [<IterationSetup(Target = "TaskMailbox")>]
    member _.SetupTaskMailbox() =
        Driven.taskMailbox
            d
            (fun inbox ->
                cancellableTask {
                    use cts = new CancellationTokenSource()
                    let receive = inbox.Receive () cts.Token
                    cts.Cancel()

                    try
                        let! _ = receive
                        return false
                    with :? OperationCanceledException ->
                        return true
                }
            )

    [<IterationSetup(Target = "Channel")>]
    member _.SetupChannel() =
        Driven.channel
            d
            (fun reader ->
                task {
                    use cts = new CancellationTokenSource()
                    let read = reader.ReadAsync(cts.Token).AsTask()
                    cts.Cancel()

                    try
                        let! _ = read
                        return false
                    with :? OperationCanceledException ->
                        return true
                }
            )

    [<IterationCleanup>]
    member _.Cleanup() = d.Dispose()

    [<Benchmark(Baseline = true, OperationsPerInvoke = 18000)>]
    member _.MailboxProcessor() = d.Run()

    [<Benchmark(OperationsPerInvoke = 18000)>]
    member _.TaskMailbox() = d.Run()

    [<Benchmark(OperationsPerInvoke = 18000)>]
    member _.Channel() = d.Run()
