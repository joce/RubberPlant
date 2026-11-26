/// Continuous-time DOL-system derivation (ABOP Chapter 6, axioms A1-A3).
module RubberPlant.TimedDerivation

open System
open System.Collections.Generic
open RubberPlant.Model
open RubberPlant.LString

exception private TimedError of Diagnostic

let private normZero (x: float) = if x = 0.0 then 0.0 else x

let private checkLimit (maxModules: int option) (count: int) (stage: string) =
    match maxModules with
    | Some limit when count > limit ->
        raise (
            TimedError(
                Diagnostic.error
                    Codes.ModuleLimitExceeded
                    None
                    $"module limit of %s{string limit} exceeded in %s{stage} (%s{string count} symbols)"
            )
        )
    | _ -> ()

/// Derives a timed DOL-system to evaluation time `t` using an explicit
/// iterative work stack (no OS recursion), enforcing `limits.MaxModules`
/// on both output length and total A3 divisions.
let deriveWithTrace (limits: Limits) (model: Model) : Result<LString * Derivation.DerivationTrace, Diagnostic list> =
    let rawT = defaultArg model.Settings.Time 0.0

    if not (Double.IsFinite rawT) || rawT < 0.0 then
        Error
            [ Diagnostic.error
                  Codes.InvalidGeometry
                  None
                  $"evaluation time must be finite and non-negative, got %s{formatNumber rawT}" ]
    else
        let t = normZero rawT

        try
            checkLimit limits.MaxModules 0 "timed derivation"
            checkLimit limits.MaxModules model.Axiom.Length "the axiom"

            let byName = Dictionary<string, int * CTimedRule>()

            model.TimedProductions
            |> List.iteri (fun i r ->
                if not (byName.ContainsKey r.Name) then
                    byName[r.Name] <- (i, r))

            let counts = Array.zeroCreate<int> model.TimedProductions.Length
            let mutable totalDivisions = 0

            let initialCapacity =
                match limits.MaxModules with
                | Some limit -> min (max 16 model.Axiom.Length) (max 0 limit)
                | None -> max 16 model.Axiom.Length

            let out = ResizeArray<Sym>(initialCapacity)

            let ensureOutRoom () =
                match limits.MaxModules with
                | Some limit when out.Count >= limit ->
                    raise (
                        TimedError(
                            Diagnostic.error
                                Codes.ModuleLimitExceeded
                                None
                                $"module limit of %s{string limit} exceeded during timed derivation"
                        )
                    )
                | _ -> ()

            let addSym sym =
                ensureOutRoom ()
                out.Add sym

            let recordDivision (ruleIdx: int) =
                totalDivisions <- totalDivisions + 1

                match limits.MaxModules with
                | Some limit when totalDivisions > limit ->
                    raise (
                        TimedError(
                            Diagnostic.error
                                Codes.ModuleLimitExceeded
                                None
                                $"module limit of %s{string limit} exceeded during timed derivation (more than %s{string limit} divisions)"
                        )
                    )
                | _ -> ()

                counts[ruleIdx] <- counts[ruleIdx] + 1

            let stack = Stack<struct (LString * int * float * float)>()

            if model.Axiom.Length > 0 then
                stack.Push(struct (model.Axiom, 0, 0.0, 0.0))

            while stack.Count > 0 do
                let struct (str, startIdx, elapsed, comp) = stack.Pop()
                let remT = max 0.0 ((t - elapsed) + comp) |> normZero
                let mutable i = startIdx
                let mutable descended = false

                while not descended && i < str.Length do
                    match str[i] with
                    | Push
                    | Pop
                    | Cut as s ->
                        addSym s
                        i <- i + 1
                    | Mod(name, args) as m ->
                        if turtleModuleNames.Contains name then
                            addSym m
                            i <- i + 1
                        elif args.Length <> 1 then
                            raise (
                                TimedError(
                                    Diagnostic.error
                                        Codes.InvalidTimedModule
                                        None
                                        $"timed module `%s{name}` must have arity 1, got %s{string args.Length}"
                                )
                            )
                        else
                            let tau = args[0]

                            let emitAged (newAge: float) =
                                if not (Double.IsFinite newAge) then
                                    raise (
                                        TimedError(
                                            Diagnostic.error
                                                Codes.InvalidGeometry
                                                None
                                                $"age of `%s{name}` is no longer finite (overflow)"
                                        )
                                    )

                                addSym (Mod(name, [| newAge |]))
                                i <- i + 1

                            match byName.TryGetValue name with
                            | true, (ruleIdx, rule) ->
                                let tol =
                                    64.0
                                    * 2.220446049250313e-16
                                    * max (abs t) (max (abs rule.TerminalAge) (abs tau))

                                let dt = rule.TerminalAge - tau

                                let shouldDivide =
                                    if remT <= 0.0 then false
                                    elif dt <= 0.0 then remT > tol
                                    else remT - dt > tol

                                if shouldDivide then
                                    // Axiom A3: D((a, tau), t) = D(succ, t - (beta - tau))
                                    recordDivision ruleIdx
                                    let y = dt - comp
                                    let elapsedNext = elapsed + y
                                    let compNext = (elapsedNext - elapsed) - y

                                    if i + 1 < str.Length then
                                        stack.Push(struct (str, i + 1, elapsed, comp))

                                    if rule.Successor.Length > 0 then
                                        stack.Push(struct (rule.Successor, 0, elapsedNext, compNext))

                                    descended <- true
                                else
                                    // Axiom A2: D((a, tau), t) = (a, tau + t)
                                    let rawAge = tau + remT

                                    let newAge =
                                        if Double.IsFinite rawAge then
                                            min rule.TerminalAge (normZero rawAge)
                                        else
                                            rule.TerminalAge

                                    emitAged newAge
                            | false, _ ->
                                // Axiom A2: D((a, tau), t) = (a, tau + t)
                                emitAged (normZero (tau + remT))

            let ruleFirings: Derivation.RuleFiring list =
                model.TimedProductions
                |> List.mapi (fun i r ->
                    { Line = r.Pos.Line
                      Predecessor = r.Name
                      Arity = 1
                      Count = counts[i] })

            let trace: Derivation.DerivationTrace =
                { Steps = 0
                  Time = Some t
                  FixedPointReached = false
                  TotalRuleFirings = totalDivisions
                  RuleFirings = ruleFirings }

            Ok(out.ToArray(), trace)
        with TimedError d ->
            Error [ d ]

/// Derives a timed DOL-system under `limits`, returning only the derived L-string.
let deriveWith (limits: Limits) (model: Model) : Result<LString, Diagnostic list> =
    deriveWithTrace limits model |> Result.map fst

/// Derives a timed DOL-system under default execution limits.
let derive (model: Model) : Result<LString, Diagnostic list> =
    deriveWith Limits.defaults model
