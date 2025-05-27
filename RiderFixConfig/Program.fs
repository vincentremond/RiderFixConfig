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

    let folders = [ Path.Join(userProfileFolder, "repos") ]

    let element (name: string) (attributes: (string * string) seq) (container: XContainer) : XElement =
        let name = xName name

        container.Elements()
        |> Seq.tryFind (fun x ->
            x.Name = name
            && attributes |> Seq.forall (fun (name, value) -> x.Attribute(name).Value = value)
        )
        |> Option.defaultWith (fun () ->
            let element =
                XElement(name, attributes |> Seq.map (fun (name, value) -> XAttribute(name, value)))

            container.Add(element)
            element
        )

    let elements (name: string) (container: XContainer) : XElement seq = container.Elements(name)

    let getAttribute (name: XName) (element: XElement) = element.Attribute(name).Value

    let tryGetAttribute (name: XName) (element: XElement) =
        element.Attribute(name) |> Option.ofObj |> Option.map _.Value

    let setAttribute (name: XName) (value: obj) (element: XElement) = element.SetAttributeValue(name, value)

    let getGitInfos file =

        let locateGitDirRoot (file: string) =

            let tryCombine sub path =
                let combined = Path.Combine(path, sub)
                if Directory.Exists(combined) then Some combined else None

            let tryParent (path: string) =
                path |> Path.GetDirectoryName |> Option.ofObj

            let rec locateGitDir' path =
                match path |> tryCombine ".git" with
                | Some _ -> Some path
                | None ->
                    match tryParent path with
                    | Some parent -> locateGitDir' parent
                    | None -> None

            locateGitDir' (Path.GetDirectoryName file)

        let getOriginUrl (gitDir: string) =
            use repo = new LibGit2Sharp.Repository(gitDir)

            let configurationEntry =
                repo.Config.Get<string>("remote.origin.url") |> Option.ofObj

            configurationEntry
            |> Option.map (fun configurationEntry -> configurationEntry.Value |> Uri)

        let extractGitInfos (uri: Uri) =

            let result = {|
                RemoteUrl = uri.ToString()
                RemoteName = uri.Segments |> Array.last |> Path.GetFileNameWithoutExtension
                PresentableString =
                    let presentableUri = uri.ToString() |> Regex.replace @"\.git$" ""

                    $"GitLab %s{presentableUri}"
                WorkSpace =
                    uri.Segments
                    |> Seq.rev
                    |> Seq.skip 1
                    |> Seq.rev
                    |> String.concat String.Empty
                    |> String.trim '/'
                BaseUrl = uri.GetLeftPart(UriPartial.Authority)
            |}

            result

        file
        |> locateGitDirRoot
        |> Option.bind getOriginUrl
        |> Option.map extractGitInfos

    let applyFix workspaceFile =
        async {
            printf $"Modifying %s{workspaceFile}"

            let xDocument =
                if File.Exists(workspaceFile) then
                    XDocument.Load(workspaceFile)
                else
                    let directoryName = Path.GetDirectoryName(workspaceFile)

                    if not (Directory.Exists(directoryName)) then
                        Directory.CreateDirectory(directoryName) |> ignore

                    XDocument(XElement("project", XAttribute("version", "4")))

            let gitInfos = getGitInfos workspaceFile

            let root = xDocument.Root

            //
            root
            |> element "component" [ "name", "VcsManagerConfiguration" ]
            |> element "option" [ "name", "LOCAL_CHANGES_DETAILS_PREVIEW_SHOWN" ]
            |> setAttribute (xName "value") "false"

            let projectLevelVcsManager =
                root |> element "component" [ "name", "ProjectLevelVcsManager" ]

            projectLevelVcsManager
            |> element "ConfirmationsSetting" [ "id", "Add" ]
            |> setAttribute (xName "value") "2"

            projectLevelVcsManager
            |> element "ConfirmationsSetting" [ "id", "Remove" ]
            |> setAttribute (xName "value") "2"

            // Run manager

            let runManager = root |> element "component" [ "name", "RunManager" ]

            let configurations = runManager |> elements "configuration"

            for configuration in configurations do
                let name = configuration |> tryGetAttribute (xName "name")

                match name with
                | None -> ()
                | Some name ->
                    let split = name.Split(":", 2)

                    match split with
                    | [| folderName; _ |] -> configuration |> setAttribute (xName "folderName") folderName
                    | _ -> ()

            // Save result
            xDocument.Save(workspaceFile)

            printfn $" - done"

        }

    let main args =
        async {

            let targetFolders, singleFolder =
                match args with
                | [ folder ] -> [ folder ], true
                | _ -> folders, false

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

                let rec check () =
                    if anyProcessList processesToLookFor then
                        printfn "Rider is running - do you want to kill it?  [Y/Enter] = yes kill  | [I] = ignore |  [N] = no wait"
                        let consoleKeyInfo = Console.ReadKey(true)

                        match consoleKeyInfo.Key with
                        | ConsoleKey.I -> () // ignore
                        | ConsoleKey.Enter
                        | ConsoleKey.Y -> processesToLookFor |> List.collect getProcess |> List.iter _.Kill()
                        | _ ->
                            Thread.Sleep(1000)
                            check ()

                check ()

            let deductWorkspaceFileLocation (slnPath: string) : string =
                let solutionName = Path.GetFileNameWithoutExtension slnPath
                let folder = Path.GetDirectoryName slnPath

                folder </> ".idea" </> $".idea.%s{solutionName}" </> ".idea" </> "workspace.xml"

            do!
                (targetFolders
                 |> List.collect (Directory.getAllFiles "*.sln")
                 |> List.map deductWorkspaceFileLocation
                 |> List.distinct
                 |> List.map applyFix
                 |> Async.Sequential
                 |> Async.Ignore)

            if not singleFolder then
                printfn "Done - press [ENTER] to exit"
                Console.ReadLine() |> ignore

            return 0
        }

    [<EntryPoint>]
    let mainAsync argv =
        main (argv |> Array.toList) |> Async.RunSynchronously
