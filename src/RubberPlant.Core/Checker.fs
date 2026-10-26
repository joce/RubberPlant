/// Static checks (Appendix A.3) and compilation of the AST into the model.
/// Reports every error it finds, not just the first.
module RubberPlant.Checker

open System.Collections.Generic
open RubberPlant.Ast
open RubberPlant.Model
open RubberPlant.LString
open RubberPlant.Geometry

/// Names the turtle predefines (§4.2). Symbols like `+` can't be identifiers anyway.
let turtleNames = [ "F"; "f" ]

let private lineOf (p: Pos option) =
    match p with
    | Some p -> $"line %d{p.Line}"
    | None -> "predefined by the turtle"

// Collecting names ------------------------------------------------------------

let rec private actualModules (s: ActualString) : ActualModule seq =
    seq {
        for e in s do
            match e with
            | Module m -> yield m
            | Branch b -> yield! actualModules b
            | ActualElem.Cut -> ()
    }

let rec private rightModules (r: RightElem list) : FormalModule seq =
    seq {
        for e in r do
            match e with
            | RModule m -> yield m
            | RAny -> ()
            | RBranch b -> yield! rightModules b
    }

let private headModules (h: Head) : FormalModule seq =
    seq {
        for e in h.Left do
            match e with
            | LModule m -> yield m
            | LAny -> ()

        yield h.Pred
        yield! rightModules h.Right
    }

let private successors (r: Rule) : ActualString list =
    match r with
    | Production(_, ProdSimple s, _)
    | Interpretation(_, InterpSimple s, _) -> [ s ]
    | Production(_, ProdGuarded(bs, e), _)
    | Interpretation(_, InterpGuarded(bs, e), _) -> (bs |> List.map _.Successor) @ Option.toList e
    | Production(_, ProdWeighted(bs, e), _) -> (bs |> List.map _.Successor) @ Option.toList e

/// Every name used as a module, with the position of its first use
/// (`None` for names predefined by the turtle).
let private collectModules (file: File) : Dictionary<string, Pos option> =
    let modules = Dictionary<string, Pos option>()

    for n in turtleNames do
        modules[n] <- None

    let add name pos =
        if not (modules.ContainsKey name) then
            modules[name] <- Some pos

    for item in file do
        match item with
        | Axiom(a, _) ->
            for m in actualModules a do
                add m.Name m.Pos
        | Ignore(names, _)
        | Consider(names, _) ->
            for n, p in names do
                add n p
        | RuleItem r ->
            for m in headModules (Rule.head r) do
                add m.Name m.Pos

            for s in successors r do
                for m in actualModules s do
                    add m.Name m.Pos
        | Settings _
        | Let _ -> ()

    modules

/// Names a module name could be confused with: anything that looks like an identifier.
let private isIdentLike (name: string) = Unicode.startsName name

// Checking --------------------------------------------------------------------

type private Context =
    { Modules: Dictionary<string, Pos option>
      /// All `let`s in the file, with their position (for "used before definition").
      AllDefines: Dictionary<string, Pos>
      Diags: ResizeArray<Diagnostic> }

    member c.Error code pos msg =
        c.Diags.Add(Diagnostic.error code (Some pos) msg)

    member c.Warn code pos msg =
        c.Diags.Add(Diagnostic.warning code (Some pos) msg)

/// Resolves an expression. `defines` holds the `let`s seen so far (source
/// order); `paramSlots` maps formal parameter names to slots.
let rec private resolve (ctx: Context) (defines: Map<string, float>) (paramSlots: Map<string, int>) (e: Expr) : CExpr =
    let recur = resolve ctx defines paramSlots

    match e with
    | Num v -> CNum v
    | Neg e -> CNeg(recur e)
    | Not e -> CNot(recur e)
    | Binary(op, a, b) -> CBin(op, recur a, recur b)
    | Ident(name, pos) ->
        match paramSlots.TryFind name, defines.TryFind name with
        | Some slot, _ -> CParam slot
        | None, Some v -> CNum v
        | None, None ->
            if ctx.AllDefines.ContainsKey name then
                ctx.Error
                    Codes.LetUsedBeforeDefinition
                    pos
                    $"`%s{name}` is used before its definition (line %d{ctx.AllDefines[name].Line})"
            elif ctx.Modules.ContainsKey name then
                ctx.Error Codes.NotAValue pos $"`%s{name}` is a module, not a value"
            elif Builtins.isBuiltin name then
                ctx.Error Codes.NotAValue pos $"`%s{name}` is a function; call it with arguments, e.g. `%s{name}(x)`"
            else
                ctx.Error Codes.UnknownName pos $"unknown name `%s{name}`"

            CNum 0.0
    | Call(name, args, pos) ->
        let cargs = args |> List.map recur |> Array.ofList

        match Builtins.byName.TryFind name with
        | Some(f, arity) ->
            if arity <> cargs.Length then
                let plural = if arity = 1 then "" else "s"

                ctx.Error
                    Codes.WrongArity
                    pos
                    $"`%s{name}` takes %d{arity} argument%s{plural}, got %d{cargs.Length}"

                // Constant expressions are evaluated right away: don't call
                // the function with the wrong number of arguments.
                CNum 0.0
            else
                CCall(f, cargs)
        | None ->
            if paramSlots.ContainsKey name || defines.ContainsKey name || ctx.Modules.ContainsKey name then
                ctx.Error Codes.UnknownFunction pos $"`%s{name}` is not a function"
            else
                ctx.Error Codes.UnknownFunction pos $"unknown function `%s{name}`"

            CNum 0.0

/// Evaluates a constant expression (prelude values).
let private evalConst (ctx: Context) (pos: Pos) (what: string) (e: CExpr) : float option =
    try
        Some(Eval.eval [||] e)
    with Eval.EvalError(code, msg) ->
        ctx.Error code pos $"in %s{what}: %s{msg}"
        None

let rec private compileActual (ctx: Context) defines paramSlots (s: ActualString) : CSym list =
    s
    |> List.collect (function
        | Module m -> [ TMod(m.Name, m.Args |> List.map (resolve ctx defines paramSlots) |> Array.ofList) ]
        | Branch b -> TPush :: compileActual ctx defines paramSlots b @ [ TPop ]
        | ActualElem.Cut -> [ TCut ])

/// Compiles a rule head, assigning parameter slots left to right.
let private compileHead (ctx: Context) (h: Head) =
    let slots = Dictionary<string, int>()

    let pattern =
        function
        | PWild -> Skip
        | PNum v -> Literal v
        | PVar(name, pos) ->
            if slots.ContainsKey name then
                ctx.Error Codes.DuplicateParameter pos $"parameter `%s{name}` appears more than once in this rule's head"
            else
                match ctx.Modules.TryGetValue name with
                | true, mp ->
                    ctx.Error
                        Codes.NamespaceClash
                        pos
                        $"`%s{name}` is used as a module (%s{lineOf mp}) and as a parameter (line %d{pos.Line})"
                | _ ->
                    if ctx.AllDefines.ContainsKey name then
                        ctx.Error
                            Codes.NamespaceClash
                            pos
                            $"`%s{name}` is used as a `let` (line %d{ctx.AllDefines[name].Line}) and as a parameter (line %d{pos.Line})"
                    elif Builtins.isBuiltin name then
                        ctx.Error
                            Codes.NamespaceClash
                            pos
                            $"`%s{name}` is a built-in function and can't be a parameter"

            let slot =
                match slots.TryGetValue name with
                | true, s -> s
                | _ ->
                    let s = slots.Count
                    slots[name] <- s
                    s

            Bind slot

    let formal (m: FormalModule) : CFormal =
        { Name = m.Name
          Params = m.Params |> List.map pattern |> Array.ofList }

    let left =
        h.Left
        |> List.map (function
            | LModule m -> CLModule(formal m)
            | LAny -> CLAny)

    let pred = formal h.Pred

    let rec right elems =
        elems
        |> List.map (function
            | RModule m -> CRModule(formal m)
            | RAny -> CRAny
            | RBranch b -> CRBranch(right b))

    let right = right h.Right
    let paramSlots = slots |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
    left, pred, right, paramSlots

let private compileRule (ctx: Context) (defines: Map<string, float>) (rule: Rule) : CRule =
    let left, pred, right, paramSlots = compileHead ctx (Rule.head rule)
    let expr = resolve ctx defines paramSlots
    let succ s = compileActual ctx defines paramSlots s |> Array.ofList

    let guarded (bs: GuardBranch list) e =
        Guarded(bs |> List.map (fun b -> expr b.Guard, succ b.Successor), Option.map succ e)

    let body =
        match rule with
        | Production(_, ProdSimple s, _)
        | Interpretation(_, InterpSimple s, _) -> Simple(succ s)
        | Production(_, ProdGuarded(bs, e), _)
        | Interpretation(_, InterpGuarded(bs, e), _) -> guarded bs e
        | Production(_, ProdWeighted(bs, e), _) ->
            Weighted(
                bs
                |> List.map (fun b ->
                    { Guard = Option.map expr b.Guard
                      Weight = expr b.Weight
                      Successor = succ b.Successor }),
                Option.map succ e
            )

    { Left = left
      Pred = pred
      Right = right
      Slots = paramSlots.Count
      Body = body
      Pos = Rule.pos rule }

/// A rule that fires whenever its predecessor matches: later rules without
/// context for the same module can never fire (§4.4, lint).
let private alwaysFires (r: CRule) =
    not r.HasContext
    && r.Pred.Params
       |> Array.forall (function
           | Literal _ -> false
           | _ -> true)
    && match r.Body with
       | Simple _ -> true
       | Guarded(_, e) -> e.IsSome
       | Weighted(bs, e) -> e.IsSome || bs |> List.exists (fun b -> b.Guard.IsNone)

let private lintUnreachable (ctx: Context) (rules: CRule list) =
    let firing = Dictionary<struct (string * int), CRule>()

    for r in rules do
        if not r.HasContext then
            let key = struct (r.Pred.Name, r.Pred.Params.Length)

            match firing.TryGetValue key with
            | true, first ->
                ctx.Warn
                    Codes.UnreachableRule
                    r.Pos
                    $"this rule never fires: the rule at line %d{first.Pos.Line} always fires first"
            | _ ->
                if alwaysFires r then
                    firing[key] <- r

let private checkSettings
    (ctx: Context)
    (values: Map<ScalarKey, float * Pos>)
    (vectors: Map<VectorKey, Vec3 * Pos>)
    : Settings =
    let get key =
        values.TryFind key |> Option.map fst

    let isInteger (v: float) = v = System.Math.Floor v && not (System.Double.IsInfinity v)

    let invalid key msg =
        ctx.Error Codes.InvalidSetting (snd values[key]) msg

    let elasticity =
        match get Elasticity with
        | Some v when System.Double.IsFinite v -> v
        | Some v ->
            invalid Elasticity $"`elasticity` must be a finite number, got %s{formatNumber v}"
            defaultSettings.Elasticity
        | None -> defaultSettings.Elasticity

    let tropism =
        match vectors.TryFind Tropism with
        | Some(v, pos) ->
            let text = $"(%s{formatNumber v.X}, %s{formatNumber v.Y}, %s{formatNumber v.Z})"

            if not (System.Double.IsFinite v.X && System.Double.IsFinite v.Y && System.Double.IsFinite v.Z) then
                ctx.Error Codes.InvalidSetting pos $"the components of `tropism` must be finite, got %s{text}"
                defaultSettings.Tropism
            elif v = Vec3.zero then
                ctx.Error Codes.InvalidSetting pos "`tropism` gives a direction, so it can't be the zero vector"
                defaultSettings.Tropism
            else
                v
        | None -> defaultSettings.Tropism

    let derivations =
        match get Derivations with
        | Some v when v >= 0.0 && isInteger v && v <= float System.Int32.MaxValue -> int v
        | Some v ->
            invalid Derivations $"`derivations` must be a non-negative integer, got %s{formatNumber v}"
            0
        | None -> defaultSettings.Derivations

    let taper =
        match get Taper with
        | Some v when v >= 0.0 && v <= 100.0 -> v
        | Some v ->
            invalid Taper $"`taper` is a percentage and must be between 0 and 100, got %s{formatNumber v}"
            defaultSettings.Taper
        | None -> defaultSettings.Taper

    let width =
        match get Width with
        | Some v when v >= 0.0 && System.Double.IsFinite v -> v
        | Some v ->
            invalid Width $"`width` must be a finite, non-negative number, got %s{formatNumber v}"
            defaultSettings.Width
        | None -> defaultSettings.Width

    { Derivations = derivations
      Angle = get Angle |> Option.defaultValue defaultSettings.Angle
      Step = get Step |> Option.defaultValue defaultSettings.Step
      Width = width
      Taper = taper
      Tropism = tropism
      Elasticity = elasticity
      // The seed is a run parameter, set by the host (§4.7).
      Seed = defaultSettings.Seed }

/// Checks a parsed file and compiles it into a model. On success, also
/// returns the warnings.
let check (file: File) : Result<Model * Diagnostic list, Diagnostic list> =
    let modules = collectModules file
    let allDefines = Dictionary<string, Pos>()
    let diags = ResizeArray<Diagnostic>()

    let ctx =
        { Modules = modules
          AllDefines = allDefines
          Diags = diags }

    // `let`s: unique, and not clashing with modules or built-ins.
    for item in file do
        match item with
        | Let(name, _, pos) ->
            match allDefines.TryGetValue name with
            | true, first ->
                ctx.Error Codes.DuplicateLet pos $"`%s{name}` is already defined (line %d{first.Line})"
            | _ -> allDefines[name] <- pos

            match modules.TryGetValue name with
            | true, mp ->
                ctx.Error
                    Codes.NamespaceClash
                    pos
                    $"`%s{name}` is used as a module (%s{lineOf mp}) and as a `let` (line %d{pos.Line})"
            | _ ->
                if Builtins.isBuiltin name then
                    ctx.Error Codes.NamespaceClash pos $"`%s{name}` is a built-in function and can't be a `let`"
        | _ -> ()

    // Modules named like built-in functions.
    for kv in modules do
        match kv.Value with
        | Some pos when isIdentLike kv.Key && Builtins.isBuiltin kv.Key ->
            ctx.Error Codes.NamespaceClash pos $"`%s{kv.Key}` is a built-in function and can't be used as a module"
        | _ -> ()

    // Everything else, in source order: `let`s are only visible after their definition.
    let mutable defines = Map.empty<string, float>
    let mutable settingsSeen = Map.empty<SettingKey, Pos>
    let mutable scalars = Map.empty<ScalarKey, float * Pos>
    let mutable vectors = Map.empty<VectorKey, Vec3 * Pos>
    let mutable settingsBlock: Pos option = None
    let mutable ignoreList: Pos option = None
    let mutable considerList: Pos option = None
    let mutable axiom: (LString * Pos) option = None
    let mutable firstRule: Pos option = None
    let listed = HashSet<string>()
    let productions = ResizeArray<CRule>()
    let interpretations = ResizeArray<CRule>()

    for item in file do
        match firstRule, item with
        | Some r, (Settings(_, p) | Let(_, _, p) | Ignore(_, p) | Consider(_, p) | Axiom(_, p)) ->
            ctx.Error
                Codes.DeclarationAfterRules
                p
                $"declaration after rules: `settings`, `let`, `ignore:`, `consider:` and `axiom:` must come before the first rule (line %d{r.Line})"
        | _ -> ()

        match item with
        | Settings(entries, pos) ->
            match settingsBlock with
            | Some first -> ctx.Error Codes.DuplicateSettings pos $"there is already a `settings` block (line %d{first.Line})"
            | None ->
                settingsBlock <- Some pos

                for entry in entries do
                    let key = SettingEntry.key entry
                    let pos = SettingEntry.pos entry
                    let what = $"`%s{SettingKey.name key}`"
                    let value e = resolve ctx defines Map.empty e |> evalConst ctx pos what

                    match settingsSeen.TryFind key with
                    | Some first ->
                        ctx.Error Codes.DuplicateSetting pos $"%s{what} is already set (line %d{first.Line})"
                    | None ->
                        settingsSeen <- settingsSeen.Add(key, pos)

                        match entry with
                        | ScalarEntry(k, e, _) ->
                            match value e with
                            | Some v -> scalars <- scalars.Add(k, (v, pos))
                            | None -> ()
                        | VectorEntry(k, x, y, z, _) ->
                            // Evaluate every component, so each error is reported. A
                            // component in error isn't checked further (no cascade into
                            // "zero vector").
                            let before = diags.Count

                            match value x, value y, value z with
                            | Some x, Some y, Some z when diags.Count = before ->
                                vectors <- vectors.Add(k, (Vec3.create x y z, pos))
                            | _ -> ()
        | Let(name, e, pos) ->
            if not (defines.ContainsKey name) then
                let v =
                    resolve ctx defines Map.empty e
                    |> evalConst ctx pos $"`let %s{name}`"

                // On error, still define the name so it doesn't cascade into "unknown name" errors.
                defines <- defines.Add(name, defaultArg v 0.0)
        | Ignore(names, pos) ->
            match ignoreList, considerList with
            | Some first, _ ->
                ctx.Error Codes.DuplicateIgnore pos $"there is already an `ignore:` list (line %d{first.Line})"
            | None, Some other ->
                ctx.Error
                    Codes.IgnoreAndConsider
                    pos
                    $"a file can't have both `ignore:` and `consider:`; there is already a `consider:` list (line %d{other.Line})"
            | None, None ->
                ignoreList <- Some pos

                for n, _ in names do
                    listed.Add n |> ignore
        | Consider(names, pos) ->
            match considerList, ignoreList with
            | Some first, _ ->
                ctx.Error Codes.DuplicateConsider pos $"there is already a `consider:` list (line %d{first.Line})"
            | None, Some other ->
                ctx.Error
                    Codes.IgnoreAndConsider
                    pos
                    $"a file can't have both `ignore:` and `consider:`; there is already an `ignore:` list (line %d{other.Line})"
            | None, None ->
                considerList <- Some pos

                for n, _ in names do
                    listed.Add n |> ignore
        | Axiom(a, pos) ->
            match axiom with
            | Some(_, first) -> ctx.Error Codes.DuplicateAxiom pos $"the axiom is already set (line %d{first.Line})"
            | None ->
                // Walk the AST directly (not compileActual) so an evaluation error
                // points at the failing module, not at `axiom`: axioms can span lines.
                let rec evalAxiom (s: ActualString) : Sym list =
                    s
                    |> List.collect (function
                        | Module m ->
                            let arg e =
                                resolve ctx defines Map.empty e
                                |> evalConst ctx m.Pos "`axiom:`"
                                |> Option.defaultValue 0.0

                            [ Mod(m.Name, m.Args |> List.map arg |> Array.ofList) ]
                        | Branch b -> Push :: evalAxiom b @ [ Pop ]
                        | ActualElem.Cut -> [ Sym.Cut ])

                let syms = evalAxiom a |> Array.ofList

                axiom <- Some(syms, pos)
        | RuleItem r ->
            if firstRule.IsNone then
                firstRule <- Some(Rule.pos r)

            let compiled = compileRule ctx defines r

            match r with
            | Production _ -> productions.Add compiled
            | Interpretation _ -> interpretations.Add compiled

    let checkedSettings = checkSettings ctx scalars vectors

    let context =
        if considerList.IsSome then
            ConsiderList(Set.ofSeq listed)
        else
            IgnoreList(Set.ofSeq listed)

    if axiom.IsNone then
        diags.Add(Diagnostic.error Codes.MissingAxiom None "missing `axiom:`")

    lintUnreachable ctx (List.ofSeq productions)
    lintUnreachable ctx (List.ofSeq interpretations)

    let all = diags |> List.ofSeq |> List.sortBy (fun d -> d.Pos)

    if all |> List.exists Diagnostic.isError then
        Error all
    else
        Ok(
            { Settings = checkedSettings
              Context = context
              Axiom = fst axiom.Value
              Productions = List.ofSeq productions
              Interpretations = List.ofSeq interpretations },
            all
        )
