/// CLI entry point for `rubberplant-viewer` (PR 2 scaffolding stub; OpenGL
/// headless rendering and interactive window land in PR 3 and PR 4).
module RubberPlant.Viewer.Program

open System
open System.Text
open RubberPlant.Viewer

let private ensureUtf8Stdout () =
    if
        Console.OutputEncoding.CodePage <> Encoding.UTF8.CodePage
        || Console.OutputEncoding.GetPreamble().Length <> 0
    then
        Console.OutputEncoding <- UTF8Encoding(false)

[<EntryPoint>]
let main argv =
    ensureUtf8Stdout ()

    match Protocol.parseCli (List.ofArray argv) with
    | Ok(Protocol.Help code) ->
        printf "%s" Protocol.usage
        code
    | Error msg ->
        eprintfn "rubberplant-viewer: %s" msg
        eprintf "\n%s" Protocol.usage
        2
    | Ok _ ->
        eprintfn "rubberplant-viewer: OpenGL rendering is not implemented yet (lands in BP.2 PR 3/PR 4)"
        1
