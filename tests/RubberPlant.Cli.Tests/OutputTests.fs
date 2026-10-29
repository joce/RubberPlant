module RubberPlant.Cli.Tests.OutputTests

open System.IO
open System.Collections.Generic
open Expecto
open RubberPlant.Cli

/// An in-memory file system whose operations can be made to fail.
type private MemoryFs() =
    member val Files = Dictionary<string, string>()
    member val ReadOnly = HashSet<string>()
    member val Directories = HashSet<string>()
    /// (operation, path) -> fail? Operations: check, write, move-from, move-to, delete.
    member val FailWhen: string -> string -> bool = (fun _ _ -> false) with get, set
    member val Counter = 0 with get, set

    member fs.Api: Output.FileSystem =
        let fail op path =
            if fs.FailWhen op path then
                raise (IOException $"injected failure: %s{op} %s{path}")

        { FileExists = fun p -> fs.Files.ContainsKey p
          DirectoryExists = fun p -> fs.Directories.Contains p
          CheckWritable =
            fun p ->
                fail "check" p

                if fs.ReadOnly.Contains p then
                    raise (System.UnauthorizedAccessException "read-only")
          WriteText =
            fun p text ->
                fail "write" p

                if fs.ReadOnly.Contains p || fs.Directories.Contains p then
                    raise (System.UnauthorizedAccessException "can't write")

                fs.Files[p] <- text
          Move =
            fun source dest ->
                fail "move-from" source
                fail "move-to" dest

                if not (fs.Files.ContainsKey source) then
                    raise (FileNotFoundException source)

                if fs.Files.ContainsKey dest || fs.Directories.Contains dest then
                    raise (IOException $"exists: %s{dest}")

                fs.Files[dest] <- fs.Files[source]
                fs.Files.Remove source |> ignore
          Delete =
            fun p ->
                fail "delete" p
                fs.Files.Remove p |> ignore
          Unique =
            fun () ->
                fs.Counter <- fs.Counter + 1
                string fs.Counter }

/// A failure that happens only the first time.
let private once (pred: string -> string -> bool) =
    let mutable fired = false

    fun op path ->
        if not fired && pred op path then
            fired <- true
            true
        else
            false

let private outputs = [ "tree.obj", "new obj"; "tree.mtl", "new mtl" ]

/// A file system holding an existing pair.
let private withPair () =
    let fs = MemoryFs()
    fs.Files["tree.obj"] <- "old obj"
    fs.Files["tree.mtl"] <- "old mtl"
    fs

let private files (fs: MemoryFs) = fs.Files |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq

let private unchanged (fs: MemoryFs) =
    Expect.equal (files fs) (Map [ "tree.obj", "old obj"; "tree.mtl", "old mtl" ]) "the old pair, nothing else"

let private isError (r: Result<string list, string list>) =
    match r with
    | Error messages -> messages
    | Ok _ -> failtest "expected an error"

[<Tests>]
let tests =
    testList
        "Output.writeAll"
        [ test "replaces an existing pair and leaves nothing else" {
              let fs = withPair ()
              Expect.equal (Output.writeAll fs.Api outputs) (Ok [ "tree.obj"; "tree.mtl" ]) ""
              Expect.equal (files fs) (Map [ "tree.obj", "new obj"; "tree.mtl", "new mtl" ]) ""
          }

          test "writes new files" {
              let fs = MemoryFs()
              Expect.equal (Output.writeAll fs.Api outputs) (Ok [ "tree.obj"; "tree.mtl" ]) ""
              Expect.equal (files fs) (Map outputs) ""
          }

          test "a read-only output fails before anything changes" {
              let fs = withPair ()
              fs.ReadOnly.Add "tree.mtl" |> ignore
              Output.writeAll fs.Api outputs |> isError |> ignore
              unchanged fs
          }

          test "a directory in the way fails before anything changes" {
              let fs = MemoryFs()
              fs.Directories.Add "tree.mtl" |> ignore
              let messages = Output.writeAll fs.Api outputs |> isError
              Expect.stringContains messages.Head "is a directory" ""
              Expect.isEmpty fs.Files ""
          }

          test "a staging failure changes nothing and leaves no temporary file" {
              let fs = withPair ()
              fs.FailWhen <- fun op p -> op = "write" && p.StartsWith "tree.mtl."
              Output.writeAll fs.Api outputs |> isError |> ignore
              unchanged fs
          }

          test "a failure while swapping puts the old pair back" {
              // The OBJ is already in place when the MTL can't be.
              let fs = withPair ()
              fs.FailWhen <- once (fun op p -> op = "move-to" && p = "tree.mtl")
              Output.writeAll fs.Api outputs |> isError |> ignore
              unchanged fs
          }

          test "if the failure persists, the old file it blocks is kept as a backup" {
              // Placing the new MTL fails, and so does putting the old one back.
              let fs = withPair ()
              fs.FailWhen <- fun op p -> op = "move-to" && p = "tree.mtl"
              let messages = Output.writeAll fs.Api outputs |> isError
              let backup = fs.Files.Keys |> Seq.find (fun p -> p.StartsWith "tree.mtl.bak-")
              Expect.equal fs.Files[backup] "old mtl" "the old MTL survives"
              Expect.equal fs.Files["tree.obj"] "old obj" "the old OBJ is back"
              Expect.isFalse (fs.Files.ContainsKey "tree.mtl") "no new MTL"
              Expect.exists messages (fun m -> m.Contains backup) "the error says where the old MTL is"
          }

          test "if the rollback fails too, the old file is kept as a backup and reported" {
              let fs = withPair ()
              // The MTL can't be placed, and the OBJ's backup can't be moved back.
              let placeMtl = once (fun op p -> op = "move-to" && p = "tree.mtl")
              fs.FailWhen <- fun op p -> placeMtl op p || (op = "move-from" && p.StartsWith "tree.obj.bak-")
              let messages = Output.writeAll fs.Api outputs |> isError

              let backup =
                  fs.Files.Keys |> Seq.find (fun p -> p.StartsWith "tree.obj.bak-")

              Expect.equal fs.Files[backup] "old obj" "the old OBJ survives"
              Expect.equal fs.Files["tree.mtl"] "old mtl" "the old MTL is back"

              Expect.exists messages (fun m -> m.Contains backup) "the error says where the old OBJ is"
          }

          test "a backup that can't be removed after success is only a warning" {
              let fs = withPair ()
              fs.FailWhen <- fun op p -> op = "delete" && p.StartsWith "tree.obj.bak-"
              Expect.equal (Output.writeAll fs.Api outputs) (Ok [ "tree.obj"; "tree.mtl" ]) ""
              Expect.equal fs.Files["tree.obj"] "new obj" ""
          } ]
