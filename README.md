# TaskMailbox

## What is TaskMailbox?

A task-native [MailboxProcessor](https://fsharp.github.io/fsharp-core-docs/reference/fsharp-control-fsharpmailboxprocessor-1.html). The body is an [IcedTasks](https://github.com/TheAngryByrd/IcedTasks) `CancellableTask` instead of an `Async`, and messages queue in a [System.Threading.Channels](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels) channel.

- `TaskMailboxProcessor<'Msg>` - Same shape and semantics as FSharp.Core's `MailboxProcessor<'Msg>`.
    - `Post`, `Receive`, `TryReceive`, `Scan`, `TryScan`, `Start`, `StartImmediate`, `DefaultTimeout`, `CurrentQueueLength`, `Error`, `Dispose`
    - `PostAndReply`, `TryPostAndReply`, `PostAndAsyncReply`, `PostAndTryAsyncReply`
- `TaskReplyChannel<'Reply>` - The reply channel the `PostAnd*Reply` methods pass to `buildMessage`.
- `mailbox` - A `cancellableTask` whose `return!` is a tail call, so a recursive agent loop runs in constant stack and memory.

## Why should I use this?

- Your agent's body awaits `Task`s and `ValueTask`s, and you don't want to wrap each one in `Async.AwaitTask`.
- You want the context's `CancellationToken` to flow through the body like `Async` does.
- You already know `MailboxProcessor`.

```fsharp
open System.Threading
open IcedTasks
open TaskMailbox

type Message =
    | Increment
    | Get of TaskReplyChannel<int>

let counter =
    TaskMailboxProcessor.Start(fun inbox ->
        let rec loop count =
            mailbox {
                match! inbox.Receive() with
                | Increment -> return! loop (count + 1)
                | Get reply ->
                    reply.Reply count
                    return! loop count
            }

        loop 0
    )

counter.Post Increment

// Cold: posts the request when run
let getCount = counter.PostAndAsyncReply Get
let count = getCount CancellationToken.None
```

Write recursive loops with `mailbox`, not `cancellableTask`: a task's `return!` is not a tail call, so each message would grow the stack and keep memory.

`Scan` waits for a message a scanner matches, leaving messages it skips queued for later:

```fsharp
type Message =
    | Work of int
    | Stop

let worker =
    TaskMailboxProcessor.Start(fun inbox ->
        mailbox {
            do! inbox.Scan(function
                | Stop -> Some(cancellableTask { return () })
                | _ -> None)
        }
    )

worker.Post(Work 1)
worker.Post Stop
```

See [Compatibility with MailboxProcessor](docsSrc/Explanations/Compatibility.md) for where behavior differs.
See [Choosing TaskMailbox](docsSrc/Explanations/Choosing-TaskMailbox.md) for how it compares with other lightweight options.

---

### Developing

Install the [.NET SDK](https://dotnet.microsoft.com/download) version that `global.json` specifies. The tests also run on .NET 8.0, so install that runtime too.

---

### Environment Variables

- `CONFIGURATION` sets the [configuration](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-build#options) of the dotnet commands. The default is `Debug`. When the `DotnetPack` target runs, the default is `Release`.
- `CI` makes the build run in CI mode. Restore retries on failure, and `CheckFormatCode` only checks the format.

---

### Building

```sh
> build.cmd <optional buildtarget> // on windows
$ ./build.sh  <optional buildtarget>// on unix
```

The default target is `DotnetPack`. It runs `Clean`, `DotnetRestore`, `CheckFormatCode`, `DotnetBuild`, `DotnetTest`, and `DotnetPack`, and writes the package to `dist/`.

---

### Build Targets

- `Clean` - Cleans the `bin`, `temp`, and `dist` directories, and the `bin` and `obj` directories of the source and test projects.
- `DotnetRestore` - Runs [dotnet restore](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-restore) on the solution file.
- `CheckFormatCode` - On CI, runs [Fantomas](https://github.com/fsprojects/fantomas) with `--check` and fails if a file needs formatting. Locally, formats the code.
- `DotnetBuild` - Runs [dotnet build](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-build) on the solution file.
- `DotnetTest` - Runs [dotnet test](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test) on the solution file.
- `WatchTests` - Runs [dotnet watch](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-watch) test on each test project.
- `DotnetPack` - Runs [dotnet pack](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-pack) and writes the packages to `dist/`.
- `FormatCode` - Runs [Fantomas](https://github.com/fsprojects/fantomas) on all F# files.
- `BuildDocs` - Generates the documentation from `docsSrc` and the [XML documentation comments](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/xml-documentation) in `src`.
- `WatchDocs` - Generates the documentation and starts a local web server.
