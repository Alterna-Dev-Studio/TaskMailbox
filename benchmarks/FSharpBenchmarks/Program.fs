open System.IO
open System.Reflection
open BenchmarkDotNet.Running
open TaskMailbox.Benchmarks

[<EntryPoint>]
let main argv =
    let quick = Array.contains "--quick" argv
    let args = Array.filter ((<>) "--quick") argv

    // Keep results beside the benchmarks whatever the working directory; a config's ArtifactsPath is ignored by the switcher.
    let args =
        if Array.contains "--artifacts" args then
            args
        else
            Array.append args [|
                "--artifacts"
                Path.Combine(__SOURCE_DIRECTORY__, "BenchmarkDotNet.Artifacts")
            |]

    BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args, MailboxConfig quick)
    |> ignore

    0
