/// Mesh digests for every example (backends.md §9): the full OBJ and glTF of the
/// large trees run to megabytes, so golden/mesh-digests.txt holds their SHA-256.
module RubberPlant.Backends.Tests.MeshDigestTests

open System
open System.IO
open System.Security.Cryptography
open System.Text
open Expecto
open RubberPlant.Backends
open RubberPlant.Backends.Mesh

let private path = Path.Combine(Examples.goldenDir, "mesh-digests.txt")

let private header =
    [ "# <example> <sha256 of the OBJ's bytes then the MTL's> <sha256 of the .gltf>"
      "# Default options and the seeds of seeds.txt (docs/backends.md §9). scripts/check-goldens.sh"
      "# checks the CLI against them. Regenerate with RUBBERPLANT_UPDATE_GOLDEN=1 and review the diff." ]

let private sha (bytes: byte[]) =
    Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()

let private utf8 (s: string) = UTF8Encoding(false).GetBytes s

let private ok (name: string) (r: Result<'a, string>) =
    match r with
    | Ok x -> x
    | Error msg -> failtestf "%s: %s" name msg

/// `<example> <obj+mtl digest> <gltf digest>`.
let private digestLine (name: string) =
    let geometry = Examples.geometry name
    let obj = Obj.render defaultMeshOptions (name + ".mtl") geometry |> ok name
    let gltf = Gltf.renderText defaultMeshOptions geometry |> ok name
    $"%s{name} %s{sha (Array.append (utf8 obj.Obj) (utf8 obj.Mtl))} %s{sha (utf8 gltf)}"

[<Tests>]
let tests =
    test "mesh digests of every example" {
        let actual = Examples.names |> List.map digestLine

        if Examples.update then
            File.WriteAllText(path, String.concat "\n" (header @ actual) + "\n")
        elif not (File.Exists path) then
            failtest "golden/mesh-digests.txt is missing (set RUBBERPLANT_UPDATE_GOLDEN=1 to create it)"
        else
            let expected =
                File.ReadAllLines path |> Array.filter (fun l -> l.Trim() <> "" && not (l.StartsWith "#")) |> List.ofArray

            let byName (lines: string list) =
                lines |> List.map (fun l -> l.Split(' ')[0], l) |> Map.ofList

            let e, a = byName expected, byName actual

            let problems =
                [ for name in Set.union (set e.Keys) (set a.Keys) do
                      match e.TryFind name, a.TryFind name with
                      | Some x, Some y when x = y -> ()
                      | Some _, Some y -> yield $"differs: %s{y}"
                      | None, Some _ -> yield $"missing from mesh-digests.txt: %s{name}"
                      | Some _, None -> yield $"no example for: %s{name}"
                      | None, None -> () ]

            if not problems.IsEmpty then
                failtestf
                    "%s\n(set RUBBERPLANT_UPDATE_GOLDEN=1 to accept)"
                    (String.concat "\n" problems)
    }
