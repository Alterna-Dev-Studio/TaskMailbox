namespace TaskMailbox.Tests

open System
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
