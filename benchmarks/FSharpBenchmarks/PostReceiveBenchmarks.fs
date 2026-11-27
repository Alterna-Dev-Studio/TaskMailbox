namespace TaskMailbox.Benchmarks

open System.Threading
open System.Threading.Tasks
open BenchmarkDotNet.Attributes
open IcedTasks
open TaskMailbox
open TaskMailbox.Benchmarks.Agents

/// Post one message and wait for the agent to receive it, N times: the cost of waking an idle agent.
[<BenchmarkCategory("PostReceive.Empty")>]
type PostReceiveEmptyBenchmarks() =
    let n = 60000
    let mutable scope = new Scope()
    let mutable sum = 0L
    let mutable received = new SemaphoreSlim(0)
    let mutable post: int -> unit = ignore

    let reset () =
        scope <- new Scope()
        sum <- 0L
        received <- new SemaphoreSlim(0)
        scope.Add received

    let run () =
        task {
            for i in 1..n do
                post i
                do! boundedUnit (received.WaitAsync())

            return checkSum n sum
        }

    [<IterationSetup(Target = "MailboxProcessor")>]
    member _.SetupMailboxProcessor() =
        reset ()
        let sem = received

        let agent =
            MailboxProcessor.Start(
                (fun inbox ->
                    async {
                        while true do
                            let! msg = inbox.Receive()

                            sum <-
                                sum
                                + int64 msg

                            sem.Release()
                            |> ignore
                    }
                ),
                scope.Token
            )

        scope.Add agent
        post <- agent.Post

    [<IterationSetup(Target = "TaskMailbox")>]
    member _.SetupTaskMailbox() =
        reset ()
        let sem = received

        let agent =
            TaskMailboxProcessor.Start(
                (fun inbox ->
                    cancellableTask {
                        while true do
                            let! msg = inbox.Receive()

                            sum <-
                                sum
                                + int64 msg

                            sem.Release()
                            |> ignore
                    }
                ),
                scope.Token
            )

        scope.Add agent
        post <- agent.Post

    [<IterationSetup(Target = "Channel")>]
    member _.SetupChannel() =
        reset ()
        let sem = received
        let channel = newChannel ()
        let reader = channel.Reader
        let token = scope.Token

        scope.Track(
            Task.Run(fun () ->
                task {
                    let mutable go = true

                    while go do
                        let! ready = reader.WaitToReadAsync token

                        if not ready then
                            go <- false
                        else
                            let mutable msg = 0

                            while reader.TryRead &msg do
                                sum <-
                                    sum
                                    + int64 msg

                                sem.Release()
                                |> ignore
                }
                :> Task
            )
        )

        post <-
            fun msg ->
                channel.Writer.TryWrite msg
                |> ignore

    [<IterationCleanup>]
    member _.Cleanup() = (scope :> System.IDisposable).Dispose()

    [<Benchmark(Baseline = true, OperationsPerInvoke = 60000)>]
    member _.MailboxProcessor() = run ()

    [<Benchmark(OperationsPerInvoke = 60000)>]
    member _.TaskMailbox() = run ()

    [<Benchmark(OperationsPerInvoke = 60000)>]
    member _.Channel() = run ()

/// Drain N messages that were queued before the agent started receiving: the cost of a receive that never waits.
[<BenchmarkCategory("PostReceive.Queued")>]
type PostReceiveQueuedBenchmarks() =
    let n = 6000000
    let mutable scope = new Scope()
    let mutable gate = newGate ()
    let mutable finished = newDone ()

    let reset () =
        scope <- new Scope()
        gate <- newGate ()
        finished <- newDone ()

    let run () =
        task {
            gate.SetResult()
            let! sum = bounded finished.Task
            return checkSum n sum
        }

    [<IterationSetup(Target = "MailboxProcessor")>]
    member _.SetupMailboxProcessor() =
        reset ()
        let gate, finished = gate, finished

        let agent =
            MailboxProcessor.Start(
                (fun inbox ->
                    async {
                        do! Async.AwaitTask gate.Task
                        let mutable sum = 0L
                        let mutable count = 0

                        while count < n do
                            let! msg = inbox.Receive()

                            sum <-
                                sum
                                + int64 msg

                            count <- count + 1

                        finished.SetResult sum
                    }
                ),
                scope.Token
            )

        scope.Add agent

        for i in 1..n do
            agent.Post i

    [<IterationSetup(Target = "TaskMailbox")>]
    member _.SetupTaskMailbox() =
        reset ()
        let gate, finished = gate, finished

        let agent =
            TaskMailboxProcessor.Start(
                (fun inbox ->
                    cancellableTask {
                        do! gate.Task
                        let mutable sum = 0L
                        let mutable count = 0

                        while count < n do
                            let! msg = inbox.Receive()

                            sum <-
                                sum
                                + int64 msg

                            count <- count + 1

                        finished.SetResult sum
                    }
                ),
                scope.Token
            )

        scope.Add agent

        for i in 1..n do
            agent.Post i

    [<IterationSetup(Target = "Channel")>]
    member _.SetupChannel() =
        reset ()
        let gate, finished = gate, finished
        let channel = newChannel ()
        let reader = channel.Reader
        let token = scope.Token

        for i in 1..n do
            channel.Writer.TryWrite i
            |> ignore

        scope.Track(
            Task.Run(fun () ->
                task {
                    do! gate.Task
                    let mutable sum = 0L
                    let mutable count = 0

                    while count < n do
                        let! ready = reader.WaitToReadAsync token

                        if ready then
                            let mutable msg = 0

                            while count < n
                                  && reader.TryRead &msg do
                                sum <-
                                    sum
                                    + int64 msg

                                count <- count + 1

                    finished.SetResult sum
                }
                :> Task
            )
        )

    [<IterationCleanup>]
    member _.Cleanup() = (scope :> System.IDisposable).Dispose()

    [<Benchmark(Baseline = true, OperationsPerInvoke = 6000000)>]
    member _.MailboxProcessor() = run ()

    [<Benchmark(OperationsPerInvoke = 6000000)>]
    member _.TaskMailbox() = run ()

    [<Benchmark(OperationsPerInvoke = 6000000)>]
    member _.Channel() = run ()
