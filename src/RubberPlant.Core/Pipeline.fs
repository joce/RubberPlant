/// The whole pure pipeline, source text to geometry (§1, §4.6).
module RubberPlant.Pipeline

open RubberPlant.Model
open RubberPlant.LString
open RubberPlant.Geometry
open RubberPlant.Derivation
open RubberPlant.Turtle

type Output =
    { Model: Model
      Derived: LString
      Drawing: LString
      Geometry: Geometry
      DerivationTrace: DerivationTrace
      TurtleTrace: TurtleTrace
      Warnings: Diagnostic list }

/// Parses and checks a source file. On success, returns the model and warnings.
let compile (source: string) : Result<Model * Diagnostic list, Diagnostic list> =
    Parser.parse source |> Result.bind Checker.check

/// Resolves every `include` declaration using `readText`, parsing `.s` files
/// with `Bezier.parse`. Returns the updated model and any `RP0307` warnings,
/// or `RP0208` errors at the `include` source positions.
let loadSurfaces
    (readText: string -> Result<string, string>)
    (model: Model)
    : Result<Model * Diagnostic list, Diagnostic list> =
    if model.Includes.IsEmpty then
        Ok(model, [])
    else
        let mutable surfaces = model.Surfaces
        let warnings = ResizeArray<Diagnostic>()
        let errors = ResizeArray<Diagnostic>()

        for decl in model.Includes do
            match readText decl.Path with
            | Error msg ->
                errors.Add(
                    Diagnostic.error
                        Codes.SurfaceLoadError
                        (Some decl.Pos)
                        $"failed to load surface `%s{decl.Name}` from `%s{decl.Path}`: %s{msg}"
                )
            | Ok text ->
                match Bezier.parse decl.Name (Some decl.Pos) text with
                | Error msg ->
                    errors.Add(
                        Diagnostic.error
                            Codes.SurfaceLoadError
                            (Some decl.Pos)
                            $"failed to parse surface `%s{decl.Name}` from `%s{decl.Path}`: %s{msg}"
                    )
                | Ok(surf, ws) ->
                    surfaces <- surfaces.Add(decl.Name, surf)
                    warnings.AddRange ws

        if errors.Count > 0 then
            Error(List.ofSeq errors)
        else
            Ok({ model with Surfaces = surfaces }, List.ofSeq warnings)

/// Derives, interprets and draws a model under `limits`.
let runWith (limits: Limits) (model: Model) : Result<Output, Diagnostic list> =
    let deriveResult =
        if Model.isTimed model then
            TimedDerivation.deriveWithTrace limits model
        else
            Derivation.deriveWithTrace limits model

    deriveResult
    |> Result.bind (fun (derived, derivationTrace) ->
        Derivation.interpretWith limits model derived
        |> Result.bind (fun drawing ->
            Turtle.interpretWithSurfacesTrace limits model.Settings model.Surfaces drawing
            |> Result.map (fun (geometry, turtleTrace, warnings) ->
                { Model = model
                  Derived = derived
                  Drawing = drawing
                  Geometry = geometry
                  DerivationTrace = derivationTrace
                  TurtleTrace = turtleTrace
                  Warnings = warnings })))

/// Derives, interprets and draws a model under default execution limits.
let run (model: Model) : Result<Output, Diagnostic list> = runWith Limits.defaults model
