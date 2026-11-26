namespace TaskMailbox.Tests

open System
open System.Threading
open System.Threading.Tasks
open IcedTasks
open TaskMailbox

/// The receiving side of a mailbox, as seen by its body.
type IInbox<'Msg> =
    abstract Receive: ?timeout: int -> CancellableTask<'Msg>
    abstract TryReceive: ?timeout: int -> CancellableTask<'Msg option>

    abstract Scan:
        scanner: ('Msg -> CancellableTask<'T> option) * ?timeout: int -> CancellableTask<'T>

    abstract TryScan:
        scanner: ('Msg -> CancellableTask<'T> option) * ?timeout: int -> CancellableTask<'T option>

    abstract CurrentQueueLength: int

/// A test-only reply channel: `AsyncReplyChannel` can't be constructed directly, so messages in
/// compatibility tests carry a `Replier` instead, built from either implementation's own channel.
type Replier<'T>(reply: 'T -> unit) =
    member _.Reply(value: 'T) = reply value

/// The owning side of a mailbox.
type IMailbox<'Msg> =
    inherit IDisposable
    abstract Post: 'Msg -> unit
    abstract Start: unit -> unit
    abstract StartImmediate: unit -> unit
    abstract CurrentQueueLength: int
    abstract DefaultTimeout: int with get, set
    abstract Error: IEvent<exn>

    abstract PostAndAsyncReply:
        buildMessage: (Replier<'R> -> 'Msg) * ?timeout: int -> CancellableTask<'R>

    abstract PostAndTryAsyncReply:
        buildMessage: (Replier<'R> -> 'Msg) * ?timeout: int -> CancellableTask<'R option>

/// Creates mailboxes of one implementation so a scenario can run against each.
type IMailboxes =
    abstract Name: string

    abstract Create:
        body: (IInbox<'Msg> -> CancellableTask<unit>) *
        isThrowExceptionAfterDisposed: bool *
        ?cancellationToken: CancellationToken ->
            IMailbox<'Msg>

module Mailboxes =

    /// Runs a CancellableTask as an Async the way FSharp.Core sees an Async body:
    /// a fault is an exception, and cancellation only when the token is cancelled.
    let private toAsync (body: CancellableTask<'T>) =
        async {
            let! ct = Async.CancellationToken
            let t = body ct

            return!
                Async.FromContinuations(fun (ok, error, cancel) ->
                    t.ContinueWith(
                        (fun (t: Task<'T>) ->
                            try
                                t.GetAwaiter().GetResult()
                                |> ok
                            with
                            | :? OperationCanceledException as e when ct.IsCancellationRequested ->
                                cancel e
                            | e -> error e
                        ),
                        TaskContinuationOptions.ExecuteSynchronously
                    )
                    |> ignore
                )
        }

    /// FSharp.Core MailboxProcessor, the behavioral oracle.
    let fsharpCore =
        { new IMailboxes with
            member _.Name = "FSharp.Core MailboxProcessor"

            member _.Create(body, isThrowExceptionAfterDisposed, ?cancellationToken) =
                let inbox (mb: MailboxProcessor<_>) =
                    { new IInbox<_> with
                        member _.Receive(?timeout) =
                            let receive = mb.Receive(?timeout = timeout)
                            fun ct -> Async.StartImmediateAsTask(receive, ct)

                        member _.TryReceive(?timeout) =
                            let tryReceive = mb.TryReceive(?timeout = timeout)
                            fun ct -> Async.StartImmediateAsTask(tryReceive, ct)

                        member _.Scan(scanner, ?timeout) =
                            let scan =
                                mb.Scan(
                                    scanner
                                    >> Option.map toAsync,
                                    ?timeout = timeout
                                )

                            fun ct -> Async.StartImmediateAsTask(scan, ct)

                        member _.TryScan(scanner, ?timeout) =
                            let tryScan =
                                mb.TryScan(
                                    scanner
                                    >> Option.map toAsync,
                                    ?timeout = timeout
                                )

                            fun ct -> Async.StartImmediateAsTask(tryScan, ct)

                        member _.CurrentQueueLength = mb.CurrentQueueLength
                    }

                let mb =
                    new MailboxProcessor<_>(
                        (fun mb -> toAsync (body (inbox mb))),
                        isThrowExceptionAfterDisposed,
                        ?cancellationToken = cancellationToken
                    )

                { new IMailbox<_> with
                    member _.Post msg = mb.Post msg
                    member _.Start() = mb.Start()
                    member _.StartImmediate() = mb.StartImmediate()
                    member _.CurrentQueueLength = mb.CurrentQueueLength

                    member _.DefaultTimeout
                        with get () = mb.DefaultTimeout
                        and set v = mb.DefaultTimeout <- v

                    member _.Error = mb.Error
                    member _.Dispose() = (mb :> IDisposable).Dispose()

                    // FSharp.Core is hot: the Async is built, and posts, at call time.
                    member _.PostAndAsyncReply(build, ?timeout) =
                        let a =
                            mb.PostAndAsyncReply(
                                (fun ch -> build (Replier ch.Reply)),
                                ?timeout = timeout
                            )

                        fun ct -> Async.StartImmediateAsTask(a, ct)

                    member _.PostAndTryAsyncReply(build, ?timeout) =
                        let a =
                            mb.PostAndTryAsyncReply(
                                (fun ch -> build (Replier ch.Reply)),
                                ?timeout = timeout
                            )

                        fun ct -> Async.StartImmediateAsTask(a, ct)
                }
        }

    /// TaskMailboxProcessor, the implementation under test.
    let taskMailbox =
        { new IMailboxes with
            member _.Name = "TaskMailboxProcessor"

            member _.Create(body, isThrowExceptionAfterDisposed, ?cancellationToken) =
                let inbox (mb: TaskMailboxProcessor<_>) =
                    { new IInbox<_> with
                        member _.Receive(?timeout) = mb.Receive(?timeout = timeout)
                        member _.TryReceive(?timeout) = mb.TryReceive(?timeout = timeout)
                        member _.Scan(scanner, ?timeout) = mb.Scan(scanner, ?timeout = timeout)

                        member _.TryScan(scanner, ?timeout) =
                            mb.TryScan(scanner, ?timeout = timeout)

                        member _.CurrentQueueLength = mb.CurrentQueueLength
                    }

                let mb =
                    new TaskMailboxProcessor<_>(
                        (fun mb -> body (inbox mb)),
                        isThrowExceptionAfterDisposed,
                        ?cancellationToken = cancellationToken
                    )

                { new IMailbox<_> with
                    member _.Post msg = mb.Post msg
                    member _.Start() = mb.Start()
                    member _.StartImmediate() = mb.StartImmediate()
                    member _.CurrentQueueLength = mb.CurrentQueueLength

                    member _.DefaultTimeout
                        with get () = mb.DefaultTimeout
                        and set v = mb.DefaultTimeout <- v

                    member _.Error = mb.Error
                    member _.Dispose() = (mb :> IDisposable).Dispose()

                    member _.PostAndAsyncReply(build, ?timeout) =
                        mb.PostAndAsyncReply(
                            (fun ch -> build (Replier ch.Reply)),
                            ?timeout = timeout
                        )

                    member _.PostAndTryAsyncReply(build, ?timeout) =
                        mb.PostAndTryAsyncReply(
                            (fun ch -> build (Replier ch.Reply)),
                            ?timeout = timeout
                        )
                }
        }

    let all = [
        fsharpCore
        taskMailbox
    ]

    let create (mailboxes: IMailboxes) body = mailboxes.Create(body, false)

    let start (mailboxes: IMailboxes) body =
        let mb = create mailboxes body
        mb.Start()
        mb
