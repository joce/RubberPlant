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

/// Derives, interprets and draws a model under `limits`.
let runWith (limits: Limits) (model: Model) : Result<Output, Diagnostic list> =
    Derivation.deriveWith limits model
    |> Result.bind (fun derived ->
        Derivation.interpretWith limits model derived
        |> Result.bind (fun drawing ->
            Turtle.interpretWith limits model.Settings drawing
            |> Result.map (fun (geometry, warnings) ->
                { Model = model
                  Derived = derived
                  Drawing = drawing
                  Geometry = geometry
                  Warnings = warnings })))

/// Derives, interprets and draws a model under default execution limits.
let run (model: Model) : Result<Output, Diagnostic list> = runWith Limits.defaults model
