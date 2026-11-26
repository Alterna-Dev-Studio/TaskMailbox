namespace TaskMailbox.Tests

open System
open System.Threading
open System.Threading.Tasks
open Expecto
open IcedTasks
open TaskMailbox

module TaskMailboxProcessorTests =
    open TestHelpers

    let waitTime = 5000

    let idle () =
        new TaskMailboxProcessor<int>(fun _ -> cancellableTask { () })

    let idleWithToken () =
        new TaskMailboxProcessor<int>((fun _ -> cancellableTask { () }), CancellationToken.None)

    /// A scanner that never matches, typed by use site.
    let neverMatches (_: int) : CancellableTask<'T> option = None

    /// FSharp.Core ignores the caller's token here (mailbox.fs `waitOne`); TaskMailbox cancels.
    let cancelsInfiniteWait
        name
        (call: TaskMailboxProcessor<int> -> CancellationToken -> Task<'T>)
        =
        testCaseTask
            $"Throws OperationCanceledException when the caller cancels an infinite wait ({name})"
        <| fun () ->
            task {
                use mb = idle ()
                use cts = new CancellationTokenSource()
                cts.CancelAfter 10

                do!
                    Expect.throwsTaskT<OperationCanceledException>
                        (fun () ->
                            Expect.completesWithin
                                waitTime
                                (call mb cts.Token)
                                "Should cancel an infinite wait"
                            :> Task
                        )
                        "Should cancel an infinite wait"
            }

    /// TaskMailbox-only: FSharp.Core's mailbox.fs `AsyncHelpers.timeout` passes
    /// `cancellationContinuation = ignore` to `awaitEither`, so a caller cancel under a finite
    /// timeout never completes there.
    let cancelsFiniteWait
        name
        (createMb: unit -> TaskMailboxProcessor<int>)
        (call: TaskMailboxProcessor<int> -> CancellationToken -> Task<'T>)
        =
        testCaseTask
            $"Throws OperationCanceledException when the caller cancels a finite wait ({name})"
        <| fun () ->
            task {
                use mb = createMb ()
                use cts = new CancellationTokenSource()
                cts.CancelAfter 10

                do!
                    Expect.throwsTaskT<OperationCanceledException>
                        (fun () ->
                            Expect.completesWithin
                                waitTime
                                (call mb cts.Token)
                                "Should cancel a finite wait"
                            :> Task
                        )
                        "Should cancel a finite wait"
            }

    [<Tests>]
    let taskMailboxProcessorTests =
        testList "TaskMailboxProcessor" [
            testList "Receive" [
                testCaseTask "Throws OperationCanceledException when the caller cancels"
                <| fun () ->
                    task {
                        use mb = idle ()
                        use cts = new CancellationTokenSource(10)

                        do!
                            Expect.throwsTaskT<OperationCanceledException>
                                (fun () -> mb.Receive waitTime cts.Token :> Task)
                                "Should cancel, not time out"
                    }

                testCaseTask "Throws InvalidOperationException when another Receive is waiting"
                <| fun () ->
                    task {
                        use mb = idle ()
                        let first = mb.Receive () CancellationToken.None

                        do!
                            Expect.throwsTaskT<InvalidOperationException>
                                (fun () -> mb.Receive () CancellationToken.None :> Task)
                                "Should allow one reader"

                        mb.Post 1
                        let! msg = first
                        Expect.equal msg 1 "First Receive should still receive"
                    }

                cancelsInfiniteWait "no timeout" (fun mb ct -> mb.Receive () ct)
                cancelsInfiniteWait "timeout -2" (fun mb ct -> mb.Receive (-2) ct)
            ]

            testList "TryReceive" [
                cancelsInfiniteWait "no timeout" (fun mb ct -> mb.TryReceive () ct)
                cancelsInfiniteWait "timeout -2" (fun mb ct -> mb.TryReceive (-2) ct)
            ]

            testList "Scan" [
                cancelsInfiniteWait "no timeout" (fun mb ct -> mb.Scan (neverMatches) ct)
                cancelsInfiniteWait "timeout -2" (fun mb ct -> mb.Scan (neverMatches, -2) ct)

                cancelsFiniteWait
                    "token-less agent"
                    idle
                    (fun mb ct -> mb.Scan (neverMatches, waitTime) ct)

                cancelsFiniteWait
                    "agent with a token"
                    idleWithToken
                    (fun mb ct -> mb.Scan (neverMatches, waitTime) ct)
            ]

            testList "TryScan" [
                cancelsInfiniteWait "no timeout" (fun mb ct -> mb.TryScan (neverMatches) ct)
                cancelsInfiniteWait "timeout -2" (fun mb ct -> mb.TryScan (neverMatches, -2) ct)

                cancelsFiniteWait
                    "token-less agent"
                    idle
                    (fun mb ct -> mb.TryScan (neverMatches, waitTime) ct)

                cancelsFiniteWait
                    "agent with a token"
                    idleWithToken
                    (fun mb ct -> mb.TryScan (neverMatches, waitTime) ct)
            ]

            testList "Error" [
                testCaseTask
                    "Is raised for OperationCanceledException when the token is not cancelled"
                <| fun () ->
                    task {
                        use mb =
                            new TaskMailboxProcessor<int>(fun _ ->
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
            ]
        ]
