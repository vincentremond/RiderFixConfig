namespace RiderFixConfig

open System
open System.Diagnostics
open System.IO
open Microsoft.FSharp.Core
open System.Xml.Linq

[<RequireQualifiedAccess>]
module Directory =
    let getAllDirectories (searchPattern: string) (path: string) =
        Directory.GetDirectories(path, searchPattern, SearchOption.AllDirectories)

    let getAllFiles (searchPattern: string) (path: string) =
        Directory.GetFiles(path, searchPattern, SearchOption.AllDirectories)

[<RequireQualifiedAccess>]
module Array =
    let iterAsync (f: 'T -> Async<'U>) (array: 'T[]) =
        array |> Array.map f |> Async.Sequential

module Program =

    let inline xName (localName: string) = XName.Get(localName)

    let folders = [| @"D:\VRM\"; @"D:\GIT\"; @"D:\TMP\" |]

    let element (name: string) (attributes: (string * string) seq) (container: XContainer) : XElement =
        let name = xName name

        container.Elements()
        |> Seq.tryFind (fun x ->
            x.Name = name
            && attributes |> Seq.forall (fun (name, value) -> x.Attribute(name).Value = value)
        ) |> Option.defaultWith (fun () ->
            let element = XElement(name, attributes |> Seq.map (fun (name, value) -> XAttribute(name, value)))
            container.Add(element)
            element
        )

    let attribute (name: XName) (value: obj) (element: XElement) = element.SetAttributeValue(name, value)


    let getGitInfos file =



        let locateGitDir (file: string) =

            let tryCombine sub path =
                let combined = Path.Combine(path, sub)
                if Directory.Exists(combined) then Some combined else None

            let tryParent (path: string) =
                path |> Path.GetDirectoryName |> Option.ofObj

            let rec locateGitDir' path =
                path
                |> tryCombine ".git"
                |> Option.orElseWith (fun () -> tryParent path |> Option.bind locateGitDir')

            locateGitDir' (Path.GetDirectoryName file)

        let getRemotes (gitDir: string) =
            use repo = new LibGit2Sharp.Repository(gitDir)
            repo.Network.Remotes |> Seq.toList

        let pickRemote (remotes: LibGit2Sharp.Remote list) =
            remotes
            |> Seq.tryFind (fun remote -> remote.Name = "origin")
            |> Option.map (fun remote -> remote.Url |> Uri)

        let extractGitInfos (uri: Uri) =

            let result = {|
                RemoteUrl = uri.ToString()
                RemoteName = uri.Segments |> Array.last |> Path.GetFileNameWithoutExtension
                PresentableString = $"GitLab {uri.ToString()}"
                WorkSpace = uri.Segments |> Seq.rev |> Seq.skip 1 |> Seq.rev |> String.concat "/"
                BaseUrl = uri.GetLeftPart(UriPartial.Authority)
            |}
            
            result

        file
        |> locateGitDir
        |> Option.map getRemotes
        |> Option.bind pickRemote
        |> Option.map extractGitInfos

    let applyFix workspaceFile =
        async {
            printf $"Modifying %s{workspaceFile}"

            let gitInfos = getGitInfos workspaceFile

            let xDocument = XDocument.Load(workspaceFile)

            let root = xDocument.Root

            //
            root
            |> element "component" [ "name", "VcsManagerConfiguration" ]
            |> element "option" [ "name", "LOCAL_CHANGES_DETAILS_PREVIEW_SHOWN" ]
            |> attribute (xName "value") "false"

            let gitLabMergeRequest =
                root |> element "component" [ "name", "GitlabMajeraCodeReviewSettings" ]

            match gitInfos with
            | None -> ()
            | Some gitInfos ->

                gitLabMergeRequest.ReplaceWith(
                    XElement(
                        "component",
                        XAttribute("name", "GitlabMajeraCodeReviewSettings"),
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


    let main _ =
        async {

            let any =  Seq.isEmpty>> not
            let anyProcess = Process.GetProcessesByName >> any
            while  (anyProcess "Rider") || (anyProcess "Rider64") || (anyProcess "Rider.Backend") do
                printfn "Please close Rider before running this tool"
                do! Async.Sleep 1000

            do!
                (folders
                 |> Array.collect (Directory.getAllDirectories ".idea")
                 |> Array.collect (Directory.getAllFiles "workspace.xml")
                 |> Array.distinct
                 |> Array.map applyFix
                 |> Async.Sequential
                 |> Async.Ignore)

            return 0
        }

    [<EntryPoint>]
    let mainAsync argv = main argv |> Async.RunSynchronously
