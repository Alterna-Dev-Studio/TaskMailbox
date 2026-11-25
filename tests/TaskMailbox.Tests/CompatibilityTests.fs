namespace TaskMailbox.Tests

open System
open System.Threading
open System.Threading.Tasks
open Expecto
open IcedTasks

/// Scenarios from dotnet/fsharp MailboxProcessorType.fs, run against FSharp.Core and TaskMailbox alike.
module CompatibilityTests =
    open TestHelpers

    type Message =
        | Increment of int
        | Fetch of TaskCompletionSource<int>
        | Reset

    type ThreadInfo = { Id: int; Name: string }

    let getThreadInfo () = {
        Id = Thread.CurrentThread.ManagedThreadId
        Name = Thread.CurrentThread.Name
    }

    let waitTime = 5000

    let getSimpleMailbox (mailboxes: IMailboxes) =
        Mailboxes.create
            mailboxes
            (fun inbox ->
                let rec loop n =
                    cancellableTask {
                        let! msg = inbox.Receive()
                        do! Task.Delay 100

                        match msg with
                        | Increment m -> return! loop (n + m)
                        | Reset -> return! loop 0
                        | Fetch reply ->
                            reply.SetResult n
                            return! loop n
                    }

                loop 0
            )

    /// Collects the messages that `addMsg` receives, separated by spaces.
    let recorder () =
        let mutable result = None

        let addMsg msg =
            result <-
                match result with
                | Some text ->
                    Some(
                        text
                        + " "
                        + msg
                    )
                | None -> Some msg

        addMsg, (fun () -> result)

    let receiveHandlesCancellation
        (mailboxes: IMailboxes)
        (receive: IInbox<int> -> CancellableTask<int>)
        =
        task {
            let addMsg, result = recorder ()
            let received = tcs<unit> ()
            let disposed = tcs<unit> ()
            use cts = new CancellationTokenSource()

            let mb =
                mailboxes.Create(
                    (fun inbox ->
                        cancellableTask {
                            use _ =
                                makeDisposable (fun () ->
                                    addMsg "Disposed"
                                    disposed.SetResult()
                                )

                            while true do
                                let! msg = receive inbox
                                addMsg (sprintf "Received %i" msg)

                                received.TrySetResult()
                                |> ignore
                        }
                    ),
                    false,
                    cts.Token
                )

            let error = firstFrom mb.Error
            mb.Start()
            mb.Post 1
            do! Expect.completesWithin waitTime received.Task "Should receive"
            cts.Cancel()
            do! Expect.completesWithin waitTime disposed.Task "Should dispose"
            Expect.equal (result ()) (Some "Received 1 Disposed") "Should receive then dispose"
            do! Expect.staysIncomplete 100 error "Cancellation should not raise Error"
        }

    let receiveRacesWithPost
        (mailboxes: IMailboxes)
        iterations
        (receive: IInbox<unit -> unit> -> CancellableTask<unit -> unit>)
        =
        task {
            use receiveEv = new AutoResetEvent(false)
            use postEv = new AutoResetEvent(false)
            use finishedEv = new AutoResetEvent(false)
            use cts = new CancellationTokenSource()
            use mailboxCts = new CancellationTokenSource()

            let mb =
                mailboxes.Create(
                    (fun inbox ->
                        cancellableTask {
                            while true do
                                receiveEv.WaitOne()
                                |> ignore

                                let! _ = receive inbox

                                finishedEv.Set()
                                |> ignore
                        }
                    ),
                    false,
                    mailboxCts.Token
                )

            let isErrored = firstFrom mb.Error
            mb.Start()

            let post =
                Task.Run(fun () ->
                    while not cts.IsCancellationRequested do
                        postEv.WaitOne()
                        |> ignore

                        mb.Post ignore
                )

            for i in 0..iterations do
                if i % 2 = 0 then
                    receiveEv.Set()
                    |> ignore

                    postEv.Set()
                    |> ignore
                else
                    postEv.Set()
                    |> ignore

                    receiveEv.Set()
                    |> ignore

                while not (finishedEv.WaitOne 100) do
                    if isErrored.IsCompleted then
                        raise
                        <| Exception("Mailbox should not fail!", isErrored.Result)

            cts.Cancel()

            postEv.Set()
            |> ignore

            do! post
            mailboxCts.Cancel()

            receiveEv.Set()
            |> ignore
        }

    let afterDispose (mailboxes: IMailboxes) isThrowExceptionAfterDisposed =
        task {
            let mutable isSkip = false
            let mutable actualSkipMessagesCount = 0
            let mutable actualMessagesCount = 0
            let sleepDueTime = 100
            let expectedMessagesCount = 2
            let processed = tcs<unit> ()

            let mb =
                mailboxes.Create(
                    (fun inbox ->
                        let rec loop () =
                            cancellableTask {
                                match! inbox.Receive() with
                                | Increment _ ->
                                    if isSkip then
                                        actualSkipMessagesCount <-
                                            actualSkipMessagesCount
                                            + 1

                                        return! loop ()
                                    else
                                        do! Task.Delay sleepDueTime

                                        if not isSkip then
                                            actualMessagesCount <-
                                                actualMessagesCount
                                                + 1

                                            if actualMessagesCount = expectedMessagesCount then
                                                processed.SetResult()

                                            do! Task.Delay sleepDueTime

                                        return! loop ()
                                | _ -> ()
                            }

                        loop ()
                    ),
                    isThrowExceptionAfterDisposed
                )

            mb.Start()
            let post () = mb.Post(Increment 1)

            for _ in 1..4 do
                post ()

            do! Expect.completesWithin waitTime processed.Task "Should process two messages"
            isSkip <- true
            mb.Dispose()

            if isThrowExceptionAfterDisposed then
                Expect.throwsT<ObjectDisposedException> post "Post should throw after Dispose"
            else
                post ()

            do!
                Task.Delay(
                    sleepDueTime
                    * 3
                )

            Expect.equal actualMessagesCount expectedMessagesCount "Should stop processing"
            Expect.equal actualSkipMessagesCount 0 "Should not receive after Dispose"
            Expect.equal mb.CurrentQueueLength 0 "Should clear the queue"
        }

    let tests (mailboxes: IMailboxes) =
        testList mailboxes.Name [
            testList "MailboxProcessorType.fs" [
                testCase "DefaultTimeout is infinite"
                <| fun () ->
                    use mb = getSimpleMailbox mailboxes
                    mb.Start()
                    Expect.equal mb.DefaultTimeout Timeout.Infinite "Should default to infinite"

                testCaseTask "DefaultTimeout applies to Receive"
                <| fun () ->
                    task {
                        use mb =
                            Mailboxes.create
                                mailboxes
                                (fun inbox ->
                                    cancellableTask {
                                        let! (_: int) = inbox.Receive()
                                        ()
                                    }
                                )

                        mb.DefaultTimeout <- 10
                        let error = firstFrom mb.Error
                        mb.Start()
                        let! error = Expect.completesWithin waitTime error "Should time out"

                        Expect.isTrue
                            (error :? TimeoutException)
                            $"Should be TimeoutException, was {error}"
                    }

                testCaseTask "Receive handles cancellation token"
                <| fun () -> receiveHandlesCancellation mailboxes (fun inbox -> inbox.Receive())

                testCaseTask "Receive with timeout argument handles cancellation token"
                <| fun () ->
                    receiveHandlesCancellation mailboxes (fun inbox -> inbox.Receive(100000))

                testCaseTask
                    "Receive Races with Post"
                    (fun () -> receiveRacesWithPost mailboxes 100000 (fun inbox -> inbox.Receive()))
                |> testSequenced

                testCaseTask
                    "Receive Races with Post on timeout"
                    (fun () ->
                        receiveRacesWithPost mailboxes 10000 (fun inbox -> inbox.Receive(5000))
                    )
                |> testSequenced

                testCaseTask
                    "After dispose is called, mailbox should stop receiving and processing messages"
                <| fun () -> afterDispose mailboxes false

                testCaseTask
                    "After dispose is called, mailbox should stop receiving and processing messages with exception"
                <| fun () -> afterDispose mailboxes true

                testCase "Dispose"
                <| fun () ->
                    use mb = getSimpleMailbox mailboxes
                    mb.Start()
                    mb.Post Reset
                    mb.Post(Increment 10)

                testCase "StartImmediate starts on current thread"
                <| fun () ->
                    let callingThreadInfo = getThreadInfo ()

                    use mb =
                        Mailboxes.create
                            mailboxes
                            (fun inbox ->
                                cancellableTask {
                                    let threadInfo = getThreadInfo ()

                                    let! (reply: TaskCompletionSource<ThreadInfo>) =
                                        inbox.Receive()

                                    reply.SetResult threadInfo
                                }
                            )

                    mb.StartImmediate()
                    let reply = tcs<ThreadInfo> ()
                    mb.Post reply

                    Expect.equal
                        reply.Task.Result
                        callingThreadInfo
                        "Should start on the calling thread"

                testCaseTask "Disposed MailboxProcessor does not throw on Post"
                <| fun () ->
                    task {
                        let step = ordered ()
                        use cts = new CancellationTokenSource()

                        let mb =
                            mailboxes.Create(
                                (fun inbox ->
                                    cancellableTask {
                                        step 1
                                        do! inbox.Receive()
                                        do! inbox.Receive()
                                    }
                                ),
                                false,
                                cts.Token
                            )

                        mb.Start()
                        step 2
                        do! Task.Delay 100
                        mb.Post()
                        mb.Dispose()
                        do! Task.Delay 100
                        mb.Post()
                        cts.Cancel()
                    }
            ]

            testList "Lifecycle" [
                testCase "Start twice throws"
                <| fun () ->
                    use mb = getSimpleMailbox mailboxes
                    mb.Start()
                    Expect.throwsT<InvalidOperationException> mb.Start "Should not start twice"

                testCase "StartImmediate after Start throws"
                <| fun () ->
                    use mb = getSimpleMailbox mailboxes
                    mb.Start()

                    Expect.throwsT<InvalidOperationException>
                        mb.StartImmediate
                        "Should not start twice"

                testCase "Start does not run the body on the caller's SynchronizationContext"
                <| fun () ->
                    let context = tcs<SynchronizationContext> ()

                    use mb =
                        Mailboxes.create
                            mailboxes
                            (fun _ ->
                                cancellableTask {
                                    context.SetResult SynchronizationContext.Current
                                }
                            )

                    do
                        use _ = setSyncContext (SynchronizationContext())
                        mb.Start()

                    Expect.isNull context.Task.Result "Should run on the thread pool"

                testCase "StartImmediate runs the body on the caller's SynchronizationContext"
                <| fun () ->
                    let callerContext = SynchronizationContext()
                    let mutable context = null

                    use mb =
                        Mailboxes.create
                            mailboxes
                            (fun _ -> cancellableTask { context <- SynchronizationContext.Current })

                    do
                        use _ = setSyncContext callerContext
                        mb.StartImmediate()

                    Expect.isTrue (obj.ReferenceEquals(context, callerContext)) "Should run inline"

                testCase "Start with a cancelled token does not run the body"
                <| fun () ->
                    let mutable ran = false

                    use mb =
                        mailboxes.Create(
                            (fun _ -> cancellableTask { ran <- true }),
                            false,
                            CancellationToken(true)
                        )

                    mb.Start()
                    Thread.Sleep 100
                    Expect.isFalse ran "Should not run"

                testCase "StartImmediate with a cancelled token does not run the body"
                <| fun () ->
                    let mutable ran = false

                    use mb =
                        mailboxes.Create(
                            (fun _ -> cancellableTask { ran <- true }),
                            false,
                            CancellationToken(true)
                        )

                    mb.StartImmediate()
                    Expect.isFalse ran "Should not run"

                testCase "Dispose twice does not throw"
                <| fun () ->
                    let mb = getSimpleMailbox mailboxes
                    mb.Dispose()
                    mb.Dispose()

                testCaseTask "Dispose discards queued messages"
                <| fun () ->
                    task {
                        let gate = tcs<unit> ()
                        let received = tcs<int> ()

                        use mb =
                            Mailboxes.create
                                mailboxes
                                (fun inbox ->
                                    cancellableTask {
                                        do! gate.Task
                                        let! msg = inbox.Receive(200)
                                        received.SetResult msg
                                    }
                                )

                        let error = firstFrom mb.Error
                        mb.Post 1
                        mb.Post 2
                        mb.Start()
                        mb.Dispose()
                        gate.SetResult()
                        let! error = Expect.completesWithin waitTime error "Should time out"

                        Expect.isTrue
                            (error :? TimeoutException)
                            $"Should be TimeoutException, was {error}"

                        Expect.isFalse received.Task.IsCompleted "Should not receive"
                    }

                testCaseTask "Dispose does not cancel a waiting Receive"
                <| fun () ->
                    task {
                        let disposed = tcs<unit> ()
                        use cts = new CancellationTokenSource()

                        let mb =
                            mailboxes.Create(
                                (fun inbox ->
                                    cancellableTask {
                                        use _ = makeDisposable disposed.SetResult
                                        let! (_: int) = inbox.Receive()
                                        ()
                                    }
                                ),
                                false,
                                cts.Token
                            )

                        let error = firstFrom mb.Error
                        mb.Start()
                        do! Task.Delay 50
                        mb.Dispose()
                        mb.Post 1
                        do! Expect.staysIncomplete 200 disposed.Task "Body should keep waiting"
                        Expect.isFalse error.IsCompleted "Should not raise Error"
                        cts.Cancel()

                        do!
                            Expect.completesWithin
                                waitTime
                                disposed.Task
                                "Token should still cancel"
                    }
            ]

            testList "Receive" [
                testCaseTask "Receives in arrival order"
                <| fun () ->
                    task {
                        let received = tcs<int list> ()

                        use mb =
                            Mailboxes.create
                                mailboxes
                                (fun inbox ->
                                    cancellableTask {
                                        let mutable msgs = []

                                        for _ in 1..5 do
                                            let! msg = inbox.Receive()

                                            msgs <-
                                                msg
                                                :: msgs

                                        received.SetResult(List.rev msgs)
                                    }
                                )

                        for i in 1..3 do
                            mb.Post i

                        mb.Start()

                        for i in 4..5 do
                            mb.Post i

                        let! received =
                            Expect.completesWithin waitTime received.Task "Should receive"

                        Expect.equal received [ 1..5 ] "Should receive in order"
                    }

                testCaseTask "Receive 0 on an empty mailbox times out"
                <| fun () ->
                    task {
                        use mb =
                            Mailboxes.create
                                mailboxes
                                (fun inbox ->
                                    cancellableTask {
                                        let! (_: int) = inbox.Receive(0)
                                        ()
                                    }
                                )

                        let error = firstFrom mb.Error
                        mb.Start()
                        let! error = Expect.completesWithin waitTime error "Should time out"

                        Expect.isTrue
                            (error :? TimeoutException)
                            $"Should be TimeoutException, was {error}"
                    }

                testCaseTask "Receive 0 returns a queued message"
                <| fun () ->
                    task {
                        let received = tcs<int> ()

                        use mb =
                            Mailboxes.create
                                mailboxes
                                (fun inbox ->
                                    cancellableTask {
                                        let! msg = inbox.Receive(0)
                                        received.SetResult msg
                                    }
                                )

                        mb.Post 42
                        mb.Start()

                        let! received =
                            Expect.completesWithin waitTime received.Task "Should receive"

                        Expect.equal received 42 "Should receive the queued message"
                    }

                testCaseTask "Receive with a negative timeout waits indefinitely"
                <| fun () ->
                    task {
                        use mb =
                            Mailboxes.create
                                mailboxes
                                (fun inbox ->
                                    cancellableTask {
                                        let! (_: int) = inbox.Receive(-5)
                                        ()
                                    }
                                )

                        let error = firstFrom mb.Error
                        mb.Start()
                        do! Expect.staysIncomplete 200 error "Should not time out"
                    }

                testCaseTask "A message posted after a timeout is not lost"
                <| fun () ->
                    task {
                        let timedOut = tcs<unit> ()
                        let received = tcs<int> ()

                        use mb =
                            Mailboxes.create
                                mailboxes
                                (fun inbox ->
                                    cancellableTask {
                                        try
                                            let! (_: int) = inbox.Receive(10)
                                            ()
                                        with :? TimeoutException ->
                                            timedOut.SetResult()

                                        let! msg = inbox.Receive()
                                        received.SetResult msg
                                    }
                                )

                        mb.Start()
                        do! Expect.completesWithin waitTime timedOut.Task "Should time out"
                        mb.Post 7

                        let! received =
                            Expect.completesWithin waitTime received.Task "Should receive"

                        Expect.equal received 7 "Should receive the later message"
                    }
            ]

            testList "CurrentQueueLength" [
                testCase "Counts messages posted before Start"
                <| fun () ->
                    use mb = getSimpleMailbox mailboxes
                    mb.Post Reset
                    mb.Post Reset
                    Expect.equal mb.CurrentQueueLength 2 "Should count queued messages"

                testCaseTask "Decreases as messages are received"
                <| fun () ->
                    task {
                        let length = tcs<int> ()

                        use mb =
                            Mailboxes.create
                                mailboxes
                                (fun inbox ->
                                    cancellableTask {
                                        let! (_: int) = inbox.Receive()
                                        length.SetResult inbox.CurrentQueueLength
                                    }
                                )

                        for i in 1..3 do
                            mb.Post i

                        mb.Start()
                        let! length = Expect.completesWithin waitTime length.Task "Should receive"
                        Expect.equal length 2 "Should count the remaining messages"
                    }

                testCase "Is zero after Dispose"
                <| fun () ->
                    let mb = getSimpleMailbox mailboxes
                    mb.Post Reset
                    mb.Post Reset
                    mb.Dispose()
                    mb.Post Reset
                    Expect.equal mb.CurrentQueueLength 0 "Should clear and drop messages"
            ]

            testList "Error" [
                testCaseTask "Is raised with the body's exception"
                <| fun () ->
                    task {
                        let boom = InvalidOperationException "boom"

                        use mb =
                            Mailboxes.create
                                mailboxes
                                (fun _ ->
                                    cancellableTask {
                                        do! Task.Yield()
                                        raise boom
                                    }
                                )

                        let error = firstFrom mb.Error
                        mb.Start()
                        let! error = Expect.completesWithin waitTime error "Should raise Error"

                        Expect.isTrue
                            (obj.ReferenceEquals(error, boom))
                            $"Should be the same exception, was {error}"
                    }

                testCase "Is raised on the calling thread when StartImmediate throws synchronously"
                <| fun () ->
                    let threadId = Thread.CurrentThread.ManagedThreadId
                    let mutable errorThreadId = None

                    use mb =
                        Mailboxes.create mailboxes (fun _ -> cancellableTask { failwith "sync" })

                    mb.Error.Add(fun _ ->
                        errorThreadId <- Some Thread.CurrentThread.ManagedThreadId
                    )

                    mb.StartImmediate()
                    Expect.equal errorThreadId (Some threadId) "Should raise inline"

                testCaseTask
                    "Is raised for OperationCanceledException when the token is not cancelled"
                <| fun () ->
                    task {
                        use mb =
                            Mailboxes.create
                                mailboxes
                                (fun _ ->
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

                testCaseTask "Is not raised when the body completes"
                <| fun () ->
                    task {
                        let completed = tcs<unit> ()

                        use mb =
                            Mailboxes.create
                                mailboxes
                                (fun _ -> cancellableTask { completed.SetResult() })

                        let error = firstFrom mb.Error
                        mb.Start()
                        do! Expect.completesWithin waitTime completed.Task "Should run"
                        do! Expect.staysIncomplete 100 error "Should not raise Error"
                    }
            ]
        ]

    [<Tests>]
    let compatibilityTests =
        testList
            "Compatibility"
            (Mailboxes.all
             |> List.map tests)
