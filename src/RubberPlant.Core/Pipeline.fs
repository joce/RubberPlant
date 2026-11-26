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
            Turtle.interpretWithTrace limits model.Settings drawing
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
