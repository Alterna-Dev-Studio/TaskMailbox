namespace TaskMailbox

/// Contains the <c>mailbox</c> computation expression: a <c>cancellableTask</c> whose <c>return!</c> is a tail call.
[<AutoOpen>]
module MailboxBuilders =

    open System
    open System.Runtime.CompilerServices
    open System.Threading
    open System.Threading.Tasks
    open Microsoft.FSharp.Core.CompilerServices
    open Microsoft.FSharp.Core.CompilerServices.StateMachineHelpers
    open IcedTasks

    /// <exclude/>
    [<Struct; NoComparison; NoEquality>]
    type TailStep<'T> =
        | Done of result: 'T
        | Next of next: (CancellationToken -> Task<TailStep<'T>>)

    /// <exclude/>
    /// AsyncTaskMethodBuilder plus the pending tail call, which SetResult hands to the driver.
    [<Struct; NoComparison; NoEquality>]
    type MailboxMethodBuilder<'T> =
        [<DefaultValue(false)>]
        val mutable Inner: AsyncTaskMethodBuilder<TailStep<'T>>

        [<DefaultValue(false)>]
        val mutable TailCall: CancellationToken -> Task<TailStep<'T>>

        static member Create() : MailboxMethodBuilder<'T> =
            let mutable builder = MailboxMethodBuilder<'T>()
            builder.Inner <- AsyncTaskMethodBuilder<TailStep<'T>>.Create()
            builder

        member this.Start<'S when 'S :> IAsyncStateMachine>(sm: byref<'S>) = this.Inner.Start(&sm)
        member this.SetStateMachine(sm: IAsyncStateMachine) = this.Inner.SetStateMachine sm
        member this.SetException(e: exn) = this.Inner.SetException e

        member this.SetResult(value: 'T) =
            let tailCall = this.TailCall

            if obj.ReferenceEquals(tailCall, null) then
                this.Inner.SetResult(Done value)
            else
                this.TailCall <- Unchecked.defaultof<_>
                this.Inner.SetResult(Next tailCall)

        member this.AwaitOnCompleted<'A, 'S
            when 'A :> INotifyCompletion and 'S :> IAsyncStateMachine>
            (awaiter: byref<'A>, sm: byref<'S>)
            =
            this.Inner.AwaitOnCompleted(&awaiter, &sm)

        member this.AwaitUnsafeOnCompleted<'A, 'S
            when 'A :> ICriticalNotifyCompletion and 'S :> IAsyncStateMachine>
            (awaiter: byref<'A>, sm: byref<'S>)
            =
            this.Inner.AwaitUnsafeOnCompleted(&awaiter, &sm)

        member this.Task: Task<TailStep<'T>> = this.Inner.Task

    /// <exclude/>
    /// The value of a <c>mailbox</c> expression. It is a CancellableTask that runs its chain of tail calls in one loop.
    [<Sealed>]
    type MailboxTask<'T>(step: CancellationToken -> Task<TailStep<'T>>) =
        inherit FSharpFunc<CancellationToken, Task<'T>>()

        member _.Step = step

        static member Drive(step: CancellationToken -> Task<TailStep<'T>>, ct: CancellationToken) =
            task {
                let mutable current = step
                let mutable result = Unchecked.defaultof<'T>
                let mutable finished = false

                while not finished do
                    match! current ct with
                    | Done value ->
                        result <- value
                        finished <- true
                    | Next next -> current <- next

                return result
            }

        override _.Invoke(ct: CancellationToken) : Task<'T> = MailboxTask<'T>.Drive(step, ct)

        // F# does not coerce an FSharpFunc subclass to a function type
        member this.AsCancellableTask: CancellableTask<'T> = unbox (box this)

        static member StepOf(cancellableTask: CancellableTask<'T>) =
            match box cancellableTask with
            | :? MailboxTask<'T> as mailboxTask -> mailboxTask.Step
            | _ ->
                fun ct ->
                    task {
                        let! value = cancellableTask ct
                        return Done value
                    }

    /// <exclude/>
    /// A CancellableTask as seen by <c>return!</c>, so it can be tail called.
    [<Struct; NoComparison; NoEquality>]
    type TailCallable<'T> = { CancellableTask: CancellableTask<'T> }

    /// <summary>
    /// Builds a CancellableTask whose <c>return!</c> of a CancellableTask is a tail call,
    /// so a recursive <c>let rec loop () = mailbox { ... return! loop () }</c> runs in constant stack and memory.
    /// </summary>
    /// <remarks>
    /// As in <c>async</c>, <c>return!</c> inside <c>try</c>, <c>use</c>, a loop, or followed by more code is not a tail call.
    /// Only statically compiled state machines are supported.
    /// </remarks>
    type MailboxBuilder() =
        inherit CancellableTaskBuilderBase()

        /// <exclude/>
        /// Runs any tail call that `code` leaves pending before leaving this scope.
        member inline this.Flush
            (code: CancellableTaskBaseCode<'TOverall, 'U, 'Builder>)
            : CancellableTaskBaseCode<'TOverall, 'U, 'Builder> =
            let mutable pending = true

            CancellableTaskBaseCode<'TOverall, 'U, 'Builder>(fun sm ->
                ResumableCode
                    .Combine(
                        CancellableTaskBaseCode<'TOverall, unit, 'Builder>(fun sm ->
                            pending <- true
                            code.Invoke(&sm)
                        ),
                        ResumableCode.While(
                            (fun () -> pending),
                            CancellableTaskBaseCode<'TOverall, unit, 'Builder>(fun sm ->
                                // Run fixes 'Builder to MailboxMethodBuilder<'TOverall>
                                let next =
                                    (Unsafe.As<'Builder, MailboxMethodBuilder<'TOverall>>(
                                        &sm.Data.MethodBuilder
                                    ))
                                        .TailCall

                                if obj.ReferenceEquals(next, null) then
                                    pending <- false
                                    true
                                else
                                    (Unsafe.As<'Builder, MailboxMethodBuilder<'TOverall>>(
                                        &sm.Data.MethodBuilder
                                    ))
                                        .TailCall <- Unchecked.defaultof<_>

                                    this
                                        .Bind(
                                            getAwaiter =
                                                (fun ct -> Awaitable.GetTaskAwaiter(next ct)),
                                            continuation =
                                                (fun (step: TailStep<'TOverall>) ->
                                                    CancellableTaskBaseCode<
                                                        'TOverall,
                                                        unit,
                                                        'Builder
                                                     >
                                                        (fun sm ->
                                                            match step with
                                                            | Done value ->
                                                                sm.Data.Result <- value
                                                            | Next next ->
                                                                (Unsafe.As<
                                                                    'Builder,
                                                                    MailboxMethodBuilder<
                                                                        'TOverall
                                                                     >
                                                                  >(
                                                                    &sm.Data.MethodBuilder
                                                                ))
                                                                    .TailCall <- next

                                                            true
                                                        )
                                                )
                                        )
                                        .Invoke(&sm)
                            )
                        )
                    )
                    .Invoke(&sm)
            )

        /// <exclude/>
        member inline _.Source(cancellableTask: CancellableTask<'T>) : TailCallable<'T> = {
            CancellableTask = cancellableTask
        }

        /// <exclude/>
        [<NoEagerConstraintApplication>]
        member inline this.Bind
            (
                tailCallable: TailCallable<'T1>,
                continuation: 'T1 -> CancellableTaskBaseCode<'TOverall, 'T2, 'Builder>
            ) : CancellableTaskBaseCode<'TOverall, 'T2, 'Builder> =
            this.Bind(
                getAwaiter = (fun ct -> Awaitable.GetTaskAwaiter(tailCallable.CancellableTask ct)),
                continuation = continuation
            )

        /// <exclude/>
        member inline this.BindReturn
            (tailCallable: TailCallable<'T1>, [<InlineIfLambda>] mapper: 'T1 -> 'T2)
            : CancellableTaskBaseCode<'T2, 'T2, 'Builder> =
            this.Bind(tailCallable, (fun value -> this.Return(mapper value)))

        /// <summary>Tail calls a CancellableTask: the current step ends and the driver runs the next.</summary>
        member inline _.ReturnFrom
            (tailCallable: TailCallable<'T>)
            : CancellableTaskBaseCode<'T, 'T, MailboxMethodBuilder<'T>> =
            CancellableTaskBaseCode<'T, 'T, MailboxMethodBuilder<'T>>(fun sm ->
                sm.Data.MethodBuilder.TailCall <-
                    MailboxTask<'T>.StepOf tailCallable.CancellableTask

                true
            )

        /// <exclude/>
        member inline this.Combine
            (
                task1: CancellableTaskBaseCode<'TOverall, unit, 'Builder>,
                task2: CancellableTaskBaseCode<'TOverall, 'T, 'Builder>
            ) : CancellableTaskBaseCode<'TOverall, 'T, 'Builder> =
            ResumableCode.Combine(this.Flush task1, task2)

        /// <exclude/>
        member inline this.While
            (
                [<InlineIfLambda>] guard: unit -> bool,
                computation: CancellableTaskBaseCode<'TOverall, unit, 'Builder>
            ) : CancellableTaskBaseCode<'TOverall, unit, 'Builder> =
            ResumableCode.While(guard, this.Flush computation)

        /// <exclude/>
        member inline this.TryWith
            (
                computation: CancellableTaskBaseCode<'TOverall, 'T, 'Builder>,
                catchHandler: exn -> CancellableTaskBaseCode<'TOverall, 'T, 'Builder>
            ) : CancellableTaskBaseCode<'TOverall, 'T, 'Builder> =
            // The handler is outside the protected region, so its return! stays a tail call, as in async
            ResumableCode.TryWith(this.Flush computation, catchHandler)

        /// <exclude/>
        member inline this.TryFinally
            (
                computation: CancellableTaskBaseCode<'TOverall, 'T, 'Builder>,
                [<InlineIfLambda>] compensation: unit -> unit
            ) : CancellableTaskBaseCode<'TOverall, 'T, 'Builder> =
            ResumableCode.TryFinally(
                this.Flush computation,
                ResumableCode<_, _>(fun _ ->
                    compensation ()
                    true
                )
            )

        /// <exclude/>
        member inline this.Using
            (
                resource: 'Resource,
                binder: 'Resource -> CancellableTaskBaseCode<'TOverall, 'T, 'Builder>
            ) : CancellableTaskBaseCode<'TOverall, 'T, 'Builder> when 'Resource :> IDisposable =
            ResumableCode.Using(resource, (fun resource -> this.Flush(binder resource)))

        /// <exclude/>
        member inline this.For
            (sequence: seq<'E>, body: 'E -> CancellableTaskBaseCode<'TOverall, unit, 'Builder>)
            : CancellableTaskBaseCode<'TOverall, unit, 'Builder> =
            ResumableCode.For(sequence, (fun element -> this.Flush(body element)))

        /// <summary>Hosts the code in a state machine and returns it as a CancellableTask.</summary>
        member inline _.Run
            (code: CancellableTaskBaseCode<'T, 'T, MailboxMethodBuilder<'T>>)
            : CancellableTask<'T> =
            if __useResumableCode then
                __stateMachine<
                    CancellableTaskBaseStateMachineData<'T, MailboxMethodBuilder<'T>>,
                    CancellableTask<'T>
                 >
                    (MoveNextMethodImpl<_>(fun sm ->
                        __resumeAt sm.ResumptionPoint
                        let mutable __stack_exn = null

                        try
                            let __stack_code_fin = code.Invoke(&sm)

                            if __stack_code_fin then
                                sm.Data.MethodBuilder.SetResult(sm.Data.Result)
                        with exn ->
                            __stack_exn <- exn

                        match __stack_exn with
                        | null -> ()
                        | exn -> sm.Data.MethodBuilder.SetException(exn)
                    ))
                    (SetStateMachineMethodImpl<_>(fun sm state ->
                        sm.Data.MethodBuilder.SetStateMachine(state)
                    ))
                    (AfterCode<_, _>(fun sm ->
                        let sm = sm

                        let step (ct: CancellationToken) : Task<TailStep<'T>> =
                            if ct.IsCancellationRequested then
                                Task.FromCanceled<TailStep<'T>>(ct)
                            else
                                let mutable sm = sm
                                sm.Data.CancellationToken <- ct
                                sm.Data.MethodBuilder <- MailboxMethodBuilder<'T>.Create()
                                sm.Data.MethodBuilder.Start(&sm)
                                sm.Data.MethodBuilder.Task

                        MailboxTask<'T>(step).AsCancellableTask
                    ))
            else
                raise (
                    NotSupportedException(
                        "mailbox { } needs a statically compiled state machine. Warning FS3511 at the call site means it could not be compiled."
                    )
                )

    /// Contains the <c>mailbox</c> computation expression builder.
    [<AutoOpen>]
    module MailboxBuilder =

        /// <summary>
        /// Builds a CancellableTask for an agent body whose <c>return!</c> is a tail call.
        /// </summary>
        let mailbox = MailboxBuilder()
