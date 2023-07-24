namespace RiderFixConfig

open System.IO


[<RequireQualifiedAccess>]
module String =
    let trim (c: char) (s: string) : string = s.Trim(c)

[<RequireQualifiedAccess>]
module Directory =
    let getAllDirectories (searchPattern: string) (path: string) =
        Directory.GetDirectories(path, searchPattern, SearchOption.AllDirectories)

    let getAllFiles (searchPattern: string) (path: string) =
        Directory.GetFiles(path, searchPattern, SearchOption.AllDirectories) |> Array.toList

[<RequireQualifiedAccess>]
module Regex =
    let replace (pattern: string) (replacement: string) (input: string) =
        System.Text.RegularExpressions.Regex.Replace(input, pattern, replacement)

[<RequireQualifiedAccess>]
module Array =
    let iterAsync (f: 'T -> Async<'U>) (array: 'T[]) =
        array
        |> Array.map f
        |> Async.Sequential

[<AutoOpen>]
module Path =
    let (</>) (path1: string) (path2: string) = Path.Combine(path1, path2)

