/// The checked model: names resolved, constants folded, settings evaluated.
/// Everything downstream of the checker works on this, never on the AST.
module RubberPlant.Model

open RubberPlant.Ast
open RubberPlant.Builtins
open RubberPlant.LString
open RubberPlant.Geometry

/// A resolved expression. Formal parameters are slots into the rule's
/// binding array; defines are folded to numbers.
type CExpr =
    | CNum of float
    | CParam of slot: int
    | CCall of Builtin * CExpr[]
    | CNeg of CExpr
    | CNot of CExpr
    | CBin of BinOp * CExpr * CExpr

type CPattern =
    | Bind of slot: int
    | Literal of float
    | Skip

type CFormal = { Name: string; Params: CPattern[] }

type CLeft =
    | CLModule of CFormal
    | CLAny

type CRight =
    | CRModule of CFormal
    | CRAny
    | CRBranch of CRight list

/// A successor: a flat template whose module arguments are expressions.
type CSym =
    | TPush
    | TPop
    | TCut
    | TMod of name: string * args: CExpr[]

type Successor = CSym[]

type CWeightBranch =
    { Guard: CExpr option
      Weight: CExpr
      Successor: Successor }

type Body =
    | Simple of Successor
    | Guarded of (CExpr * Successor) list * elseBranch: Successor option
    | Weighted of CWeightBranch list * elseBranch: Successor option

type CRule =
    { Left: CLeft list
      Pred: CFormal
      Right: CRight list
      /// Number of parameter slots bound by the head.
      Slots: int
      Body: Body
      Pos: Pos }

    member r.HasContext = not r.Left.IsEmpty || not r.Right.IsEmpty

type Settings =
    { Derivations: int
      Angle: float
      Step: float
      Width: float
      Taper: float
      /// Direction of the tropism vector T, as written (§4.7):
      /// finite and non-zero; the turtle normalizes it.
      Tropism: Vec3
      /// Elasticity e; 0 disables tropism.
      Elasticity: float
      /// Not a file setting: a run parameter supplied by the host (§4.7).
      Seed: int64 }

/// Seeds are integers that a float represents exactly: |seed| <= 2^53 - 1.
let maxSeed = 9007199254740991.0

let defaultSettings =
    { Derivations = 0
      Angle = 90.0
      Step = 1.0
      Width = 1.0
      Taper = 30.0
      Tropism = Vec3.create 0.0 -1.0 0.0
      Elasticity = 0.0
      Seed = 0L }

/// Which modules exist during context matching (§4.5). Brackets always do.
type ContextFilter =
    /// `ignore:`: every module except these (no list: every module).
    | IgnoreList of Set<string>
    /// `consider:` (v0.2): only these modules. The cut `%` can't be listed, so
    /// it is skipped.
    | ConsiderList of Set<string>

module ContextFilter =
    let none = IgnoreList Set.empty

    /// True if context matching skips the module `name`.
    let skipsModule (f: ContextFilter) (name: string) =
        match f with
        | IgnoreList names -> names.Contains name
        | ConsiderList names -> not (names.Contains name)

    /// True if context matching skips the cut `%`: it can't be listed.
    let skipsCut (f: ContextFilter) =
        match f with
        | IgnoreList _ -> false
        | ConsiderList _ -> true

type Model =
    { Settings: Settings
      Context: ContextFilter
      Axiom: LString
      Productions: CRule list
      Interpretations: CRule list }
