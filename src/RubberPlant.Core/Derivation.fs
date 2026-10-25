/// Derivation (§4.4), context matching (§4.5) and interpretation (§4.6).
module RubberPlant.Derivation

open System.Collections.Generic
open RubberPlant.Model
open RubberPlant.LString
open RubberPlant.Rng

/// Rules by strict predecessor (name, parameter count), in the order they're
/// tried: rules with context first, then rules without; source order within
/// each group (§4.4).
type RuleIndex = private { Rules: Dictionary<struct (string * int), CRule[]> }

let buildIndex (rules: CRule list) : RuleIndex =
    let d = Dictionary<struct (string * int), CRule[]>()

    rules
    |> List.groupBy (fun r -> struct (r.Pred.Name, r.Pred.Params.Length))
    |> List.iter (fun (key, rs) ->
        let withContext, without = rs |> List.partition _.HasContext
        d[key] <- Array.ofList (withContext @ without))

    { Rules = d }

/// The string being rewritten, with its bracket partners precomputed.
type private Subject =
    { Str: LString
      Partner: int[]
      Ignore: Set<string> }

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
            | Mod(n, _) when s.Ignore.Contains n -> seek (j - 1)
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
            | Mod(name, _) when s.Ignore.Contains name -> seekModule (j + 1)
            | Mod _ -> j

    let rec seekBranch j =
        if j >= n then
            -1
        else
            match s.Str[j] with
            | Push -> j
            | Mod(name, _) when s.Ignore.Contains name -> seekBranch (j + 1)
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

let private emit (out: ResizeArray<Sym>) (succ: Successor) (binds: float[]) =
    for t in succ do
        match t with
        | TPush -> out.Add Push
        | TPop -> out.Add Pop
        | TMod(name, args) -> out.Add(Mod(name, args |> Array.map (Eval.eval binds)))

/// Raised with a fully formed diagnostic; caught at the API boundary.
exception private RewriteError of Diagnostic

/// One parallel rewriting pass over `str` (§4.4).
let private rewrite (index: RuleIndex) (ignore: Set<string>) (what: string) (rng: Rng) (str: LString) =
    let subject =
        { Str = str
          Partner = matchBrackets str
          Ignore = ignore }

    let out = ResizeArray<Sym>(str.Length * 2)
    let mutable rng = rng

    for i in 0 .. str.Length - 1 do
        match str[i] with
        | Push
        | Pop -> out.Add str[i]
        | Mod(name, args) as m ->
            match index.Rules.TryGetValue(struct (name, args.Length)) with
            | false, _ -> out.Add m
            | true, rules ->
                let mutable fired = false
                let mutable r = 0

                while not fired && r < rules.Length do
                    let rule = rules[r]
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
                                emit out succ binds
                                fired <- true
                            | None -> ()
                    with Eval.EvalError(code, msg) ->
                        let modText = format [| m |]

                        raise (
                            RewriteError(
                                Diagnostic.error
                                    code
                                    (Some rule.Pos)
                                    $"%s{msg} in the rule at line %d{rule.Pos.Line}, %s{what} `%s{modText}`"
                            )
                        )

                    r <- r + 1

                if not fired then
                    out.Add m

    struct (out.ToArray(), rng)

/// All derivation steps: the axiom, then each derived string (n + 1 strings).
let deriveSteps (model: Model) : Result<LString list, Diagnostic list> =
    let index = buildIndex model.Productions
    let mutable rng = Rng.create model.Settings.Seed
    let mutable current = model.Axiom
    let steps = ResizeArray [ current ]

    try
        for step in 1 .. model.Settings.Derivations do
            let struct (next, rng') =
                rewrite index model.Ignore $"rewriting (derivation step %d{step})" rng current

            rng <- rng'
            current <- next
            steps.Add current

        Ok(List.ofSeq steps)
    with RewriteError d ->
        Error [ d ]

/// The derived string after `#derivations` steps.
let derive (model: Model) : Result<LString, Diagnostic list> =
    deriveSteps model |> Result.map List.last

/// Applies the interpretation (`as`) rules once, producing the drawing
/// string. The derived string is left untouched (§4.6).
let interpret (model: Model) (derived: LString) : Result<LString, Diagnostic list> =
    if model.Interpretations.IsEmpty then
        Ok derived
    else
        let index = buildIndex model.Interpretations

        try
            // Interpretation rules have no weights, so the generator is never used.
            let struct (drawing, _) =
                rewrite index model.Ignore "interpreting" (Rng.create 0L) derived

            Ok drawing
        with RewriteError d ->
            Error [ d ]
