namespace TaskMailbox.Tests

open System
open System.Threading
open System.Threading.Tasks
open IcedTasks
open TaskMailbox

/// The receiving side of a mailbox, as seen by its body.
type IInbox<'Msg> =
    abstract Receive: ?timeout: int -> CancellableTask<'Msg>
    abstract CurrentQueueLength: int

/// The owning side of a mailbox.
type IMailbox<'Msg> =
    inherit IDisposable
    abstract Post: 'Msg -> unit
    abstract Start: unit -> unit
    abstract StartImmediate: unit -> unit
    abstract CurrentQueueLength: int
    abstract DefaultTimeout: int with get, set
    abstract Error: IEvent<exn>

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
    let private toAsync (body: CancellableTask<unit>) =
        async {
            let! ct = Async.CancellationToken
            let t = body ct

            return!
                Async.FromContinuations(fun (ok, error, cancel) ->
                    t.ContinueWith(
                        (fun (t: Task<unit>) ->
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
