namespace TaskMailbox.Benchmarks

open System
open System.Threading.Tasks
open BenchmarkDotNet.Attributes
open IcedTasks
open TaskMailbox
open TaskMailbox.Benchmarks.Agents

/// Sequential request/reply: N times, post a value and wait for the agent to reply with it.
[<BenchmarkCategory("PostAndAsyncReply")>]
type ReplyBenchmarks() =
    let n = 75000
    let mutable scope = new Scope()
    let mutable start: unit -> Task<int64> = fun () -> Task.FromResult 0L

    let run () =
        task {
            let! sum = bounded (start ())
            return checkSum n sum
        }

    [<IterationSetup(Target = "MailboxProcessor")>]
    member _.SetupMailboxProcessor() =
        scope <- new Scope()

        let agent =
            MailboxProcessor.Start(
                (fun (inbox: MailboxProcessor<int * AsyncReplyChannel<int>>) ->
                    async {
                        while true do
                            let! value, reply = inbox.Receive()
                            reply.Reply value
                    }
                ),
                scope.Token
            )

        scope.Add agent

        start <-
            fun () ->
                async {
                    let mutable sum = 0L

                    for i in 1..n do
                        let! reply = agent.PostAndAsyncReply(fun ch -> i, ch)

                        sum <-
                            sum
                            + int64 reply

                    return sum
                }
                |> Async.StartAsTask

    [<IterationSetup(Target = "TaskMailbox")>]
    member _.SetupTaskMailbox() =
        scope <- new Scope()
        let token = scope.Token

        let agent =
            TaskMailboxProcessor.Start(
                (fun (inbox: TaskMailboxProcessor<int * TaskReplyChannel<int>>) ->
                    cancellableTask {
                        while true do
                            let! value, reply = inbox.Receive()
                            reply.Reply value
                    }
                ),
                token
            )

        scope.Add agent

        start <-
            fun () ->
                let request =
                    cancellableTask {
                        let mutable sum = 0L

                        for i in 1..n do
                            let! reply = agent.PostAndAsyncReply(fun ch -> i, ch)

                            sum <-
                                sum
                                + int64 reply

                        return sum
                    }

                request token

    [<IterationSetup(Target = "Channel")>]
    member _.SetupChannel() =
        scope <- new Scope()

        let channel = newChannel<int * TaskCompletionSource<int>> ()

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
                            let mutable msg = Unchecked.defaultof<int * TaskCompletionSource<int>>

                            while reader.TryRead &msg do
                                let value, reply = msg
                                reply.SetResult value
                }
                :> Task
            )
        )

        start <-
            fun () ->
                task {
                    let mutable sum = 0L

                    for i in 1..n do
                        let reply =
                            TaskCompletionSource<int>(
                                TaskCreationOptions.RunContinuationsAsynchronously
                            )

                        channel.Writer.TryWrite((i, reply))
                        |> ignore

                        let! value = reply.Task

                        sum <-
                            sum
                            + int64 value

                    return sum
                }

    [<IterationCleanup>]
    member _.Cleanup() = (scope :> IDisposable).Dispose()

    [<Benchmark(Baseline = true, OperationsPerInvoke = 75000)>]
    member _.MailboxProcessor() = run ()

    [<Benchmark(OperationsPerInvoke = 75000)>]
    member _.TaskMailbox() = run ()

    [<Benchmark(OperationsPerInvoke = 75000)>]
    member _.Channel() = run ()
