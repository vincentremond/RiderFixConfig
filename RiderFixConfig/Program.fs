namespace RiderFixConfig

open System
open System.Diagnostics
open System.IO
open System.Threading
open Microsoft.FSharp.Core
open System.Xml.Linq
open Pinicola.FSharp.SpectreConsole

type KillResponse =
    | Kill
    | Continue
    | Wait

module Program =

    let userProfileFolder =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)

    let defaultFolders = [
        userProfileFolder </> "repos"
        userProfileFolder </> "tmp"
    ]

    let applyFix workspaceFile =

        AnsiConsole.markupLineInterpolated $"Fixing [yellow]{workspaceFile}[/]"

        let xDocument =
            if File.Exists(workspaceFile) then
                XDocument.Load(workspaceFile)
            else
                let directoryName = Path.GetDirectoryName(workspaceFile)

                if not (Directory.Exists(directoryName)) then
                    Directory.CreateDirectory(directoryName) |> ignore

                XDocument(XElement("project", XAttribute("version", "4")))

        xDocument.Root |> Fixer.fix

        // Save modified document
        xDocument.Save(workspaceFile)

    [<EntryPoint>]
    let main args =

        let timer = Stopwatch.StartNew()

        let targetFolders, singleFolder =
            match args with
            | [| folder |] -> [ folder ], true
            | _ -> defaultFolders, false

        let any = Seq.isEmpty >> not

        let getProcess = Process.GetProcessesByName >> Seq.toList

        let anyProcess = Process.GetProcessesByName >> any

        let anyProcessList = (List.map anyProcess) >> List.reduce (||)

        if not singleFolder then

            let processesToLookFor = [
                "Rider"
                "Rider64"
                "Rider.Backend"
            ]

            let rec checkIfRiderIsRunning () =
                if anyProcessList processesToLookFor then
                    let killResponse =
                        SelectionPrompt.init ()
                        |> SelectionPrompt.withRawTitle "Rider is running - do you want to kill it?"
                        |> SelectionPrompt.addChoices [
                            Kill
                            Continue
                            Wait
                        ]
                        |> AnsiConsole.prompt

                    match killResponse with
                    | Continue -> () // ignore
                    | Kill -> processesToLookFor |> List.collect getProcess |> List.iter _.Kill()
                    | Wait ->
                        Thread.Sleep(1000)
                        checkIfRiderIsRunning ()

            checkIfRiderIsRunning ()

        let deductWorkspaceFileLocation (slnPath: string) : string =
            let solutionName = Path.GetFileNameWithoutExtension slnPath
            let folder = Path.GetDirectoryName slnPath

            folder </> ".idea" </> $".idea.%s{solutionName}" </> ".idea" </> "workspace.xml"

        targetFolders
        |> List.collect (Directory.getAllFiles "*.sln")
        |> List.map deductWorkspaceFileLocation
        |> List.iter applyFix

        timer.Stop()

        0
