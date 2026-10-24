/// The checked model: names resolved, constants folded, settings evaluated.
/// Everything downstream of the checker works on this, never on the AST.
module RubberPlant.Model

open RubberPlant.Ast
open RubberPlant.Builtins
open RubberPlant.LString

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
      Seed: int64 }

let defaultSettings =
    { Derivations = 0
      Angle = 90.0
      Step = 1.0
      Width = 1.0
      Taper = 30.0
      Seed = 0L }

type Model =
    { Settings: Settings
      Ignore: Set<string>
      Axiom: LString
      Productions: CRule list
      Interpretations: CRule list }
