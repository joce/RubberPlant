/// Syntax tree, mirroring the grammar of `docs/language.md`, Appendix A.2.
module RubberPlant.Ast

type BinOp =
    | Or
    | And
    | Lt
    | Le
    | Gt
    | Ge
    | Eq
    | Ne
    | Add
    | Sub
    | Mul
    | Div
    | Mod
    | Pow

type Expr =
    | Num of float
    | Ident of name: string * pos: Pos
    | Call of name: string * args: Expr list * pos: Pos
    | Neg of Expr
    | Not of Expr
    | Binary of BinOp * Expr * Expr

// Module strings ----------------------------------------------------------

type ActualModule =
    { Name: string
      Args: Expr list
      Pos: Pos }

/// `ActualElem = ActualModule | "[" ActualString "]" | "%"`: brackets are
/// structural, so a successor is a tree and is always balanced. The cut `%`
/// (v0.2) is structural too; the parser keeps it out of `as` successors.
type ActualElem =
    | Module of ActualModule
    | Branch of ActualElem list
    | Cut

/// A module string. The empty list is `empty` (v0.2).
type ActualString = ActualElem list

type Pattern =
    | PVar of name: string * pos: Pos
    | PNum of float
    | PWild

type FormalModule =
    { Name: string
      Params: Pattern list
      Pos: Pos }

/// A left context is a path toward the root: no brackets.
type LeftElem =
    | LModule of FormalModule
    | LAny

type RightElem =
    | RModule of FormalModule
    | RAny
    | RBranch of RightElem list

type Head =
    { Left: LeftElem list
      Pred: FormalModule
      Right: RightElem list }

// Rules -------------------------------------------------------------------

type GuardBranch = { Guard: Expr; Successor: ActualString }

type WeightBranch =
    { Guard: Expr option
      Weight: Expr
      Successor: ActualString }

type ProdBody =
    /// `Head -> succ`
    | ProdSimple of ActualString
    /// `Head : when … -> … [else -> …]`
    | ProdGuarded of GuardBranch list * elseBranch: ActualString option
    /// `Head : [when …] weight … -> … [else -> …]`. The parser only allows an
    /// `else` when every branch is guarded.
    | ProdWeighted of WeightBranch list * elseBranch: ActualString option

type InterpBody =
    /// `Head as succ`
    | InterpSimple of ActualString
    /// `Head : when … as … [else as …]`
    | InterpGuarded of GuardBranch list * elseBranch: ActualString option

type Rule =
    | Production of Head * ProdBody * pos: Pos
    | Interpretation of Head * InterpBody * pos: Pos

// Prelude and file --------------------------------------------------------

/// Settings whose value is a single expression.
type ScalarKey =
    | Derivations
    | Angle
    | Step
    | Width
    | Taper
    | Elasticity

/// Settings whose value is a vector `(x, y, z)` (v0.2).
type VectorKey = Tropism

type SettingKey =
    | ScalarKey of ScalarKey
    | VectorKey of VectorKey

/// One entry of a `settings` block, at the key's position. The grammar
/// decides the shape of the value from the key (§3.2).
type SettingEntry =
    | ScalarEntry of ScalarKey * Expr * pos: Pos
    | VectorEntry of VectorKey * x: Expr * y: Expr * z: Expr * pos: Pos

type Item =
    | Settings of SettingEntry list * pos: Pos
    | Let of name: string * Expr * pos: Pos
    | Ignore of (string * Pos) list * pos: Pos
    /// `consider:` (v0.2)
    | Consider of (string * Pos) list * pos: Pos
    | Axiom of ActualString * pos: Pos
    | RuleItem of Rule

type File = Item list

module SettingKey =
    let name =
        function
        | ScalarKey Derivations -> "derivations"
        | ScalarKey Angle -> "angle"
        | ScalarKey Step -> "step"
        | ScalarKey Width -> "width"
        | ScalarKey Taper -> "taper"
        | ScalarKey Elasticity -> "elasticity"
        | VectorKey Tropism -> "tropism"

    let all =
        [ ScalarKey Derivations
          ScalarKey Angle
          ScalarKey Step
          ScalarKey Width
          ScalarKey Taper
          VectorKey Tropism
          ScalarKey Elasticity ]

    let tryParse (s: string) = all |> List.tryFind (fun k -> name k = s)

module SettingEntry =
    let key =
        function
        | ScalarEntry(k, _, _) -> ScalarKey k
        | VectorEntry(k, _, _, _, _) -> VectorKey k

    let pos =
        function
        | ScalarEntry(_, _, p)
        | VectorEntry(_, _, _, _, p) -> p

module Rule =
    let head =
        function
        | Production(h, _, _)
        | Interpretation(h, _, _) -> h

    let pos =
        function
        | Production(_, _, p)
        | Interpretation(_, _, p) -> p
