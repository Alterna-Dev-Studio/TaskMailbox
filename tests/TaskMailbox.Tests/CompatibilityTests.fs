namespace TaskMailbox.Tests

open System
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks
open Expecto
open IcedTasks
open TaskMailbox

/// Scenarios from dotnet/fsharp MailboxProcessorType.fs and controlMailbox/test.fsx, run against FSharp.Core and TaskMailbox alike.
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

    /// A scanner that never matches, typed by use site.
    let neverMatches (_: 'Msg) : CancellableTask<'T> option = None

    /// A scanner that matches every message, so "received" means scanned.
    let matchesAll (m: 'Msg) : CancellableTask<'Msg> option = Some(cancellableTask { return m })

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

            mb.Start()
            mb.Post 1
            do! Expect.completesWithin waitTime received.Task "Should receive"
            cts.Cancel()
            do! Expect.completesWithin waitTime disposed.Task "Should dispose"
            Expect.equal (result ()) (Some "Received 1 Disposed") "Should receive then dispose"
        }

    /// A mailbox on `cts` whose body hands out its inbox, then idles.
    let createIdleMailbox (mailboxes: IMailboxes) (cts: CancellationTokenSource) =
        let inboxTcs = tcs<IInbox<int>> ()

        let mb =
            mailboxes.Create(
                (fun inbox ->
                    cancellableTask {
                        inboxTcs.SetResult inbox
                        let! ct = CancellableTask.getCancellationToken ()
                        do! Task.Delay(Timeout.Infinite, ct)
                    }
                ),
                false,
                cts.Token
            )

        mb, inboxTcs.Task

    let startAndGetInbox (mailboxes: IMailboxes) (cts: CancellationTokenSource) =
        task {
            let mb, inboxTask = createIdleMailbox mailboxes cts
            mb.Start()
            let! inbox = Expect.completesWithin waitTime inboxTask "Should hand out inbox"
            return mb, inbox
        }

    let callerCancelWhileWaiting
        (mailboxes: IMailboxes)
        (receive: IInbox<int> -> CancellableTask<'a>)
        =
        task {
            use cts = new CancellationTokenSource()
            let! _, inbox = startAndGetInbox mailboxes cts
            use callerCts = new CancellationTokenSource()
            callerCts.CancelAfter 20

            do!
                Expect.throwsTaskT<OperationCanceledException>
                    (fun () -> (receive inbox) callerCts.Token :> Task)
                    "Should cancel, not time out"
        }

    let alreadyCancelledWithQueuedMessage
        (mailboxes: IMailboxes)
        (receive: IInbox<int> -> CancellableTask<'a>)
        =
        task {
            use cts = new CancellationTokenSource()
            let mb, inboxTask = createIdleMailbox mailboxes cts
            mb.Post 7
            mb.Start()
            let! inbox = Expect.completesWithin waitTime inboxTask "Should hand out inbox"

            do!
                Expect.throwsTaskT<OperationCanceledException>
                    (fun () -> (receive inbox) (CancellationToken(true)) :> Task)
                    "Should cancel, not consume the message"

            Expect.equal inbox.CurrentQueueLength 1 "Should not consume the queued message"

            let! msg =
                Expect.completesWithin
                    waitTime
                    (inbox.Receive () CancellationToken.None)
                    "Should receive"

            Expect.equal msg 7 "Should get the queued message"
        }

    /// Like `alreadyCancelledWithQueuedMessage`, but also asserts the scanner is never called.
    let alreadyCancelledScan
        (mailboxes: IMailboxes)
        (scan: IInbox<int> -> (int -> CancellableTask<int> option) -> CancellationToken -> Task<'a>)
        =
        task {
            use cts = new CancellationTokenSource()
            let mb, inboxTask = createIdleMailbox mailboxes cts
            mb.Post 7
            mb.Start()
            let! inbox = Expect.completesWithin waitTime inboxTask "Should hand out inbox"
            let mutable scannerCalls = 0

            let scanner m =
                Interlocked.Increment(&scannerCalls)
                |> ignore

                matchesAll m

            do!
                Expect.throwsTaskT<OperationCanceledException>
                    (fun () -> (scan inbox scanner) (CancellationToken(true)) :> Task)
                    "Should cancel, not consume the message"

            Expect.equal inbox.CurrentQueueLength 1 "Should not consume the queued message"
            Expect.equal scannerCalls 0 "Should not call the scanner"

            let! msg =
                Expect.completesWithin
                    waitTime
                    (inbox.Receive () CancellationToken.None)
                    "Should receive"

            Expect.equal msg 7 "Should get the queued message"
        }

    let postAfterCallerCancelIsReceived
        (mailboxes: IMailboxes)
        (receive: IInbox<int> -> CancellableTask<'a>)
        =
        task {
            use cts = new CancellationTokenSource()
            let! mb, inbox = startAndGetInbox mailboxes cts
            use callerCts = new CancellationTokenSource()
            callerCts.CancelAfter 20

            do!
                Expect.throwsTaskT<OperationCanceledException>
                    (fun () -> (receive inbox) callerCts.Token :> Task)
                    "Should cancel"

            mb.Post 9

            let! msg =
                Expect.completesWithin
                    waitTime
                    (inbox.Receive () CancellationToken.None)
                    "Should receive"

            Expect.equal msg 9 "Should receive the message posted after cancel"
        }

    let mailboxCancelDoesNotCancelUncancellableReceive
        (mailboxes: IMailboxes)
        (receive: IInbox<int> -> CancellableTask<'a>)
        (unwrap: 'a -> int)
        =
        task {
            use cts = new CancellationTokenSource()
            let! mb, inbox = startAndGetInbox mailboxes cts
            let receiveTask = (receive inbox) CancellationToken.None
            do! Task.Delay 20
            cts.Cancel()
            mb.Post 5
            let! result = Expect.completesWithin waitTime receiveTask "Should still receive"
            Expect.equal (unwrap result) 5 "Should receive despite mailbox cancellation"
        }

    let receiveRacesNearTimeout (mailboxes: IMailboxes) =
        task {
            let n = 200
            use cts = new CancellationTokenSource()
            let! mb, inbox = startAndGetInbox mailboxes cts
            let rnd = Random()

            for i in 0 .. n - 1 do
                let post =
                    Task.Run(fun () ->
                        Thread.Sleep(rnd.Next(0, 41))
                        mb.Post i
                    )

                let! received =
                    task {
                        try
                            return! inbox.Receive (20) CancellationToken.None
                        with :? TimeoutException ->
                            return! inbox.Receive (1000) CancellationToken.None
                    }

                do! post
                Expect.equal received i $"Should receive message {i} exactly once"
                Expect.equal inbox.CurrentQueueLength 0 "Should drain the queue"
        }

    let tryReceiveRacesNearTimeout (mailboxes: IMailboxes) =
        task {
            let n = 200
            use cts = new CancellationTokenSource()
            let! mb, inbox = startAndGetInbox mailboxes cts
            let rnd = Random()

            for i in 0 .. n - 1 do
                let post =
                    Task.Run(fun () ->
                        Thread.Sleep(rnd.Next(0, 41))
                        mb.Post i
                    )

                let! received =
                    task {
                        match! inbox.TryReceive (20) CancellationToken.None with
                        | Some m -> return m
                        | None ->
                            match! inbox.TryReceive (1000) CancellationToken.None with
                            | Some m -> return m
                            | None -> return failwithf "Timed out waiting for message %d" i
                    }

                do! post
                Expect.equal received i $"Should receive message {i} exactly once"
                Expect.equal inbox.CurrentQueueLength 0 "Should drain the queue"
        }

    let tryScanRacesNearTimeout (mailboxes: IMailboxes) =
        task {
            let n = 200
            use cts = new CancellationTokenSource()
            let! mb, inbox = startAndGetInbox mailboxes cts
            let rnd = Random()

            for i in 0 .. n - 1 do
                let post =
                    Task.Run(fun () ->
                        Thread.Sleep(rnd.Next(0, 41))
                        mb.Post i
                    )

                let scanner m =
                    if m = i then Some(cancellableTask { return m }) else None

                let! received =
                    task {
                        match! (inbox.TryScan(scanner, 20)) CancellationToken.None with
                        | Some m -> return m
                        | None ->
                            match! (inbox.TryScan(scanner, 1000)) CancellationToken.None with
                            | Some m -> return m
                            | None -> return failwithf "Timed out waiting for message %d" i
                    }

                do! post
                Expect.equal received i $"Should receive message {i} exactly once"
                Expect.equal inbox.CurrentQueueLength 0 "Should drain the queue"
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

            let mb =
                Mailboxes.create
                    mailboxes
                    (fun inbox ->
                        cancellableTask {
                            while true do
                                receiveEv.WaitOne()
                                |> ignore

                                let! _ = receive inbox

                                finishedEv.Set()
                                |> ignore
                        }
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


    /// controlMailbox/test.fsx `mailboxes ()`: `getSimpleMailbox` without the delay.
    /// Loops with `while`: unlike Async, `return!` in a task is not a tail call, so recursing
    /// through synchronously completed Receives overflows the stack.
    let getCounter (mailboxes: IMailboxes) =
        Mailboxes.create
            mailboxes
            (fun inbox ->
                cancellableTask {
                    let mutable n = 0

                    while true do
                        match! inbox.Receive() with
                        | Increment m -> n <- n + m
                        | Reset -> n <- 0
                        | Fetch reply -> reply.SetResult n
                }
            )

    /// controlMailbox/test.fsx `test6`: posts `Increment n` down to 0 and computes the same sum.
    let rec compute (mb: IMailbox<Message>) n =
        async {
            mb.Post(Increment n)

            if n <> 0 then
                let! res = compute mb (n - 1)
                return n + res
            else
                return 0
        }

    /// A recursive `mailbox { }` body, the MailboxProcessor idiom.
    let mailboxTests (mailboxes: IMailboxes) =
        testList "mailbox { }" [
            testCaseTask "Recursive loop drains a pre-queued backlog in order"
            <| fun () ->
                task {
                    let n = 100000
                    let received = ResizeArray()
                    let completed = tcs<unit> ()

                    use mb =
                        Mailboxes.create
                            mailboxes
                            (fun inbox ->
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
                    Mailboxes.create
                        mailboxes
                        (fun inbox ->
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

            testCaseTask "Error is raised from deep in a recursive loop"
            <| fun () ->
                task {
                    let boom = Exception "boom"

                    use mb =
                        Mailboxes.create
                            mailboxes
                            (fun inbox ->
                                let rec loop n =
                                    mailbox {
                                        let! (_: int) = inbox.Receive()

                                        if n = 1000 then
                                            raise boom

                                        return! loop (n + 1)
                                    }

                                loop 0
                            )

                    let error = firstFrom mb.Error

                    for _ in 0..2000 do
                        mb.Post 0

                    mb.Start()
                    let! error = Expect.completesWithin waitTime error "Should raise Error"

                    Expect.isTrue
                        (obj.ReferenceEquals(error, boom))
                        $"Should be the body's exception, was {error}"
                }

            testCaseTask "Cancellation ends a recursive loop"
            <| fun () ->
                task {
                    let received = tcs<unit> ()
                    let disposed = tcs<unit> ()
                    use cts = new CancellationTokenSource()

                    let mb =
                        mailboxes.Create(
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
                            false,
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
                            Mailboxes.create
                                mailboxes
                                (fun inbox ->
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

                            SpinWait.SpinUntil(fun () ->
                                Volatile.Read(&count.contents) = start
                                                                 + batch
                            )

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

    let tests (mailboxes: IMailboxes) =
        testList mailboxes.Name [
            testList "MailboxProcessorType.fs" [
                testCase "DefaultTimeout is infinite"
                <| fun () ->
                    use mb = getSimpleMailbox mailboxes
                    mb.Start()
                    Expect.equal mb.DefaultTimeout Timeout.Infinite "Should default to infinite"

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

            testList "controlMailbox test.fsx" [
                testList "Post/Receive" [
                    for n in
                        [
                            0
                            1
                            100
                            1000
                            100000
                        ] do
                        testCaseTask $"n={n}"
                        <| fun () ->
                            task {
                                let received = ResizeArray()
                                let completed = tcs<unit> ()

                                use mb =
                                    Mailboxes.create
                                        mailboxes
                                        (fun inbox ->
                                            cancellableTask {
                                                for _ in 0 .. n - 1 do
                                                    let! msg = inbox.Receive()
                                                    received.Add msg

                                                completed.SetResult()
                                            }
                                        )

                                mb.Start()

                                for i in 0 .. n - 1 do
                                    mb.Post i

                                do!
                                    Expect.completesWithin
                                        waitTime
                                        completed.Task
                                        "Should receive all"

                                Expect.equal
                                    (List.ofSeq received)
                                    [ 0 .. n - 1 ]
                                    "Should receive in arrival order"
                            }
                ]

                testCaseTask "Error (0)"
                <| fun () ->
                    task {
                        use mb = Mailboxes.create mailboxes (fun _ -> cancellableTask { return () })
                        let error = firstFrom mb.Error
                        mb.Start()
                        do! Task.Delay 200
                        Expect.isFalse error.IsCompleted "Should not raise Error"
                    }

                testCaseTask "Error (1)"
                <| fun () ->
                    task {
                        let fail = Exception "fail"

                        use mb =
                            Mailboxes.create mailboxes (fun _ -> cancellableTask { raise fail })

                        let error = firstFrom mb.Error
                        mb.Start()
                        let! error = Expect.completesWithin waitTime error "Should raise Error"

                        Expect.isTrue
                            (obj.ReferenceEquals(error, fail))
                            $"Should be the body's exception, was {error}"
                    }

                testCase "test5 - Mailbox.Start"
                <| fun () ->
                    use mb = getCounter mailboxes
                    mb.Start()
                    Expect.throwsT<InvalidOperationException> mb.Start "Should not start twice"

                testCaseTask "test6a - mailbox & parallel"
                <| fun () ->
                    task {
                        use mb = getCounter mailboxes
                        mb.Start()

                        mb.Post Reset

                        let! computations =
                            Async.Parallel(List.map (compute mb) [ 1..50 ])
                            |> Async.StartAsTask

                        let reply = tcs<int> ()
                        mb.Post(Fetch reply)
                        let! mails = Expect.completesWithin waitTime reply.Task "Should reply"
                        Expect.equal mails (Array.sum computations) "Should sum every Increment"
                    }

                testCaseTask "LotsOfMessages"
                <| fun () ->
                    task {
                        let n = 200000
                        let count = ref n

                        use mb =
                            Mailboxes.create
                                mailboxes
                                (fun inbox ->
                                    cancellableTask {
                                        while true do
                                            let! (_: string) = inbox.Receive()

                                            Interlocked.Decrement(&count.contents)
                                            |> ignore
                                    }
                                )

                        mb.Start()

                        for _ in 1..n do
                            mb.Post "Message!"

                        let mutable queueLength = mb.CurrentQueueLength

                        while Volatile.Read(&count.contents) > 0 do
                            let current = mb.CurrentQueueLength
                            Expect.isLessThanOrEqual current queueLength "Should only drain"
                            queueLength <- current
                            do! Task.Delay 10

                        Expect.equal mb.CurrentQueueLength 0 "Should drain to 0"
                    }
            ]

            testList "Receive" [
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

            testList "Negative timeout" [
                testCaseTask "Receive(-2) without a token waits, like -1"
                <| fun () ->
                    task {
                        let received = tcs<int> ()

                        use mb =
                            Mailboxes.create
                                mailboxes
                                (fun inbox ->
                                    cancellableTask {
                                        let! msg = inbox.Receive(-2)
                                        received.SetResult msg
                                    }
                                )

                        mb.Start()
                        mb.Post 7

                        let! received =
                            Expect.completesWithin waitTime received.Task "Should receive"

                        Expect.equal received 7 "Should receive the posted message"
                    }

                testCaseTask "Receive(-2) without a token returns a queued message"
                <| fun () ->
                    task {
                        let received = tcs<int> ()

                        use mb =
                            Mailboxes.create
                                mailboxes
                                (fun inbox ->
                                    cancellableTask {
                                        let! msg = inbox.Receive(-2)
                                        received.SetResult msg
                                    }
                                )

                        mb.Post 7
                        mb.Start()

                        let! received =
                            Expect.completesWithin waitTime received.Task "Should receive"

                        Expect.equal received 7 "Should receive the queued message"
                    }

                testCaseTask
                    "Receive(-2) with a token throws ArgumentOutOfRangeException when empty"
                <| fun () ->
                    task {
                        use cts = new CancellationTokenSource()

                        use mb =
                            mailboxes.Create(
                                (fun inbox ->
                                    cancellableTask {
                                        let! (_: int) = inbox.Receive(-2)
                                        ()
                                    }
                                ),
                                false,
                                cts.Token
                            )

                        let error = firstFrom mb.Error
                        mb.Start()
                        let! error = Expect.completesWithin waitTime error "Should raise Error"

                        Expect.isTrue
                            (error :? ArgumentOutOfRangeException)
                            $"Should be ArgumentOutOfRangeException, was {error}"
                    }

                testCaseTask "Receive(-2) with a token returns a queued message"
                <| fun () ->
                    task {
                        use cts = new CancellationTokenSource()
                        let received = tcs<int> ()

                        use mb =
                            mailboxes.Create(
                                (fun inbox ->
                                    cancellableTask {
                                        let! msg = inbox.Receive(-2)
                                        received.SetResult msg
                                    }
                                ),
                                false,
                                cts.Token
                            )

                        mb.Post 7
                        mb.Start()

                        let! received =
                            Expect.completesWithin waitTime received.Task "Should receive"

                        Expect.equal received 7 "Should receive the queued message"
                    }

                testCaseTask
                    "DefaultTimeout -5 with a token throws ArgumentOutOfRangeException when empty"
                <| fun () ->
                    task {
                        use cts = new CancellationTokenSource()

                        use mb =
                            mailboxes.Create(
                                (fun inbox ->
                                    cancellableTask {
                                        let! (_: int) = inbox.Receive()
                                        ()
                                    }
                                ),
                                false,
                                cts.Token
                            )

                        mb.DefaultTimeout <- -5
                        let error = firstFrom mb.Error
                        mb.Start()
                        let! error = Expect.completesWithin waitTime error "Should raise Error"

                        Expect.isTrue
                            (error :? ArgumentOutOfRangeException)
                            $"Should be ArgumentOutOfRangeException, was {error}"
                    }

                testCaseTask "TryReceive(-2) without a token waits, like -1"
                <| fun () ->
                    task {
                        let received = tcs<int option> ()

                        use mb =
                            Mailboxes.create
                                mailboxes
                                (fun inbox ->
                                    cancellableTask {
                                        let! msg = inbox.TryReceive(-2)
                                        received.SetResult msg
                                    }
                                )

                        mb.Start()
                        mb.Post 7

                        let! received =
                            Expect.completesWithin waitTime received.Task "Should receive"

                        Expect.equal received (Some 7) "Should receive the posted message"
                    }

                testCaseTask "TryReceive(-2) without a token returns a queued message"
                <| fun () ->
                    task {
                        let received = tcs<int option> ()

                        use mb =
                            Mailboxes.create
                                mailboxes
                                (fun inbox ->
                                    cancellableTask {
                                        let! msg = inbox.TryReceive(-2)
                                        received.SetResult msg
                                    }
                                )

                        mb.Post 7
                        mb.Start()

                        let! received =
                            Expect.completesWithin waitTime received.Task "Should receive"

                        Expect.equal received (Some 7) "Should receive the queued message"
                    }

                testCaseTask
                    "TryReceive(-2) with a token throws ArgumentOutOfRangeException when empty"
                <| fun () ->
                    task {
                        use cts = new CancellationTokenSource()

                        use mb =
                            mailboxes.Create(
                                (fun inbox ->
                                    cancellableTask {
                                        let! (_: int option) = inbox.TryReceive(-2)
                                        ()
                                    }
                                ),
                                false,
                                cts.Token
                            )

                        let error = firstFrom mb.Error
                        mb.Start()
                        let! error = Expect.completesWithin waitTime error "Should raise Error"

                        Expect.isTrue
                            (error :? ArgumentOutOfRangeException)
                            $"Should be ArgumentOutOfRangeException, was {error}"
                    }

                testCaseTask "TryReceive(-2) with a token returns a queued message"
                <| fun () ->
                    task {
                        use cts = new CancellationTokenSource()
                        let received = tcs<int option> ()

                        use mb =
                            mailboxes.Create(
                                (fun inbox ->
                                    cancellableTask {
                                        let! msg = inbox.TryReceive(-2)
                                        received.SetResult msg
                                    }
                                ),
                                false,
                                cts.Token
                            )

                        mb.Post 7
                        mb.Start()

                        let! received =
                            Expect.completesWithin waitTime received.Task "Should receive"

                        Expect.equal received (Some 7) "Should receive the queued message"
                    }
            ]

            testList "TryReceive" [
                testCaseTask "Returns Some for a queued message"
                <| fun () ->
                    task {
                        let received = tcs<int option> ()

                        use mb =
                            Mailboxes.create
                                mailboxes
                                (fun inbox ->
                                    cancellableTask {
                                        let! msg = inbox.TryReceive()
                                        received.SetResult msg
                                    }
                                )

                        mb.Post 7
                        mb.Start()

                        let! received =
                            Expect.completesWithin waitTime received.Task "Should receive"

                        Expect.equal received (Some 7) "Should receive the queued message"
                    }

                testCaseTask "TryReceive 0 on an empty mailbox returns None"
                <| fun () ->
                    task {
                        let received = tcs<int option> ()

                        use mb =
                            Mailboxes.create
                                mailboxes
                                (fun inbox ->
                                    cancellableTask {
                                        let! msg = inbox.TryReceive(0)
                                        received.SetResult msg
                                    }
                                )

                        mb.Start()

                        let! received =
                            Expect.completesWithin waitTime received.Task "Should return None"

                        Expect.equal received None "Should be None"
                    }

                testCaseTask "TryReceive 10 on an empty mailbox returns None"
                <| fun () ->
                    task {
                        let received = tcs<int option> ()

                        use mb =
                            Mailboxes.create
                                mailboxes
                                (fun inbox ->
                                    cancellableTask {
                                        let! msg = inbox.TryReceive(10)
                                        received.SetResult msg
                                    }
                                )

                        mb.Start()

                        let! received =
                            Expect.completesWithin waitTime received.Task "Should return None"

                        Expect.equal received None "Should be None"
                    }

                testCaseTask "DefaultTimeout applies to TryReceive"
                <| fun () ->
                    task {
                        let received = tcs<int option> ()

                        use mb =
                            Mailboxes.create
                                mailboxes
                                (fun inbox ->
                                    cancellableTask {
                                        let! msg = inbox.TryReceive()
                                        received.SetResult msg
                                    }
                                )

                        mb.DefaultTimeout <- 10
                        mb.Start()

                        let! received =
                            Expect.completesWithin waitTime received.Task "Should return None"

                        Expect.equal received None "Should be None"
                    }

                testCaseTask "DefaultTimeout is read when TryReceive is called, not when it runs"
                <| fun () ->
                    task {
                        let created = tcs<unit> ()
                        let go = tcs<unit> ()
                        let received = tcs<int option> ()

                        use mb =
                            Mailboxes.create
                                mailboxes
                                (fun inbox ->
                                    cancellableTask {
                                        let tryReceive = inbox.TryReceive()
                                        created.SetResult()
                                        do! go.Task
                                        let! msg = tryReceive
                                        received.SetResult msg
                                    }
                                )

                        mb.Start()

                        do!
                            Expect.completesWithin
                                waitTime
                                created.Task
                                "Should create the CancellableTask"

                        mb.DefaultTimeout <- 10
                        go.SetResult()
                        do! Task.Delay 100
                        mb.Post 7

                        let! received =
                            Expect.completesWithin waitTime received.Task "Should receive"

                        Expect.equal
                            received
                            (Some 7)
                            "Should still use the timeout from when TryReceive was called"
                    }

                testCaseTask "A message posted after None is not lost"
                <| fun () ->
                    task {
                        let timedOut = tcs<unit> ()
                        let received = tcs<int> ()

                        use mb =
                            Mailboxes.create
                                mailboxes
                                (fun inbox ->
                                    cancellableTask {
                                        let! none = inbox.TryReceive(10)
                                        Expect.equal none None "Should be None"
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

            testList "Scan" [
                testCaseTask "Scan takes a message out of order"
                <| fun () ->
                    task {
                        use cts = new CancellationTokenSource()
                        let! mb, inbox = startAndGetInbox mailboxes cts

                        mb.Post 1
                        mb.Post 2
                        mb.Post 3
                        mb.Post 4

                        let scanner m =
                            if m = 3 then Some(cancellableTask { return m }) else None

                        let! scanned =
                            Expect.completesWithin
                                waitTime
                                ((inbox.Scan(scanner)) CancellationToken.None)
                                "Should scan"

                        Expect.equal scanned 3 "Should match 3 out of order"

                        let! r1 =
                            Expect.completesWithin
                                waitTime
                                (inbox.Receive () CancellationToken.None)
                                "Should receive"

                        Expect.equal r1 1 "Should receive skipped 1 first"

                        let! r2 =
                            Expect.completesWithin
                                waitTime
                                (inbox.TryReceive () CancellationToken.None)
                                "Should try-receive"

                        Expect.equal r2 (Some 2) "Should receive skipped 2"

                        let! r3 =
                            Expect.completesWithin
                                waitTime
                                (inbox.Receive () CancellationToken.None)
                                "Should receive"

                        Expect.equal r3 4 "Should receive arrival 4"
                    }

                testCaseTask "TryScan re-offers skipped messages before arrivals, in order"
                <| fun () ->
                    task {
                        use cts = new CancellationTokenSource()
                        let! mb, inbox = startAndGetInbox mailboxes cts
                        let seen = ConcurrentQueue<int>()

                        mb.Post 1
                        mb.Post 2

                        let scanTask =
                            (inbox.TryScan(fun m ->
                                seen.Enqueue m
                                if m = 3 then Some(cancellableTask { return m }) else None
                            ))
                                CancellationToken.None

                        mb.Post 3

                        let! scanned = Expect.completesWithin waitTime scanTask "Should scan"
                        Expect.equal scanned (Some 3) "Should match 3"

                        Expect.equal
                            (List.ofSeq seen)
                            [
                                1
                                2
                                3
                            ]
                            "Should see skipped messages then arrivals"

                        seen.Clear()
                        mb.Post 4

                        let! scanned2 =
                            Expect.completesWithin
                                waitTime
                                ((inbox.TryScan(fun m ->
                                    seen.Enqueue m
                                    if m = 4 then Some(cancellableTask { return m }) else None
                                ))
                                    CancellationToken.None)
                                "Should scan again"

                        Expect.equal scanned2 (Some 4) "Should match 4"

                        Expect.equal
                            (List.ofSeq seen)
                            [
                                1
                                2
                                4
                            ]
                            "Should re-offer skipped 1 and 2, then arrival 4"
                    }

                testCaseTask "Scan waits for a match posted later"
                <| fun () ->
                    task {
                        use cts = new CancellationTokenSource()
                        let! mb, inbox = startAndGetInbox mailboxes cts

                        let scanTask =
                            (inbox.Scan(fun m ->
                                if m = 5 then Some(cancellableTask { return m }) else None
                            ))
                                CancellationToken.None

                        mb.Post 1
                        mb.Post 5

                        let! scanned = Expect.completesWithin waitTime scanTask "Should scan"
                        Expect.equal scanned 5 "Should match the later message"
                    }

                testCaseTask "Scan runs only the matched message's computation, once"
                <| fun () ->
                    task {
                        use cts = new CancellationTokenSource()
                        let! mb, inbox = startAndGetInbox mailboxes cts
                        let ran = ConcurrentQueue<int>()

                        mb.Post 1
                        mb.Post 2
                        mb.Post 3

                        let scanner m =
                            let computation =
                                cancellableTask {
                                    ran.Enqueue m
                                    return m
                                }

                            if m = 2 then Some computation else None

                        let! scanned =
                            Expect.completesWithin
                                waitTime
                                ((inbox.Scan(scanner)) CancellationToken.None)
                                "Should scan"

                        Expect.equal scanned 2 "Should match 2"

                        Expect.equal
                            (List.ofSeq ran)
                            [ 2 ]
                            "Should run only the matched computation"
                    }

                testCaseTask "CurrentQueueLength counts skipped messages"
                <| fun () ->
                    task {
                        use cts = new CancellationTokenSource()
                        let! mb, inbox = startAndGetInbox mailboxes cts

                        mb.Post 1
                        mb.Post 2
                        mb.Post 3

                        let! scanned =
                            Expect.completesWithin
                                waitTime
                                ((inbox.Scan(fun m ->
                                    if m = 3 then Some(cancellableTask { return m }) else None
                                ))
                                    CancellationToken.None)
                                "Should scan"

                        Expect.equal scanned 3 "Should match 3"
                        Expect.equal inbox.CurrentQueueLength 2 "Should count the skipped messages"
                    }

                testCaseTask "A Scan computation can Receive"
                <| fun () ->
                    task {
                        use cts = new CancellationTokenSource()
                        let! mb, inbox = startAndGetInbox mailboxes cts

                        mb.Post 1
                        mb.Post 2
                        mb.Post 3

                        let scanner m =
                            if m = 2 then
                                Some(
                                    cancellableTask {
                                        let! r = inbox.Receive()
                                        return m, r
                                    }
                                )
                            else
                                None

                        let! result =
                            Expect.completesWithin
                                waitTime
                                ((inbox.Scan(scanner)) CancellationToken.None)
                                "Should scan"

                        Expect.equal
                            result
                            (2, 1)
                            "Should receive the skipped message from the computation"
                    }

                testCaseTask "A Scan computation's exception propagates and the message is consumed"
                <| fun () ->
                    task {
                        use cts = new CancellationTokenSource()
                        let! mb, inbox = startAndGetInbox mailboxes cts
                        let boom = Exception "boom"

                        mb.Post 1
                        mb.Post 2

                        let scanner m =
                            if m = 2 then
                                Some(cancellableTask { return raise boom })
                            else
                                None

                        let! thrown =
                            task {
                                try
                                    let! (_: int) = (inbox.Scan(scanner)) CancellationToken.None
                                    return None
                                with e ->
                                    return Some e
                            }

                        match thrown with
                        | Some e ->
                            Expect.isTrue
                                (obj.ReferenceEquals(e, boom))
                                $"Should be the computation's own exception, was {e}"
                        | None -> failtest "Should propagate the computation's exception"

                        let! r1 =
                            Expect.completesWithin
                                waitTime
                                (inbox.Receive () CancellationToken.None)
                                "Should receive"

                        Expect.equal r1 1 "Should receive the skipped message"

                        Expect.equal
                            inbox.CurrentQueueLength
                            0
                            "Should have consumed the matched message"
                    }

                testCaseTask "Scan 0 on an empty mailbox times out"
                <| fun () ->
                    task {
                        use cts = new CancellationTokenSource()
                        let! mb, inbox = startAndGetInbox mailboxes cts

                        do!
                            Expect.completesWithin
                                waitTime
                                (Expect.throwsTaskT<TimeoutException>
                                    (fun () ->
                                        ((inbox.Scan(neverMatches, 0)) CancellationToken.None)
                                        :> Task
                                    )
                                    "Should time out")
                                "Should time out quickly"
                    }

                testCaseTask "Scan 50 with no match times out and keeps the messages"
                <| fun () ->
                    task {
                        use cts = new CancellationTokenSource()
                        let! mb, inbox = startAndGetInbox mailboxes cts

                        mb.Post 1
                        mb.Post 2

                        let scanner m =
                            if m = 3 then Some(cancellableTask { return m }) else None

                        do!
                            Expect.completesWithin
                                waitTime
                                (Expect.throwsTaskT<TimeoutException>
                                    (fun () ->
                                        ((inbox.Scan(scanner, 50)) CancellationToken.None) :> Task
                                    )
                                    "Should time out")
                                "Should time out quickly"

                        Expect.equal
                            inbox.CurrentQueueLength
                            2
                            "Should keep both rejected messages skipped"

                        mb.Post 3

                        let! r1 =
                            Expect.completesWithin
                                waitTime
                                (inbox.Receive () CancellationToken.None)
                                "Should receive"

                        Expect.equal r1 1 "Should receive skipped 1 first"

                        let! r2 =
                            Expect.completesWithin
                                waitTime
                                (inbox.Receive () CancellationToken.None)
                                "Should receive"

                        Expect.equal r2 2 "Should receive skipped 2"

                        let! r3 =
                            Expect.completesWithin
                                waitTime
                                (inbox.Receive () CancellationToken.None)
                                "Should receive"

                        Expect.equal r3 3 "Should receive 3"
                    }

                testCaseTask "Scan(-2) with a token waits for a message"
                <| fun () ->
                    task {
                        use cts = new CancellationTokenSource()
                        let! mb, inbox = startAndGetInbox mailboxes cts

                        let scanner m =
                            if m = 7 then Some(cancellableTask { return m }) else None

                        let scanTask = (inbox.Scan(scanner, -2)) CancellationToken.None
                        mb.Post 7

                        let! scanned = Expect.completesWithin waitTime scanTask "Should scan"
                        Expect.equal scanned 7 "Should match the later message"
                    }

                testCaseTask "DefaultTimeout applies to Scan"
                <| fun () ->
                    task {
                        use mb =
                            Mailboxes.create
                                mailboxes
                                (fun inbox ->
                                    cancellableTask {
                                        let! (_: int) = inbox.Scan(neverMatches)
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

                testCaseTask "Dispose drops skipped messages"
                <| fun () ->
                    task {
                        use cts = new CancellationTokenSource()
                        let! mb, inbox = startAndGetInbox mailboxes cts

                        mb.Post 1
                        mb.Post 2

                        let! none =
                            Expect.completesWithin
                                waitTime
                                ((inbox.TryScan(neverMatches, 0)) CancellationToken.None)
                                "Should try-scan"

                        Expect.equal none None "Should skip both"

                        mb.Dispose()

                        Expect.equal inbox.CurrentQueueLength 0 "Should drop skipped messages"

                        let! received =
                            Expect.completesWithin
                                waitTime
                                (inbox.TryReceive (0) CancellationToken.None)
                                "Should try-receive"

                        Expect.equal received None "Should not receive after Dispose"

                        let! scanned =
                            Expect.completesWithin
                                waitTime
                                ((inbox.TryScan(matchesAll, 0)) CancellationToken.None)
                                "Should try-scan"

                        Expect.equal scanned None "Should not scan after Dispose"
                    }

                testCaseTask "A scanner exception leaves a skipped message queued"
                <| fun () ->
                    task {
                        use cts = new CancellationTokenSource()
                        let! mb, inbox = startAndGetInbox mailboxes cts
                        let boom = Exception "boom"

                        mb.Post 1
                        mb.Post 2

                        let! none =
                            Expect.completesWithin
                                waitTime
                                ((inbox.TryScan(neverMatches, 0)) CancellationToken.None)
                                "Should try-scan"

                        Expect.equal none None "Should skip both"

                        let! thrown =
                            task {
                                try
                                    let! (_: int option) =
                                        (inbox.TryScan((fun (_: int) -> raise boom), 0))
                                            CancellationToken.None

                                    return None
                                with e ->
                                    return Some e
                            }

                        match thrown with
                        | Some e ->
                            Expect.isTrue
                                (obj.ReferenceEquals(e, boom))
                                $"Should be the scanner's own exception, was {e}"
                        | None -> failtest "Should propagate the scanner's exception"

                        Expect.equal inbox.CurrentQueueLength 2 "Should keep both skipped messages"

                        let! r1 =
                            Expect.completesWithin
                                waitTime
                                (inbox.Receive () CancellationToken.None)
                                "Should receive"

                        Expect.equal r1 1 "Should receive skipped 1 first"

                        let! r2 =
                            Expect.completesWithin
                                waitTime
                                (inbox.Receive () CancellationToken.None)
                                "Should receive"

                        Expect.equal r2 2 "Should receive skipped 2"
                    }
            ]

            testList "TryScan" [
                testCaseTask "TryScan 0 on an empty mailbox returns None"
                <| fun () ->
                    task {
                        use cts = new CancellationTokenSource()
                        let! mb, inbox = startAndGetInbox mailboxes cts

                        let! scanned =
                            Expect.completesWithin
                                waitTime
                                ((inbox.TryScan(neverMatches, 0)) CancellationToken.None)
                                "Should return None"

                        Expect.equal scanned None "Should be None"
                    }

                testCaseTask "TryScan 0 with a queued match returns Some"
                <| fun () ->
                    task {
                        use cts = new CancellationTokenSource()
                        let! mb, inbox = startAndGetInbox mailboxes cts

                        mb.Post 7

                        let scanner m =
                            if m = 7 then Some(cancellableTask { return m }) else None

                        let! scanned =
                            Expect.completesWithin
                                waitTime
                                ((inbox.TryScan(scanner, 0)) CancellationToken.None)
                                "Should scan"

                        Expect.equal scanned (Some 7) "Should match the queued message"
                    }

                testCaseTask "TryScan 10 on an empty mailbox returns None"
                <| fun () ->
                    task {
                        use cts = new CancellationTokenSource()
                        let! mb, inbox = startAndGetInbox mailboxes cts

                        let! scanned =
                            Expect.completesWithin
                                waitTime
                                ((inbox.TryScan(neverMatches, 10)) CancellationToken.None)
                                "Should return None"

                        Expect.equal scanned None "Should be None"
                    }

                testCaseTask "TryScan's timeout bounds the search, not the matched computation"
                <| fun () ->
                    task {
                        use cts = new CancellationTokenSource()
                        let! mb, inbox = startAndGetInbox mailboxes cts
                        let gate = tcs<unit> ()

                        mb.Post 1

                        let scanner m =
                            if m = 1 then
                                Some(
                                    cancellableTask {
                                        do! gate.Task
                                        return m
                                    }
                                )
                            else
                                None

                        let scanTask = (inbox.TryScan(scanner, 50)) CancellationToken.None

                        do!
                            task {
                                do! Task.Delay 200
                                gate.SetResult()
                            }

                        let! scanned =
                            Expect.completesWithin
                                waitTime
                                scanTask
                                "Should scan despite the slow computation"

                        Expect.equal
                            scanned
                            (Some 1)
                            "Should match despite the computation outlasting the timeout"
                    }

                testCaseTask "TryScan(-2) with a token waits for a message"
                <| fun () ->
                    task {
                        use cts = new CancellationTokenSource()
                        let! mb, inbox = startAndGetInbox mailboxes cts

                        let scanner m =
                            if m = 7 then Some(cancellableTask { return m }) else None

                        let scanTask = (inbox.TryScan(scanner, -2)) CancellationToken.None
                        mb.Post 7

                        let! scanned = Expect.completesWithin waitTime scanTask "Should scan"
                        Expect.equal scanned (Some 7) "Should match the later message"
                    }

                testCaseTask "DefaultTimeout applies to TryScan"
                <| fun () ->
                    task {
                        let received = tcs<int option> ()

                        use mb =
                            Mailboxes.create
                                mailboxes
                                (fun inbox ->
                                    cancellableTask {
                                        let! msg = inbox.TryScan(neverMatches)
                                        received.SetResult msg
                                    }
                                )

                        mb.DefaultTimeout <- 10
                        mb.Start()

                        let! received =
                            Expect.completesWithin waitTime received.Task "Should return None"

                        Expect.equal received None "Should be None"
                    }

                testCaseTask "DefaultTimeout is read when TryScan is called, not when it runs"
                <| fun () ->
                    task {
                        let created = tcs<unit> ()
                        let go = tcs<unit> ()
                        let received = tcs<int option> ()

                        use mb =
                            Mailboxes.create
                                mailboxes
                                (fun inbox ->
                                    cancellableTask {
                                        let scanner m =
                                            if m = 7 then
                                                Some(cancellableTask { return m })
                                            else
                                                None

                                        let tryScan = inbox.TryScan(scanner)
                                        created.SetResult()
                                        do! go.Task
                                        let! msg = tryScan
                                        received.SetResult msg
                                    }
                                )

                        mb.Start()

                        do!
                            Expect.completesWithin
                                waitTime
                                created.Task
                                "Should create the CancellableTask"

                        mb.DefaultTimeout <- 10
                        go.SetResult()
                        do! Task.Delay 100
                        mb.Post 7

                        let! received =
                            Expect.completesWithin waitTime received.Task "Should receive"

                        Expect.equal
                            received
                            (Some 7)
                            "Should still use the timeout from when TryScan was called"
                    }

                testCaseTask "A message posted after TryScan returns None is not lost"
                <| fun () ->
                    task {
                        let timedOut = tcs<unit> ()
                        let received = tcs<int> ()

                        use mb =
                            Mailboxes.create
                                mailboxes
                                (fun inbox ->
                                    cancellableTask {
                                        let! none = inbox.TryScan(neverMatches, 10)
                                        Expect.equal none None "Should be None"
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

                testCaseTask
                    "TryScan races with Post near its timeout"
                    (fun () -> tryScanRacesNearTimeout mailboxes)
                |> testSequenced
            ]

            testList "Cancellation" [
                testCaseTask "Receive is cancelled by the caller's token while waiting"
                <| fun () ->
                    callerCancelWhileWaiting mailboxes (fun inbox -> inbox.Receive(waitTime))

                testCaseTask "TryReceive is cancelled by the caller's token while waiting"
                <| fun () ->
                    callerCancelWhileWaiting mailboxes (fun inbox -> inbox.TryReceive(waitTime))

                testCaseTask "Scan is cancelled by the caller's token while waiting"
                <| fun () ->
                    callerCancelWhileWaiting mailboxes (fun inbox -> inbox.Scan(matchesAll))

                testCaseTask "TryScan is cancelled by the caller's token while waiting"
                <| fun () ->
                    callerCancelWhileWaiting mailboxes (fun inbox -> inbox.TryScan(matchesAll))

                testCaseTask "An already-cancelled Receive does not consume a queued message"
                <| fun () ->
                    alreadyCancelledWithQueuedMessage mailboxes (fun inbox -> inbox.Receive())

                testCaseTask "An already-cancelled TryReceive does not consume a queued message"
                <| fun () ->
                    alreadyCancelledWithQueuedMessage mailboxes (fun inbox -> inbox.TryReceive())

                testCaseTask
                    "An already-cancelled Scan does not consume a queued message or call the scanner"
                <| fun () ->
                    alreadyCancelledScan mailboxes (fun inbox scanner -> inbox.Scan(scanner))

                testCaseTask
                    "An already-cancelled TryScan does not consume a queued message or call the scanner"
                <| fun () ->
                    alreadyCancelledScan mailboxes (fun inbox scanner -> inbox.TryScan(scanner))

                testCaseTask "A message posted after a cancelled Receive is not lost"
                <| fun () ->
                    postAfterCallerCancelIsReceived mailboxes (fun inbox -> inbox.Receive(waitTime))

                testCaseTask "A message posted after a cancelled TryReceive is not lost"
                <| fun () ->
                    postAfterCallerCancelIsReceived
                        mailboxes
                        (fun inbox -> inbox.TryReceive(waitTime))

                testCaseTask "A message posted after a cancelled Scan is not lost"
                <| fun () ->
                    postAfterCallerCancelIsReceived mailboxes (fun inbox -> inbox.Scan(matchesAll))

                testCaseTask "A message posted after a cancelled TryScan is not lost"
                <| fun () ->
                    postAfterCallerCancelIsReceived
                        mailboxes
                        (fun inbox -> inbox.TryScan(matchesAll))

                testCaseTask "The agent's token does not cancel a Receive run on another token"
                <| fun () ->
                    mailboxCancelDoesNotCancelUncancellableReceive
                        mailboxes
                        (fun inbox -> inbox.Receive())
                        id

                testCaseTask "The agent's token does not cancel a TryReceive run on another token"
                <| fun () ->
                    mailboxCancelDoesNotCancelUncancellableReceive
                        mailboxes
                        (fun inbox -> inbox.TryReceive())
                        (function
                        | Some m -> m
                        | None -> failwith "Should receive a message"
                        )

                testCaseTask "The agent's token does not cancel a Scan run on another token"
                <| fun () ->
                    mailboxCancelDoesNotCancelUncancellableReceive
                        mailboxes
                        (fun inbox -> inbox.Scan(matchesAll))
                        id

                testCaseTask "The agent's token does not cancel a TryScan run on another token"
                <| fun () ->
                    mailboxCancelDoesNotCancelUncancellableReceive
                        mailboxes
                        (fun inbox -> inbox.TryScan(matchesAll))
                        (function
                        | Some m -> m
                        | None -> failwith "Should receive a message"
                        )

                testCaseTask "TryReceive handles cancellation token"
                <| fun () ->
                    receiveHandlesCancellation
                        mailboxes
                        (fun inbox ->
                            cancellableTask {
                                match! inbox.TryReceive() with
                                | Some m -> return m
                                | None -> return failwith "Should not time out"
                            }
                        )

                testCaseTask
                    "Receive races with Post near its timeout"
                    (fun () -> receiveRacesNearTimeout mailboxes)
                |> testSequenced

                testCaseTask
                    "TryReceive races with Post near its timeout"
                    (fun () -> tryReceiveRacesNearTimeout mailboxes)
                |> testSequenced
            ]

            testList "Start" [
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
            ]

            testList "CurrentQueueLength" [
                testCase "Counts messages posted before Start"
                <| fun () ->
                    use mb = getSimpleMailbox mailboxes
                    mb.Post Reset
                    mb.Post Reset
                    Expect.equal mb.CurrentQueueLength 2 "Should count queued messages"
            ]

            mailboxTests mailboxes
        ]

    [<Tests>]
    let compatibilityTests =
        testList
            "Compatibility"
            (Mailboxes.all
             |> List.map tests)
