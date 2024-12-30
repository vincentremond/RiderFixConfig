namespace RiderFixConfig

open System
open System.Diagnostics
open System.IO
open System.Threading
open Microsoft.FSharp.Core
open System.Xml.Linq

module Program =

    let folders = [
        // @"D:\VRM\"
        // @"D:\GIT\"
        // @"D:\TMP\"
        @"C:\Users\remond\repos\PrestK\PrestK"
    ]

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

    let attribute (name: XName) (value: obj) (element: XElement) = element.SetAttributeValue(name, value)

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
            |> attribute (xName "value") "false"

            match gitInfos with
            | None -> ()
            | Some gitInfos ->

                let gitLabMergeRequest =
                    root |> element "component" [ "name", @"GitlabMajeraCodeReviewSettings" ]

                gitLabMergeRequest.ReplaceWith(
                    XElement(
                        "component",
                        XAttribute("name", @"GitlabMajeraCodeReviewSettings"),
                        XElement(
                            "option",
                            XAttribute("name", "processedVcsRemotes"),
                            XElement(
                                "set",
                                XElement(
                                    "StoredVcsRemote",
                                    XElement(
                                        "option",
                                        XAttribute("name", "vcsRemoteUrl"),
                                        XAttribute("value", gitInfos.RemoteUrl)
                                    ),
                                    XElement(
                                        "option",
                                        XAttribute("name", "vcsRootUrl"),
                                        XAttribute("value", "file://$PROJECT_DIR$")
                                    )
                                )
                            )
                        ),
                        XElement(
                            "option",
                            XAttribute("name", "pullRequestSearchScopePresentableStr"),
                            XAttribute("value", gitInfos.PresentableString)
                        ),
                        XElement(
                            "option",
                            XAttribute("name", "servers"),
                            XElement(
                                "list",
                                XElement(
                                    "DiscoveredCodeReviewServer",
                                    XElement("option", XAttribute("name", "serviceId"), XAttribute("value", "gitlab")),
                                    XElement(
                                        "option",
                                        XAttribute("name", "storedCodeReviewRepositories"),
                                        XElement(
                                            "list",
                                            XElement(
                                                "StoredCodeReviewRepository",
                                                XElement(
                                                    "option",
                                                    XAttribute("name", "name"),
                                                    XAttribute("value", gitInfos.RemoteName)
                                                ),
                                                XElement(
                                                    "option",
                                                    XAttribute("name", "storedVcsRemote"),
                                                    XElement(
                                                        "StoredVcsRemote",
                                                        XElement(
                                                            "option",
                                                            XAttribute("name", "vcsRemoteUrl"),
                                                            XAttribute("value", gitInfos.RemoteUrl)
                                                        ),
                                                        XElement(
                                                            "option",
                                                            XAttribute("name", "vcsRootUrl"),
                                                            XAttribute("value", "file://$PROJECT_DIR$")
                                                        )
                                                    )
                                                ),
                                                XElement(
                                                    "option",
                                                    XAttribute("name", "workspace"),
                                                    XAttribute("value", gitInfos.WorkSpace)
                                                )
                                            )
                                        )
                                    ),
                                    XElement("option", XAttribute("name", "url"), XAttribute("value", gitInfos.BaseUrl))
                                )
                            )
                        )
                    )
                )

            let projectLevelVcsManager =
                root |> element "component" [ "name", "ProjectLevelVcsManager" ]

            projectLevelVcsManager
            |> element "ConfirmationsSetting" [ "id", "Add" ]
            |> attribute (xName "value") "2"

            projectLevelVcsManager
            |> element "ConfirmationsSetting" [ "id", "Remove" ]
            |> attribute (xName "value") "2"

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

            if not singleFolder then
                while (anyProcess "Rider") || (anyProcess "Rider64") || (anyProcess "Rider.Backend") do
                    printfn "Rider is running - do you want to kill it?  [Y/Enter] = yes kill  |  [N] = no wait"
                    let consoleKeyInfo = Console.ReadKey(true)

                    match consoleKeyInfo.Key with
                    | ConsoleKey.Enter
                    | ConsoleKey.Y ->
                        [
                            "Rider"
                            "Rider64"
                            "Rider.Backend"
                        ]
                        |> List.collect getProcess
                        |> List.iter (fun p -> p.Kill())
                    | _ -> Thread.Sleep(1000)

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
