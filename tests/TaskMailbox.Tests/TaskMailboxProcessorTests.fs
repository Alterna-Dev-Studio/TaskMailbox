namespace TaskMailbox.Tests

open System
open System.Reflection
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
                let t = call mb cts.Token
                Expect.isFalse t.IsCompleted "Should be waiting"
                cts.Cancel()

                do!
                    Expect.throwsTaskT<OperationCanceledException>
                        (fun () ->
                            Expect.completesWithin 1000 t "Should cancel an infinite wait" :> Task
                        )
                        "Should cancel an infinite wait"
            }

    /// TaskMailbox-only: the wait is cancelled inline by the canceller's Cancel(); the call must
    /// hop to the thread pool rather than resume its caller there.
    let resumesOffCancellerThread
        name
        (call: TaskMailboxProcessor<int> -> CancellationToken -> Task<'T>)
        =
        testCaseTask $"A cancelled {name} does not resume on the canceller's thread"
        <| fun () ->
            task {
                for _ in 1..5 do
                    use mb = idle ()
                    use cts = new CancellationTokenSource()
                    let mutable resumedOn = -1

                    let t =
                        task {
                            try
                                let! _ = call mb cts.Token
                                ()
                            with :? OperationCanceledException ->
                                resumedOn <- Thread.CurrentThread.ManagedThreadId
                        }

                    Expect.isFalse t.IsCompleted "Should be waiting"
                    let mutable cancellerOn = -1

                    let canceller =
                        Thread(fun () ->
                            cancellerOn <- Thread.CurrentThread.ManagedThreadId
                            cts.Cancel()
                        )

                    canceller.Start()
                    canceller.Join()
                    do! Expect.completesWithin waitTime t "Should cancel"
                    Expect.notEqual resumedOn -1 "Should resume after the cancellation"
                    Expect.notEqual resumedOn cancellerOn "Should resume off the canceller's thread"
            }

    /// TaskMailbox-only: a wait that completed for one delivery must not satisfy the next call.
    let waitsAgainAfterDelivery
        name
        (call: TaskMailboxProcessor<int> -> CancellationToken -> Task<int>)
        =
        testCaseTask $"A {name} after a delivered {name} waits for the next message"
        <| fun () ->
            task {
                use mb = idle ()
                use cts = new CancellationTokenSource()
                let first = call mb cts.Token
                Expect.isFalse first.IsCompleted "Should be waiting"
                mb.Post 1
                let! one = Expect.completesWithin waitTime first "Should deliver the first"
                Expect.equal one 1 "Should get the first message"
                let second = call mb cts.Token
                Expect.isFalse second.IsCompleted "Should wait for the next message"
                mb.Post 2
                let! two = Expect.completesWithin waitTime second "Should deliver the second"
                Expect.equal two 2 "Should get the second message"
            }

    [<Tests>]
    let taskMailboxProcessorTests =
        testList "TaskMailboxProcessor" [
            testList "Receive" [
                testCaseTask
                    "The exception from a cancelled infinite Receive carries the caller's token"
                <| fun () ->
                    task {
                        use mb = new TaskMailboxProcessor<int>(fun _ -> cancellableTask { () })
                        use cts = new CancellationTokenSource()
                        let t = (task { let! _ = mb.Receive () cts.Token in () })
                        cts.Cancel()

                        let! token =
                            task {
                                try
                                    do! t
                                    return CancellationToken.None
                                with :? OperationCanceledException as e ->
                                    return e.CancellationToken
                            }

                        Expect.equal token cts.Token "Should carry the caller's token"
                    }

                testCaseTask
                    "Throws OperationCanceledException when the caller cancels an infinite wait (no timeout)"
                <| fun () ->
                    task {
                        for name, call in
                            [
                                "Receive",
                                (fun (mb: TaskMailboxProcessor<int>) (ct: CancellationToken) ->
                                    task {
                                        let! _ = mb.Receive () ct
                                        ()
                                    }
                                )

                                "TryReceive",
                                (fun mb ct ->
                                    task {
                                        let! _ = mb.TryReceive () ct
                                        ()
                                    }
                                )
                            ] do
                            use mb = new TaskMailboxProcessor<int>(fun _ -> cancellableTask { () })
                            use cts = new CancellationTokenSource()
                            let t = call mb cts.Token
                            Expect.isFalse t.IsCompleted $"Should be waiting ({name})"
                            cts.Cancel()

                            do!
                                Expect.throwsTaskT<OperationCanceledException>
                                    (fun () ->
                                        Expect.completesWithin
                                            1000
                                            t
                                            $"Should cancel an infinite wait ({name})"
                                        :> Task
                                    )
                                    $"Should cancel an infinite wait ({name})"
                    }

                cancelsInfiniteWait "timeout -2" (fun mb ct -> mb.Receive (-2) ct)

                resumesOffCancellerThread "Receive" (fun mb ct -> mb.Receive () ct)

                waitsAgainAfterDelivery "Receive" (fun mb ct -> mb.Receive () ct)

                testCaseTask
                    "Faults the task, rather than throwing when run, on a timeout 0 Receive of an empty mailbox"
                <| fun () ->
                    task {
                        use mb = idle ()
                        let t = mb.Receive 0 CancellationToken.None
                        Expect.isTrue t.IsFaulted "Should fault the returned task"

                        do!
                            Expect.throwsTaskT<TimeoutException>
                                (fun () -> t :> Task)
                                "Should time out"
                    }

                testCaseTask
                    "An already-cancelled Receive or TryReceive is cancelled with the caller's token"
                <| fun () ->
                    task {
                        for name, call in
                            [
                                "Receive",
                                (fun (mb: TaskMailboxProcessor<int>) (ct: CancellationToken) ->
                                    mb.Receive () ct :> Task
                                )

                                "TryReceive", (fun mb ct -> mb.TryReceive () ct :> Task)
                            ] do
                            use mb = idle ()
                            use cts = new CancellationTokenSource()
                            cts.Cancel()
                            let t = call mb cts.Token
                            Expect.isTrue t.IsCanceled $"Should be cancelled, not faulted ({name})"

                            let! token =
                                task {
                                    try
                                        do! t
                                        return CancellationToken.None
                                    with :? OperationCanceledException as e ->
                                        return e.CancellationToken
                                }

                            Expect.equal token cts.Token $"Should carry the caller's token ({name})"
                    }

                testCaseTask "A Receive cancelled while waiting ends cancelled, not faulted"
                <| fun () ->
                    task {
                        use mb = idle ()
                        use cts = new CancellationTokenSource()
                        let t = mb.Receive () cts.Token
                        Expect.isFalse t.IsCompleted "Should be waiting"
                        cts.Cancel()

                        let! _ =
                            Expect.completesWithin
                                waitTime
                                (task {
                                    try
                                        let! _ = t
                                        ()
                                    with _ ->
                                        ()
                                })
                                "Should end"

                        Expect.isTrue t.IsCanceled "Should be cancelled, not faulted"
                    }

                testCaseTask "A Receive cancelled as a message arrives never loses the message"
                <| fun () ->
                    task {
                        for i in 1..2000 do
                            use mb = idle ()
                            use cts = new CancellationTokenSource()
                            let t = mb.Receive () cts.Token
                            use gate = new ManualResetEventSlim()

                            let poster =
                                Task.Run(fun () ->
                                    gate.Wait()
                                    mb.Post i
                                )

                            let canceller =
                                Task.Run(fun () ->
                                    gate.Wait()
                                    cts.Cancel()
                                )

                            gate.Set()
                            do! poster
                            do! canceller

                            let! received =
                                task {
                                    try
                                        let! msg = t
                                        return Some msg
                                    with :? OperationCanceledException ->
                                        return None
                                }

                            match received with
                            | Some msg -> Expect.equal msg i "Should deliver the posted message"
                            | None ->
                                let! next = mb.TryReceive 0 CancellationToken.None
                                Expect.equal next (Some i) "Should leave the message when cancelled"
                    }

                testCaseTask "The agent's token does not cancel a Receive run on another token"
                <| fun () ->
                    task {
                        use cts = new CancellationTokenSource()

                        use mb =
                            new TaskMailboxProcessor<int>(
                                (fun _ -> cancellableTask { () }),
                                cts.Token
                            )

                        let t = mb.Receive () CancellationToken.None
                        Expect.isFalse t.IsCompleted "Should be waiting"
                        cts.Cancel()
                        mb.Post 5

                        let! result = Expect.completesWithin waitTime t "Should still receive"
                        Expect.equal result 5 "Should receive despite the agent's token cancelling"
                    }
            ]

            testList "Reader guard" [
                testCaseTask "Faults the task, rather than throwing when run, on a second reader"
                <| fun () ->
                    task {
                        use mb = idle ()
                        let first = mb.Receive () CancellationToken.None
                        let second = mb.Receive () CancellationToken.None
                        Expect.isTrue second.IsFaulted "Should fault the returned task"

                        do!
                            Expect.throwsTaskT<InvalidOperationException>
                                (fun () -> second :> Task)
                                "Should reject a second reader"

                        mb.Post 1

                        let! msg =
                            Expect.completesWithin
                                waitTime
                                first
                                "First reader should still receive"

                        Expect.equal msg 1 "First reader should still receive"
                    }

                testCaseTask
                    "Throws InvalidOperationException when a second reader is already waiting"
                <| fun () ->
                    task {
                        for name, startFirst, tryStart in
                            [
                                "Receive then Receive",
                                (fun (mb: TaskMailboxProcessor<int>) ->
                                    mb.Receive () CancellationToken.None
                                ),
                                (fun (mb: TaskMailboxProcessor<int>) ->
                                    mb.Receive () CancellationToken.None
                                )

                                "Receive then Scan",
                                (fun (mb: TaskMailboxProcessor<int>) ->
                                    mb.Receive () CancellationToken.None
                                ),
                                (fun (mb: TaskMailboxProcessor<int>) ->
                                    mb.Scan (neverMatches, 0) CancellationToken.None
                                )

                                "Scan then Receive",
                                (fun (mb: TaskMailboxProcessor<int>) ->
                                    (mb.Scan(fun m -> Some(cancellableTask { return m })))
                                        CancellationToken.None
                                ),
                                (fun (mb: TaskMailboxProcessor<int>) ->
                                    mb.Receive () CancellationToken.None
                                )
                            ] do
                            use mb = idle ()
                            let first = startFirst mb

                            do!
                                Expect.throwsTaskT<InvalidOperationException>
                                    (fun () ->
                                        Expect.completesWithin
                                            waitTime
                                            (tryStart mb)
                                            $"Should allow one reader ({name})"
                                        :> Task
                                    )
                                    $"Should allow one reader ({name})"

                            mb.Post 1

                            let! msg =
                                Expect.completesWithin
                                    waitTime
                                    first
                                    $"First reader should still receive ({name})"

                            Expect.equal msg 1 $"First reader should still receive ({name})"
                    }
            ]

            testList "Scan" [
                resumesOffCancellerThread "Scan" (fun mb ct -> (mb.Scan neverMatches) ct)

                waitsAgainAfterDelivery
                    "Scan"
                    (fun mb ct -> (mb.Scan(fun m -> Some(cancellableTask { return m }))) ct)

                testCaseTask
                    "The exception from a cancelled infinite Scan carries the caller's token"
                <| fun () ->
                    task {
                        use mb = new TaskMailboxProcessor<int>(fun _ -> cancellableTask { () })
                        use cts = new CancellationTokenSource()
                        let t = (task { let! _ = (mb.Scan neverMatches) cts.Token in () })
                        cts.Cancel()

                        let! token =
                            task {
                                try
                                    do! t
                                    return CancellationToken.None
                                with :? OperationCanceledException as e ->
                                    return e.CancellationToken
                            }

                        Expect.equal token cts.Token "Should carry the caller's token"
                    }

                testCaseTask
                    "Throws OperationCanceledException when the caller cancels an infinite wait (no timeout)"
                <| fun () ->
                    task {
                        for name, call in
                            [
                                "Scan",
                                (fun (mb: TaskMailboxProcessor<int>) (ct: CancellationToken) ->
                                    task {
                                        let! _ = (mb.Scan(neverMatches)) ct
                                        ()
                                    }
                                )

                                "TryScan",
                                (fun mb ct ->
                                    task {
                                        let! _ = (mb.TryScan(neverMatches)) ct
                                        ()
                                    }
                                )
                            ] do
                            use mb = new TaskMailboxProcessor<int>(fun _ -> cancellableTask { () })
                            use cts = new CancellationTokenSource()
                            let t = call mb cts.Token
                            Expect.isFalse t.IsCompleted $"Should be waiting ({name})"
                            cts.Cancel()

                            do!
                                Expect.throwsTaskT<OperationCanceledException>
                                    (fun () ->
                                        Expect.completesWithin
                                            1000
                                            t
                                            $"Should cancel an infinite wait ({name})"
                                        :> Task
                                    )
                                    $"Should cancel an infinite wait ({name})"
                    }

                testCaseTask
                    "Throws OperationCanceledException when the caller cancels a finite wait"
                <| fun () ->
                    task {
                        for name, createMb, call in
                            [
                                "Scan, token-less agent",
                                idle,
                                (fun (mb: TaskMailboxProcessor<int>) (ct: CancellationToken) ->
                                    task {
                                        let! _ = (mb.Scan(neverMatches, waitTime)) ct
                                        ()
                                    }
                                )

                                "Scan, agent with a token",
                                idleWithToken,
                                (fun mb ct ->
                                    task {
                                        let! _ = (mb.Scan(neverMatches, waitTime)) ct
                                        ()
                                    }
                                )

                                "TryScan, token-less agent",
                                idle,
                                (fun mb ct ->
                                    task {
                                        let! _ = (mb.TryScan(neverMatches, waitTime)) ct
                                        ()
                                    }
                                )

                                "TryScan, agent with a token",
                                idleWithToken,
                                (fun mb ct ->
                                    task {
                                        let! _ = (mb.TryScan(neverMatches, waitTime)) ct
                                        ()
                                    }
                                )
                            ] do
                            use mb = createMb ()
                            use cts = new CancellationTokenSource()
                            let t = call mb cts.Token
                            Expect.isFalse t.IsCompleted $"Should be waiting ({name})"
                            cts.Cancel()

                            do!
                                Expect.throwsTaskT<OperationCanceledException>
                                    (fun () ->
                                        Expect.completesWithin
                                            1000
                                            t
                                            $"Should cancel a finite wait ({name})"
                                        :> Task
                                    )
                                    $"Should cancel a finite wait ({name})"
                    }

                testCaseTask "The agent's token does not cancel a Scan run on another token"
                <| fun () ->
                    task {
                        use cts = new CancellationTokenSource()

                        use mb =
                            new TaskMailboxProcessor<int>(
                                (fun _ -> cancellableTask { () }),
                                cts.Token
                            )

                        let t =
                            (mb.Scan(fun m -> Some(cancellableTask { return m })))
                                CancellationToken.None

                        Expect.isFalse t.IsCompleted "Should be waiting"
                        cts.Cancel()
                        mb.Post 5

                        let! result = Expect.completesWithin waitTime t "Should still scan"
                        Expect.equal result 5 "Should scan despite the agent's token cancelling"
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

                // TaskMailbox-only: FSharp.Core runs the computation regardless of the token
                testCaseTask
                    "A Scan whose match arrives after the caller cancels does not run the computation"
                <| fun () ->
                    task {
                        for name, postFirst, call in
                            [
                                "Scan, queued",
                                true,
                                (fun (mb: TaskMailboxProcessor<int>) scanner ct ->
                                    mb.Scan scanner ct :> Task
                                )
                                "Scan, waiting",
                                false,
                                (fun mb scanner ct -> mb.Scan scanner ct :> Task)
                                "TryScan, queued",
                                true,
                                (fun mb scanner ct -> mb.TryScan scanner ct :> Task)
                                "TryScan, waiting",
                                false,
                                (fun mb scanner ct -> mb.TryScan scanner ct :> Task)
                            ] do
                            use mb = idle ()
                            use cts = new CancellationTokenSource()
                            let mutable ran = false

                            let scanner m =
                                cts.Cancel()

                                Some(fun (_: CancellationToken) ->
                                    ran <- true
                                    Task.FromResult m
                                )

                            if postFirst then
                                mb.Post 1

                            let t = call mb scanner cts.Token

                            if not postFirst then
                                mb.Post 1

                            let! token =
                                task {
                                    try
                                        do! t
                                        return CancellationToken.None
                                    with :? OperationCanceledException as e ->
                                        return e.CancellationToken
                                }

                            Expect.equal
                                token
                                cts.Token
                                $"Should cancel with the caller's token ({name})"

                            Expect.isFalse ran $"Should not run the computation ({name})"
                    }

                testCaseTask "A scanner that throws OperationCanceledException cancels the Scan"
                <| fun () ->
                    task {
                        use scannerCts = new CancellationTokenSource()
                        scannerCts.Cancel()

                        for name, scannerToken in
                            [
                                "its own cancelled token", scannerCts.Token
                                "no token", CancellationToken.None
                            ] do
                            use mb = idle ()
                            mb.Post 1

                            let t =
                                mb.Scan
                                    ((fun _ -> raise (OperationCanceledException scannerToken)), 0)
                                    CancellationToken.None

                            let! thrown =
                                task {
                                    try
                                        do! t
                                        return None
                                    with :? OperationCanceledException as e ->
                                        return Some e
                                }

                            Expect.isTrue t.IsCanceled $"Should be cancelled ({name})"

                            Expect.equal
                                thrown.Value.CancellationToken
                                scannerToken
                                $"Should keep the scanner's token ({name})"
                    }

                testCaseTask "Runs the matched computation with the caller's token"
                <| fun () ->
                    task {
                        for name, call in
                            [
                                "Scan",
                                (fun (mb: TaskMailboxProcessor<int>) scanner ct ->
                                    mb.Scan scanner ct :> Task
                                )
                                "TryScan", (fun mb scanner ct -> mb.TryScan scanner ct :> Task)
                            ] do
                            use mb = idle ()
                            use cts = new CancellationTokenSource()
                            let mutable seen = CancellationToken.None
                            mb.Post 1

                            do!
                                call
                                    mb
                                    (fun m ->
                                        Some(fun (ct: CancellationToken) ->
                                            seen <- ct
                                            Task.FromResult m
                                        )
                                    )
                                    cts.Token

                            Expect.equal seen cts.Token $"Should pass the caller's token ({name})"
                    }

                testCaseTask "A computation that throws faults the Scan rather than throwing"
                <| fun () ->
                    task {
                        for name, call in
                            [
                                "Scan",
                                (fun (mb: TaskMailboxProcessor<int>) scanner ->
                                    mb.Scan scanner CancellationToken.None :> Task
                                )
                                "TryScan",
                                (fun mb scanner ->
                                    mb.TryScan scanner CancellationToken.None :> Task
                                )
                            ] do
                            use mb = idle ()
                            mb.Post 1

                            let t =
                                call
                                    mb
                                    (fun _ -> Some(fun (_: CancellationToken) -> failwith "boom"))

                            Expect.isTrue t.IsFaulted $"Should fault, not throw ({name})"
                    }
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

                // Only the async variants take a caller token, and build an op separately from
                // running it, so coldness and an already-cancelled token can both be checked.
                let asyncOps
                    : (string *
                      (TaskMailboxProcessor<TaskReplyChannel<int>>
                              -> (TaskReplyChannel<int> -> TaskReplyChannel<int>)
                              -> (CancellationToken -> Task<unit>))) list = [
                    "PostAndTryAsyncReply",
                    (fun mb build ->
                        let op = mb.PostAndTryAsyncReply build
                        fun ct -> task { let! _ = op ct in () }
                    )

                    "PostAndAsyncReply",
                    (fun mb build ->
                        let op = mb.PostAndAsyncReply build
                        fun ct -> task { let! _ = op ct in () }
                    )
                ]

                testCaseTask "Timeout -2 posts nothing"
                <| fun () ->
                    task {
                        for name, call in calls do
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
                                            $"Should complete ({name})"
                                        :> Task
                                    )
                                    $"Should throw ArgumentOutOfRangeException ({name})"

                            Expect.equal mb.CurrentQueueLength 0 $"Should post nothing ({name})"
                            Expect.equal builds 0 $"Should not call buildMessage ({name})"
                    }

                testCaseTask
                    "Throws OperationCanceledException when the caller cancels an infinite wait (token-less agent)"
                <| fun () ->
                    task {
                        for name, call in
                            [
                                "PostAndTryAsyncReply",
                                (fun
                                    (mb: TaskMailboxProcessor<TaskReplyChannel<int>>)
                                    (ct: CancellationToken) ->
                                    task {
                                        let! _ = (mb.PostAndTryAsyncReply id) ct
                                        ()
                                    }
                                )

                                "PostAndAsyncReply",
                                (fun mb ct ->
                                    task {
                                        let! _ = (mb.PostAndAsyncReply id) ct
                                        ()
                                    }
                                )
                            ] do
                            use mb =
                                new TaskMailboxProcessor<TaskReplyChannel<int>>(fun _ ->
                                    cancellableTask { () }
                                )

                            use cts = new CancellationTokenSource()
                            let t = call mb cts.Token
                            Expect.isFalse t.IsCompleted $"Should be waiting ({name})"
                            cts.Cancel()

                            do!
                                Expect.throwsTaskT<OperationCanceledException>
                                    (fun () ->
                                        Expect.completesWithin
                                            1000
                                            t
                                            $"Should cancel an infinite wait ({name})"
                                        :> Task
                                    )
                                    $"Should cancel an infinite wait ({name})"
                    }

                testCaseTask
                    "Creating the op posts nothing until it runs, even with an already-cancelled token"
                <| fun () ->
                    task {
                        for name, createOp in asyncOps do
                            use mb =
                                new TaskMailboxProcessor<TaskReplyChannel<int>>(fun _ ->
                                    cancellableTask { () }
                                )

                            mb.Start()
                            let mutable builds = 0

                            let build (ch: TaskReplyChannel<int>) =
                                builds <- builds + 1
                                ch

                            let run = createOp mb build

                            Expect.equal
                                mb.CurrentQueueLength
                                0
                                $"Should post nothing before running ({name})"

                            Expect.equal
                                builds
                                0
                                $"Should not call buildMessage before running ({name})"

                            use cts = new CancellationTokenSource()
                            cts.Cancel()

                            do!
                                Expect.throwsTaskT<OperationCanceledException>
                                    (fun () ->
                                        Expect.completesWithin
                                            waitTime
                                            (run cts.Token)
                                            $"Should complete ({name})"
                                        :> Task
                                    )
                                    $"Should throw OperationCanceledException ({name})"

                            Expect.equal mb.CurrentQueueLength 0 $"Should post nothing ({name})"
                            Expect.equal builds 0 $"Should not call buildMessage ({name})"
                    }

                // Each member's call to the agent's reply channel, ignoring the reply value.
                let replyOps
                    : (string *
                      (TaskMailboxProcessor<TaskReplyChannel<int>> -> CancellationToken -> Task)) list = [
                    "PostAndAsyncReply",
                    (fun mb ct ->
                        task {
                            let! _ = (mb.PostAndAsyncReply id) ct
                            ()
                        }
                    )

                    "PostAndTryAsyncReply",
                    (fun mb ct ->
                        task {
                            let! _ = (mb.PostAndTryAsyncReply id) ct
                            ()
                        }
                    )
                ]

                testCaseTask "Reply does not run the caller's continuation inline"
                <| fun () ->
                    task {
                        for name, op in replyOps do
                            let gate = tcs<unit> ()
                            let afterReply = TaskCompletionSource()

                            use mb =
                                new TaskMailboxProcessor<TaskReplyChannel<int>>(fun inbox ->
                                    cancellableTask {
                                        let! (r: TaskReplyChannel<int>) = inbox.Receive()
                                        do! gate.Task
                                        r.Reply 1
                                        afterReply.SetResult()
                                    }
                                )

                            mb.Start()

                            let caller =
                                task {
                                    do! op mb CancellationToken.None
                                    return afterReply.Task.Wait 1000
                                }

                            Expect.isFalse
                                caller.IsCompleted
                                $"Should be waiting on the gate ({name})"

                            gate.SetResult()

                            let! ok =
                                Expect.completesWithin waitTime caller $"Should reply ({name})"

                            Expect.isTrue
                                ok
                                $"Reply should not run the caller's continuation inline ({name})"
                    }

                // Both async request members. Each builds its op once, so running it twice
                // reuses the same cold CancellableTask, as a caller would.
                let asyncRequestCalls
                    : (string *
                      (TaskMailboxProcessor<TaskReplyChannel<int>>
                              -> (TaskReplyChannel<int> -> TaskReplyChannel<int>)
                              -> (CancellationToken -> Task<int>))) list = [
                    "PostAndTryAsyncReply",
                    (fun mb build ->
                        let op = mb.PostAndTryAsyncReply build

                        fun ct ->
                            task {
                                let! r = op ct
                                return r.Value
                            }
                    )

                    "PostAndAsyncReply", (fun mb build -> mb.PostAndAsyncReply build)
                ]

                testCaseTask "Running the same op twice posts two requests, each with its own reply"
                <| fun () ->
                    task {
                        for name, call in asyncRequestCalls do
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

                            let op = call mb build

                            let! r1 =
                                Expect.completesWithin
                                    waitTime
                                    (op CancellationToken.None)
                                    $"Should reply ({name})"

                            let! r2 =
                                Expect.completesWithin
                                    waitTime
                                    (op CancellationToken.None)
                                    $"Should reply ({name})"

                            Expect.equal builds 2 $"Should call buildMessage for each run ({name})"

                            Expect.equal
                                (r1, r2)
                                (1, 2)
                                $"Should get a fresh reply for each run ({name})"
                    }

                // TaskMailbox-only: FSharp.Core's default (isThrowExceptionAfterDisposed = false)
                // drops a Post after Dispose and leaves the reply task waiting, which hangs an
                // infinite-timeout request instead of throwing.
                testCaseTask
                    "New requests throw ObjectDisposedException after Dispose, even without isThrowExceptionAfterDisposed"
                <| fun () ->
                    task {
                        for name, call in requestCalls None do
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
                                            $"Should complete ({name})"
                                        :> Task
                                    )
                                    $"Should throw ObjectDisposedException ({name})"

                            Expect.equal mb.CurrentQueueLength 0 $"Should post nothing ({name})"
                            Expect.equal builds 0 $"Should not call buildMessage ({name})"
                    }

                testCaseTask
                    "A request whose buildMessage disposes the agent fails with ObjectDisposedException"
                <| fun () ->
                    task {
                        for name, call in requestCalls None do
                            let mb =
                                new TaskMailboxProcessor<TaskReplyChannel<int>>(fun _ ->
                                    cancellableTask { () }
                                )

                            mb.Start()

                            let build (ch: TaskReplyChannel<int>) =
                                mb.Dispose()
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
                                            $"Should complete ({name})"
                                        :> Task
                                    )
                                    $"Should throw ObjectDisposedException ({name})"

                            Expect.equal mb.CurrentQueueLength 0 $"Should post nothing ({name})"
                    }
            ]

            testList "Error" [
                testCaseTask
                    "Is raised for OperationCanceledException when the token is not cancelled"
                <| fun () ->
                    task {
                        let mutable canBeCanceled = true

                        use mb =
                            new TaskMailboxProcessor<int>(fun _ ->
                                cancellableTask {
                                    let! ct = CancellableTask.getCancellationToken ()
                                    canBeCanceled <- ct.CanBeCanceled
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

                        Expect.isFalse
                            canBeCanceled
                            "A token-less agent's body gets CancellationToken.None, not Async.DefaultCancellationToken"
                    }

                testCaseTask "An Error handler's exception does not escape StartImmediate"
                <| fun () ->
                    task {
                        let mutable handlerRan = false

                        use mb =
                            new TaskMailboxProcessor<int>(fun _ ->
                                cancellableTask { raise (Exception "body") }
                            )

                        mb.Error.Add(fun _ ->
                            handlerRan <- true
                            raise (Exception "handler")
                        )

                        mb.StartImmediate()

                        Expect.isTrue handlerRan "Should run the Error handler"
                    }
            ]

            testList "Public API" [
                testCase "Every MailboxProcessor member has a TaskMailboxProcessor counterpart"
                <| fun () ->
                    let surface (t: Type) =
                        let flags =
                            BindingFlags.Public
                            ||| BindingFlags.Instance
                            ||| BindingFlags.Static
                            ||| BindingFlags.DeclaredOnly

                        set [
                            for c in t.GetConstructors() ->
                                ".ctor", false, [ for p in c.GetParameters() -> p.Name ]
                            for m in t.GetMethods flags ->
                                m.Name, m.IsStatic, [ for p in m.GetParameters() -> p.Name ]
                        ]

                    let fsharp = surface typedefof<MailboxProcessor<_>>
                    let taskMailbox = surface typedefof<TaskMailboxProcessor<_>>

                    // Types differ by design (Async vs CancellableTask), so only names are compared.
                    // Each entry here needs a row in Compatibility.md.
                    let omitted = Set.empty
                    let added = Set.empty

                    Expect.equal
                        (Set.difference fsharp taskMailbox)
                        omitted
                        "MailboxProcessor members missing from TaskMailboxProcessor"

                    Expect.equal
                        (Set.difference taskMailbox fsharp)
                        added
                        "TaskMailboxProcessor members MailboxProcessor lacks"
            ]
        ]
