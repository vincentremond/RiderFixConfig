namespace RiderFixConfig

open System.Xml.Linq

[<AutoOpen>]
module Operators =
    let (!?) a = Option.ofObj a

[<RequireQualifiedAccess>]
module XElement =
    let element (name: XName) (x: XContainer) = !? x.Element(name)

    let findOrAddElement (finder: XElement -> bool) (init: unit -> XElement) (x: XContainer) : XElement =
        match
            (x.Elements()
             |> Seq.tryFind finder)
        with
        | Some e -> e
        | None ->
            let newElement = init ()
            x.Add(newElement)
            newElement

    let getOrAddElement (name: XName) (init: unit -> XElement) (x: XContainer) : XElement =
        findOrAddElement (fun e -> e.Name = name) init x
