namespace TaskMailbox.Tests

open System
open System.Threading
open System.Threading.Tasks
open Expecto
open IcedTasks
open TaskMailbox

module MailboxBuilderTests =

    let rec spin n : CancellableTask<int> =
        mailbox { if n = 0 then return 42 else return! spin (n - 1) }

    let rec countdown (order: ResizeArray<string>) n : CancellableTask<int> =
        mailbox {
            do! Task.Yield()
            order.Add $"step{n}"

            if n = 0 then return 0 else return! countdown order (n - 1)
        }

    [<Tests>]
    let mailboxBuilderTests =
        testList "MailboxBuilder" [
            testList "ReturnFrom" [
                testCaseTask "Tail calls do not grow the stack"
                <| fun () ->
                    task {
                        let! result = spin 1_000_000 CancellationToken.None
                        Expect.equal result 42 "Should run 1,000,000 synchronous tail calls"
                    }

                testCaseTask "Can ReturnFrom CancellableTask"
                <| fun () ->
                    task {
                        let inner = cancellableTask { return 7 }
                        let outer = mailbox { return! inner }
                        let! result = outer CancellationToken.None
                        Expect.equal result 7 "Should return the cancellableTask's value"
                    }
            ]

            testList "Binds" [
                testCaseTask "Can Bind Task, ValueTask, Async, and the CancellationToken"
                <| fun () ->
                    task {
                        use cts = new CancellationTokenSource()

                        let binds =
                            mailbox {
                                let! a = Task.FromResult 1
                                let! b = ValueTask<int>(2)
                                let! c = async { return 3 }
                                do! Task.CompletedTask
                                let! ct = CancellableTask.getCancellationToken ()
                                return a + b + c, ct
                            }

                        let! result, token = binds cts.Token

                        Expect.equal result 6 "Should bind each value"
                        Expect.equal token cts.Token "Should flow the token"
                    }
            ]

            testList "TryWith" [
                testCaseTask "Catches an exception from a tail-called computation"
                <| fun () ->
                    task {
                        let failsLater =
                            mailbox {
                                do! Task.Yield()
                                failwith "late"
                                return "unreachable"
                            }

                        let guarded =
                            mailbox {
                                try
                                    return! failsLater
                                with e ->
                                    return
                                        "caught "
                                        + e.Message
                            }

                        let! result = guarded CancellationToken.None

                        Expect.equal result "caught late" "Should catch inside the try"
                    }
            ]

            testList "TryFinally" [
                testCaseTask "Runs after the tail-called chain"
                <| fun () ->
                    task {
                        let order = ResizeArray()

                        let outer =
                            mailbox {
                                try
                                    return! countdown order 2
                                finally
                                    order.Add "finally"
                            }

                        let! result = outer CancellationToken.None

                        Expect.equal result 0 "Should return the chain's value"

                        Expect.sequenceEqual
                            order
                            [
                                "step2"
                                "step1"
                                "step0"
                                "finally"
                            ]
                            "Should run finally last"
                    }
            ]
        ]
