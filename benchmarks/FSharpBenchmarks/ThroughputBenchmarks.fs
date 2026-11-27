namespace TaskMailbox.Benchmarks

open System.Threading.Tasks
open BenchmarkDotNet.Attributes
open IcedTasks
open TaskMailbox
open TaskMailbox.Benchmarks.Agents

/// Starts, in the scope, an agent that sums <c>n</c> received messages and completes <c>finished</c> with the
/// sum. Each returns the function that posts to it.
module private Consumers =

    let mailboxProcessor (scope: Scope) n (finished: TaskCompletionSource<int64>) =
        let agent =
            MailboxProcessor.Start(
                (fun inbox ->
                    async {
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
        agent.Post

    let taskMailbox (scope: Scope) n (finished: TaskCompletionSource<int64>) =
        let agent =
            TaskMailboxProcessor.Start(
                (fun inbox ->
                    cancellableTask {
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
        agent.Post

    let channel (scope: Scope) n (finished: TaskCompletionSource<int64>) =
        let channel = newChannel ()
        let reader = channel.Reader
        let token = scope.Token

        scope.Track(
            Task.Run(fun () ->
                task {
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

        fun msg ->
            channel.Writer.TryWrite msg
            |> ignore

/// One producer posts N messages while the agent consumes them concurrently.
[<BenchmarkCategory("Throughput.Single")>]
type ThroughputSingleBenchmarks() =
    let n = 1000000
    let mutable scope = new Scope()
    let mutable finished = newDone ()
    let mutable post: int -> unit = ignore

    let reset () =
        scope <- new Scope()
        finished <- newDone ()

    let run () =
        task {
            for i in 1..n do
                post i

            let! sum = bounded finished.Task
            return checkSum n sum
        }

    [<IterationSetup(Target = "MailboxProcessor")>]
    member _.SetupMailboxProcessor() =
        reset ()
        post <- Consumers.mailboxProcessor scope n finished

    [<IterationSetup(Target = "TaskMailbox")>]
    member _.SetupTaskMailbox() =
        reset ()
        post <- Consumers.taskMailbox scope n finished

    [<IterationSetup(Target = "Channel")>]
    member _.SetupChannel() =
        reset ()
        post <- Consumers.channel scope n finished

    [<IterationCleanup>]
    member _.Cleanup() = (scope :> System.IDisposable).Dispose()

    [<Benchmark(Baseline = true, OperationsPerInvoke = 1000000)>]
    member _.MailboxProcessor() = run ()

    [<Benchmark(OperationsPerInvoke = 1000000)>]
    member _.TaskMailbox() = run ()

    [<Benchmark(OperationsPerInvoke = 1000000)>]
    member _.Channel() = run ()

/// Several producers post disjoint shares of 1..N concurrently to one agent.
[<BenchmarkCategory("Throughput.Producers")>]
type ThroughputProducersBenchmarks() =
    let n = 1000000
    let mutable scope = new Scope()
    let mutable finished = newDone ()
    let mutable post: int -> unit = ignore

    /// Number of concurrent producers.
    [<Params(4)>]
    member val Producers = 1 with get, set

    member private x.Reset() =
        scope <- new Scope()
        finished <- newDone ()

    /// Producer <c>p</c> posts p+1, p+1+Producers, ...: the shares partition 1..N.
    member private x.Run() =
        task {
            let producers = x.Producers

            let tasks =
                Array.init
                    producers
                    (fun p ->
                        Task.Run(fun () ->
                            for i in 1 + p .. producers .. n do
                                post i
                        )
                    )

            do! boundedUnit (Task.WhenAll tasks)
            let! sum = bounded finished.Task
            return checkSum n sum
        }

    [<IterationSetup(Target = "MailboxProcessor")>]
    member x.SetupMailboxProcessor() =
        x.Reset()
        post <- Consumers.mailboxProcessor scope n finished

    [<IterationSetup(Target = "TaskMailbox")>]
    member x.SetupTaskMailbox() =
        x.Reset()
        post <- Consumers.taskMailbox scope n finished

    [<IterationSetup(Target = "Channel")>]
    member x.SetupChannel() =
        x.Reset()
        post <- Consumers.channel scope n finished

    [<IterationCleanup>]
    member _.Cleanup() = (scope :> System.IDisposable).Dispose()

    [<Benchmark(Baseline = true, OperationsPerInvoke = 1000000)>]
    member x.MailboxProcessor() = x.Run()

    [<Benchmark(OperationsPerInvoke = 1000000)>]
    member x.TaskMailbox() = x.Run()

    [<Benchmark(OperationsPerInvoke = 1000000)>]
    member x.Channel() = x.Run()
