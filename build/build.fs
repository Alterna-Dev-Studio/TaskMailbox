open System
open Fake.Core
open Fake.DotNet
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open Fake.Core.TargetOperators
open Fake.BuildServer

let environVarAsBoolOrDefault varName defaultValue =
    let truthyConsts = [
        "1"
        "Y"
        "YES"
        "T"
        "TRUE"
    ]

    try
        let envvar = (Environment.environVar varName).ToUpper()

        truthyConsts
        |> List.exists ((=) envvar)
    with _ ->
        defaultValue

//-----------------------------------------------------------------------------
// Metadata and Configuration
//-----------------------------------------------------------------------------

let rootDirectory =
    __SOURCE_DIRECTORY__
    </> ".."

let sln =
    rootDirectory
    </> "TaskMailbox.slnx"

let srcGlob =
    rootDirectory
    </> "src/**/*.??proj"

let testsGlob =
    rootDirectory
    </> "tests/**/*.??proj"

let distDir =
    rootDirectory
    </> "dist"

let docsDir =
    rootDirectory
    </> "docs"

let docsSrcDir =
    rootDirectory
    </> "docsSrc"

let changelogPath =
    rootDirectory
    </> "CHANGELOG.md"

let changelog = Fake.Core.Changelog.load changelogPath

let latestEntry =
    if Seq.isEmpty changelog.Entries then
        Changelog.ChangelogEntry.New("0.0.1", "0.0.1-alpha.1", Some DateTime.Today, None, [], false)
    else
        changelog.LatestEntry

//-----------------------------------------------------------------------------
// Helpers
//-----------------------------------------------------------------------------

let isRelease (targets: Target list) =
    targets
    |> Seq.map (fun t -> t.Name)
    |> Seq.exists ((=) "DotnetPack")

let invokeAsync f = async { f () }

let configuration (targets: Target list) =
    let defaultVal = if isRelease targets then "Release" else "Debug"

    match Environment.environVarOrDefault "CONFIGURATION" defaultVal with
    | "Debug" -> DotNet.BuildConfiguration.Debug
    | "Release" -> DotNet.BuildConfiguration.Release
    | config -> DotNet.BuildConfiguration.Custom config

let failOnBadExitAndPrint (p: ProcessResult) =
    if
        p.ExitCode
        <> 0
    then
        p.Errors
        |> Seq.iter Trace.traceError

        failwithf "failed with exitcode %d" p.ExitCode

let isCI = lazy (environVarAsBoolOrDefault "CI" false)

// CI Servers can have bizarre failures that have nothing to do with your code
let rec retryIfInCI times fn =
    match isCI.Value with
    | true ->
        if times > 1 then
            try
                fn ()
            with _ ->
                retryIfInCI (times - 1) fn
        else
            fn ()
    | _ -> fn ()

module dotnet =
    let watch cmdParam program args =
        DotNet.exec cmdParam (sprintf "watch %s" program) args

    let tool optionConfig command args =
        DotNet.exec optionConfig (sprintf "%s" command) args
        |> failOnBadExitAndPrint

    let fantomas args = DotNet.exec id "fantomas" args

    let fsdocs args = tool id "fsdocs" args

//-----------------------------------------------------------------------------
// Target Implementations
//-----------------------------------------------------------------------------

let clean _ =
    [
        "bin"
        "temp"
        distDir
    ]
    |> Shell.cleanDirs

    !!srcGlob
    ++ testsGlob
    |> Seq.collect (fun p ->
        [
            "bin"
            "obj"
        ]
        |> Seq.map (fun sp ->
            IO.Path.GetDirectoryName p
            </> sp
        )
    )
    |> Shell.cleanDirs

let dotnetRestore _ =
    [ sln ]
    |> Seq.map (fun dir -> fun () -> DotNet.restore id dir)
    |> Seq.iter (retryIfInCI 10)

let dotnetBuild ctx =
    DotNet.build
        (fun c -> {
            c with
                Configuration = configuration (ctx.Context.AllExecutingTargets)
                NoRestore = true
                MSBuildParams = {
                    c.MSBuildParams with
                        Properties = [ "PackageVersion", latestEntry.NuGetVersion ]
                }
        })
        sln

let dotnetTest ctx =
    DotNet.test
        (fun c -> {
            c with
                Configuration = configuration (ctx.Context.AllExecutingTargets)
                NoBuild = true
                Logger = Some "trx;LogFilePrefix=testResults"
        })
        sln

let watchTests _ =
    !!testsGlob
    |> Seq.map (fun proj ->
        fun () ->
            dotnet.watch
                (fun opt ->
                    opt
                    |> DotNet.Options.withWorkingDirectory (IO.Path.GetDirectoryName proj)
                    |> DotNet.Options.withVerbosity (Some DotNet.Verbosity.Quiet)
                )
                "test"
                ""
            |> ignore
    )
    |> Seq.iter (
        invokeAsync
        >> Async.Catch
        >> Async.Ignore
        >> Async.Start
    )

    printfn "Press Ctrl+C (or Ctrl+Break) to stop..."

    let cancelEvent =
        Console.CancelKeyPress
        |> Async.AwaitEvent
        |> Async.RunSynchronously

    cancelEvent.Cancel <- true

let dotnetPack ctx =
    DotNet.pack
        (fun c -> {
            c with
                Configuration = configuration (ctx.Context.AllExecutingTargets)
                OutputPath = Some distDir
                MSBuildParams = {
                    c.MSBuildParams with
                        Properties = [ "PackageVersion", latestEntry.NuGetVersion ]
                }
        })
        sln

let formatCode _ =
    let result = dotnet.fantomas $"{rootDirectory}"

    if not result.OK then
        printfn "Errors while formatting all files: %A" result.Messages

let checkFormatCode ctx =
    if isCI.Value then
        let result = dotnet.fantomas $"{rootDirectory} --check"

        if result.ExitCode = 0 then
            Trace.log "No files need formatting"
        elif result.ExitCode = 99 then
            failwith "Some files need formatting, check output for more info"
        else
            Trace.logf "Errors while formatting: %A" result.Errors
    else
        formatCode ctx

let fsdocsArgs ctx =
    let configuration = configuration (ctx.Context.AllExecutingTargets)

    $"--input {docsSrcDir} --output {docsDir} --properties Configuration={configuration}"

let buildDocs ctx =
    dotnet.fsdocs $"build --clean --eval {fsdocsArgs ctx}"

let watchDocs ctx =
    dotnet.fsdocs $"watch --eval {fsdocsArgs ctx}"

let initTargets () =
    BuildServer.install [ GitHubActions.Installer ]

    /// Defines a dependency - y is dependent on x. Finishes the chain.
    let (==>!) x y =
        x ==> y
        |> ignore

    /// Defines a soft dependency. x must run before y, if it is present, but y does not require x to be run. Finishes the chain.
    let (?=>!) x y =
        x ?=> y
        |> ignore

    //-----------------------------------------------------------------------------
    // Target Declaration
    //-----------------------------------------------------------------------------

    Target.create "Clean" clean
    Target.create "DotnetRestore" dotnetRestore
    Target.create "DotnetBuild" dotnetBuild
    Target.create "DotnetTest" dotnetTest
    Target.create "WatchTests" watchTests
    Target.create "DotnetPack" dotnetPack
    Target.create "FormatCode" formatCode
    Target.create "CheckFormatCode" checkFormatCode
    Target.create "BuildDocs" buildDocs
    Target.create "WatchDocs" watchDocs

    //-----------------------------------------------------------------------------
    // Target Dependencies
    //-----------------------------------------------------------------------------

    // Only call Clean if DotnetPack was in the call chain
    // Ensure Clean is called before DotnetRestore
    "Clean"
    ?=>! "DotnetRestore"

    "Clean"
    ==>! "DotnetPack"

    "DotnetBuild"
    ==>! "BuildDocs"

    "DotnetBuild"
    ==>! "WatchDocs"

    "DotnetRestore"
    ==> "CheckFormatCode"
    ==> "DotnetBuild"
    ==> "DotnetTest"
    ==>! "DotnetPack"

    "DotnetRestore"
    ==>! "WatchTests"

//-----------------------------------------------------------------------------
// Target Start
//-----------------------------------------------------------------------------
[<EntryPoint>]
let main argv =
    argv
    |> Array.toList
    |> Context.FakeExecutionContext.Create false "build.fsx"
    |> Context.RuntimeContext.Fake
    |> Context.setExecutionContext

    initTargets ()
    Target.runOrDefaultWithArguments "DotnetPack"

    0 // return an integer exit code
