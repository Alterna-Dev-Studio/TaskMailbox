# TaskMailbox

## What is TaskMailbox?

A task-native [MailboxProcessor](https://fsharp.github.io/fsharp-core-docs/reference/fsharp-control-fsharpmailboxprocessor-1.html). The body is an [IcedTasks](https://github.com/TheAngryByrd/IcedTasks) `CancellableTask` instead of an `Async`, and messages queue in a [System.Threading.Channels](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels) channel.

- `TaskMailboxProcessor<'Msg>` - Same shape and semantics as FSharp.Core's `MailboxProcessor<'Msg>`.
    - `Post`, `Receive`, `Start`, `StartImmediate`, `DefaultTimeout`, `CurrentQueueLength`, `Error`, `Dispose`

## Why should I use this?

- Your agent's body awaits `Task`s and `ValueTask`s, and you don't want to wrap each one in `Async.AwaitTask`.
- You want the context's `CancellationToken` to flow through the body like `Async` does.
- You already know `MailboxProcessor`.

```fsharp
open System.Threading.Tasks
open IcedTasks
open TaskMailbox

type Message =
    | Increment
    | Get of TaskCompletionSource<int>

let counter =
    TaskMailboxProcessor.Start(fun inbox ->
        let rec loop count =
            cancellableTask {
                match! inbox.Receive() with
                | Increment -> return! loop (count + 1)
                | Get reply ->
                    reply.SetResult count
                    return! loop count
            }

        loop 0
    )

counter.Post Increment
```

See [Compatibility with MailboxProcessor](docsSrc/Explanations/Compatibility.md) for where behavior differs.

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
