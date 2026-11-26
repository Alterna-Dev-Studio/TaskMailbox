namespace TaskMailbox.Tests

open System
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks
open Expecto

module TestHelpers =

    /// A SynchronizationContext whose Post enqueues to a queue a test thread pumps with RunOne.
    type SingleThreadContext() =
        inherit SynchronizationContext()

        let queue = new BlockingCollection<SendOrPostCallback * obj>()

        let mutable posts = 0

        /// The number of callbacks posted to this context.
        member _.Posts = Volatile.Read(&posts)

        /// Pumps one queued callback, waiting up to `timeout` ms. Returns whether one ran.
        member _.RunOne(timeout: int) =
            let mutable item = Unchecked.defaultof<SendOrPostCallback * obj>

            if queue.TryTake(&item, timeout) then
                let (d, s) = item
                d.Invoke s
                true
            else
                false

        override _.Post(d, s) =
            Interlocked.Increment(&posts)
            |> ignore

            queue.Add((d, s))

        override this.CreateCopy() = this

    let makeDisposable (callback) =
        { new IDisposable with
            member this.Dispose() = callback ()
        }

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

            if not (SpinWait.SpinUntil((fun () -> n = current), 5000)) then
                failtestf "step %d did not come" n

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
