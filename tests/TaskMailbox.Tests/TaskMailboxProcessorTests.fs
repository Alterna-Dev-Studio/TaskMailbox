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
