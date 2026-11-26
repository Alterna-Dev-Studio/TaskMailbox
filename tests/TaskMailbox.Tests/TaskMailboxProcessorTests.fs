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
