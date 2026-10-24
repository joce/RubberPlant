namespace RubberPlant

/// A 1-based source position.
[<Struct>]
type Pos = { Line: int; Column: int }

[<RequireQualifiedAccess>]
type Severity =
    | Error
    | Warning

/// A compiler-style message: `file.rp(12,5): error RP0012: message`.
type Diagnostic =
    { Severity: Severity
      Code: string
      Message: string
      Pos: Pos option }

/// Stable diagnostic codes. Tests match on codes, not on message text.
module Codes =
    // Syntax (parser)
    [<Literal>]
    let Syntax = "RP0001"

    [<Literal>]
    let MissingSemicolon = "RP0002"

    [<Literal>]
    let UnterminatedComment = "RP0003"

    [<Literal>]
    let ReservedChar = "RP0004"

    [<Literal>]
    let UseCOperator = "RP0005"

    [<Literal>]
    let UnknownDirective = "RP0006"

    [<Literal>]
    let ReservedDirective = "RP0007"

    [<Literal>]
    let DirectiveNotAtLineStart = "RP0008"

    [<Literal>]
    let ElseAfterUnguardedWeight = "RP0009"

    [<Literal>]
    let MixedBranches = "RP0010"

    [<Literal>]
    let NumberInModuleMode = "RP0011"

    [<Literal>]
    let BracketNotAllowed = "RP0012"

    [<Literal>]
    let ReservedName = "RP0013"

    // Static checks (checker)
    [<Literal>]
    let NamespaceClash = "RP0101"

    [<Literal>]
    let DuplicateParameter = "RP0102"

    [<Literal>]
    let UnknownName = "RP0103"

    [<Literal>]
    let DefineUsedBeforeDefinition = "RP0104"

    [<Literal>]
    let DuplicateDefine = "RP0105"

    [<Literal>]
    let UnknownFunction = "RP0106"

    [<Literal>]
    let WrongArity = "RP0107"

    [<Literal>]
    let DuplicateSetting = "RP0108"

    [<Literal>]
    let DuplicateAxiom = "RP0109"

    [<Literal>]
    let MissingAxiom = "RP0110"

    [<Literal>]
    let NotAValue = "RP0111"

    [<Literal>]
    let InvalidSetting = "RP0112"

    // Evaluation (derivation, interpretation)
    [<Literal>]
    let DivisionByZero = "RP0201"

    [<Literal>]
    let NotANumber = "RP0202"

    [<Literal>]
    let InvalidWeight = "RP0203"

    [<Literal>]
    let NoPositiveWeight = "RP0204"

    [<Literal>]
    let InvalidGeometry = "RP0205"

    // Warnings
    [<Literal>]
    let UnreachableRule = "RP0301"

    [<Literal>]
    let ExtraTurtleParameters = "RP0302"

    [<Literal>]
    let UnbalancedPolygon = "RP0303"

module Diagnostic =
    let error code pos message =
        { Severity = Severity.Error
          Code = code
          Message = message
          Pos = pos }

    let warning code pos message =
        { Severity = Severity.Warning
          Code = code
          Message = message
          Pos = pos }

    /// Formats a diagnostic in the compiler style understood by editors and CI.
    let format (file: string) (d: Diagnostic) =
        let sev =
            match d.Severity with
            | Severity.Error -> "error"
            | Severity.Warning -> "warning"

        match d.Pos with
        | Some p -> $"%s{file}(%d{p.Line},%d{p.Column}): %s{sev} %s{d.Code}: %s{d.Message}"
        | None -> $"%s{file}: %s{sev} %s{d.Code}: %s{d.Message}"

    let isError d = d.Severity = Severity.Error
