namespace TaskMailbox.Tests

open System
open System.Threading
open System.Threading.Tasks
open Expecto
open IcedTasks
open TaskMailbox

module MailboxBuilderTests =
    open TestHelpers

    let waitTime = 5000

    let rec spin n : CancellableTask<int> =
        mailbox { if n = 0 then return 42 else return! spin (n - 1) }

    let rec countdown (order: ResizeArray<string>) n : CancellableTask<int> =
        mailbox {
            do! Task.Yield()
            order.Add $"step{n}"

            if n = 0 then return 0 else return! countdown order (n - 1)
        }

    [<Tests>]
    let mailboxBuilderTests =
        testList "MailboxBuilder" [
            testList "ReturnFrom" [
                testCaseTask "Tail calls do not grow the stack"
                <| fun () ->
                    task {
                        let! result = spin 1_000_000 CancellationToken.None
                        Expect.equal result 42 "Should run 1,000,000 synchronous tail calls"
                    }

                testCaseTask "Can ReturnFrom CancellableTask"
                <| fun () ->
                    task {
                        let inner = cancellableTask { return 7 }
                        let outer = mailbox { return! inner }
                        let! result = outer CancellationToken.None
                        Expect.equal result 7 "Should return the cancellableTask's value"
                    }
            ]

            testList "Binds" [
                testCaseTask "Can Bind Task, ValueTask, Async, and the CancellationToken"
                <| fun () ->
                    task {
                        use cts = new CancellationTokenSource()

                        let binds =
                            mailbox {
                                let! a = Task.FromResult 1
                                let! b = ValueTask<int>(2)
                                let! c = async { return 3 }
                                do! Task.CompletedTask
                                let! ct = CancellableTask.getCancellationToken ()
                                return a + b + c, ct
                            }

                        let! result, token = binds cts.Token

                        Expect.equal result 6 "Should bind each value"
                        Expect.equal token cts.Token "Should flow the token"
                    }
            ]

            testList "TryWith" [
                testCaseTask "Catches an exception from a tail-called computation"
                <| fun () ->
                    task {
                        let failsLater =
                            mailbox {
                                do! Task.Yield()
                                failwith "late"
                                return "unreachable"
                            }

                        let guarded =
                            mailbox {
                                try
                                    return! failsLater
                                with e ->
                                    return
                                        "caught "
                                        + e.Message
                            }

                        let! result = guarded CancellationToken.None

                        Expect.equal result "caught late" "Should catch inside the try"
                    }
            ]

            testList "TryFinally" [
                testCaseTask "Runs after the tail-called chain"
                <| fun () ->
                    task {
                        let order = ResizeArray()

                        let outer =
                            mailbox {
                                try
                                    return! countdown order 2
                                finally
                                    order.Add "finally"
                            }

                        let! result = outer CancellationToken.None

                        Expect.equal result 0 "Should return the chain's value"

                        Expect.sequenceEqual
                            order
                            [
                                "step2"
                                "step1"
                                "step0"
                                "finally"
                            ]
                            "Should run finally last"
                    }
            ]

            testList "mailbox { } agents" [
                testCaseTask "Recursive loop drains a pre-queued backlog in order"
                <| fun () ->
                    task {
                        let n = 100000
                        let received = ResizeArray()
                        let completed = tcs<unit> ()

                        use mb =
                            new TaskMailboxProcessor<int>(fun inbox ->
                                let rec loop () =
                                    mailbox {
                                        let! msg = inbox.Receive()
                                        received.Add msg

                                        if msg = n - 1 then
                                            completed.SetResult()

                                        return! loop ()
                                    }

                                loop ()
                            )

                        for i in 0 .. n - 1 do
                            mb.Post i

                        mb.Start()
                        do! Expect.completesWithin waitTime completed.Task "Should receive all"

                        Expect.equal
                            (List.ofSeq received)
                            [ 0 .. n - 1 ]
                            "Should receive in arrival order"
                    }

                testCase "StartImmediate runs tail-called iterations inline"
                <| fun () ->
                    let mutable steps = 0

                    use mb =
                        new TaskMailboxProcessor<int>(fun inbox ->
                            let rec loop n =
                                mailbox {
                                    steps <- n

                                    if n < 5 then
                                        return! loop (n + 1)
                                    else
                                        let! (_: int) = inbox.Receive()
                                        ()
                                }

                            loop 0
                        )

                    mb.StartImmediate()
                    Expect.equal steps 5 "Should run until the first Receive"

                testCaseTask "Cancellation ends a recursive loop"
                <| fun () ->
                    task {
                        let received = tcs<unit> ()
                        let disposed = tcs<unit> ()
                        use cts = new CancellationTokenSource()

                        let mb =
                            new TaskMailboxProcessor<int>(
                                (fun inbox ->
                                    let rec loop () =
                                        mailbox {
                                            let! (_: int) = inbox.Receive()

                                            received.TrySetResult()
                                            |> ignore

                                            return! loop ()
                                        }

                                    mailbox {
                                        use _ = makeDisposable disposed.SetResult
                                        return! loop ()
                                    }
                                ),
                                cts.Token
                            )

                        mb.Start()
                        mb.Post 1
                        do! Expect.completesWithin waitTime received.Task "Should receive"
                        cts.Cancel()
                        do! Expect.completesWithin waitTime disposed.Task "Should end and dispose"
                    }

                testCaseTask
                    "Recursive loop does not retain memory per message"
                    (fun () ->
                        task {
                            let n = 200000
                            let batch = 100
                            let count = ref 0

                            use mb =
                                new TaskMailboxProcessor<int>(fun inbox ->
                                    let rec loop () =
                                        mailbox {
                                            let! (_: int) = inbox.Receive()

                                            Interlocked.Increment(&count.contents)
                                            |> ignore

                                            return! loop ()
                                        }

                                    loop ()
                                )

                            mb.Start()

                            let postBatch start =
                                for i in
                                    start .. start
                                             + batch
                                             - 1 do
                                    mb.Post i

                                let reachedBatch =
                                    SpinWait.SpinUntil(
                                        (fun () ->
                                            Volatile.Read(&count.contents) = start
                                                                             + batch
                                        ),
                                        waitTime
                                    )

                                Expect.isTrue reachedBatch "Should process the batch"

                            postBatch 0
                            let before = GC.GetTotalMemory true

                            for start in batch..batch .. n - 1 do
                                postBatch start

                            let retained =
                                GC.GetTotalMemory true
                                - before

                            Expect.isLessThan
                                retained
                                (int64 n
                                 * 20L)
                                $"Should not retain memory per message, retained {retained} bytes"
                        }
                    )
                |> testSequenced
            ]
        ]
