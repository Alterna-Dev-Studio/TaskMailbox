namespace TaskMailbox.Tests

open System
open System.Collections.Concurrent
open System.Diagnostics
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

    /// Starts an echo agent directly on each implementation, bypassing the IInbox/CancellableTask
    /// bridging: a bridged body's own await would capture the ambient SynchronizationContext itself,
    /// so testing that behaviour needs each implementation's genuine Async/CancellableTask body.
    let startEchoImmediate (mailboxes: IMailboxes) : IDisposable * (int -> int option) =
        if obj.ReferenceEquals(mailboxes, Mailboxes.fsharpCore) then
            let mb =
                MailboxProcessor<AsyncReplyChannel<int>>.StartImmediate(fun inbox ->
                    async {
                        while true do
                            let! r = inbox.Receive()
                            r.Reply 100
                    }
                )

            (mb :> IDisposable), (fun timeout -> mb.TryPostAndReply(id, timeout))
        else
            let mb =
                TaskMailboxProcessor<TaskReplyChannel<int>>.StartImmediate(fun inbox ->
                    cancellableTask {
                        while true do
                            let! (r: TaskReplyChannel<int>) = inbox.Receive()
                            r.Reply 100
                    }
                )

            (mb :> IDisposable), (fun timeout -> mb.TryPostAndReply(id, timeout))

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
            let t = (receive inbox) callerCts.Token
            Expect.isFalse t.IsCompleted "Should be waiting"
            callerCts.Cancel()

            do!
                Expect.throwsTaskT<OperationCanceledException>
                    (fun () -> Expect.completesWithin 1000 t "Should cancel, not time out" :> Task)
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
            let t = (receive inbox) callerCts.Token
            Expect.isFalse t.IsCompleted "Should be waiting"
            callerCts.Cancel()

            do!
                Expect.throwsTaskT<OperationCanceledException>
                    (fun () -> Expect.completesWithin 1000 t "Should cancel" :> Task)
                    "Should cancel"

            mb.Post 9

            let! msg =
                Expect.completesWithin
                    waitTime
                    (inbox.Receive () CancellationToken.None)
                    "Should receive"

            Expect.equal msg 9 "Should receive the message posted after cancel"
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
                                if not (receiveEv.WaitOne waitTime) then
                                    failtestf "receiveEv was not set within %dms" waitTime

                                let! _ = receive inbox

                                finishedEv.Set()
                                |> ignore
                        }
                    )

            let isErrored = firstFrom mb.Error
            mb.Start()
            let deadline = Stopwatch.StartNew()

            let post =
                Task.Run(fun () ->
                    while not cts.IsCancellationRequested
                          && postEv.WaitOne waitTime do
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
                    elif deadline.Elapsed > TimeSpan.FromSeconds 120. then
                        failtestf
                            "Receive races with post did not finish within %A"
                            deadline.Elapsed

            cts.Cancel()

            postEv.Set()
            |> ignore

            do! post.WaitAsync(TimeSpan.FromMilliseconds(float waitTime))
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

    type CounterMsg =
        | Bump of int
        | Zero
        | Get of Replier<int>

    /// Like `getCounter`, but fetched through a request's reply channel rather than a bare TCS.
    let getReplyCounter (mailboxes: IMailboxes) =
        Mailboxes.start
            mailboxes
            (fun inbox ->
                cancellableTask {
                    let mutable n = 0

                    while true do
                        match! inbox.Receive() with
                        | Bump m -> n <- n + m
                        | Zero -> n <- 0
                        | Get reply -> reply.Reply n
                }
            )

    type SlowCounterMsg =
        | SlowBump of int
        | SlowZero
        | SlowGet of Replier<int>

    /// Like `getReplyCounter`, but each message is delayed 100ms before being applied, so a short
    /// timeout can race the queue (MailboxProcessorType.fs `DefaultTimeout`).
    let getSlowCounter (mailboxes: IMailboxes) =
        Mailboxes.create
            mailboxes
            (fun inbox ->
                cancellableTask {
                    let mutable n = 0

                    while true do
                        let! msg = inbox.Receive()
                        do! Task.Delay 100

                        match msg with
                        | SlowBump m -> n <- n + m
                        | SlowZero -> n <- 0
                        | SlowGet reply -> reply.Reply n
                }
            )

    /// Like `compute`, for `CounterMsg`.
    let rec computeCounter (mb: IMailbox<CounterMsg>) n =
        async {
            mb.Post(Bump n)

            if n <> 0 then
                let! res = computeCounter mb (n - 1)
                return n + res
            else
                return 0
        }

    /// The four request members, each returning `None` on timeout instead of throwing.
    let replyCalls: (string * (IMailbox<Replier<int>> -> int -> Task<int option>)) list = [
        "PostAndTryAsyncReply",
        (fun mb timeout -> (mb.PostAndTryAsyncReply(id, timeout)) CancellationToken.None)

        "PostAndAsyncReply",
        (fun mb timeout ->
            task {
                try
                    let! r = (mb.PostAndAsyncReply(id, timeout)) CancellationToken.None
                    return Some r
                with :? TimeoutException ->
                    return None
            }
        )

        "TryPostAndReply", (fun mb timeout -> Task.Run(fun () -> mb.TryPostAndReply(id, timeout)))

        "PostAndReply",
        (fun mb timeout ->
            Task.Run(fun () ->
                try
                    Some(mb.PostAndReply(id, timeout))
                with :? TimeoutException ->
                    None
            )
        )
    ]

    /// The four request members, posting with `timeout` when given. Call and run are wrapped
    /// together so a test passes whether an exception is thrown at the call (FSharp.Core) or
    /// when the returned op runs (TaskMailbox, which is cold). Folding call and run together
    /// means a test using this only sees the exception type, not when or whether anything posted.
    let requestCalls (timeout: int option) : (string * (IMailbox<Replier<int>> -> Task)) list = [
        "PostAndTryAsyncReply",
        (fun mb ->
            task {
                let! _ = (mb.PostAndTryAsyncReply(id, ?timeout = timeout)) CancellationToken.None
                ()
            }
        )

        "PostAndAsyncReply",
        (fun mb ->
            task {
                let! _ = (mb.PostAndAsyncReply(id, ?timeout = timeout)) CancellationToken.None
                ()
            }
        )

        "TryPostAndReply",
        (fun mb ->
            task {
                do!
                    Task.Run(fun () ->
                        mb.TryPostAndReply(id, ?timeout = timeout)
                        |> ignore
                    )
            }
        )

        "PostAndReply",
        (fun mb ->
            task {
                do!
                    Task.Run(fun () ->
                        mb.PostAndReply(id, ?timeout = timeout)
                        |> ignore
                    )
            }
        )
    ]

    /// controlMailbox/test.fsx test6b-test6e, and dotnet/fsharp controlMailbox c32398u7-u9, run
    /// against PostAndReply, TryPostAndReply, PostAndAsyncReply, and PostAndTryAsyncReply.
    let requestReplyTests (mailboxes: IMailboxes) =
        testList "Request/reply" [
            testCaseTask "PostAndAsyncReply returns the echo agent's reply"
            <| fun () ->
                task {
                    use mb =
                        Mailboxes.start
                            mailboxes
                            (fun inbox ->
                                cancellableTask {
                                    while true do
                                        let! (r: Replier<int>) = inbox.Receive()
                                        r.Reply 42
                                }
                            )

                    let! reply =
                        Expect.completesWithin
                            waitTime
                            ((mb.PostAndAsyncReply id) CancellationToken.None)
                            "Should reply"

                    Expect.equal reply 42 "Should get the echoed reply"
                }

            testCaseTask "PostAndTryAsyncReply returns Some for the echo agent's reply"
            <| fun () ->
                task {
                    use mb =
                        Mailboxes.start
                            mailboxes
                            (fun inbox ->
                                cancellableTask {
                                    while true do
                                        let! (r: Replier<int>) = inbox.Receive()
                                        r.Reply 42
                                }
                            )

                    let! reply =
                        Expect.completesWithin
                            waitTime
                            ((mb.PostAndTryAsyncReply id) CancellationToken.None)
                            "Should reply"

                    Expect.equal reply (Some 42) "Should get Some the echoed reply"
                }

            testCaseTask "c32398u8 - two sequential requests each get their own reply"
            <| fun () ->
                task {
                    use mb =
                        Mailboxes.start
                            mailboxes
                            (fun inbox ->
                                cancellableTask {
                                    let! (r1: Replier<int>) = inbox.Receive()
                                    r1.Reply 100
                                    let! (r2: Replier<int>) = inbox.Receive()
                                    r2.Reply 200
                                }
                            )

                    let! reply1 =
                        Expect.completesWithin
                            waitTime
                            ((mb.PostAndAsyncReply id) CancellationToken.None)
                            "Should reply 100"

                    let! reply2 =
                        Expect.completesWithin
                            waitTime
                            ((mb.PostAndAsyncReply id) CancellationToken.None)
                            "Should reply 200"

                    Expect.equal (reply1, reply2) (100, 200) "Should get each reply in order"
                }

            testCaseTask "c32398u9 - a TryReceive-driven agent replies to sequential requests"
            <| fun () ->
                task {
                    use mb =
                        Mailboxes.start
                            mailboxes
                            (fun inbox ->
                                cancellableTask {
                                    match! inbox.TryReceive() with
                                    | Some(r1: Replier<int>) ->
                                        r1.Reply 100

                                        match! inbox.TryReceive() with
                                        | Some(r2: Replier<int>) -> r2.Reply 200
                                        | None -> failwith "Should receive"
                                    | None -> failwith "Should receive"
                                }
                            )

                    let! reply1 =
                        Expect.completesWithin
                            waitTime
                            ((mb.PostAndAsyncReply id) CancellationToken.None)
                            "Should reply 100"

                    let! reply2 =
                        Expect.completesWithin
                            waitTime
                            ((mb.PostAndAsyncReply id) CancellationToken.None)
                            "Should reply 200"

                    Expect.equal (reply1, reply2) (100, 200) "Should get each reply in order"
                }

            testCaseTask "A reply sent from inside a Scan computation reaches the caller"
            <| fun () ->
                task {
                    use mb =
                        Mailboxes.start
                            mailboxes
                            (fun inbox ->
                                cancellableTask {
                                    let! (_: int) =
                                        inbox.Scan(fun (r: Replier<int>) ->
                                            Some(
                                                cancellableTask {
                                                    r.Reply 7
                                                    return 0
                                                }
                                            )
                                        )

                                    ()
                                }
                            )

                    let! reply =
                        Expect.completesWithin
                            waitTime
                            ((mb.PostAndAsyncReply id) CancellationToken.None)
                            "Should reply"

                    Expect.equal reply 7 "Should get the reply sent from the Scan computation"
                }

            testCaseTask
                "Only the first of two replies reaches the caller; the second does not throw"
            <| fun () ->
                task {
                    let secondReplied = tcs<unit> ()

                    use mb =
                        Mailboxes.start
                            mailboxes
                            (fun inbox ->
                                cancellableTask {
                                    let! (r: Replier<int>) = inbox.Receive()
                                    r.Reply 1
                                    r.Reply 2
                                    secondReplied.SetResult()
                                }
                            )

                    let! reply =
                        Expect.completesWithin
                            waitTime
                            ((mb.PostAndAsyncReply id) CancellationToken.None)
                            "Should reply"

                    Expect.equal reply 1 "Should get only the first reply"

                    do!
                        Expect.completesWithin
                            waitTime
                            secondReplied.Task
                            "Second Reply should not throw"
                }

            testCaseTask "test6d - PostAndTryAsyncReply after posts returns the expected total"
            <| fun () ->
                task {
                    use mb = getReplyCounter mailboxes
                    mb.Post Zero

                    let! computations =
                        Async.Parallel(List.map (computeCounter mb) [ 1..50 ])
                        |> Async.StartAsTask

                    let! reply =
                        Expect.completesWithin
                            waitTime
                            ((mb.PostAndTryAsyncReply Get) CancellationToken.None)
                            "Should reply"

                    Expect.equal reply (Some(Array.sum computations)) "Should sum every Bump"
                }

            testCaseTask "test6e - PostAndAsyncReply after posts returns the expected total"
            <| fun () ->
                task {
                    use mb = getReplyCounter mailboxes
                    mb.Post Zero

                    let! computations =
                        Async.Parallel(List.map (computeCounter mb) [ 1..50 ])
                        |> Async.StartAsTask

                    let! reply =
                        Expect.completesWithin
                            waitTime
                            ((mb.PostAndAsyncReply Get) CancellationToken.None)
                            "Should reply"

                    Expect.equal reply (Array.sum computations) "Should sum every Bump"
                }

            testCaseTask "c32398u7 - PostAndReply returns the echo agent's reply"
            <| fun () ->
                task {
                    use mb =
                        Mailboxes.start
                            mailboxes
                            (fun inbox ->
                                cancellableTask {
                                    while true do
                                        let! (r: Replier<int>) = inbox.Receive()
                                        r.Reply 100
                                }
                            )

                    let! reply =
                        Expect.completesWithin
                            waitTime
                            (Task.Run(fun () -> mb.PostAndReply id))
                            "Should reply"

                    Expect.equal reply 100 "Should get the echoed reply"
                }

            testCaseTask "c32398u8 - two sequential PostAndReply calls each get their own reply"
            <| fun () ->
                task {
                    use mb =
                        Mailboxes.start
                            mailboxes
                            (fun inbox ->
                                cancellableTask {
                                    let! (r1: Replier<int>) = inbox.Receive()
                                    r1.Reply 100
                                    let! (r2: Replier<int>) = inbox.Receive()
                                    r2.Reply 200
                                }
                            )

                    let! reply1 =
                        Expect.completesWithin
                            waitTime
                            (Task.Run(fun () -> mb.PostAndReply id))
                            "Should reply 100"

                    let! reply2 =
                        Expect.completesWithin
                            waitTime
                            (Task.Run(fun () -> mb.PostAndReply id))
                            "Should reply 200"

                    Expect.equal (reply1, reply2) (100, 200) "Should get each reply in order"
                }

            testCaseTask
                "c32398u9 - a TryReceive-driven agent replies to sequential PostAndReply calls"
            <| fun () ->
                task {
                    use mb =
                        Mailboxes.start
                            mailboxes
                            (fun inbox ->
                                cancellableTask {
                                    match! inbox.TryReceive() with
                                    | Some(r1: Replier<int>) ->
                                        r1.Reply 100

                                        match! inbox.TryReceive() with
                                        | Some(r2: Replier<int>) -> r2.Reply 200
                                        | None -> failwith "Should receive"
                                    | None -> failwith "Should receive"
                                }
                            )

                    let! reply1 =
                        Expect.completesWithin
                            waitTime
                            (Task.Run(fun () -> mb.PostAndReply id))
                            "Should reply 100"

                    let! reply2 =
                        Expect.completesWithin
                            waitTime
                            (Task.Run(fun () -> mb.PostAndReply id))
                            "Should reply 200"

                    Expect.equal (reply1, reply2) (100, 200) "Should get each reply in order"
                }

            testCaseTask "TryPostAndReply races with DefaultTimeout against a slow queue"
            <| fun () ->
                task {
                    use mb = getSlowCounter mailboxes
                    mb.Start()
                    mb.Post SlowZero
                    mb.Post(SlowBump 1)

                    let! reply =
                        Expect.completesWithin
                            waitTime
                            (Task.Run(fun () -> mb.TryPostAndReply SlowGet))
                            "Should reply"

                    Expect.equal reply (Some 1) "Should reply once the queued messages are applied"

                    mb.DefaultTimeout <- 10
                    mb.Post SlowZero
                    mb.Post(SlowBump 1)

                    let! reply2 =
                        Expect.completesWithin
                            waitTime
                            (Task.Run(fun () -> mb.TryPostAndReply SlowGet))
                            "Should return None"

                    Expect.equal
                        reply2
                        None
                        "Should time out while the queued messages are still being applied"
                }

            testCaseTask "test6b - PostAndReply after posts returns the expected total"
            <| fun () ->
                task {
                    use mb = getReplyCounter mailboxes
                    mb.Post Zero

                    let! computations =
                        Async.Parallel(List.map (computeCounter mb) [ 1..50 ])
                        |> Async.StartAsTask

                    let! reply =
                        Expect.completesWithin
                            waitTime
                            (Task.Run(fun () -> mb.PostAndReply Get))
                            "Should reply"

                    Expect.equal reply (Array.sum computations) "Should sum every Bump"
                }

            testCaseTask "test6c - TryPostAndReply after posts returns the expected total"
            <| fun () ->
                task {
                    use mb = getReplyCounter mailboxes
                    mb.Post Zero

                    let! computations =
                        Async.Parallel(List.map (computeCounter mb) [ 1..50 ])
                        |> Async.StartAsTask

                    let! reply =
                        Expect.completesWithin
                            waitTime
                            (Task.Run(fun () -> mb.TryPostAndReply Get))
                            "Should reply"

                    Expect.equal reply (Some(Array.sum computations)) "Should sum every Bump"
                }

            testCaseTask "PostAndReply's buildMessage exception propagates and posts nothing"
            <| fun () ->
                task {
                    use mb =
                        Mailboxes.create
                            mailboxes
                            (fun inbox ->
                                cancellableTask {
                                    let! (_: Replier<int>) = inbox.Receive()
                                    ()
                                }
                            )

                    let boom = Exception "boom"

                    let! thrown =
                        Expect.completesWithin
                            waitTime
                            (Task.Run(fun () ->
                                try
                                    mb.PostAndReply(fun (_: Replier<int>) -> raise boom)
                                    |> ignore

                                    None
                                with e ->
                                    Some e
                            ))
                            "Should run"

                    match thrown with
                    | Some e ->
                        Expect.isTrue
                            (obj.ReferenceEquals(e, boom))
                            $"Should be buildMessage's own exception, was {e}"
                    | None -> failtest "Should propagate buildMessage's exception"

                    Expect.equal mb.CurrentQueueLength 0 "Should not post anything"
                }

            testCaseTask "TryPostAndReply's buildMessage exception propagates and posts nothing"
            <| fun () ->
                task {
                    use mb =
                        Mailboxes.create
                            mailboxes
                            (fun inbox ->
                                cancellableTask {
                                    let! (_: Replier<int>) = inbox.Receive()
                                    ()
                                }
                            )

                    let boom = Exception "boom"

                    let! thrown =
                        Expect.completesWithin
                            waitTime
                            (Task.Run(fun () ->
                                try
                                    mb.TryPostAndReply(fun (_: Replier<int>) -> raise boom)
                                    |> ignore

                                    None
                                with e ->
                                    Some e
                            ))
                            "Should run"

                    match thrown with
                    | Some e ->
                        Expect.isTrue
                            (obj.ReferenceEquals(e, boom))
                            $"Should be buildMessage's own exception, was {e}"
                    | None -> failtest "Should propagate buildMessage's exception"

                    Expect.equal mb.CurrentQueueLength 0 "Should not post anything"
                }

            // Uniform `Task<int option>` shape over the four calls: None stands for a timeout,
            // whether the call itself returns an option or throws TimeoutException.
            testList "Timeouts" [
                let replyCallsDefault: (string * (IMailbox<Replier<int>> -> Task<int option>)) list = [
                    "PostAndTryAsyncReply",
                    (fun mb -> (mb.PostAndTryAsyncReply id) CancellationToken.None)

                    "PostAndAsyncReply",
                    (fun mb ->
                        task {
                            try
                                let! r = (mb.PostAndAsyncReply id) CancellationToken.None
                                return Some r
                            with :? TimeoutException ->
                                return None
                        }
                    )

                    "TryPostAndReply", (fun mb -> Task.Run(fun () -> mb.TryPostAndReply id))

                    "PostAndReply",
                    (fun mb ->
                        Task.Run(fun () ->
                            try
                                Some(mb.PostAndReply id)
                            with :? TimeoutException ->
                                None
                        )
                    )
                ]

                // Timeout -2 throws ArgumentOutOfRangeException before posting.
                let argExCalls = requestCalls (Some -2)

                /// An agent that never receives, so no reply can race the timeout.
                let idleAgent () =
                    Mailboxes.start
                        mailboxes
                        (fun (_: IInbox<Replier<int>>) -> cancellableTask { () })

                testList "Timeout 0 on an idle agent returns None/throws TimeoutException" [
                    for name, call in replyCalls ->
                        testCaseTask name
                        <| fun () ->
                            task {
                                use mb = idleAgent ()

                                let! result =
                                    Expect.completesWithin waitTime (call mb 0) "Should complete"

                                Expect.equal result None "Should time out"
                            }
                ]

                // controlMailbox/test.fsx timeout_tpar/timeout_tpara/timeout_par/timeout_para: a
                // finite explicit timeout on an agent that never receives.
                testList "Finite timeout on an idle agent returns None/throws TimeoutException" [
                    for name, call in replyCalls ->
                        testCaseTask name
                        <| fun () ->
                            task {
                                use mb = idleAgent ()

                                let! result =
                                    Expect.completesWithin waitTime (call mb 50) "Should complete"

                                Expect.equal result None "Should time out"
                            }
                ]

                testList
                    "Explicit timeout -1 waits for the reply, instead of defaulting or timing out"
                    [
                        for name, call in replyCalls ->
                            testCaseTask name
                            <| fun () ->
                                task {
                                    use mb =
                                        Mailboxes.start
                                            mailboxes
                                            (fun inbox ->
                                                cancellableTask {
                                                    let! (r: Replier<int>) = inbox.Receive()
                                                    r.Reply 42
                                                }
                                            )

                                    let! result =
                                        Expect.completesWithin waitTime (call mb -1) "Should reply"

                                    Expect.equal result (Some 42) "Should get the reply"
                                }
                    ]

                testList
                    "Finite timeout elapses, then a late reply does not throw and the agent still answers"
                    [
                        for name, call in replyCalls ->
                            testCaseTask name
                            <| fun () ->
                                task {
                                    let gate = tcs<unit> ()
                                    let firstReplied = tcs<unit> ()

                                    use mb =
                                        Mailboxes.start
                                            mailboxes
                                            (fun inbox ->
                                                cancellableTask {
                                                    let! (r: Replier<int>) = inbox.Receive()
                                                    do! gate.Task
                                                    r.Reply 42
                                                    firstReplied.SetResult()
                                                    let! (r2: Replier<int>) = inbox.Receive()
                                                    r2.Reply 99
                                                }
                                            )

                                    let! first =
                                        Expect.completesWithin
                                            waitTime
                                            (call mb 20)
                                            "Should time out"

                                    Expect.equal
                                        first
                                        None
                                        "Should time out while the agent is gated"

                                    gate.SetResult()

                                    do!
                                        Expect.completesWithin
                                            waitTime
                                            firstReplied.Task
                                            "The late reply should not throw"

                                    let! second =
                                        Expect.completesWithin
                                            waitTime
                                            (call mb waitTime)
                                            "A following request should still get its reply"

                                    Expect.equal second (Some 99) "Should get the following reply"
                                }
                    ]

                testList "The agent's token cancelling does not end a finite wait early" [
                    for name, call in replyCalls ->
                        testCaseTask name
                        <| fun () ->
                            task {
                                use cts = new CancellationTokenSource()

                                use mb =
                                    mailboxes.Create(
                                        (fun (_: IInbox<Replier<int>>) -> cancellableTask { () }),
                                        false,
                                        cts.Token
                                    )

                                mb.Start()
                                let callTask = call mb 200
                                cts.Cancel()

                                let! result =
                                    Expect.completesWithin waitTime callTask "Should complete"

                                Expect.equal
                                    result
                                    None
                                    "Should time out, not end early from the agent's token"
                            }
                ]

                testList
                    "DefaultTimeout applies, as controlMailbox timeout_t?par_def/timeout_t?para_def"
                    [
                        for name, call in replyCallsDefault ->
                            testCaseTask name
                            <| fun () ->
                                task {
                                    use mb =
                                        Mailboxes.create
                                            mailboxes
                                            (fun (_: IInbox<Replier<int>>) ->
                                                cancellableTask { () }
                                            )

                                    mb.DefaultTimeout <- 10
                                    mb.Start()

                                    let! result =
                                        Expect.completesWithin waitTime (call mb) "Should complete"

                                    Expect.equal result None "Should time out using DefaultTimeout"
                                }
                    ]

                testCaseTask
                    "DefaultTimeout is read when PostAndTryAsyncReply is created, not when it runs"
                <| fun () ->
                    task {
                        use mb =
                            Mailboxes.create
                                mailboxes
                                (fun (_: IInbox<Replier<int>>) -> cancellableTask { () })

                        mb.DefaultTimeout <- 10
                        let op = mb.PostAndTryAsyncReply id
                        mb.DefaultTimeout <- Timeout.Infinite
                        mb.Start()

                        let! result =
                            Expect.completesWithin
                                waitTime
                                (op CancellationToken.None)
                                "Should complete"

                        Expect.equal
                            result
                            None
                            "Should time out using the DefaultTimeout read at creation"
                    }

                testCaseTask
                    "DefaultTimeout is read when PostAndAsyncReply is created, not when it runs"
                <| fun () ->
                    task {
                        use mb =
                            Mailboxes.create
                                mailboxes
                                (fun (_: IInbox<Replier<int>>) -> cancellableTask { () })

                        mb.DefaultTimeout <- 10
                        let op = mb.PostAndAsyncReply id
                        mb.DefaultTimeout <- Timeout.Infinite
                        mb.Start()

                        let! result =
                            Expect.completesWithin
                                waitTime
                                (task {
                                    try
                                        let! r = op CancellationToken.None
                                        return Some r
                                    with :? TimeoutException ->
                                        return None
                                })
                                "Should complete"

                        Expect.equal
                            result
                            None
                            "Should time out using the DefaultTimeout read at creation"
                    }

                testList
                    "Timeout -2 throws ArgumentOutOfRangeException on an idle agent without a token"
                    [
                        for name, call in argExCalls ->
                            testCaseTask name
                            <| fun () ->
                                task {
                                    use mb = idleAgent ()

                                    do!
                                        Expect.throwsTaskT<ArgumentOutOfRangeException>
                                            (fun () ->
                                                Expect.completesWithin
                                                    waitTime
                                                    (task {
                                                        do! call mb
                                                        return ()
                                                    })
                                                    "Should complete"
                                                :> Task
                                            )
                                            "Should throw ArgumentOutOfRangeException"
                                }
                    ]

                testList
                    "Timeout -2 throws ArgumentOutOfRangeException on an idle agent with a token"
                    [
                        for name, call in argExCalls ->
                            testCaseTask name
                            <| fun () ->
                                task {
                                    use mb =
                                        mailboxes.Create(
                                            (fun (_: IInbox<Replier<int>>) ->
                                                cancellableTask { () }
                                            ),
                                            false,
                                            CancellationToken.None
                                        )

                                    mb.Start()

                                    do!
                                        Expect.throwsTaskT<ArgumentOutOfRangeException>
                                            (fun () ->
                                                Expect.completesWithin
                                                    waitTime
                                                    (task {
                                                        do! call mb
                                                        return ()
                                                    })
                                                    "Should complete"
                                                :> Task
                                            )
                                            "Should throw ArgumentOutOfRangeException"
                                }
                    ]
            ]

            // Only the async variants take a caller token; PostAndReply/TryPostAndReply block
            // the calling thread and have no cancellation parameter.
            testList "Cancellation" [
                let asyncCalls
                    : (string * (IMailbox<Replier<int>> -> int -> CancellationToken -> Task<unit>)) list = [
                    "PostAndTryAsyncReply",
                    (fun mb timeout ct ->
                        task {
                            let! _ = (mb.PostAndTryAsyncReply(id, timeout)) ct
                            ()
                        }
                    )

                    "PostAndAsyncReply",
                    (fun mb timeout ct ->
                        task {
                            let! _ = (mb.PostAndAsyncReply(id, timeout)) ct
                            ()
                        }
                    )
                ]

                /// An agent that never receives, so no reply can race the cancellation.
                let unresponsiveAgent withToken =
                    if withToken then
                        mailboxes.Create(
                            (fun (_: IInbox<Replier<int>>) -> cancellableTask { () }),
                            false,
                            CancellationToken.None
                        )
                    else
                        mailboxes.Create(
                            (fun (_: IInbox<Replier<int>>) -> cancellableTask { () }),
                            false
                        )

                let cancelWhileWaiting name withToken timeout =
                    testList name [
                        for callName, call in asyncCalls ->
                            testCaseTask callName
                            <| fun () ->
                                task {
                                    use mb = unresponsiveAgent withToken
                                    mb.Start()
                                    use cts = new CancellationTokenSource()
                                    let t = call mb timeout cts.Token
                                    Expect.isFalse t.IsCompleted "Should be waiting"
                                    cts.Cancel()

                                    do!
                                        Expect.throwsTaskT<OperationCanceledException>
                                            (fun () ->
                                                Expect.completesWithin 1000 t "Should cancel"
                                                :> Task
                                            )
                                            "Should cancel"
                                }
                    ]

                // Never the token-less, infinite-timeout case here: FSharp.Core's mailbox.fs
                // `waitOne` ignores the caller's token on a token-less agent's infinite wait, so
                // that combination would hang FSharp.Core instead of cancelling. It is covered as
                // a TaskMailbox-only test instead (reusing `cancelsInfiniteWait`).
                cancelWhileWaiting "Agent with a token, infinite timeout" true -1
                cancelWhileWaiting "Agent with a token, finite timeout" true waitTime
                cancelWhileWaiting "Token-less agent, finite timeout" false waitTime

                testCaseTask
                    "A late Reply after the caller cancels does not throw, and the next request still succeeds"
                <| fun () ->
                    task {
                        let gate = tcs<unit> ()
                        let replied = tcs<unit> ()

                        use mb =
                            Mailboxes.start
                                mailboxes
                                (fun inbox ->
                                    cancellableTask {
                                        let! (r: Replier<int>) = inbox.Receive()
                                        do! gate.Task
                                        r.Reply 1
                                        replied.SetResult()
                                        let! (r2: Replier<int>) = inbox.Receive()
                                        r2.Reply 2
                                    }
                                )

                        use cts = new CancellationTokenSource()

                        do!
                            Expect.throwsTaskT<OperationCanceledException>
                                (fun () ->
                                    task {
                                        let op = (mb.PostAndAsyncReply(id, waitTime)) cts.Token
                                        cts.Cancel()
                                        let! _ = Expect.completesWithin waitTime op "Should cancel"
                                        ()
                                    }
                                    :> Task
                                )
                                "Should cancel"

                        gate.SetResult()

                        do!
                            Expect.completesWithin
                                waitTime
                                replied.Task
                                "The late Reply should not throw"

                        let! second =
                            Expect.completesWithin
                                waitTime
                                ((mb.PostAndAsyncReply id) CancellationToken.None)
                                "The next request should still succeed"

                        Expect.equal second 2 "Should get the next reply"
                    }
            ]

            testList "Dispose" [
                /// An agent that never receives, so no reply can race the timeout or Dispose.
                let idleAgent () =
                    Mailboxes.start
                        mailboxes
                        (fun (_: IInbox<Replier<int>>) -> cancellableTask { () })

                testList "A reply already in flight still delivers after Dispose" [
                    for name, call in replyCalls ->
                        testCaseTask name
                        <| fun () ->
                            task {
                                let received = tcs<unit> ()
                                let gate = tcs<unit> ()

                                let mb =
                                    Mailboxes.start
                                        mailboxes
                                        (fun inbox ->
                                            cancellableTask {
                                                let! (r: Replier<int>) = inbox.Receive()
                                                received.SetResult()
                                                do! gate.Task
                                                r.Reply 42
                                            }
                                        )

                                let callTask = call mb waitTime

                                do!
                                    Expect.completesWithin
                                        waitTime
                                        received.Task
                                        "Should take the request"

                                mb.Dispose()
                                gate.SetResult()

                                let! result =
                                    Expect.completesWithin waitTime callTask "Should still deliver"

                                Expect.equal result (Some 42) "Should deliver the in-flight reply"
                            }
                ]

                testList
                    "A queued request still waits for its own timeout, not cut short by Dispose"
                    [
                        let asyncCalls2
                            : (string * (IMailbox<Replier<int>> -> int -> Task<int option>)) list = [
                            "PostAndTryAsyncReply",
                            (fun mb timeout ->
                                (mb.PostAndTryAsyncReply(id, timeout)) CancellationToken.None
                            )

                            "PostAndAsyncReply",
                            (fun mb timeout ->
                                task {
                                    try
                                        let! r =
                                            (mb.PostAndAsyncReply(id, timeout))
                                                CancellationToken.None

                                        return Some r
                                    with :? TimeoutException ->
                                        return None
                                }
                            )
                        ]

                        for name, call in asyncCalls2 ->
                            testCaseTask name
                            <| fun () ->
                                task {
                                    use mb = idleAgent ()
                                    // Cold: calling `call` posts the request synchronously, then Dispose.
                                    let callTask = call mb 100
                                    mb.Dispose()

                                    let! result =
                                        Expect.completesWithin
                                            waitTime
                                            callTask
                                            "Should still time out"

                                    Expect.equal result None "Should wait for its own timeout"
                                }

                        let syncCalls2
                            : (string * (IMailbox<Replier<int>> -> int -> Task<int option>)) list = [
                            "TryPostAndReply",
                            (fun mb timeout -> Task.Run(fun () -> mb.TryPostAndReply(id, timeout)))

                            "PostAndReply",
                            (fun mb timeout ->
                                Task.Run(fun () ->
                                    try
                                        Some(mb.PostAndReply(id, timeout))
                                    with :? TimeoutException ->
                                        None
                                )
                            )
                        ]

                        for name, call in syncCalls2 ->
                            testCaseTask name
                            <| fun () ->
                                task {
                                    use mb = idleAgent ()
                                    let callTask = call mb 100

                                    SpinWait.SpinUntil(
                                        (fun () -> mb.CurrentQueueLength > 0),
                                        waitTime
                                    )
                                    |> ignore

                                    mb.Dispose()

                                    let! result =
                                        Expect.completesWithin
                                            waitTime
                                            callTask
                                            "Should still time out"

                                    Expect.equal result None "Should wait for its own timeout"
                                }
                    ]

                testList "New requests throw ObjectDisposedException after Dispose" [
                    let throwingCalls = requestCalls None

                    for name, call in throwingCalls ->
                        testCaseTask name
                        <| fun () ->
                            task {
                                let mb =
                                    mailboxes.Create(
                                        (fun (_: IInbox<Replier<int>>) -> cancellableTask { () }),
                                        true
                                    )

                                mb.Start()
                                mb.Dispose()

                                do!
                                    Expect.throwsTaskT<ObjectDisposedException>
                                        (fun () ->
                                            Expect.completesWithin
                                                waitTime
                                                (task {
                                                    do! call mb
                                                    return ()
                                                })
                                                "Should complete"
                                            :> Task
                                        )
                                        "Should throw ObjectDisposedException"
                            }
                ]

                testList "Returns None, not a reply, when the body faults after taking the request" [
                    testCaseTask "PostAndTryAsyncReply"
                    <| fun () ->
                        task {
                            use mb =
                                Mailboxes.start
                                    mailboxes
                                    (fun inbox ->
                                        cancellableTask {
                                            let! (_: Replier<int>) = inbox.Receive()
                                            failwith "boom"
                                        }
                                    )

                            let! result =
                                Expect.completesWithin
                                    waitTime
                                    ((mb.PostAndTryAsyncReply(id, 100)) CancellationToken.None)
                                    "Should complete"

                            Expect.equal result None "Should not get a reply when the body faults"
                        }

                    testCaseTask "TryPostAndReply"
                    <| fun () ->
                        task {
                            use mb =
                                Mailboxes.start
                                    mailboxes
                                    (fun inbox ->
                                        cancellableTask {
                                            let! (_: Replier<int>) = inbox.Receive()
                                            failwith "boom"
                                        }
                                    )

                            let! result =
                                Expect.completesWithin
                                    waitTime
                                    (Task.Run(fun () -> mb.TryPostAndReply(id, 100)))
                                    "Should complete"

                            Expect.equal result None "Should not get a reply when the body faults"
                        }
                ]
            ]
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

                testCaseTask "Scan handles cancellation token"
                <| fun () ->
                    receiveHandlesCancellation mailboxes (fun inbox -> inbox.Scan(matchesAll))

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
                        let deadline = Stopwatch.StartNew()

                        while Volatile.Read(&count.contents) > 0
                              && deadline.Elapsed < TimeSpan.FromSeconds 30. do
                            let current = mb.CurrentQueueLength
                            Expect.isLessThanOrEqual current queueLength "Should only drain"
                            queueLength <- current
                            do! Task.Delay 10

                        if Volatile.Read(&count.contents) > 0 then
                            failtestf
                                "Did not drain within %A, %d messages remaining"
                                deadline.Elapsed
                                (Volatile.Read(&count.contents))

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
            ]

            testList "Negative timeout" [
                testCaseTask "Receive(-2) without a token waits, like -1"
                <| fun () ->
                    task {
                        for variant in
                            [
                                "queued"
                                "waiting"
                            ] do
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

                            match variant with
                            | "queued" ->
                                mb.Post 7
                                mb.Start()
                            | _ ->
                                mb.StartImmediate()

                                Expect.isFalse
                                    received.Task.IsCompleted
                                    $"Should be waiting ({variant})"

                                mb.Post 7

                            let! received =
                                Expect.completesWithin
                                    waitTime
                                    received.Task
                                    $"Should receive ({variant})"

                            Expect.equal received 7 $"Should receive the message ({variant})"
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

            ]

            testList "TryReceive" [
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

                        let! scanned =
                            Expect.completesWithin
                                waitTime
                                ((inbox.TryScan(matchesAll, 0)) CancellationToken.None)
                                "Should try-scan"

                        Expect.equal scanned None "Should not scan after Dispose"

                        let! received =
                            Expect.completesWithin
                                waitTime
                                (inbox.TryReceive (0) CancellationToken.None)
                                "Should try-receive"

                        Expect.equal received None "Should not receive after Dispose"
                    }

                testCaseTask "Scan after Dispose does not return a message queued before Dispose"
                <| fun () ->
                    task {
                        use cts = new CancellationTokenSource()
                        let! mb, inbox = startAndGetInbox mailboxes cts

                        mb.Post 1

                        mb.Dispose()

                        let! scanned =
                            Expect.completesWithin
                                waitTime
                                ((inbox.TryScan(matchesAll, 0)) CancellationToken.None)
                                "Should try-scan"

                        Expect.equal
                            scanned
                            None
                            "Should not return a message queued before Dispose"

                        Expect.equal inbox.CurrentQueueLength 0 "Should report no queued messages"
                    }

                testCaseTask "Scan matching a skipped message consumes it"
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

                        let scanner m =
                            if m = 1 then Some(cancellableTask { return m }) else None

                        let! scanned =
                            Expect.completesWithin
                                waitTime
                                ((inbox.TryScan(scanner, 0)) CancellationToken.None)
                                "Should try-scan"

                        Expect.equal scanned (Some 1) "Should match the skipped message"

                        Expect.equal
                            inbox.CurrentQueueLength
                            1
                            "Should drop the matched message from the skipped count"

                        let! r2 =
                            Expect.completesWithin
                                waitTime
                                (inbox.Receive () CancellationToken.None)
                                "Should receive"

                        Expect.equal r2 2 "Should receive the other skipped message"
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

                testCaseTask
                    "TryScan races with Post near its timeout"
                    (fun () -> tryScanRacesNearTimeout mailboxes)
                |> testSequenced
            ]

            testList "Cancellation" [
                testCaseTask "Receive is cancelled by the caller's token while waiting"
                <| fun () ->
                    callerCancelWhileWaiting mailboxes (fun inbox -> inbox.Receive(waitTime))

                testCaseTask "Scan is cancelled by the caller's token while waiting"
                <| fun () ->
                    callerCancelWhileWaiting mailboxes (fun inbox -> inbox.Scan(matchesAll))

                testCaseTask "An already-cancelled Receive does not consume a queued message"
                <| fun () ->
                    alreadyCancelledWithQueuedMessage mailboxes (fun inbox -> inbox.Receive())

                testCaseTask
                    "An already-cancelled Scan does not consume a queued message or call the scanner"
                <| fun () ->
                    alreadyCancelledScan mailboxes (fun inbox scanner -> inbox.Scan(scanner))

                testCaseTask "A message posted after a cancelled Receive is not lost"
                <| fun () ->
                    postAfterCallerCancelIsReceived mailboxes (fun inbox -> inbox.Receive(waitTime))

                testCaseTask "A message posted after a cancelled Scan is not lost"
                <| fun () ->
                    postAfterCallerCancelIsReceived mailboxes (fun inbox -> inbox.Scan(matchesAll))

                testCaseTask
                    "Receive races with Post near its timeout"
                    (fun () -> receiveRacesNearTimeout mailboxes)
                |> testSequenced
            ]

            testList "Start" [
                testCase "Cancelling the agent's token does not raise Error"
                <| fun () ->
                    use cts = new CancellationTokenSource()

                    use mb =
                        mailboxes.Create(
                            (fun _ ->
                                cancellableTask {
                                    cts.Cancel()
                                    let! ct = CancellableTask.getCancellationToken ()
                                    ct.ThrowIfCancellationRequested()
                                }
                            ),
                            false,
                            cts.Token
                        )

                    let error = firstFrom mb.Error
                    mb.StartImmediate()
                    Expect.isFalse error.IsCompleted "Should not raise Error"

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

                testCaseTask
                    "StartImmediate does not resume the body on the caller's SynchronizationContext"
                <| fun () ->
                    task {
                        let result = tcs<int option * int option * int> ()

                        let th =
                            Thread(fun () ->
                                try
                                    let ctx = SingleThreadContext()
                                    SynchronizationContext.SetSynchronizationContext ctx

                                    let mb, tryPostAndReply = startEchoImmediate mailboxes
                                    use _ = mb

                                    let reply1 = tryPostAndReply 2000
                                    let reply2 = tryPostAndReply 2000

                                    result.SetResult(reply1, reply2, ctx.Posts)
                                with e ->
                                    result.SetException e
                            )

                        th.IsBackground <- true
                        th.Start()

                        let! (reply1, reply2, posts) =
                            Expect.completesWithin
                                waitTime
                                result.Task
                                "Blocking TryPostAndReply should complete"

                        th.Join()

                        Expect.equal reply1 (Some 100) "Should get the first echoed reply"
                        Expect.equal reply2 (Some 100) "Should get the second echoed reply"

                        Expect.equal
                            posts
                            0
                            "Body should never resume on the caller's SynchronizationContext"
                    }

                testCaseTask "Start does not run the body on the calling thread"
                <| fun () ->
                    task {
                        let callingThreadInfo = tcs<ThreadInfo> ()
                        let bodyThreadInfo = tcs<ThreadInfo> ()

                        use mb =
                            Mailboxes.create
                                mailboxes
                                (fun inbox ->
                                    cancellableTask {
                                        bodyThreadInfo.SetResult(getThreadInfo ())
                                        let! (_: int) = inbox.Receive()
                                        ()
                                    }
                                )

                        let th =
                            Thread(fun () ->
                                callingThreadInfo.SetResult(getThreadInfo ())
                                mb.Start()
                            )

                        th.IsBackground <- true
                        th.Start()

                        if not (th.Join waitTime) then
                            failtestf "Thread did not finish within %dms" waitTime

                        let! bodyThreadInfo =
                            Expect.completesWithin waitTime bodyThreadInfo.Task "Body should run"

                        Expect.notEqual
                            bodyThreadInfo
                            callingThreadInfo.Task.Result
                            "Should not run on the calling thread"
                    }
            ]

            requestReplyTests mailboxes
        ]

    [<Tests>]
    let compatibilityTests =
        testList
            "Compatibility"
            (Mailboxes.all
             |> List.map tests)
