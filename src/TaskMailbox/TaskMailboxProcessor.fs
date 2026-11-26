namespace TaskMailbox

open System
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open IcedTasks

/// <summary>
/// A message-processing agent that runs a <see cref='T:IcedTasks.CancellableTasks.CancellableTask`1'/> body.
/// A task-native equivalent of <see cref='T:Microsoft.FSharp.Control.FSharpMailboxProcessor`1'/>.
/// </summary>
/// <remarks>
/// Only one <c>Receive</c>, <c>TryReceive</c>, <c>Scan</c>, or <c>TryScan</c> may wait at a time,
/// normally the one in the body's loop.
/// </remarks>
[<Sealed>]
[<AutoSerializable(false)>]
type TaskMailboxProcessor<'Msg>
    /// <summary>Creates an agent. The body does not run until <c>Start</c> or <c>StartImmediate</c>.</summary>
    /// <param name="body">The function that produces the agent's computation.</param>
    /// <param name="isThrowExceptionAfterDisposed">Whether <c>Post</c> throws <see cref='T:System.ObjectDisposedException'/> after <c>Dispose</c>.</param>
    /// <param name="cancellationToken">The token passed to the body.</param>
    (
        body: TaskMailboxProcessor<'Msg> -> CancellableTask<unit>,
        isThrowExceptionAfterDisposed: bool,
        ?cancellationToken: CancellationToken
    ) =

    let hasToken = cancellationToken.IsSome
    let cancellationToken = defaultArg cancellationToken CancellationToken.None

    let arrivals =
        Channel.CreateUnbounded<'Msg>(
            UnboundedChannelOptions(SingleReader = true, SingleWriter = false)
        )

    // Messages a Scan rejected, re-offered to the next reader. Touched only under the `receiving` guard.
    let skipped = ResizeArray<'Msg>()

    // A single-reader unbounded channel cannot count its items, skipped included
    let mutable queueLength = 0
    let mutable defaultTimeout = Timeout.Infinite
    let mutable started = 0
    let mutable disposed = 0
    let mutable receiving = 0
    let errorEvent = Event<Exception>()

    let isDisposed () = Volatile.Read(&disposed) = 1

    let timedOut () =
        TimeoutException("TaskMailboxProcessor.Receive timed out.")

    let scanTimedOut () =
        TimeoutException("TaskMailboxProcessor.Scan timed out.")

    // Only the caller's token or the timeout ends this wait; the reply task never faults, so a
    // caught TimeoutException can only be WaitAsync's own.
    let waitReply (reply: Task<'R>) (timeout: int) (ct: CancellationToken) : Task<'R voption> =
        task {
            try
                let! r =
                    if timeout = Timeout.Infinite then
                        reply.WaitAsync ct
                    else
                        reply.WaitAsync(TimeSpan.FromMilliseconds(float timeout), ct)

                return ValueSome r
            with :? TimeoutException ->
                return ValueNone
        }

    let tryReadArrival () = arrivals.Reader.TryRead()

    let tryRead () =
        match tryReadArrival () with
        | true, msg ->
            Interlocked.Decrement(&queueLength)
            |> ignore

            ValueSome msg
        | _ -> ValueNone

    // A disposed mailbox never yields a message; the wait ends only by cancellation
    let waitForArrival (ct: CancellationToken) : Task =
        if isDisposed () then
            Task.Delay(Timeout.Infinite, ct)
        else
            arrivals.Reader.WaitToReadAsync(ct).AsTask()

    let readAsync (ct: CancellationToken) =
        task {
            let mutable result = ValueNone

            while result.IsNone do
                if not (isDisposed ()) then
                    match tryRead () with
                    | ValueSome _ as msg -> result <- msg
                    | ValueNone -> ()

                if result.IsNone then
                    do! (waitForArrival ct).ConfigureAwait(false)

            return result.Value
        }

    let enterReader () =
        if
            Interlocked.CompareExchange(&receiving, 1, 0)
            <> 0
        then
            raise (
                InvalidOperationException(
                    "Another Receive or Scan is already waiting on this TaskMailboxProcessor."
                )
            )

    let exitReader () = Volatile.Write(&receiving, 0)

    let tryReceive (timeout: int) : CancellableTask<'Msg voption> =
        cancellableTask {
            let! ct = CancellableTask.getCancellationToken ()
            enterReader ()

            try
                let queued =
                    if isDisposed () then
                        skipped.Clear()
                        ValueNone
                    elif skipped.Count > 0 then
                        let msg = skipped[0]
                        skipped.RemoveAt 0

                        Interlocked.Decrement(&queueLength)
                        |> ignore

                        ValueSome msg
                    else
                        tryRead ()

                match queued with
                | ValueSome msg -> return ValueSome msg
                | ValueNone when timeout = 0 -> return ValueNone
                // As FSharp.Core, whose mailbox.fs `waitOne` hands the timeout to a wait handle when it has a token
                | ValueNone when
                    timeout < -1
                    && hasToken
                    ->
                    return
                        raise (
                            ArgumentOutOfRangeException(
                                nameof timeout,
                                timeout,
                                "Must be -1 or greater when the agent has a cancellation token."
                            )
                        )
                | ValueNone when timeout < 0 ->
                    let! msg = readAsync ct
                    return ValueSome msg
                | ValueNone ->
                    use timeoutCts = CancellationTokenSource.CreateLinkedTokenSource ct
                    timeoutCts.CancelAfter timeout

                    try
                        let! msg = readAsync timeoutCts.Token
                        return ValueSome msg
                    with :? OperationCanceledException when not ct.IsCancellationRequested ->
                        return ValueNone
            finally
                exitReader ()
        }

    let scanSkipped (scanner: 'Msg -> CancellableTask<'T> option) =
        let mutable found = ValueNone
        let mutable i = 0

        while found.IsNone
              && i < skipped.Count do
            match scanner skipped[i] with
            | Some computation ->
                skipped.RemoveAt i

                Interlocked.Decrement(&queueLength)
                |> ignore

                found <- ValueSome computation
            | None -> i <- i + 1

        found

    // A disposed mailbox never yields a message; clears skipped too
    let scanArrivals (scanner: 'Msg -> CancellableTask<'T> option) =
        if isDisposed () then
            skipped.Clear()
            ValueNone
        else
            let mutable found = ValueNone
            let mutable keepGoing = true

            while found.IsNone
                  && keepGoing do
                match tryReadArrival () with
                | true, msg ->
                    match
                        (try
                            scanner msg
                         with _ ->
                             skipped.Add msg
                             reraise ())
                    with
                    | Some computation ->
                        Interlocked.Decrement(&queueLength)
                        |> ignore

                        found <- ValueSome computation
                    | None -> skipped.Add msg
                | false, _ -> keepGoing <- false

            found

    let waitForMatch (scanner: 'Msg -> CancellableTask<'T> option) (ct: CancellationToken) =
        task {
            let mutable found = scanArrivals scanner

            while found.IsNone do
                do! (waitForArrival ct).ConfigureAwait(false)
                found <- scanArrivals scanner

            return found.Value
        }

    let tryScan
        (scanner: 'Msg -> CancellableTask<'T> option)
        (timeout: int)
        : CancellableTask<'T voption> =
        cancellableTask {
            let! ct = CancellableTask.getCancellationToken ()

            let! found =
                cancellableTask {
                    enterReader ()

                    try
                        if isDisposed () then
                            skipped.Clear()

                        match scanSkipped scanner with
                        | ValueSome _ as found -> return found
                        | ValueNone ->
                            match scanArrivals scanner with
                            | ValueSome _ as found -> return found
                            | ValueNone when timeout = 0 -> return ValueNone
                            // As FSharp.Core's scanNoTimeout: any negative timeout waits indefinitely, even with a token
                            | ValueNone when timeout < 0 ->
                                let! found = waitForMatch scanner ct
                                return ValueSome found
                            | ValueNone ->
                                use timeoutCts = CancellationTokenSource.CreateLinkedTokenSource ct
                                timeoutCts.CancelAfter timeout

                                try
                                    let! found = waitForMatch scanner timeoutCts.Token
                                    return ValueSome found
                                with :? OperationCanceledException when
                                    timeoutCts.IsCancellationRequested
                                    && not ct.IsCancellationRequested ->
                                    return ValueNone
                    finally
                        exitReader ()
                }

            match found with
            | ValueSome computation ->
                let! result = computation
                return ValueSome result
            | ValueNone -> return ValueNone
        }

    /// <summary>Creates an agent. The body does not run until <c>Start</c> or <c>StartImmediate</c>.</summary>
    /// <param name="body">The function that produces the agent's computation.</param>
    /// <param name="cancellationToken">The token passed to the body.</param>
    new(body, ?cancellationToken: CancellationToken) =
        match cancellationToken with
        | None -> new TaskMailboxProcessor<'Msg>(body, false)
        | Some ct -> new TaskMailboxProcessor<'Msg>(body, false, ct)

    /// <summary>The number of unprocessed messages. An approximation while messages arrive.</summary>
    member _.CurrentQueueLength = if isDisposed () then 0 else Volatile.Read(&queueLength)

    /// <summary>The timeout, in milliseconds, of <c>Receive</c> when none is given. Defaults to <c>Timeout.Infinite</c>.</summary>
    member _.DefaultTimeout
        with get () = defaultTimeout
        and set v = defaultTimeout <- v

    /// <summary>Raised when the body throws. Cancellation of the agent's token is not an error.</summary>
    [<CLIEvent>]
    member _.Error = errorEvent.Publish

    /// Marks the agent as started and returns the guarded body. Throws if already started.
    member private x.PrepareToStart() : CancellableTask<unit> =
        if
            Interlocked.Exchange(&started, 1)
            <> 0
        then
            raise (InvalidOperationException("The TaskMailboxProcessor has already been started."))

        cancellableTask {
            let! ct = CancellableTask.getCancellationToken ()

            if not ct.IsCancellationRequested then
                try
                    do! body x
                with
                | :? OperationCanceledException when ct.IsCancellationRequested -> ()
                | e -> errorEvent.Trigger e
        }

    /// <summary>Starts the agent on the thread pool.</summary>
    /// <exception cref='T:System.InvalidOperationException'>Thrown when the agent has already been started.</exception>
    member x.Start() =
        let run = x.PrepareToStart()

        Task.Run<unit>(fun () -> run cancellationToken)
        |> ignore

    /// <summary>Starts the agent on the current thread, running the body until its first suspension,
    /// then on the thread pool. Never resumes the body on the caller's <c>SynchronizationContext</c>.</summary>
    /// <exception cref='T:System.InvalidOperationException'>Thrown when the agent has already been started.</exception>
    member x.StartImmediate() =
        let run = x.PrepareToStart()
        let context = SynchronizationContext.Current
        SynchronizationContext.SetSynchronizationContext null

        try
            run cancellationToken
            |> ignore
        finally
            SynchronizationContext.SetSynchronizationContext context

    /// <summary>Posts a message to the agent. Messages posted after <c>Dispose</c> are dropped.</summary>
    /// <param name="message">The message to post.</param>
    /// <exception cref='T:System.ObjectDisposedException'>Thrown after <c>Dispose</c> when the agent was created with <c>isThrowExceptionAfterDisposed</c>.</exception>
    member _.Post(message: 'Msg) =
        if isDisposed () then
            if isThrowExceptionAfterDisposed then
                raise (ObjectDisposedException(nameof TaskMailboxProcessor))
        else
            // Counted first so a concurrent read cannot make the count negative
            Interlocked.Increment(&queueLength)
            |> ignore

            // An unbounded channel that is never completed always accepts
            arrivals.Writer.TryWrite message
            |> ignore

    /// Builds a reply channel, posts the message it produces, and returns a task for its reply.
    /// Validates the timeout first; FSharp.Core posts before validating.
    member private x.PostRequest
        (buildMessage: TaskReplyChannel<'Reply> -> 'Msg, timeout: int)
        : Task<'Reply> =
        if timeout < -1 then
            raise (ArgumentOutOfRangeException(nameof timeout, timeout, "Must be -1 or greater."))

        // Unlike Post, a new request always throws after Dispose, regardless of isThrowExceptionAfterDisposed.
        if isDisposed () then
            raise (ObjectDisposedException(nameof TaskMailboxProcessor))

        let tcs =
            TaskCompletionSource<'Reply>(TaskCreationOptions.RunContinuationsAsynchronously)

        let msg =
            buildMessage (
                TaskReplyChannel(fun r ->
                    tcs.TrySetResult r
                    |> ignore
                )
            )

        x.Post msg
        tcs.Task

    /// <summary>Waits for the next message in arrival order.</summary>
    /// <param name="timeout">The timeout in milliseconds. Defaults to <c>DefaultTimeout</c>. Negative waits indefinitely.</param>
    /// <returns>A CancellableTask that takes the next message when run.</returns>
    /// <exception cref='T:System.TimeoutException'>Thrown when the timeout elapses.</exception>
    /// <exception cref='T:System.ArgumentOutOfRangeException'>Thrown when <c>timeout</c> is below -1 on an agent created with a token and the queue is empty.</exception>
    /// <exception cref='T:System.OperationCanceledException'>Thrown when the token is cancelled.</exception>
    /// <exception cref='T:System.InvalidOperationException'>Thrown when another Receive or Scan is waiting.</exception>
    member _.Receive(?timeout: int) : CancellableTask<'Msg> =
        let timeout = defaultArg timeout defaultTimeout

        cancellableTask {
            match! tryReceive timeout with
            | ValueSome msg -> return msg
            | ValueNone -> return raise (timedOut ())
        }

    /// <summary>Like <c>Receive</c>, but returns <c>None</c> when the timeout elapses.</summary>
    /// <param name="timeout">The timeout in milliseconds. Defaults to <c>DefaultTimeout</c>. Negative waits indefinitely.</param>
    /// <returns>A CancellableTask that takes the next message when run, or <c>None</c> on timeout.</returns>
    /// <exception cref='T:System.ArgumentOutOfRangeException'>Thrown when <c>timeout</c> is below -1 on an agent created with a token and the queue is empty.</exception>
    /// <exception cref='T:System.OperationCanceledException'>Thrown when the token is cancelled.</exception>
    /// <exception cref='T:System.InvalidOperationException'>Thrown when another Receive or Scan is waiting.</exception>
    member _.TryReceive(?timeout: int) : CancellableTask<'Msg option> =
        let timeout = defaultArg timeout defaultTimeout

        cancellableTask {
            match! tryReceive timeout with
            | ValueSome msg -> return Some msg
            | ValueNone -> return None
        }

    /// <summary>Waits for a message the scanner accepts, offering skipped messages before arrivals.
    /// A rejected message stays skipped for the next reader.</summary>
    /// <param name="scanner">Returns the computation to run for an accepted message, or <c>None</c> to skip it and keep waiting.</param>
    /// <param name="timeout">Bounds the search, not the computation. Defaults to <c>DefaultTimeout</c>. <c>0</c> checks once; negative waits indefinitely.</param>
    /// <returns>A CancellableTask that takes the matched message's result when run.</returns>
    /// <exception cref='T:System.TimeoutException'>Thrown when the timeout elapses.</exception>
    /// <exception cref='T:System.OperationCanceledException'>Thrown when the token is cancelled.</exception>
    /// <exception cref='T:System.InvalidOperationException'>Thrown when another Receive or Scan is waiting.</exception>
    member _.Scan
        (scanner: 'Msg -> CancellableTask<'T> option, ?timeout: int)
        : CancellableTask<'T> =
        let timeout = defaultArg timeout defaultTimeout

        cancellableTask {
            match! tryScan scanner timeout with
            | ValueSome result -> return result
            | ValueNone -> return raise (scanTimedOut ())
        }

    /// <summary>Like <c>Scan</c>, but returns <c>None</c> when the timeout elapses.</summary>
    /// <param name="scanner">Returns the computation to run for an accepted message, or <c>None</c> to skip it and keep waiting.</param>
    /// <param name="timeout">Bounds the search, not the computation. Defaults to <c>DefaultTimeout</c>. <c>0</c> checks once; negative waits indefinitely.</param>
    /// <returns>A CancellableTask that takes the matched message's result when run, or <c>None</c> on timeout.</returns>
    /// <exception cref='T:System.OperationCanceledException'>Thrown when the token is cancelled.</exception>
    /// <exception cref='T:System.InvalidOperationException'>Thrown when another Receive or Scan is waiting.</exception>
    member _.TryScan
        (scanner: 'Msg -> CancellableTask<'T> option, ?timeout: int)
        : CancellableTask<'T option> =
        let timeout = defaultArg timeout defaultTimeout

        cancellableTask {
            match! tryScan scanner timeout with
            | ValueSome result -> return Some result
            | ValueNone -> return None
        }

    /// <summary>Posts a request and asynchronously waits for its reply, like <c>PostAndAsyncReply</c>
    /// but returning <c>None</c> instead of throwing when the timeout elapses.</summary>
    /// <remarks>
    /// Cold: nothing is posted until the returned CancellableTask runs. Each run builds a fresh
    /// reply channel and message and posts a new request.
    /// </remarks>
    /// <param name="buildMessage">Builds the message to post, given the reply channel.</param>
    /// <param name="timeout">The timeout in milliseconds. Defaults to <c>DefaultTimeout</c>. -1 waits indefinitely.</param>
    /// <returns>A CancellableTask that posts a request and takes its reply when run, or <c>None</c> on timeout.</returns>
    /// <exception cref='T:System.ArgumentOutOfRangeException'>Thrown when <c>timeout</c> is below -1.</exception>
    /// <exception cref='T:System.OperationCanceledException'>Thrown when the caller's token is cancelled.</exception>
    /// <exception cref='T:System.ObjectDisposedException'>Thrown when run after <c>Dispose</c>.</exception>
    member x.PostAndTryAsyncReply
        (buildMessage: TaskReplyChannel<'Reply> -> 'Msg, ?timeout: int)
        : CancellableTask<'Reply option> =
        let timeout = defaultArg timeout defaultTimeout

        cancellableTask {
            let! ct = CancellableTask.getCancellationToken ()
            let reply = x.PostRequest(buildMessage, timeout)

            match! waitReply reply timeout ct with
            | ValueSome r -> return Some r
            | ValueNone -> return None
        }

    /// <summary>Posts a request and asynchronously waits for its reply.</summary>
    /// <remarks>
    /// Cold: nothing is posted until the returned CancellableTask runs. Each run builds a fresh
    /// reply channel and message and posts a new request.
    /// </remarks>
    /// <param name="buildMessage">Builds the message to post, given the reply channel.</param>
    /// <param name="timeout">The timeout in milliseconds. Defaults to <c>DefaultTimeout</c>. -1 waits indefinitely.</param>
    /// <returns>A CancellableTask that posts a request and takes its reply when run.</returns>
    /// <exception cref='T:System.TimeoutException'>Thrown when the timeout elapses.</exception>
    /// <exception cref='T:System.ArgumentOutOfRangeException'>Thrown when <c>timeout</c> is below -1.</exception>
    /// <exception cref='T:System.OperationCanceledException'>Thrown when the caller's token is cancelled.</exception>
    /// <exception cref='T:System.ObjectDisposedException'>Thrown when run after <c>Dispose</c>.</exception>
    member x.PostAndAsyncReply
        (buildMessage: TaskReplyChannel<'Reply> -> 'Msg, ?timeout: int)
        : CancellableTask<'Reply> =
        let timeout = defaultArg timeout defaultTimeout

        cancellableTask {
            let! ct = CancellableTask.getCancellationToken ()
            let reply = x.PostRequest(buildMessage, timeout)

            match! waitReply reply timeout ct with
            | ValueSome r -> return r
            | ValueNone ->
                return raise (TimeoutException("TaskMailboxProcessor.PostAndAsyncReply timed out."))
        }

    /// <summary>Like <c>PostAndReply</c>, but returns <c>None</c> instead of throwing when the
    /// timeout elapses.</summary>
    /// <remarks>Blocks the calling thread until the reply arrives or the timeout elapses. Calling
    /// it from the agent's own body deadlocks, as FSharp.Core.</remarks>
    /// <param name="buildMessage">Builds the message to post, given the reply channel.</param>
    /// <param name="timeout">The timeout in milliseconds. Defaults to <c>DefaultTimeout</c>. -1 waits indefinitely.</param>
    /// <returns>The reply, or <c>None</c> on timeout.</returns>
    /// <exception cref='T:System.ArgumentOutOfRangeException'>Thrown when <c>timeout</c> is below -1.</exception>
    /// <exception cref='T:System.ObjectDisposedException'>Thrown after <c>Dispose</c>.</exception>
    member x.TryPostAndReply
        (buildMessage: TaskReplyChannel<'Reply> -> 'Msg, ?timeout: int)
        : 'Reply option =
        let timeout = defaultArg timeout defaultTimeout
        let reply = x.PostRequest(buildMessage, timeout)

        if reply.Wait timeout then Some reply.Result else None

    /// <summary>Posts a request and blocks the calling thread until its reply arrives.</summary>
    /// <remarks>Calling it from the agent's own body deadlocks, as FSharp.Core.</remarks>
    /// <param name="buildMessage">Builds the message to post, given the reply channel.</param>
    /// <param name="timeout">The timeout in milliseconds. Defaults to <c>DefaultTimeout</c>. -1 waits indefinitely.</param>
    /// <returns>The reply.</returns>
    /// <exception cref='T:System.TimeoutException'>Thrown when the timeout elapses.</exception>
    /// <exception cref='T:System.ArgumentOutOfRangeException'>Thrown when <c>timeout</c> is below -1.</exception>
    /// <exception cref='T:System.ObjectDisposedException'>Thrown after <c>Dispose</c>.</exception>
    member x.PostAndReply(buildMessage: TaskReplyChannel<'Reply> -> 'Msg, ?timeout: int) : 'Reply =
        match x.TryPostAndReply(buildMessage, ?timeout = timeout) with
        | Some r -> r
        | None -> raise (TimeoutException("TaskMailboxProcessor.PostAndReply timed out."))

    /// <summary>Disposes the agent and drops its queued messages. The body is not cancelled.</summary>
    member x.Dispose() = (x :> IDisposable).Dispose()

    interface IDisposable with
        member _.Dispose() = Volatile.Write(&disposed, 1)

    /// <summary>Creates and starts an agent on the thread pool.</summary>
    /// <param name="body">The function that produces the agent's computation.</param>
    /// <param name="cancellationToken">The token passed to the body.</param>
    /// <returns>The started agent.</returns>
    static member Start(body, ?cancellationToken) =
        let mailbox =
            new TaskMailboxProcessor<'Msg>(body, ?cancellationToken = cancellationToken)

        mailbox.Start()
        mailbox

    /// <summary>Creates and starts an agent on the thread pool.</summary>
    /// <param name="body">The function that produces the agent's computation.</param>
    /// <param name="isThrowExceptionAfterDisposed">Whether <c>Post</c> throws <see cref='T:System.ObjectDisposedException'/> after <c>Dispose</c>.</param>
    /// <param name="cancellationToken">The token passed to the body.</param>
    /// <returns>The started agent.</returns>
    static member Start(body, isThrowExceptionAfterDisposed, ?cancellationToken) =
        let mailbox =
            new TaskMailboxProcessor<'Msg>(
                body,
                isThrowExceptionAfterDisposed,
                ?cancellationToken = cancellationToken
            )

        mailbox.Start()
        mailbox

    /// <summary>Creates and starts an agent on the current thread.</summary>
    /// <param name="body">The function that produces the agent's computation.</param>
    /// <param name="cancellationToken">The token passed to the body.</param>
    /// <returns>The started agent.</returns>
    static member StartImmediate(body, ?cancellationToken) =
        let mailbox =
            new TaskMailboxProcessor<'Msg>(body, ?cancellationToken = cancellationToken)

        mailbox.StartImmediate()
        mailbox

    /// <summary>Creates and starts an agent on the current thread.</summary>
    /// <param name="body">The function that produces the agent's computation.</param>
    /// <param name="isThrowExceptionAfterDisposed">Whether <c>Post</c> throws <see cref='T:System.ObjectDisposedException'/> after <c>Dispose</c>.</param>
    /// <param name="cancellationToken">The token passed to the body.</param>
    /// <returns>The started agent.</returns>
    static member StartImmediate(body, isThrowExceptionAfterDisposed, ?cancellationToken) =
        let mailbox =
            new TaskMailboxProcessor<'Msg>(
                body,
                isThrowExceptionAfterDisposed,
                ?cancellationToken = cancellationToken
            )

        mailbox.StartImmediate()
        mailbox
