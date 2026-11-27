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

            if n = 0 then return 42 else return! countdown order (n - 1)
        }

    let step (order: ResizeArray<string>) (tag: string) : CancellableTask<unit> =
        mailbox {
            do! Task.Yield()
            order.Add tag
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

                        Expect.equal result 42 "Should return the chain's value"

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

            testList "Combine" [
                testCaseTask "return! followed by more code runs that code after the callee"
                <| fun () ->
                    task {
                        let order = ResizeArray()

                        let outer =
                            mailbox {
                                return! step order "first"
                                order.Add "second"
                            }

                        do!
                            Expect.completesWithin
                                waitTime
                                (outer CancellationToken.None)
                                "Should complete"

                        Expect.sequenceEqual
                            order
                            [
                                "first"
                                "second"
                            ]
                            "Should flush the callee before running the code after it"
                    }
            ]

            testList "While" [
                testCaseTask "return! in a while body runs before the next iteration"
                <| fun () ->
                    task {
                        let order = ResizeArray()
                        let mutable i = 0

                        let outer =
                            mailbox {
                                while i < 3 do
                                    i <- i + 1
                                    return! step order $"iter{i}"
                            }

                        do!
                            Expect.completesWithin
                                waitTime
                                (outer CancellationToken.None)
                                "Should complete"

                        Expect.sequenceEqual
                            order
                            [
                                "iter1"
                                "iter2"
                                "iter3"
                            ]
                            "Should flush each iteration's tail call before the next"
                    }
            ]

            testList "For" [
                testCaseTask "return! in a for body runs per element"
                <| fun () ->
                    task {
                        let order = ResizeArray()

                        let outer =
                            mailbox {
                                for i in 1..3 do
                                    return! step order $"elem{i}"
                            }

                        do!
                            Expect.completesWithin
                                waitTime
                                (outer CancellationToken.None)
                                "Should complete"

                        Expect.sequenceEqual
                            order
                            [
                                "elem1"
                                "elem2"
                                "elem3"
                            ]
                            "Should flush each element's tail call before the next"
                    }
            ]

            testList "Using" [
                testCaseTask "use is disposed after the tail-called chain"
                <| fun () ->
                    task {
                        let order = ResizeArray()

                        let outer =
                            mailbox {
                                use _ = makeDisposable (fun () -> order.Add "disposed")
                                return! step order "body"
                            }

                        do!
                            Expect.completesWithin
                                waitTime
                                (outer CancellationToken.None)
                                "Should complete"

                        Expect.sequenceEqual
                            order
                            [
                                "body"
                                "disposed"
                            ]
                            "Should dispose after the tail-called chain"
                    }
            ]

            testList "Cancellation" [
                testCaseTask
                    "An already-cancelled token faults with OperationCanceledException and runs nothing"
                <| fun () ->
                    let count = ref 0

                    let rec loop n : CancellableTask<unit> =
                        mailbox {
                            Interlocked.Increment(&count.contents)
                            |> ignore

                            if n > 0 then
                                return! loop (n - 1)
                        }

                    task {
                        do!
                            Expect.completesWithin
                                waitTime
                                (Expect.throwsTaskT<OperationCanceledException>
                                    (fun () -> loop 3 (CancellationToken(true)) :> Task)
                                    "Should fault with OperationCanceledException")
                                "Should complete"

                        Expect.equal count.contents 0 "Should run nothing"
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

                        Expect.isFalse
                            disposed.Task.IsCompleted
                            "Should not dispose before cancellation"

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
