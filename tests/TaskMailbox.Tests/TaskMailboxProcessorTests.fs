namespace TaskMailbox.Tests

open System
open System.Threading
open System.Threading.Tasks
open Expecto
open IcedTasks
open TaskMailbox

module TaskMailboxProcessorTests =
    open TestHelpers

    let waitTime = 5000

    let idle () =
        new TaskMailboxProcessor<int>(fun _ -> cancellableTask { () })

    let idleWithToken () =
        new TaskMailboxProcessor<int>((fun _ -> cancellableTask { () }), CancellationToken.None)

    /// A scanner that never matches, typed by use site.
    let neverMatches (_: int) : CancellableTask<'T> option = None

    /// Either a request built by `PostAndAsyncReply`, or a plain message posted directly.
    type RequestOrPost =
        | Request of TaskReplyChannel<int>
        | Post of string

    let label =
        function
        | Request _ -> "Request"
        | Post s -> s

    let replyIfRequest =
        function
        | Request r -> r.Reply 1
        | Post _ -> ()

    /// FSharp.Core ignores the caller's token here (mailbox.fs `waitOne`); TaskMailbox cancels.
    let cancelsInfiniteWait
        name
        (call: TaskMailboxProcessor<'Msg> -> CancellationToken -> Task<'T>)
        =
        testCaseTask
            $"Throws OperationCanceledException when the caller cancels an infinite wait ({name})"
        <| fun () ->
            task {
                use mb = new TaskMailboxProcessor<'Msg>(fun _ -> cancellableTask { () })
                use cts = new CancellationTokenSource()
                cts.CancelAfter 10

                do!
                    Expect.throwsTaskT<OperationCanceledException>
                        (fun () ->
                            Expect.completesWithin
                                waitTime
                                (call mb cts.Token)
                                "Should cancel an infinite wait"
                            :> Task
                        )
                        "Should cancel an infinite wait"
            }

    /// TaskMailbox-only: FSharp.Core's mailbox.fs `AsyncHelpers.timeout` passes
    /// `cancellationContinuation = ignore` to `awaitEither`, so a caller cancel under a finite
    /// timeout never completes there.
    let cancelsFiniteWait
        name
        (createMb: unit -> TaskMailboxProcessor<int>)
        (call: TaskMailboxProcessor<int> -> CancellationToken -> Task<'T>)
        =
        testCaseTask
            $"Throws OperationCanceledException when the caller cancels a finite wait ({name})"
        <| fun () ->
            task {
                use mb = createMb ()
                use cts = new CancellationTokenSource()
                cts.CancelAfter 10

                do!
                    Expect.throwsTaskT<OperationCanceledException>
                        (fun () ->
                            Expect.completesWithin
                                waitTime
                                (call mb cts.Token)
                                "Should cancel a finite wait"
                            :> Task
                        )
                        "Should cancel a finite wait"
            }

    [<Tests>]
    let taskMailboxProcessorTests =
        testList "TaskMailboxProcessor" [
            testList "Receive" [
                testCaseTask "Throws OperationCanceledException when the caller cancels"
                <| fun () ->
                    task {
                        use mb = idle ()
                        use cts = new CancellationTokenSource(10)

                        do!
                            Expect.throwsTaskT<OperationCanceledException>
                                (fun () -> mb.Receive waitTime cts.Token :> Task)
                                "Should cancel, not time out"
                    }

                testCaseTask "Throws InvalidOperationException when another Receive is waiting"
                <| fun () ->
                    task {
                        use mb = idle ()
                        let first = mb.Receive () CancellationToken.None

                        do!
                            Expect.throwsTaskT<InvalidOperationException>
                                (fun () -> mb.Receive () CancellationToken.None :> Task)
                                "Should allow one reader"

                        mb.Post 1
                        let! msg = first
                        Expect.equal msg 1 "First Receive should still receive"
                    }

                testCaseTask "Throws InvalidOperationException when a Scan is waiting"
                <| fun () ->
                    task {
                        use mb = idle ()
                        let first = mb.Receive () CancellationToken.None

                        do!
                            Expect.throwsTaskT<InvalidOperationException>
                                (fun () -> mb.Scan (neverMatches, 0) CancellationToken.None :> Task)
                                "Should allow one reader"

                        mb.Post 1
                        let! msg = first
                        Expect.equal msg 1 "First Receive should still receive"
                    }

                cancelsInfiniteWait "no timeout" (fun mb ct -> mb.Receive () ct)
                cancelsInfiniteWait "timeout -2" (fun mb ct -> mb.Receive (-2) ct)
            ]

            testList "TryReceive" [
                cancelsInfiniteWait "no timeout" (fun mb ct -> mb.TryReceive () ct)
                cancelsInfiniteWait "timeout -2" (fun mb ct -> mb.TryReceive (-2) ct)
            ]

            testList "Scan" [
                cancelsInfiniteWait "no timeout" (fun mb ct -> mb.Scan (neverMatches) ct)
                cancelsInfiniteWait "timeout -2" (fun mb ct -> mb.Scan (neverMatches, -2) ct)

                cancelsFiniteWait
                    "token-less agent"
                    idle
                    (fun mb ct -> mb.Scan (neverMatches, waitTime) ct)

                cancelsFiniteWait
                    "agent with a token"
                    idleWithToken
                    (fun mb ct -> mb.Scan (neverMatches, waitTime) ct)

                testCaseTask "Throws InvalidOperationException when a Receive is waiting"
                <| fun () ->
                    task {
                        use mb = idle ()

                        let scanTask =
                            (mb.Scan(fun m ->
                                if m = 5 then Some(cancellableTask { return m }) else None
                            ))
                                CancellationToken.None

                        do!
                            Expect.throwsTaskT<InvalidOperationException>
                                (fun () -> mb.Receive () CancellationToken.None :> Task)
                                "Should allow one reader"

                        mb.Post 5

                        let! scanned = Expect.completesWithin waitTime scanTask "Should scan"
                        Expect.equal scanned 5 "First Scan should still match the later message"
                    }

                testCaseTask "Releases the reader guard when Scan times out"
                <| fun () ->
                    task {
                        use mb = idle ()

                        do!
                            Expect.throwsTaskT<TimeoutException>
                                (fun () -> mb.Scan (neverMatches, 0) CancellationToken.None :> Task)
                                "Should time out"

                        mb.Post 1

                        let! msg =
                            Expect.completesWithin
                                waitTime
                                (mb.Receive () CancellationToken.None)
                                "Should receive"

                        Expect.equal msg 1 "Should receive after Scan times out"
                    }

                testCaseTask "Releases the reader guard when the scanner throws"
                <| fun () ->
                    task {
                        use mb = idle ()
                        let boom = Exception "boom"
                        mb.Post 1

                        do!
                            Expect.throwsTaskT<Exception>
                                (fun () ->
                                    mb.Scan
                                        ((fun (_: int) -> raise boom), 0)
                                        CancellationToken.None
                                    :> Task
                                )
                                "Should propagate"

                        mb.Post 2

                        let! msg =
                            Expect.completesWithin
                                waitTime
                                (mb.Receive () CancellationToken.None)
                                "Should receive"

                        Expect.equal msg 1 "Should receive after the scanner throws"
                    }

                testCaseTask "Keeps a newly arrived message the scanner throws on, in order"
                <| fun () ->
                    task {
                        use mb = idle ()
                        let boom = Exception "boom"

                        mb.Post 1
                        mb.Post 2

                        do!
                            Expect.throwsTaskT<Exception>
                                (fun () ->
                                    mb.Scan
                                        ((fun m -> if m = 1 then raise boom else None), 0)
                                        CancellationToken.None
                                    :> Task
                                )
                                "Should propagate the scanner's exception"

                        Expect.equal mb.CurrentQueueLength 2 "Should keep both messages"

                        // FSharp.Core loses the message here (mailbox.fs ScanArrivalsUnsafe dequeues
                        // before calling the scanner); TaskMailbox keeps it, intentionally differing.
                        let! r1 =
                            Expect.completesWithin
                                waitTime
                                (mb.Receive () CancellationToken.None)
                                "Should receive"

                        Expect.equal r1 1 "Should receive 1 first, still in order"

                        let! r2 =
                            Expect.completesWithin
                                waitTime
                                (mb.Receive () CancellationToken.None)
                                "Should receive"

                        Expect.equal r2 2 "Should receive 2"
                    }
            ]

            testList "TryScan" [
                cancelsInfiniteWait "no timeout" (fun mb ct -> mb.TryScan (neverMatches) ct)
                cancelsInfiniteWait "timeout -2" (fun mb ct -> mb.TryScan (neverMatches, -2) ct)

                cancelsFiniteWait
                    "token-less agent"
                    idle
                    (fun mb ct -> mb.TryScan (neverMatches, waitTime) ct)

                cancelsFiniteWait
                    "agent with a token"
                    idleWithToken
                    (fun mb ct -> mb.TryScan (neverMatches, waitTime) ct)
            ]

            testList "TaskReplyChannel" [
                testCase "A user-constructed channel passes every Reply through"
                <| fun () ->
                    let seen = ResizeArray<int>()
                    let channel = TaskReplyChannel<int>(seen.Add)

                    channel.Reply 1
                    channel.Reply 2

                    Expect.equal
                        (List.ofSeq seen)
                        [
                            1
                            2
                        ]
                        "Should see every reply"
            ]

            testList "Request/reply" [
                // The four request members, posting with `timeout` when given.
                let requestCalls
                    (timeout: int option)
                    : (string *
                      (TaskMailboxProcessor<TaskReplyChannel<int>>
                              -> (TaskReplyChannel<int> -> TaskReplyChannel<int>)
                              -> Task)) list
                    =
                    [
                        "PostAndTryAsyncReply",
                        (fun mb build ->
                            task {
                                let! _ =
                                    (mb.PostAndTryAsyncReply(build, ?timeout = timeout))
                                        CancellationToken.None

                                ()
                            }
                        )

                        "PostAndAsyncReply",
                        (fun mb build ->
                            task {
                                let! _ =
                                    (mb.PostAndAsyncReply(build, ?timeout = timeout))
                                        CancellationToken.None

                                ()
                            }
                        )

                        "TryPostAndReply",
                        (fun mb build ->
                            task {
                                do!
                                    Task.Run(fun () ->
                                        mb.TryPostAndReply(build, ?timeout = timeout)
                                        |> ignore
                                    )
                            }
                        )

                        "PostAndReply",
                        (fun mb build ->
                            task {
                                do!
                                    Task.Run(fun () ->
                                        mb.PostAndReply(build, ?timeout = timeout)
                                        |> ignore
                                    )
                            }
                        )
                    ]

                // Timeout -2 posts nothing: validated before buildMessage runs or Post is called.
                let calls = requestCalls (Some -2)

                // Only the async variants take a caller token.
                let asyncCalls
                    : (string *
                      (TaskMailboxProcessor<TaskReplyChannel<int>>
                              -> (TaskReplyChannel<int> -> TaskReplyChannel<int>)
                              -> CancellationToken
                              -> Task<unit>)) list = [
                    "PostAndTryAsyncReply",
                    (fun mb build ct ->
                        task {
                            let! _ = (mb.PostAndTryAsyncReply build) ct
                            ()
                        }
                    )

                    "PostAndAsyncReply",
                    (fun mb build ct ->
                        task {
                            let! _ = (mb.PostAndAsyncReply build) ct
                            ()
                        }
                    )
                ]

                testList "Timeout -2 posts nothing" [
                    for name, call in calls ->
                        testCaseTask name
                        <| fun () ->
                            task {
                                use mb =
                                    new TaskMailboxProcessor<TaskReplyChannel<int>>(fun _ ->
                                        cancellableTask { () }
                                    )

                                mb.Start()
                                let mutable builds = 0

                                let build (ch: TaskReplyChannel<int>) =
                                    builds <- builds + 1
                                    ch

                                do!
                                    Expect.throwsTaskT<ArgumentOutOfRangeException>
                                        (fun () ->
                                            Expect.completesWithin
                                                waitTime
                                                (task {
                                                    do! call mb build
                                                    return ()
                                                })
                                                "Should complete"
                                            :> Task
                                        )
                                        "Should throw ArgumentOutOfRangeException"

                                Expect.equal mb.CurrentQueueLength 0 "Should post nothing"
                                Expect.equal builds 0 "Should not call buildMessage"
                            }
                ]

                cancelsInfiniteWait
                    "PostAndTryAsyncReply, token-less agent"
                    (fun mb ct -> (mb.PostAndTryAsyncReply id) ct)

                cancelsInfiniteWait
                    "PostAndAsyncReply, token-less agent"
                    (fun mb ct -> (mb.PostAndAsyncReply id) ct)

                testList "An already-cancelled token posts nothing" [
                    for name, call in asyncCalls ->
                        testCaseTask name
                        <| fun () ->
                            task {
                                use mb =
                                    new TaskMailboxProcessor<TaskReplyChannel<int>>(fun _ ->
                                        cancellableTask { () }
                                    )

                                mb.Start()
                                let mutable builds = 0

                                let build (ch: TaskReplyChannel<int>) =
                                    builds <- builds + 1
                                    ch

                                use cts = new CancellationTokenSource()
                                cts.Cancel()

                                do!
                                    Expect.throwsTaskT<OperationCanceledException>
                                        (fun () ->
                                            Expect.completesWithin
                                                waitTime
                                                (call mb build cts.Token)
                                                "Should complete"
                                            :> Task
                                        )
                                        "Should throw OperationCanceledException"

                                Expect.equal mb.CurrentQueueLength 0 "Should post nothing"
                                Expect.equal builds 0 "Should not call buildMessage"
                            }
                ]

                testList "Creating the op posts nothing until it runs" [
                    testCase "PostAndAsyncReply"
                    <| fun () ->
                        use mb =
                            new TaskMailboxProcessor<TaskReplyChannel<int>>(fun _ ->
                                cancellableTask { () }
                            )

                        mb.Start()
                        let mutable builds = 0

                        let build (ch: TaskReplyChannel<int>) =
                            builds <- builds + 1
                            ch

                        mb.PostAndAsyncReply build
                        |> ignore

                        Expect.equal mb.CurrentQueueLength 0 "Should post nothing"
                        Expect.equal builds 0 "Should not call buildMessage"

                    testCase "PostAndTryAsyncReply"
                    <| fun () ->
                        use mb =
                            new TaskMailboxProcessor<TaskReplyChannel<int>>(fun _ ->
                                cancellableTask { () }
                            )

                        mb.Start()
                        let mutable builds = 0

                        let build (ch: TaskReplyChannel<int>) =
                            builds <- builds + 1
                            ch

                        mb.PostAndTryAsyncReply build
                        |> ignore

                        Expect.equal mb.CurrentQueueLength 0 "Should post nothing"
                        Expect.equal builds 0 "Should not call buildMessage"
                ]

                testCaseTask
                    "Running the op after posting a plain message delivers that message first"
                <| fun () ->
                    task {
                        let received = ResizeArray<string>()

                        use mb =
                            new TaskMailboxProcessor<RequestOrPost>(fun inbox ->
                                cancellableTask {
                                    let! m1 = inbox.Receive()
                                    received.Add(label m1)
                                    replyIfRequest m1

                                    let! m2 = inbox.Receive()
                                    received.Add(label m2)
                                    replyIfRequest m2
                                }
                            )

                        mb.Start()
                        let op = mb.PostAndAsyncReply(Request)
                        mb.Post(Post "B")

                        let! result =
                            Expect.completesWithin
                                waitTime
                                (op CancellationToken.None)
                                "Should reply"

                        Expect.equal result 1 "Should still get the request's own reply"

                        Expect.equal
                            (List.ofSeq received)
                            [
                                "B"
                                "Request"
                            ]
                            "The agent should receive the already-posted message first"
                    }

                testCaseTask "Running the same op twice posts two requests, each with its own reply"
                <| fun () ->
                    task {
                        use mb =
                            new TaskMailboxProcessor<TaskReplyChannel<int>>(fun inbox ->
                                let rec loop n =
                                    mailbox {
                                        let! (r: TaskReplyChannel<int>) = inbox.Receive()
                                        r.Reply n
                                        return! loop (n + 1)
                                    }

                                loop 1
                            )

                        mb.Start()
                        let mutable builds = 0

                        let build (ch: TaskReplyChannel<int>) =
                            builds <- builds + 1
                            ch

                        let op = mb.PostAndAsyncReply build

                        let! r1 =
                            Expect.completesWithin
                                waitTime
                                (op CancellationToken.None)
                                "Should reply"

                        let! r2 =
                            Expect.completesWithin
                                waitTime
                                (op CancellationToken.None)
                                "Should reply"

                        Expect.equal builds 2 "Should call buildMessage for each run"
                        Expect.equal (r1, r2) (1, 2) "Should get a fresh reply for each run"
                    }

                testCaseTask
                    "buildMessage throwing faults the run instead of the call, and posts nothing"
                <| fun () ->
                    task {
                        use mb =
                            new TaskMailboxProcessor<TaskReplyChannel<int>>(fun _ ->
                                cancellableTask { () }
                            )

                        mb.Start()
                        let boom = Exception "boom"

                        let op = mb.PostAndAsyncReply(fun (_: TaskReplyChannel<int>) -> raise boom)

                        do!
                            Expect.throwsTaskT<Exception>
                                (fun () ->
                                    Expect.completesWithin
                                        waitTime
                                        (op CancellationToken.None)
                                        "Should run"
                                    :> Task
                                )
                                "Should propagate buildMessage's exception"

                        Expect.equal mb.CurrentQueueLength 0 "Should post nothing"
                    }

                // TaskMailbox-only: FSharp.Core's default (isThrowExceptionAfterDisposed = false)
                // drops a Post after Dispose and leaves the reply task waiting, which hangs an
                // infinite-timeout request instead of throwing.
                testList
                    "New requests throw ObjectDisposedException after Dispose, even without isThrowExceptionAfterDisposed"
                    [
                        let calls = requestCalls None

                        for name, call in calls ->
                            testCaseTask name
                            <| fun () ->
                                task {
                                    let mb =
                                        new TaskMailboxProcessor<TaskReplyChannel<int>>(fun _ ->
                                            cancellableTask { () }
                                        )

                                    mb.Start()
                                    mb.Dispose()
                                    let mutable builds = 0

                                    let build (ch: TaskReplyChannel<int>) =
                                        builds <- builds + 1
                                        ch

                                    do!
                                        Expect.throwsTaskT<ObjectDisposedException>
                                            (fun () ->
                                                Expect.completesWithin
                                                    waitTime
                                                    (task {
                                                        do! call mb build
                                                        return ()
                                                    })
                                                    "Should complete"
                                                :> Task
                                            )
                                            "Should throw ObjectDisposedException"

                                    Expect.equal mb.CurrentQueueLength 0 "Should post nothing"
                                    Expect.equal builds 0 "Should not call buildMessage"
                                }
                    ]
            ]

            testList "Error" [
                testCaseTask
                    "Is raised for OperationCanceledException when the token is not cancelled"
                <| fun () ->
                    task {
                        use mb =
                            new TaskMailboxProcessor<int>(fun _ ->
                                cancellableTask {
                                    do! Task.Yield()
                                    raise (OperationCanceledException())
                                }
                            )

                        let error = firstFrom mb.Error
                        mb.Start()
                        let! error = Expect.completesWithin waitTime error "Should raise Error"

                        Expect.isTrue
                            (error :? OperationCanceledException)
                            $"Should be OperationCanceledException, was {error}"
                    }
            ]
        ]
