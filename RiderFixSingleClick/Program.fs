namespace RiderFixSingleClick

open System.IO
open Microsoft.FSharp.Core
open System.Linq
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

    let folders = [| @"D:\VRM\"; @"D:\GIT\"; @"D:\REF\"; @"D:\TMP\" |]

    let applyFix workspaceFile =
        async {
            printf $"Modifying %s{workspaceFile}"

            let xDocument = XDocument.Load(workspaceFile)
            let root = xDocument.Root
            // get <component name="VcsManagerConfiguration">
            let vcsManagerConfiguration =
                match
                    (root
                        .Elements("component")
                        .SingleOrDefault(fun x -> x.Attribute("name").Value = "VcsManagerConfiguration")
                     |> Option.ofObj)
                with
                | None ->
                    let newVcsManagerConfiguration =
                        XElement("component", XAttribute("name", "VcsManagerConfiguration"))

                    root.Add(newVcsManagerConfiguration)
                    newVcsManagerConfiguration
                | Some x -> x
            // get <option name="LOCAL_CHANGES_DETAILS_PREVIEW_SHOWN">
            match
                (vcsManagerConfiguration
                    .Elements("option")
                    .SingleOrDefault(fun x -> x.Attribute("name").Value = "LOCAL_CHANGES_DETAILS_PREVIEW_SHOWN")
                 |> Option.ofObj)
            with
            | None ->
                let newLocalChangesDetailsPreviewShown =
                    XElement(
                        "option",
                        XAttribute("name", "LOCAL_CHANGES_DETAILS_PREVIEW_SHOWN"),
                        XAttribute("value", "false")
                    )

                vcsManagerConfiguration.Add(newLocalChangesDetailsPreviewShown)
            | Some localChangesDetailsPreviewShown ->
                match localChangesDetailsPreviewShown.Attribute("value") |> Option.ofObj with
                | None -> localChangesDetailsPreviewShown.Add(XAttribute("value", "false"))
                | Some attr -> attr.Value <- "false"

            xDocument.Save(workspaceFile)

            printfn $" - done"

        }


    let main _ =
        async {

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
