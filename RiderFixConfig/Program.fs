namespace RiderFixConfig

open System
open System.Diagnostics
open System.IO
open System.Threading
open Microsoft.FSharp.Core
open System.Xml.Linq

module Program =

    let userProfileFolder =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)

    let defaultFolders = [ Path.Join(userProfileFolder, "repos") ]

    let applyFix workspaceFile =

        printf $"Fixing %s{workspaceFile}"

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

        printfn " - done"

    [<EntryPoint>]
    let main args =

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
                    printfn
                        "Rider is running - do you want to kill it?  [Y/Enter] = yes kill  | [I] = ignore |  [N] = no wait"

                    let consoleKeyInfo = Console.ReadKey(true)

                    match consoleKeyInfo.Key with
                    | ConsoleKey.I -> () // ignore
                    | ConsoleKey.Enter
                    | ConsoleKey.Y -> processesToLookFor |> List.collect getProcess |> List.iter _.Kill()
                    | _ ->
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

        if not singleFolder then
            printfn "Done - press [ENTER] to exit"
            Console.ReadLine() |> ignore

        0
