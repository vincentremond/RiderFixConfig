namespace RiderFixConfig

open System.Xml.Linq

[<RequireQualifiedAccess>]
module Fixer =

    let private getOrCreateElement
        (name: string)
        (attributes: (string * string) seq)
        (container: XContainer)
        : XElement =
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

    let private elements (name: string) (container: XContainer) : XElement seq = container.Elements(name)

    let private getAttribute (name: XName) (element: XElement) = element.Attribute(name).Value

    let private tryGetAttribute (name: XName) (element: XElement) =
        element.Attribute(name) |> Option.ofObj |> Option.map _.Value

    let private setAttribute (name: XName) (value: obj) (element: XElement) = element.SetAttributeValue(name, value)

    let fix rootProjectElement =

        let getComponent (name: string) =
            rootProjectElement |> getOrCreateElement "component" [ "name", name ]

        let fixes = [
            "Disable « Show Diff Preview on Single Click »",
            (fun () ->
                let vcsManagerConfiguration = getComponent "VcsManagerConfiguration"

                vcsManagerConfiguration
                |> getOrCreateElement "option" [ "name", "LOCAL_CHANGES_DETAILS_PREVIEW_SHOWN" ]
                |> setAttribute (xName "value") "false"
            )
            "Disable pop-up to confirm add or delete version control changes",
            (fun () ->
                let projectLevelVcsManager = getComponent "ProjectLevelVcsManager"

                projectLevelVcsManager
                |> getOrCreateElement "ConfirmationsSetting" [ "id", "Add" ]
                |> setAttribute (xName "value") "2"

                projectLevelVcsManager
                |> getOrCreateElement "ConfirmationsSetting" [ "id", "Remove" ]
                |> setAttribute (xName "value") "2"
            )

            "Regroup run configurations by folder",
            (fun () ->
                let runManager = getComponent "RunManager"

                let runManager = getComponent "RunManager"

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
            )
        ]

        for (fixName, fixAction) in fixes do
            printfn $"Fixing {fixName}"
            fixAction ()
