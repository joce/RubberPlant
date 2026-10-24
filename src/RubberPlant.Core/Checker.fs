/// Static checks (Appendix A.3) and compilation of the AST into the model.
/// Reports every error it finds, not just the first.
module RubberPlant.Checker

open System.Collections.Generic
open RubberPlant.Ast
open RubberPlant.Model
open RubberPlant.LString

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
        | Ignore(names, _) ->
            for n, p in names do
                add n p
        | RuleItem r ->
            for m in headModules (Rule.head r) do
                add m.Name m.Pos

            for s in successors r do
                for m in actualModules s do
                    add m.Name m.Pos
        | Setting _
        | Define _ -> ()

    modules

/// Names a module name could be confused with: anything that looks like an identifier.
let private isIdentLike (name: string) =
    System.Char.IsLetter name[0] || name[0] = '_'

// Checking --------------------------------------------------------------------

type private Context =
    { Modules: Dictionary<string, Pos option>
      /// All defines in the file, with their position (for "used before definition").
      AllDefines: Dictionary<string, Pos>
      Diags: ResizeArray<Diagnostic> }

    member c.Error code pos msg =
        c.Diags.Add(Diagnostic.error code (Some pos) msg)

    member c.Warn code pos msg =
        c.Diags.Add(Diagnostic.warning code (Some pos) msg)

/// Resolves an expression. `defines` holds the defines seen so far (source
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
                    Codes.DefineUsedBeforeDefinition
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

            CCall(f, cargs)
        | None ->
            if paramSlots.ContainsKey name || defines.ContainsKey name || ctx.Modules.ContainsKey name then
                ctx.Error Codes.UnknownFunction pos $"`%s{name}` is not a function"
            else
                ctx.Error Codes.UnknownFunction pos $"unknown function `%s{name}`"

            CNum 0.0

/// Evaluates a constant expression (directive values).
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
        | Branch b -> TPush :: compileActual ctx defines paramSlots b @ [ TPop ])

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
                            $"`%s{name}` is used as a define (line %d{ctx.AllDefines[name].Line}) and as a parameter (line %d{pos.Line})"
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

let private checkSettings (ctx: Context) (values: Map<SettingKey, float * Pos>) : Settings =
    let get key =
        values.TryFind key |> Option.map fst

    let isInteger (v: float) = v = System.Math.Floor v && not (System.Double.IsInfinity v)

    let invalid key msg =
        ctx.Error Codes.InvalidSetting (snd values[key]) msg

    let derivations =
        match get Derivations with
        | Some v when v >= 0.0 && isInteger v && v <= float System.Int32.MaxValue -> int v
        | Some v ->
            invalid Derivations $"`#derivations` must be a non-negative integer, got %s{formatNumber v}"
            0
        | None -> defaultSettings.Derivations

    let taper =
        match get Taper with
        | Some v when v >= 0.0 && v <= 100.0 -> v
        | Some v ->
            invalid Taper $"`#taper` is a percentage and must be between 0 and 100, got %s{formatNumber v}"
            defaultSettings.Taper
        | None -> defaultSettings.Taper

    let seed =
        match get Seed with
        | Some v when isInteger v && abs v < 9.0e15 -> int64 v
        | Some v ->
            invalid Seed $"`#seed` must be an integer, got %s{formatNumber v}"
            0L
        | None -> defaultSettings.Seed

    { Derivations = derivations
      Angle = get Angle |> Option.defaultValue defaultSettings.Angle
      Step = get Step |> Option.defaultValue defaultSettings.Step
      Width = get Width |> Option.defaultValue defaultSettings.Width
      Taper = taper
      Seed = seed }

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

    // Defines: unique, and not clashing with modules or built-ins.
    for item in file do
        match item with
        | Define(name, _, pos) ->
            match allDefines.TryGetValue name with
            | true, first ->
                ctx.Error Codes.DuplicateDefine pos $"`%s{name}` is already defined (line %d{first.Line})"
            | _ -> allDefines[name] <- pos

            match modules.TryGetValue name with
            | true, mp ->
                ctx.Error
                    Codes.NamespaceClash
                    pos
                    $"`%s{name}` is used as a module (%s{lineOf mp}) and as a define (line %d{pos.Line})"
            | _ ->
                if Builtins.isBuiltin name then
                    ctx.Error Codes.NamespaceClash pos $"`%s{name}` is a built-in function and can't be a define"
        | _ -> ()

    // Modules named like built-in functions.
    for kv in modules do
        match kv.Value with
        | Some pos when isIdentLike kv.Key && Builtins.isBuiltin kv.Key ->
            ctx.Error Codes.NamespaceClash pos $"`%s{kv.Key}` is a built-in function and can't be used as a module"
        | _ -> ()

    // Everything else, in source order: defines are only visible after their definition.
    let mutable defines = Map.empty<string, float>
    let mutable settings = Map.empty<SettingKey, float * Pos>
    let mutable axiom: (LString * Pos) option = None
    let ignored = HashSet<string>()
    let productions = ResizeArray<CRule>()
    let interpretations = ResizeArray<CRule>()

    for item in file do
        match item with
        | Setting(key, e, pos) ->
            let what = $"`#%s{SettingKey.name key}`"

            match settings.TryFind key with
            | Some(_, first) ->
                ctx.Error Codes.DuplicateSetting pos $"%s{what} is already set (line %d{first.Line})"
            | None ->
                match resolve ctx defines Map.empty e |> evalConst ctx pos what with
                | Some v -> settings <- settings.Add(key, (v, pos))
                | None -> ()
        | Define(name, e, pos) ->
            if not (defines.ContainsKey name) then
                let v =
                    resolve ctx defines Map.empty e
                    |> evalConst ctx pos $"`#define %s{name}`"

                // On error, still define the name so it doesn't cascade into "unknown name" errors.
                defines <- defines.Add(name, defaultArg v 0.0)
        | Ignore(names, _) ->
            for n, _ in names do
                ignored.Add n |> ignore
        | Axiom(a, pos) ->
            match axiom with
            | Some(_, first) -> ctx.Error Codes.DuplicateAxiom pos $"the axiom is already set (line %d{first.Line})"
            | None ->
                let syms =
                    compileActual ctx defines Map.empty a
                    |> List.map (function
                        | TPush -> Push
                        | TPop -> Pop
                        | TMod(name, args) ->
                            Mod(name, args |> Array.map (fun e -> evalConst ctx pos "`#axiom`" e |> Option.defaultValue 0.0)))
                    |> Array.ofList

                axiom <- Some(syms, pos)
        | RuleItem r ->
            let compiled = compileRule ctx defines r

            match r with
            | Production _ -> productions.Add compiled
            | Interpretation _ -> interpretations.Add compiled

    let checkedSettings = checkSettings ctx settings

    if axiom.IsNone then
        diags.Add(Diagnostic.error Codes.MissingAxiom None "missing `#axiom`")

    lintUnreachable ctx (List.ofSeq productions)
    lintUnreachable ctx (List.ofSeq interpretations)

    let all = diags |> List.ofSeq |> List.sortBy (fun d -> d.Pos)

    if all |> List.exists Diagnostic.isError then
        Error all
    else
        Ok(
            { Settings = checkedSettings
              Ignore = Set.ofSeq ignored
              Axiom = fst axiom.Value
              Productions = List.ofSeq productions
              Interpretations = List.ofSeq interpretations },
            all
        )
