/// The whole pure pipeline, source text to geometry (§1, §4.6).
module RubberPlant.Pipeline

open RubberPlant.Model
open RubberPlant.LString
open RubberPlant.Geometry

type Output =
    { Model: Model
      Derived: LString
      Drawing: LString
      Geometry: Geometry
      Warnings: Diagnostic list }

/// Parses and checks a source file. On success, returns the model and warnings.
let compile (source: string) : Result<Model * Diagnostic list, Diagnostic list> =
    Parser.parse source |> Result.bind Checker.check

/// Derives, interprets and draws a model.
let run (model: Model) : Result<Output, Diagnostic list> =
    Derivation.derive model
    |> Result.bind (fun derived ->
        Derivation.interpret model derived
        |> Result.bind (fun drawing ->
            Turtle.interpret model.Settings drawing
            |> Result.map (fun (geometry, warnings) ->
                { Model = model
                  Derived = derived
                  Drawing = drawing
                  Geometry = geometry
                  Warnings = warnings })))
