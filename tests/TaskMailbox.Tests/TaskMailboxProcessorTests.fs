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
            testList "Start" [
                testCaseTask "Body runs with the constructor's CancellationToken"
                <| fun () ->
                    task {
                        use cts = new CancellationTokenSource()
                        let token = tcs<CancellationToken> ()

                        use _ =
                            TaskMailboxProcessor<int>
                                .Start(
                                    (fun _ ->
                                        cancellableTask {
                                            let! ct = CancellableTask.getCancellationToken ()
                                            token.SetResult ct
                                        }
                                    ),
                                    cts.Token
                                )

                        let! token = Expect.completesWithin waitTime token.Task "Should run"
                        Expect.equal token cts.Token "Should flow the constructor's token"
                    }

                testCase "Static StartImmediate runs the body before returning"
                <| fun () ->
                    let mutable ran = false

                    use _ =
                        TaskMailboxProcessor<int>.StartImmediate(fun _ ->
                            cancellableTask { ran <- true }
                        )

                    Expect.isTrue ran "Should run inline"
            ]

            testList "Post" [
                testCase "Throws ObjectDisposedException after Dispose when asked to"
                <| fun () ->
                    use mb =
                        TaskMailboxProcessor<int>.Start((fun _ -> cancellableTask { () }), true)

                    mb.Dispose()

                    Expect.throwsT<ObjectDisposedException>
                        (fun () -> mb.Post 1)
                        "Should throw after Dispose"
            ]

            testList "Receive" [
                testCase "Is cold"
                <| fun () ->
                    use mb = idle ()
                    mb.Post 1
                    let _ = mb.Receive()
                    Expect.equal mb.CurrentQueueLength 1 "Should not take a message until run"

                testCaseTask "Can be run outside the body"
                <| fun () ->
                    task {
                        use mb = idle ()
                        mb.Post 1
                        let! msg = mb.Receive () CancellationToken.None
                        Expect.equal msg 1 "Should receive"
                    }

                testCaseTask "Throws TimeoutException when the timeout elapses"
                <| fun () ->
                    task {
                        use mb = idle ()

                        do!
                            Expect.throwsTaskT<TimeoutException>
                                (fun () -> mb.Receive 10 CancellationToken.None :> Task)
                                "Should time out"
                    }

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

                testCaseTask "Does not take a message when the caller cancels"
                <| fun () ->
                    task {
                        use mb = idle ()
                        use cts = new CancellationTokenSource()
                        let receive = mb.Receive () cts.Token
                        cts.Cancel()

                        do!
                            Expect.throwsTaskT<OperationCanceledException>
                                (fun () -> receive :> Task)
                                "Should cancel"

                        mb.Post 1
                        let! msg = mb.Receive 0 CancellationToken.None
                        Expect.equal msg 1 "Should leave the message for the next Receive"
                    }

                testCaseTask "Throws InvalidOperationException when another Receive is waiting"
                <| fun () ->
                    task {
                        use mb = idle ()
                        use cts = new CancellationTokenSource()
                        let first = mb.Receive () cts.Token

                        do!
                            Expect.throwsTaskT<InvalidOperationException>
                                (fun () -> mb.Receive () CancellationToken.None :> Task)
                                "Should allow one reader"

                        mb.Post 1
                        let! msg = first
                        Expect.equal msg 1 "First Receive should still receive"
                    }
            ]
        ]
