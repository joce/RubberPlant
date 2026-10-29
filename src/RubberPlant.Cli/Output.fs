/// Writing output files all or nothing (backends.md §8). The file operations are
/// injected, so tests can make any step fail.
module RubberPlant.Cli.Output

open System
open System.IO

/// The file operations writeAll uses.
type FileSystem =
    { /// A file exists at the path.
      FileExists: string -> bool
      /// A directory exists at the path.
      DirectoryExists: string -> bool
      /// Fails if the existing file can't be written (read-only, locked, …).
      /// Doesn't change it.
      CheckWritable: string -> unit
      WriteText: string -> string -> unit
      /// Moves a file to a path where nothing exists.
      Move: string -> string -> unit
      Delete: string -> unit
      /// A fresh name fragment for temporary and backup files.
      Unique: unit -> string }

let realFileSystem =
    { FileExists = File.Exists
      DirectoryExists = Directory.Exists
      CheckWritable =
        fun path -> using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None)) ignore
      WriteText = fun path text -> File.WriteAllText(path, text)
      Move = fun source dest -> File.Move(source, dest)
      Delete = File.Delete
      Unique = fun () -> Path.GetRandomFileName().Replace(".", "") }

/// Runs a file operation; Error with a message if it fails with an I/O error.
let private attempt (path: string) (f: unit -> unit) =
    try
        f ()
        Ok()
    with
    | :? IOException
    | :? UnauthorizedAccessException as e -> Error $"%s{path}: %s{e.Message}"

/// Writes all the files or none (§8). Existing files must be writable; the new
/// contents are written to temporary files next to them, then swapped in, with
/// the old files kept as backups until every file is in place. If a step fails,
/// the old files are put back; if even that fails, their backups stay on disk
/// and the errors say where. Ok with the paths written, or Error with messages.
let writeAll (fs: FileSystem) (files: (string * string) list) : Result<string list, string list> =
    let sibling (path: string) (kind: string) = $"%s{path}.%s{kind}-%s{fs.Unique()}"

    // 1. Existing outputs must be writable files: read-only or locked ones fail
    //    here, before anything changes.
    let check (path: string, _) =
        if fs.DirectoryExists path then Some $"%s{path}: is a directory"
        elif fs.FileExists path then
            match attempt path (fun () -> fs.CheckWritable path) with
            | Error e -> Some e
            | Ok() -> None
        else
            None

    match files |> List.tryPick check with
    | Some e -> Error [ e ]
    | None ->
        // 2. Stage the new contents next to their destinations.
        let staged = files |> List.map (fun (path, text) -> path, text, sibling path "tmp")
        let written = Collections.Generic.List<string>()

        let deleteTemps () =
            for temp in written do
                attempt temp (fun () -> fs.Delete temp) |> ignore

        let staging =
            staged
            |> List.tryPick (fun (_, text, temp) ->
                match attempt temp (fun () -> fs.WriteText temp text) with
                | Ok() ->
                    written.Add temp
                    None
                | Error e ->
                    // A partial temporary file may exist.
                    attempt temp (fun () -> if fs.FileExists temp then fs.Delete temp) |> ignore
                    Some e)

        match staging with
        | Some e ->
            deleteTemps ()
            Error [ e ]
        | None ->
            // 3. Swap them in: each old file to a backup, then the new one in place.
            let backups = Collections.Generic.List<string * string>() // (path, backup)
            let placed = Collections.Generic.List<string>()

            let swap (path, _, temp) =
                let backup =
                    if fs.FileExists path then
                        let b = sibling path "bak"
                        attempt path (fun () -> fs.Move path b) |> Result.map (fun () -> Some b)
                    else
                        Ok None

                match backup with
                | Error e -> Some e
                | Ok b ->
                    b |> Option.iter (fun b -> backups.Add((path, b)))

                    match attempt path (fun () -> fs.Move temp path) with
                    | Ok() ->
                        placed.Add path
                        None
                    | Error e -> Some e

            match staged |> List.tryPick swap with
            | None ->
                // 4. Done: drop the backups. Failing to is only worth a warning.
                let leftovers =
                    [ for _, b in backups do
                          match attempt b (fun () -> fs.Delete b) with
                          | Ok() -> ()
                          | Error e -> yield $"warning: couldn't remove the backup %s{e}" ]

                for w in leftovers do
                    eprintfn "%s" w

                Ok(files |> List.map fst)
            | Some e ->
                // Roll back: remove what was placed, put the backups back.
                let errors = Collections.Generic.List<string>([ e ])

                for path in placed do
                    attempt path (fun () -> fs.Delete path) |> Result.mapError errors.Add |> ignore

                for path, b in backups do
                    match attempt path (fun () -> fs.Move b path) with
                    | Ok() -> ()
                    | Error e2 ->
                        errors.Add e2
                        errors.Add $"the previous %s{Path.GetFileName path} is kept as %s{b}"

                for path, _, temp in staged do
                    if not (placed.Contains path) then
                        attempt temp (fun () -> if fs.FileExists temp then fs.Delete temp) |> ignore

                Error(List.ofSeq errors)
