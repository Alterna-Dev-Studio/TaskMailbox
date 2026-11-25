namespace TaskMailbox.Tests

open System
open System.Threading
open System.Threading.Tasks

module TestHelpers =

    let makeDisposable (callback) =
        { new IDisposable with
            member this.Dispose() = callback ()
        }

    let setSyncContext newContext =
        let oldContext = SynchronizationContext.Current
        SynchronizationContext.SetSynchronizationContext newContext
        makeDisposable (fun () -> SynchronizationContext.SetSynchronizationContext oldContext)

    /// A TaskCompletionSource whose continuations do not run inline on the completing thread.
    let tcs<'T> () =
        TaskCompletionSource<'T>(TaskCreationOptions.RunContinuationsAsynchronously)

    /// Completes with the first value raised by `event`.
    let firstFrom (event: IEvent<'T>) =
        let result = tcs<'T> ()

        event.Add(fun value ->
            result.TrySetResult value
            |> ignore
        )

        result.Task

    /// Enforces that `step n` calls happen in order across threads.
    let ordered () =
        let mutable current = 1

        fun n ->
            if n < current then
                failwith $"step {n} already happened"

            SpinWait.SpinUntil(fun () -> n = current)
            current <- n + 1

module Expect =
    open Expecto

    /// Expects `task` to complete within `millisecondsTimeout` and returns its result.
    let completesWithin (millisecondsTimeout: int) (t: Task<'T>) message =
        task {
            let! completed = Task.WhenAny(t, Task.Delay millisecondsTimeout)

            if not (obj.ReferenceEquals(completed, t)) then
                failtestf "%s. Did not complete within %dms" message millisecondsTimeout

            return! t
        }

    /// Expects `t` to still be incomplete after `milliseconds`.
    let staysIncomplete (milliseconds: int) (t: Task) message =
        task {
            do! Task.Delay milliseconds

            if t.IsCompleted then
                failtestf "%s. Completed with status %A" message t.Status
        }

    /// Expects the task `f` returns to fail with `'Exn`.
    let throwsTaskT<'Exn when 'Exn :> exn> (f: unit -> Task) message =
        task {
            let! thrown =
                task {
                    try
                        do! f ()
                        return None
                    with e ->
                        return Some e
                }

            match thrown with
            | Some(:? 'Exn) -> ()
            | Some e -> failtestf "%s. Expected %s, was %O" message typeof<'Exn>.Name e
            | None -> failtestf "%s. Expected %s, nothing was thrown" message typeof<'Exn>.Name
        }
