namespace TaskMailbox.Benchmarks

open System
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks

/// Helpers shared by the benchmark classes.
module Agents =

    /// How long a measured method waits before failing instead of hanging.
    let timeout = TimeSpan.FromSeconds 30.0

    /// An agent cancellation token plus what to dispose with it; disposing cancels first.
    [<Sealed>]
    type Scope() =
        let cts = new CancellationTokenSource()
        let items = ResizeArray<IDisposable>()
        let tracked = ResizeArray<Task>()
        member _.Token = cts.Token

        member _.Add(item: IDisposable) = items.Add item

        /// Registers a background task that disposal waits for (bounded) after cancelling.
        member _.Track(task: Task) = tracked.Add task

        interface IDisposable with
            member _.Dispose() =
                cts.Cancel()

                for item in items do
                    item.Dispose()

                try
                    Task.WaitAll(tracked.ToArray(), timeout)
                    |> ignore
                with :? AggregateException as e when
                    e.InnerExceptions
                    |> Seq.forall (fun x -> x :? OperationCanceledException) ->
                    ()

                cts.Dispose()

    /// A gate bodies await until the measured method opens it.
    let newGate () =
        TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)

    /// Completed by a body with the sum of what it received.
    let newDone () =
        TaskCompletionSource<int64>(TaskCreationOptions.RunContinuationsAsynchronously)

    /// The bare channel baseline: one reader, many writers.
    let newChannel<'T> () =
        Channel.CreateUnbounded<'T>(
            UnboundedChannelOptions(SingleReader = true, SingleWriter = false)
        )

    /// Waits for the task, failing after <see cref="timeout"/>.
    let bounded (task: Task<'T>) = task.WaitAsync timeout

    /// Waits for the task, failing after <see cref="timeout"/>.
    let boundedUnit (task: Task) = task.WaitAsync timeout

    /// Fails unless <c>actual</c> is the expected sum of 1..n.
    let checkSum (n: int) (actual: int64) =
        let expected =
            int64 n
            * int64 (n + 1)
            / 2L

        if
            actual
            <> expected
        then
            failwithf "Sum was %d, expected %d" actual expected

        actual
