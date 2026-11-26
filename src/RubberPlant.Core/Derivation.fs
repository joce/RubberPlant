/// Derivation (§4.4), context matching (§4.5) and interpretation (§4.6).
module RubberPlant.Derivation

open System.Collections.Generic
open RubberPlant.Model
open RubberPlant.LString
open RubberPlant.Rng

type RuleFiring =
    { /// 1-based line number of the production rule in the source file.
      Line: int
      /// Strict predecessor module name.
      Predecessor: string
      /// Strict predecessor parameter count.
      Arity: int
      /// Total times this production rule fired across all derivation steps.
      Count: int }

type DerivationTrace =
    { Steps: int
      Time: float option
      FixedPointReached: bool
      TotalRuleFirings: int
      RuleFirings: RuleFiring list }

type private IndexedRule = { Index: int; Rule: CRule }

/// Rules by strict predecessor (name, parameter count), in the order they're
/// tried: rules with context first, then rules without; source order within
/// each group (§4.4).
type RuleIndex = private { Rules: Dictionary<struct (string * int), IndexedRule[]> }

let buildIndex (rules: CRule list) : RuleIndex =
    let d = Dictionary<struct (string * int), IndexedRule[]>()

    rules
    |> List.mapi (fun i r -> { Index = i; Rule = r })
    |> List.groupBy (fun ir -> struct (ir.Rule.Pred.Name, ir.Rule.Pred.Params.Length))
    |> List.iter (fun (key, rs) ->
        let withContext, without = rs |> List.partition (fun ir -> ir.Rule.HasContext)
        d[key] <- Array.ofList (withContext @ without))

    { Rules = d }

/// The string being rewritten, with its bracket partners precomputed.
type private Subject =
    { Str: LString
      Partner: int[]
      Context: ContextFilter }

/// Matches a formal module against an actual one, binding parameters.
let private matchFormal (f: CFormal) (name: string) (args: float[]) (binds: float[]) =
    if name <> f.Name || args.Length <> f.Params.Length then
        false
    else
        let mutable ok = true
        let mutable i = 0

        while ok && i < args.Length do
            match f.Params[i] with
            | Bind slot -> binds[slot] <- args[i]
            | Literal v -> ok <- args[i] = v
            | Skip -> ()

            i <- i + 1

        ok

/// Left context: walk toward the root. Skip ignored modules and complete
/// sibling branches; step out of the enclosing branch at a `[` (§4.5).
let private matchLeft (s: Subject) (left: CLeft list) (i: int) (binds: float[]) =
    let rec seek j =
        if j < 0 then
            -1
        else
            match s.Str[j] with
            | Pop -> seek (s.Partner[j] - 1)
            | Push -> seek (j - 1)
            | Cut when ContextFilter.skipsCut s.Context -> seek (j - 1)
            | Mod(n, _) when ContextFilter.skipsModule s.Context n -> seek (j - 1)
            | Cut
            | Mod _ -> j

    let rec go formals j =
        match formals with
        | [] -> true
        | f :: rest ->
            let k = seek j

            k >= 0
            && (match f, s.Str[k] with
                | CLAny, _ -> true
                | CLModule fm, Mod(n, a) -> matchFormal fm n a binds
                | CLModule _, _ -> false)
            && go rest (k - 1)

    go (List.rev left) (i - 1)

/// Right context: walk away from the root. A formal `[` enters an actual
/// branch; branches the formal string doesn't mention are skipped; the end
/// of the current branch fails the match (§4.5).
let private matchRight (s: Subject) (right: CRight list) (i: int) (binds: float[]) =
    let n = s.Str.Length

    let rec seekModule j =
        if j >= n then
            -1
        else
            match s.Str[j] with
            | Push -> seekModule (s.Partner[j] + 1)
            | Pop -> -1
            | Cut when ContextFilter.skipsCut s.Context -> seekModule (j + 1)
            | Mod(name, _) when ContextFilter.skipsModule s.Context name -> seekModule (j + 1)
            | Cut
            | Mod _ -> j

    let rec seekBranch j =
        if j >= n then
            -1
        else
            match s.Str[j] with
            | Push -> j
            | Cut when ContextFilter.skipsCut s.Context -> seekBranch (j + 1)
            | Mod(name, _) when ContextFilter.skipsModule s.Context name -> seekBranch (j + 1)
            | _ -> -1

    let rec go formals j =
        match formals with
        | [] -> true
        | CRBranch inner :: rest ->
            let k = seekBranch j
            k >= 0 && go inner (k + 1) && go rest (s.Partner[k] + 1)
        | f :: rest ->
            let k = seekModule j

            k >= 0
            && (match f, s.Str[k] with
                | CRAny, _ -> true
                | CRModule fm, Mod(name, a) -> matchFormal fm name a binds
                | _ -> false)
            && go rest (k + 1)

    go right (i + 1)

/// Picks the successor of a rule whose head matched, or None if no branch
/// fires (every guard false and no `else`).
let private choose (body: Body) (binds: float[]) (rng: Rng) : struct (Successor option * Rng) =
    let eval = Eval.eval binds

    match body with
    | Simple succ -> struct (Some succ, rng)
    | Guarded(branches, elseB) ->
        let fired =
            branches
            |> List.tryPick (fun (g, succ) -> if Eval.isTrue (eval g) then Some succ else None)

        struct (Option.orElse elseB fired, rng)
    | Weighted(branches, elseB) ->
        let eligible =
            branches
            |> List.filter (fun b ->
                match b.Guard with
                | None -> true
                | Some g -> Eval.isTrue (eval g))

        if eligible.IsEmpty then
            struct (elseB, rng)
        else
            let weighted =
                eligible
                |> List.map (fun b ->
                    let w = eval b.Weight

                    if w < 0.0 || System.Double.IsInfinity w then
                        raise (
                            Eval.EvalError(
                                Codes.InvalidWeight,
                                $"invalid weight %s{formatNumber w}: weights are finite and non-negative"
                            )
                        )

                    w, b.Successor)

            let maxWeight = weighted |> List.map fst |> List.max

            if maxWeight <= 0.0 then
                raise (Eval.EvalError(Codes.NoPositiveWeight, "no eligible branch has a positive weight"))

            // Scale by the largest weight so the sum can't overflow and tiny
            // (subnormal) weights still compare correctly. Zero weights never fire.
            let positive =
                weighted
                |> List.filter (fun (w, _) -> w > 0.0)
                |> List.map (fun (w, succ) -> w / maxWeight, succ)

            let total = positive |> List.sumBy fst
            let struct (u, rng) = Rng.next rng
            let target = u * total

            let rec pick acc =
                function
                | [ (_, succ) ] -> succ
                | (w, succ) :: rest -> if target < acc + w then succ else pick (acc + w) rest
                | [] -> failwith "unreachable"

            struct (Some(pick 0.0 positive), rng)

/// Raised with a fully formed diagnostic; caught at the API boundary.
exception private RewriteError of Diagnostic

let private checkLimit (maxModules: int option) (count: int) (stage: string) =
    match maxModules with
    | Some limit when count > limit ->
        raise (
            RewriteError(
                Diagnostic.error
                    Codes.ModuleLimitExceeded
                    None
                    $"module limit of %s{string limit} exceeded in %s{stage} (%s{string count} symbols)"
            )
        )
    | _ -> ()

let private emit (addSym: Sym -> unit) (ensureRoom: unit -> unit) (succ: Successor) (binds: float[]) =
    for t in succ do
        ensureRoom ()

        match t with
        | TPush -> addSym Push
        | TPop -> addSym Pop
        | TCut -> addSym Cut
        | TMod(name, args) -> addSym (Mod(name, args |> Array.map (Eval.eval binds)))

let private symEqual (a: Sym) (b: Sym) =
    match a, b with
    | Push, Push
    | Pop, Pop
    | Cut, Cut -> true
    | Mod(n1, a1), Mod(n2, a2) ->
        if n1 <> n2 || a1.Length <> a2.Length then
            false
        else
            let mutable eq = true
            let mutable i = 0

            while eq && i < a1.Length do
                if a1[i] <> a2[i] then
                    eq <- false

                i <- i + 1

            eq
    | _ -> false

let private lstringEqual (a: LString) (b: LString) =
    if a.Length <> b.Length then
        false
    else
        let mutable eq = true
        let mutable i = 0

        while eq && i < a.Length do
            if not (symEqual a[i] b[i]) then
                eq <- false

            i <- i + 1

        eq

/// One parallel rewriting pass over `str` (§4.4).
/// With `cut`, a `%` drops itself and the rest of its branch, unrewritten
/// (derivation, "Cutting branches"); otherwise it is copied (interpretation).
/// Returns the rewritten string, updated RNG, and whether any rule fired or
/// any `%` cut occurred on this pass.
let private rewrite
    (index: RuleIndex)
    (context: ContextFilter)
    (cut: bool)
    (counts: int[] voption)
    (maxModules: int option)
    (stage: string)
    (what: string)
    (rng: Rng)
    (str: LString)
    =
    checkLimit maxModules 0 stage

    let subject =
        { Str = str
          Partner = matchBrackets str
          Context = context }

    let initialCapacity =
        let doubled =
            if str.Length > System.Array.MaxLength / 2 then
                System.Array.MaxLength
            else
                str.Length * 2

        match maxModules with
        | Some limit -> min doubled (max 0 limit)
        | None -> doubled

    let out = ResizeArray<Sym>(initialCapacity)

    let ensureRoom () =
        match maxModules with
        | Some limit when out.Count >= limit ->
            raise (
                RewriteError(
                    Diagnostic.error
                        Codes.ModuleLimitExceeded
                        None
                        $"module limit of %s{string limit} exceeded during %s{stage}"
                )
            )
        | _ -> ()

    let addSym sym =
        ensureRoom ()
        out.Add sym

    let mutable rng = rng
    let mutable next = 0
    let mutable anyChanged = false

    while next < str.Length do
        let i = next
        next <- i + 1

        match str[i] with
        | Push
        | Pop -> addSym str[i]
        | Cut when cut ->
            anyChanged <- true
            // Skip to the `]` that closes the branch (kept), or the end of the string.
            let rec branchEnd j =
                if j >= str.Length then
                    j
                else
                    match str[j] with
                    | Pop -> j
                    | Push -> branchEnd (subject.Partner[j] + 1)
                    | Cut
                    | Mod _ -> branchEnd (j + 1)

            next <- branchEnd (i + 1)
        | Cut -> addSym Cut
        | Mod(name, args) as m ->
            match index.Rules.TryGetValue(struct (name, args.Length)) with
            | false, _ -> addSym m
            | true, rules ->
                let mutable fired = false
                let mutable r = 0

                while not fired && r < rules.Length do
                    let indexed = rules[r]
                    let rule = indexed.Rule
                    let binds = Array.zeroCreate rule.Slots

                    try
                        if
                            matchFormal rule.Pred name args binds
                            && matchLeft subject rule.Left i binds
                            && matchRight subject rule.Right i binds
                        then
                            let struct (succ, rng') = choose rule.Body binds rng
                            rng <- rng'

                            match succ with
                            | Some succ ->
                                emit out.Add ensureRoom succ binds
                                fired <- true
                                anyChanged <- true

                                match counts with
                                | ValueSome cs -> cs[indexed.Index] <- cs[indexed.Index] + 1
                                | ValueNone -> ()
                            | None -> ()
                    with Eval.EvalError(code, msg) ->
                        let modText = format [| m |]

                        raise (
                            RewriteError(
                                Diagnostic.error
                                    code
                                    (Some rule.Pos)
                                    $"%s{msg} in the rule at line %s{string rule.Pos.Line}, %s{what} `%s{modText}`"
                            )
                        )

                    r <- r + 1

                if not fired then
                    addSym m

    struct (out.ToArray(), rng, anyChanged)

/// All derivation steps under `limits`: the axiom, then each derived string (n + 1 strings).
let deriveStepsWith (limits: Limits) (model: Model) : Result<LString list, Diagnostic list> =
    let index = buildIndex model.Productions
    let mutable rng = Rng.create model.Settings.Seed
    let mutable current = model.Axiom

    try
        checkLimit limits.MaxModules current.Length "the axiom"
        let steps = ResizeArray [ current ]

        for step in 1 .. model.Settings.Derivations do
            let stage = $"derivation step %s{string step}"

            let struct (next, rng', _) =
                rewrite index model.Context true ValueNone limits.MaxModules stage $"rewriting (%s{stage})" rng current

            rng <- rng'
            current <- next
            steps.Add current

        Ok(List.ofSeq steps)
    with RewriteError d ->
        Error [ d ]

/// All derivation steps: the axiom, then each derived string (n + 1 strings).
let deriveSteps (model: Model) : Result<LString list, Diagnostic list> = deriveStepsWith Limits.defaults model

/// The derived string and telemetry trace after `derivations` steps under `limits`,
/// without retaining intermediate strings.
let deriveWithTrace (limits: Limits) (model: Model) : Result<LString * DerivationTrace, Diagnostic list> =
    let index = buildIndex model.Productions
    let mutable rng = Rng.create model.Settings.Seed
    let mutable current = model.Axiom
    let counts = Array.zeroCreate<int> model.Productions.Length
    let mutable fixedPointReached = false

    try
        checkLimit limits.MaxModules current.Length "the axiom"

        for step in 1 .. model.Settings.Derivations do
            let stage = $"derivation step %s{string step}"

            let struct (next, rng', anyChanged) =
                rewrite
                    index
                    model.Context
                    true
                    (ValueSome counts)
                    limits.MaxModules
                    stage
                    $"rewriting (%s{stage})"
                    rng
                    current

            if step = model.Settings.Derivations then
                fixedPointReached <- not anyChanged || lstringEqual current next

            rng <- rng'
            current <- next

        let ruleFirings =
            model.Productions
            |> List.mapi (fun i r ->
                { Line = r.Pos.Line
                  Predecessor = r.Pred.Name
                  Arity = r.Pred.Params.Length
                  Count = counts[i] })

        let trace =
            { Steps = model.Settings.Derivations
              Time = None
              FixedPointReached = fixedPointReached
              TotalRuleFirings = Array.sum counts
              RuleFirings = ruleFirings }

        Ok(current, trace)
    with RewriteError d ->
        Error [ d ]

/// The derived string after `derivations` steps under `limits`, without retaining intermediate strings.
let deriveWith (limits: Limits) (model: Model) : Result<LString, Diagnostic list> =
    deriveWithTrace limits model |> Result.map fst

/// The derived string after `derivations` steps.
let derive (model: Model) : Result<LString, Diagnostic list> = deriveWith Limits.defaults model

/// Applies the interpretation (`as`) rules once under `limits`, producing the drawing
/// string. The derived string is left untouched (§4.6).
let interpretWith (limits: Limits) (model: Model) (derived: LString) : Result<LString, Diagnostic list> =
    try
        if model.Interpretations.IsEmpty then
            checkLimit limits.MaxModules derived.Length "interpretation"
            Ok derived
        else
            let index = buildIndex model.Interpretations
            // Interpretation rules have no weights, so the generator is never used.
            let struct (drawing, _, _) =
                rewrite
                    index
                    model.Context
                    false
                    ValueNone
                    limits.MaxModules
                    "interpretation"
                    "interpreting"
                    (Rng.create 0L)
                    derived

            Ok drawing
    with RewriteError d ->
        Error [ d ]

/// Applies the interpretation (`as`) rules once, producing the drawing
/// string. The derived string is left untouched (§4.6).
let interpret (model: Model) (derived: LString) : Result<LString, Diagnostic list> =
    interpretWith Limits.defaults model derived
