namespace TaskMailbox.Benchmarks

open System.Threading.Tasks
open BenchmarkDotNet.Attributes
open IcedTasks
open TaskMailbox
open TaskMailbox.Benchmarks.Agents

/// Agents that wait on the gate, sum N received messages and complete <c>finished</c>, differing only in
/// body style. Each starts the agent in the scope and returns the function that posts to it.
module internal Bodies =

    let mailboxProcessorRecursive
        (scope: Scope)
        n
        (gate: TaskCompletionSource<unit>)
        (finished: TaskCompletionSource<int64>)
        =
        let agent =
            MailboxProcessor.Start(
                (fun inbox ->
                    let rec loop count sum =
                        async {
                            if count = n then
                                finished.SetResult sum
                            else
                                let! msg = inbox.Receive()

                                return!
                                    loop
                                        (count + 1)
                                        (sum
                                         + int64 msg)
                        }

                    async {
                        do! Async.AwaitTask gate.Task
                        return! loop 0 0L
                    }
                ),
                scope.Token
            )

        scope.Add agent
        agent.Post

    let mailboxProcessorWhile
        (scope: Scope)
        n
        (gate: TaskCompletionSource<unit>)
        (finished: TaskCompletionSource<int64>)
        =
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
        agent.Post

    let taskMailboxWhile
        (scope: Scope)
        n
        (gate: TaskCompletionSource<unit>)
        (finished: TaskCompletionSource<int64>)
        =
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
        agent.Post

    let taskMailboxRecursive
        (scope: Scope)
        n
        (gate: TaskCompletionSource<unit>)
        (finished: TaskCompletionSource<int64>)
        =
        let agent =
            TaskMailboxProcessor.Start(
                (fun inbox ->
                    let rec loop count sum =
                        mailbox {
                            if count = n then
                                finished.SetResult sum
                            else
                                let! msg = inbox.Receive()

                                return!
                                    loop
                                        (count + 1)
                                        (sum
                                         + int64 msg)
                        }

                    mailbox {
                        do! gate.Task
                        return! loop 0 0L
                    }
                ),
                scope.Token
            )

        scope.Add agent
        agent.Post

/// N messages queued before the gate opens, then drained by each body style.
[<BenchmarkCategory("BodyStyle.Queued")>]
type BodyStyleQueuedBenchmarks() =
    let n = 1000000
    let mutable scope = new Scope()
    let mutable gate = newGate ()
    let mutable finished = newDone ()

    let setup start =
        scope <- new Scope()
        gate <- newGate ()
        finished <- newDone ()
        let post: int -> unit = start scope n gate finished

        for i in 1..n do
            post i

    let run () =
        task {
            gate.SetResult()
            let! sum = bounded finished.Task
            return checkSum n sum
        }

    [<IterationSetup(Target = "MailboxProcessorRecursive")>]
    member _.SetupMailboxProcessorRecursive() = setup Bodies.mailboxProcessorRecursive

    [<IterationSetup(Target = "MailboxProcessorWhile")>]
    member _.SetupMailboxProcessorWhile() = setup Bodies.mailboxProcessorWhile

    [<IterationSetup(Target = "TaskMailboxWhile")>]
    member _.SetupTaskMailboxWhile() = setup Bodies.taskMailboxWhile

    [<IterationSetup(Target = "TaskMailboxRecursive")>]
    member _.SetupTaskMailboxRecursive() = setup Bodies.taskMailboxRecursive

    [<IterationCleanup>]
    member _.Cleanup() = (scope :> System.IDisposable).Dispose()

    [<Benchmark(Baseline = true, OperationsPerInvoke = 1000000)>]
    member _.MailboxProcessorRecursive() = run ()

    [<Benchmark(OperationsPerInvoke = 1000000)>]
    member _.MailboxProcessorWhile() = run ()

    [<Benchmark(OperationsPerInvoke = 1000000)>]
    member _.TaskMailboxWhile() = run ()

    [<Benchmark(OperationsPerInvoke = 1000000)>]
    member _.TaskMailboxRecursive() = run ()

/// One producer posts N messages while each body style consumes them concurrently.
[<BenchmarkCategory("BodyStyle.Throughput")>]
type BodyStyleThroughputBenchmarks() =
    let n = 1000000
    let mutable scope = new Scope()
    let mutable finished = newDone ()
    let mutable post: int -> unit = ignore

    let setup start =
        scope <- new Scope()
        let gate = newGate ()
        gate.SetResult()
        finished <- newDone ()
        post <- start scope n gate finished

    let run () =
        task {
            for i in 1..n do
                post i

            let! sum = bounded finished.Task
            return checkSum n sum
        }

    [<IterationSetup(Target = "MailboxProcessorRecursive")>]
    member _.SetupMailboxProcessorRecursive() = setup Bodies.mailboxProcessorRecursive

    [<IterationSetup(Target = "MailboxProcessorWhile")>]
    member _.SetupMailboxProcessorWhile() = setup Bodies.mailboxProcessorWhile

    [<IterationSetup(Target = "TaskMailboxWhile")>]
    member _.SetupTaskMailboxWhile() = setup Bodies.taskMailboxWhile

    [<IterationSetup(Target = "TaskMailboxRecursive")>]
    member _.SetupTaskMailboxRecursive() = setup Bodies.taskMailboxRecursive

    [<IterationCleanup>]
    member _.Cleanup() = (scope :> System.IDisposable).Dispose()

    [<Benchmark(Baseline = true, OperationsPerInvoke = 1000000)>]
    member _.MailboxProcessorRecursive() = run ()

    [<Benchmark(OperationsPerInvoke = 1000000)>]
    member _.MailboxProcessorWhile() = run ()

    [<Benchmark(OperationsPerInvoke = 1000000)>]
    member _.TaskMailboxWhile() = run ()

    [<Benchmark(OperationsPerInvoke = 1000000)>]
    member _.TaskMailboxRecursive() = run ()
