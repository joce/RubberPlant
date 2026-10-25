/// Parser for the RubberPlant language, following `docs/language.md`, Appendix A.
///
/// The parser has no separate lexer: FParsec combinators tokenize and parse
/// in one pass. Each token parser skips the trivia that follows it. Newlines
/// are trivia everywhere (A.4).
module RubberPlant.Parser

open System
open System.Globalization
open FParsec
open RubberPlant.Ast

type private P<'a> = Parser<'a, unit>

// Errors ------------------------------------------------------------------

let private toPos (p: Position) = { Line = int p.Line; Column = int p.Column }

let private getPos: P<Pos> = fun s -> Reply(toPos s.Position)

/// A fatal error carrying our own diagnostic, so it gets a stable code.
let private fatalAt (p: Pos) code msg : Reply<'a> =
    Reply(FatalError, otherError (box (Diagnostic.error code (Some p) msg)))

let private fatal code msg : P<'a> =
    fun s -> fatalAt (toPos s.Position) code msg

/// Like FParsec's `<?>`, but only relabels non-fatal errors, so our coded
/// diagnostics survive.
let private (<??>) (p: P<'a>) (label: string) : P<'a> =
    fun s ->
        let tag = s.StateTag
        let r = p s

        if r.Status = Error && s.StateTag = tag then
            Reply(Error, expected label)
        else
            r

// Character classes ---------------------------------------------------------

let private isDigit c = c >= '0' && c <= '9'

let private ruleKeywords = set [ "as"; "when"; "else"; "weight" ]

/// Keywords that start a prelude statement.
let private declKeywords = set [ "settings"; "let"; "ignore"; "axiom" ]

/// Keywords end a module string or an expression (§2.2, §2.5).
let private keywords = ruleKeywords + declKeywords

let private settingNames = SettingKey.all |> List.map SettingKey.name |> set

/// Why a name that isn't a keyword can't be used at all (§2.6), if it can't.
let private reservedReason (n: string) =
    if n = "include" || n = "module" then
        Some $"`%s{n}` is reserved and not supported yet"
    elif settingNames.Contains n then
        Some $"`%s{n}` is a setting name: it can only be a key in the `settings` block, and settings aren't values"
    else
        None

let private keywordAsName (n: string) =
    $"`%s{n}` is a keyword and can't be used as a name"

/// Book characters accepted as aliases (§2.7). Multi-character aliases
/// (`→`, `≤`, `≥`, `≠`) are handled where those tokens are parsed.
let private normalize c =
    match c with
    | '−' -> '-'
    | '∧' -> '^'
    | '∼' -> '~'
    | '’' -> '\''
    | '‘' -> '`'
    | c -> c

/// Characters that can't be a module-mode symbol (`ModReserved`, plus the
/// multi-character aliases whose expansions start with a reserved character).
let private isModReserved c =
    match c with
    | '('
    | ')'
    | ','
    | '#'
    | ':'
    | '<'
    | '>'
    | '*'
    | '='
    | ';'
    | '`'
    | '≤'
    | '≥'
    | '≠' -> true
    | _ -> false

let private isTriviaStart (s: CharStream<unit>) =
    match s.Peek() with
    | ' '
    | '\t'
    | '\n'
    | '\r' -> true
    | '(' -> s.Peek 1 = '*'
    | _ -> false

/// Length of the name-start character at offset `i` (0 if none). Letters
/// are Unicode scalars, so a supplementary-plane letter has length 2.
let private nameStartAt (s: CharStream<unit>) (i: int) =
    let c = s.Peek i

    if c = '_' then
        1
    else
        let struct (len, letter) = Unicode.scalarAt c (s.Peek(i + 1))
        if letter then len else 0

let private nameCharAt (s: CharStream<unit>) (i: int) =
    if isDigit (s.Peek i) then 1 else nameStartAt s i

/// Length of the name starting at the current position (0 if none).
let private nameLength (s: CharStream<unit>) =
    let first = nameStartAt s 0

    if first = 0 then
        0
    else
        let mutable i = first
        let mutable len = nameCharAt s i

        while len > 0 do
            i <- i + len
            len <- nameCharAt s i

        i

let private peekName (s: CharStream<unit>) = s.PeekString(nameLength s)

let private isArrowAt (s: CharStream<unit>) =
    let c = s.Peek()
    c = '→' || ((c = '-' || c = '−') && s.Peek 1 = '>')

/// Describes the token at the current position, for error messages.
let private describeHere (s: CharStream<unit>) =
    if s.IsEndOfStream then
        "end of file"
    else
        match s.Peek() with
        | _ when nameLength s > 0 -> $"`%s{peekName s}`"
        | _ when isArrowAt s -> "`->`"
        | c -> $"`%c{c}`"

// Trivia ----------------------------------------------------------------------

/// Skips a nested `(* … *)` comment. Precondition: at `(*`.
let private skipComment (s: CharStream<unit>) : Reply<unit> =
    let start = toPos s.Position
    s.Skip 2
    let mutable depth = 1

    while depth > 0 && not s.IsEndOfStream do
        if s.Skip "(*" then depth <- depth + 1
        elif s.Skip "*)" then depth <- depth - 1
        elif not (s.SkipNewline()) then s.Skip()

    if depth = 0 then
        Reply(())
    else
        fatalAt start Codes.UnterminatedComment "unterminated comment"

/// Skips spaces, tabs, newlines and comments.
let private skipTrivia: P<unit> =
    fun s ->
        let mutable result = Reply(())
        let mutable go = true

        while go do
            match s.Peek() with
            | ' '
            | '\t' -> s.Skip()
            | '\n' -> s.SkipNewline() |> ignore
            | '\r' when s.Peek 1 = '\n' -> s.SkipNewline() |> ignore
            | '(' when s.Peek 1 = '*' ->
                let r = skipComment s

                if r.Status <> Ok then
                    result <- r
                    go <- false
            | _ -> go <- false

        result

/// Skips the trivia after a token.
let private ws: P<unit> = skipTrivia

/// A one-character punctuation token.
let private punct (c: char) : P<unit> =
    fun s ->
        if s.Peek() = c then
            s.Skip()
            ws s
        else
            Reply(Error, expected $"`%c{c}`")

let private keyword (k: string) : P<unit> =
    fun s ->
        if peekName s = k then
            s.Skip k.Length
            ws s
        else
            Reply(Error, expected $"`%s{k}`")

let private arrow: P<unit> =
    fun s ->
        if s.Peek() = '→' then
            s.Skip()
            ws s
        elif isArrowAt s then
            s.Skip 2
            ws s
        else
            Reply(Error, expected "`->`")

// Keyword diagnostics -----------------------------------------------------------

/// True if the name at the current position is followed by `=` or `:`.
let private nameThenSeparator (s: CharStream<unit>) =
    let mutable start = s.State
    s.Skip(nameLength s)
    let r = skipTrivia s
    let hit = r.Status = Ok && (s.Peek() = '=' || s.Peek() = ':')
    s.BacktrackTo(&start)
    hit

/// True if a prelude statement starts here (`settings {`, `let x =`,
/// `axiom:`, `ignore:`), so a `;` before it is probably missing.
let private statementAhead (s: CharStream<unit>) =
    let mutable start = s.State
    let k = peekName s

    let hit =
        match k with
        | "let" ->
            s.Skip k.Length
            skipTrivia s |> ignore
            nameLength s > 0 && nameThenSeparator s
        | "settings" ->
            s.Skip k.Length
            (skipTrivia s).Status = Ok && s.Peek() = '{'
        | "axiom"
        | "ignore" -> nameThenSeparator s
        | _ -> false

    s.BacktrackTo(&start)
    hit

/// A keyword that can't appear where it is (`inWhat`: "a module string" or
/// "an expression").
let private misplacedKeyword (k: string) (inWhat: string) =
    let hint =
        match k with
        | "when"
        | "weight"
        | "else" -> "; `when`, `weight` and `else` start a branch, after the rule's `:`"
        | "as" -> "; `as` starts an interpretation rule's successor"
        | _ -> $"; `%s{k}` starts a statement"

    $"keyword `%s{k}` can't appear in %s{inWhat}%s{hint}"

// Module-mode diagnostics -------------------------------------------------------

/// Fails with the most helpful message for whatever stopped a module string.
/// `expecting` describes what should have come next (e.g. "`;`").
/// `ruleEnd`: the module string ends a statement, so a following rule head
/// or prelude keyword means a `;` is probably missing.
let private moduleStop (expecting: string) (ruleEnd: bool) : P<'a> =
    fun s ->
        let p = toPos s.Position
        let c = s.Peek()
        let fail code msg = fatalAt p code msg

        let name = peekName s

        if s.IsEndOfStream then
            if ruleEnd then
                fail Codes.MissingSemicolon "missing `;` at the end of the file"
            else
                fail Codes.Syntax $"unexpected end of file; expected %s{expecting}"
        // `as` is the next interpretation rule's arrow; other keywords mean
        // a missing `;` only if a whole statement follows.
        elif ruleEnd && keywords.Contains name && name <> "as" && not (statementAhead s) then
            fail Codes.MisplacedKeyword (misplacedKeyword name "a module string")
        elif ruleEnd && (isArrowAt s || c = ':' || c = '<' || c = '>' || keywords.Contains name) then
            fail Codes.MissingSemicolon $"missing `;`? unexpected %s{describeHere s}"
        elif c = '#' then
            fail Codes.ReservedChar "`#` has no meaning; it's reserved"
        elif isDigit c then
            fail
                Codes.NumberInModuleMode
                "a number can't appear in a module string; numbers go inside a module's parameter list, e.g. `F(2)`"
        elif c = '`' || c = '‘' then
            fail Codes.ReservedChar "the backtick has no meaning; it's reserved"
        elif c = '=' then
            fail Codes.ReservedChar "`=` can't appear in a module string"
        elif c = '*' then
            fail Codes.ReservedChar "`*` is only allowed as a wildcard in a context"
        elif c = '(' then
            fail Codes.ReservedChar "`(` must directly follow a module name"
        elif c = ']' then
            fail Codes.Syntax "unmatched `]`"
        elif name = "_" then
            fail Codes.ReservedName "`_` is the wildcard; it can only be used as a parameter pattern"
        else
            fail Codes.Syntax $"unexpected %s{describeHere s}; expected %s{expecting}"

/// Like `moduleStop` where a module name is expected: a keyword there is
/// being used as a name.
let private moduleExpected (expecting: string) : P<'a> =
    fun s ->
        let n = peekName s

        if keywords.Contains n then
            fatal Codes.ReservedName (keywordAsName n) s
        else
            moduleStop expecting false s

// Expressions -----------------------------------------------------------------

let private number: P<float> =
    // U+2212 (book minus) is an alias for "-", including in exponents (§2.7).
    regex @"(?:[0-9]+(?:\.[0-9]+)?|\.[0-9]+)(?:[eE][+\-−]?[0-9]+)?"
    |>> fun t -> Double.Parse(t.Replace('−', '-'), NumberStyles.Float, CultureInfo.InvariantCulture)
    <?> "number"

type private IdentRole =
    /// A name read in an expression: a keyword ends the expression.
    | Use
    /// A name being defined by `let`: a keyword is an error.
    | Bind
    /// A parameter pattern: like `Bind`, and a lone `_` is returned as is.
    | Param

/// An identifier: a name that isn't reserved or the lone wildcard `_`.
let private identOrWild (role: IdentRole) (what: string) : P<string * Pos> =
    fun s ->
        let p = toPos s.Position
        let n = peekName s

        if n = "" || (role = Use && keywords.Contains n) then
            Reply(Error, expected what)
        elif keywords.Contains n then
            fatalAt p Codes.ReservedName (keywordAsName n)
        elif (reservedReason n).IsSome then
            fatalAt p Codes.ReservedName (reservedReason n).Value
        elif n = "_" && role <> Param then
            fatalAt p Codes.ReservedName "`_` is the wildcard; it can only be used as a parameter pattern"
        else
            s.Skip n.Length
            let r = ws s
            if r.Status = Ok then Reply((n, p)) else Reply(r.Status, r.Error)


/// An operator token: one of `ops` (first match wins), not followed by any
/// character in `notBefore`.
let private op (ops: (string * 'r) list) (notBefore: string) : P<'r> =
    fun s ->
        let hit =
            ops
            |> List.tryFind (fun (o, _) -> s.Match o && notBefore.IndexOf(s.Peek o.Length) < 0)

        match hit with
        | Some(o, r) ->
            s.Skip o.Length
            let w = ws s
            if w.Status = Ok then Reply(r) else Reply(w.Status, w.Error)
        | None -> Reply(Error, NoErrorMessages)

let private minus = op [ "-", (); "−", () ] ">"
let private powOp = op [ "^", (); "∧", () ] ""
let private mulOp = op [ "*", Mul; "/", Div; "%", Mod ] ""
let private addOp = op [ "+", Add; "-", Sub; "−", Sub ] ">"
let private notOp = op [ "!", () ] "="

let private relOp =
    op [ "<=", Le; "≤", Le; ">=", Ge; "≥", Ge; "==", Eq; "!=", Ne; "≠", Ne; "<", Lt; ">", Gt ] ""

let private andOp = op [ "&&", () ] ""
let private orOp = op [ "||", () ] ""

/// `=`, `&`, `|` and `#` can never continue an expression (§2.5): give a
/// helpful error instead of a generic one.
let private noReservedOperator: P<unit> =
    fun s ->
        let p = toPos s.Position

        match s.Peek() with
        | '=' -> fatalAt p Codes.UseCOperator "use `==` for comparison"
        | '&' -> fatalAt p Codes.UseCOperator "use `&&` for logical and"
        | '|' -> fatalAt p Codes.UseCOperator "use `||` for logical or"
        | '#' -> fatalAt p Codes.ReservedChar "`#` is reserved here"
        | _ -> Reply(())

let private expr, private exprRef = createParserForwardedToRef<Expr, unit> ()

let private foldLeft (first: Expr) (rest: (BinOp * Expr) list) =
    List.fold (fun acc (o, e) -> Binary(o, acc, e)) first rest

let private atom: P<Expr> =
    let args = punct '(' >>. sepBy expr (punct ',') .>> punct ')'

    choice
        [ number .>> ws |>> Num
          identOrWild Use "name" .>>. opt args
          |>> (fun ((n, p), a) ->
              match a with
              | None -> Ident(n, p)
              | Some a -> Call(n, a, p))
          punct '(' >>. expr .>> punct ')' ]

let private unary, private unaryRef = createParserForwardedToRef<Expr, unit> ()

let private pow =
    atom .>>. opt (powOp >>. unary)
    |>> fun (a, e) ->
        match e with
        | None -> a
        | Some e -> Binary(Pow, a, e)

unaryRef.Value <- (minus >>. unary |>> Neg) <|> pow <??> "expression"

let private mul = unary .>>. many (mulOp .>>. unary) |>> fun (a, r) -> foldLeft a r
let private add = mul .>>. many (addOp .>>. mul) |>> fun (a, r) -> foldLeft a r

let private rel =
    add .>>. opt (relOp .>>. add)
    |>> fun (a, r) ->
        match r with
        | None -> a
        | Some(o, b) -> Binary(o, a, b)

let private notExpr, private notRef = createParserForwardedToRef<Expr, unit> ()
notRef.Value <- (notOp >>. notExpr |>> Not) <|> rel

let private andExpr =
    notExpr .>>. many (andOp >>. notExpr)
    |>> fun (a, r) -> List.fold (fun acc e -> Binary(And, acc, e)) a r

let private orExpr =
    andExpr .>>. many (orOp >>. andExpr)
    |>> fun (a, r) -> List.fold (fun acc e -> Binary(Or, acc, e)) a r

exprRef.Value <- orExpr .>> noReservedOperator

// Module strings ----------------------------------------------------------------

/// A module name: a name (not a keyword or `_`) or a one-character symbol.
/// Fails without consuming input at anything else.
let private moduleName: P<string * Pos> =
    fun s ->
        let p = toPos s.Position
        let c = s.Peek()
        let len = nameLength s

        if len > 0 then
            let n = s.PeekString len

            if keywords.Contains n then
                Reply(Error, expected "module")
            elif (reservedReason n).IsSome then
                fatalAt p Codes.ReservedName (reservedReason n).Value
            elif n = "_" then
                fatalAt p Codes.ReservedName "`_` is the wildcard; it can only be used as a parameter pattern"
            else
                s.Skip len
                Reply((n, p))
        elif
            s.IsEndOfStream
            || isArrowAt s
            || isTriviaStart s
            || Char.IsWhiteSpace c
            || Char.IsControl c
            || isDigit c
            || c = '['
            || c = ']'
            || isModReserved (normalize c)
        then
            Reply(Error, expected "module")
        else
            // One Unicode scalar: a surrogate pair is a single symbol.
            let struct (len, _) = Unicode.scalarAt c (s.Peek 1)
            let sym = if len = 1 then string (normalize c) else s.PeekString len
            s.Skip len
            Reply((sym, p))

let private moduleNameTok = moduleName .>> ws

let private actualModule: P<ActualModule> =
    moduleNameTok .>>. opt (punct '(' >>. sepBy1 expr (punct ',') .>> punct ')')
    |>> fun ((n, p), args) ->
        { Name = n
          Args = defaultArg args []
          Pos = p }

/// `[ … ]` around `inner`, with a helpful error if the `]` is missing.
let private bracketed (inner: P<'a>) : P<'a> =
    fun s ->
        let p = toPos s.Position

        if s.Peek() <> '[' then
            Reply(Error, expected "`[`")
        else
            s.Skip()
            let r = (ws >>. inner) s

            if r.Status <> Ok then r
            elif s.Peek() = ']' then
                s.Skip()
                let w = ws s
                if w.Status = Ok then r else Reply(w.Status, w.Error)
            else
                moduleStop $"`]` to close the `[` at line %d{p.Line}, column %d{p.Column}" false s

let private actualString, private actualStringRef =
    createParserForwardedToRef<ActualString, unit> ()

actualStringRef.Value <-
    many (
        (actualModule |>> Module)
        <|> (bracketed actualString |>> Branch)
    )

let private pattern: P<Pattern> =
    choice
        [ identOrWild Param "parameter"
          |>> (fun (n, p) -> if n = "_" then PWild else PVar(n, p))
          opt minus .>>. (number .>> ws)
          |>> fun (neg, v) -> PNum(if neg.IsSome then -v else v) ]

let private formalModule: P<FormalModule> =
    moduleNameTok .>>. opt (punct '(' >>. sepBy1 pattern (punct ',') .>> punct ')')
    |>> fun ((n, p), ps) ->
        { Name = n
          Params = defaultArg ps []
          Pos = p }

let private leftElem: P<LeftElem> =
    fun s ->
        if s.Peek() = '[' || s.Peek() = ']' then
            fatal Codes.BracketNotAllowed "brackets aren't allowed in a left context or a predecessor" s
        else
            ((punct '*' >>% LAny) <|> (formalModule |>> LModule)) s

let private rightContext, private rightContextRef =
    createParserForwardedToRef<RightElem list, unit> ()

let private rightElem: P<RightElem> =
    choice
        [ punct '*' >>% RAny
          bracketed rightContext |>> RBranch
          formalModule |>> RModule ]

rightContextRef.Value <- many1 rightElem

// Rules -----------------------------------------------------------------------

let private head: P<Head> =
    fun s ->
        let first = many1 leftElem s

        if first.Status <> Ok then
            if first.Status = Error then
                moduleExpected "a rule or a prelude statement" s
            else
                Reply(first.Status, first.Error)
        else
            let elems = first.Result

            let leftAndPred =
                if s.Peek() = '<' then
                    s.Skip()

                    let r = (ws >>. formalModule) s

                    if r.Status = Ok then
                        Result.Ok(elems, r.Result)
                    elif r.Status = Error then
                        Result.Error(moduleExpected "the predecessor after `<`" s)
                    else
                        Result.Error(Reply(r.Status, r.Error))
                else
                    match elems with
                    | [ LModule m ] -> Result.Ok([], m)
                    | [ LAny ] ->
                        Result.Error(fatal Codes.Syntax "`*` can't be a predecessor; it's only allowed in a context" s)
                    | _ ->
                        Result.Error(
                            fatal
                                Codes.Syntax
                                "a predecessor is a single module; write `left < pred` for a left context"
                                s
                        )

            match leftAndPred with
            | Result.Error reply -> reply
            | Result.Ok(left, pred) ->
                if s.Peek() = '>' then
                    s.Skip()
                    let r = (ws >>. rightContext) s

                    if r.Status = Ok then
                        Reply({ Left = left; Pred = pred; Right = r.Result })
                    elif r.Status = Error then
                        moduleExpected "a right context after `>`" s
                    else
                        Reply(r.Status, r.Error)
                else
                    Reply({ Left = left; Pred = pred; Right = [] })

let private semicolon: P<unit> =
    fun s ->
        if s.Peek() = ';' then
            s.Skip()
            ws s
        else
            moduleStop "`;`" true s

type private RawBranch =
    | BGuard of pos: Pos * guard: Expr * isAs: bool * ActualString
    | BWeight of pos: Pos * guard: Expr option * weight: Expr * ActualString

/// `-> succ` or `as succ`; returns true for `as`.
let private arrowOrAs: P<bool> =
    (arrow >>% false) <|> (keyword "as" >>% true)

let private weightTail (pos: Pos) (guard: Expr option) : P<RawBranch> =
    keyword "weight" >>. expr
    .>>. (fun s ->
        if peekName s = "as" then
            fatal Codes.MixedBranches "interpretation rules have no weights" s
        else
            arrow s)
    .>>. actualString
    |>> fun ((w, ()), succ) -> BWeight(pos, guard, w, succ)

let private rawBranch: P<RawBranch> =
    getPos
    >>= fun p ->
        (keyword "when" >>. expr
         >>= fun g ->
             (weightTail p (Some g))
             <|> (arrowOrAs .>>. actualString |>> fun (isAs, succ) -> BGuard(p, g, isAs, succ))
             <|> (fun s -> moduleStop "`->`, `as` or `weight` after the guard" false s))
        <|> weightTail p None

let private elseBranch: P<Pos * bool * ActualString> =
    getPos .>> keyword "else" .>>. arrowOrAs .>>. actualString
    |>> fun ((p, isAs), succ) -> (p, isAs, succ)

let private branchPos =
    function
    | BGuard(p, _, _, _)
    | BWeight(p, _, _, _) -> p

/// Classifies a branch list (§3.4), enforcing the grammar's restrictions
/// with targeted messages.
let private buildBranches
    (h: Head)
    (rulePos: Pos)
    (branches: RawBranch list)
    (elseB: (Pos * bool * ActualString) option)
    : Result<Rule, Diagnostic> =
    let err p code msg =
        Result.Error(Diagnostic.error code (Some p) msg)

    let isInterp =
        match branches.Head with
        | BGuard(_, _, isAs, _) -> isAs
        | BWeight _ -> false

    let arrowName isAs = if isAs then "as" else "->"

    let mismatch =
        branches
        |> List.tryPick (function
            | BGuard(p, _, isAs, _) when isAs <> isInterp -> Some p
            | _ -> None)

    match mismatch with
    | Some p ->
        err p Codes.MixedBranches "a rule can't mix `->` and `as` branches"
    | None ->
        match elseB with
        | Some(p, isAs, _) when isAs <> isInterp ->
            err p Codes.MixedBranches $"this rule's branches use `%s{arrowName isInterp}`; write `else %s{arrowName isInterp}`"
        | _ ->
            let elseSucc = elseB |> Option.map (fun (_, _, succ) -> succ)

            let guards =
                branches
                |> List.choose (function
                    | BGuard(_, g, _, succ) -> Some { Guard = g; Successor = succ }
                    | _ -> None)

            let weights =
                branches
                |> List.choose (function
                    | BWeight(_, g, w, succ) ->
                        Some
                            { WeightBranch.Guard = g
                              Weight = w
                              Successor = succ }
                    | _ -> None)

            let firstWeight =
                branches
                |> List.tryPick (function
                    | BWeight(p, _, _, _) -> Some p
                    | _ -> None)

            if isInterp && firstWeight.IsSome then
                err firstWeight.Value Codes.MixedBranches "interpretation rules have no weights"
            elif isInterp then
                Result.Ok(Interpretation(h, InterpGuarded(guards, elseSucc), rulePos))
            elif weights.IsEmpty then
                Result.Ok(Production(h, ProdGuarded(guards, elseSucc), rulePos))
            elif not guards.IsEmpty then
                let p =
                    branches
                    |> List.find (function
                        | BGuard _ -> true
                        | _ -> false)
                    |> branchPos

                err
                    p
                    Codes.MixedBranches
                    "a rule's branches are either all `when … ->` (deterministic) or all `weight` (stochastic)"
            else
                let unguarded =
                    branches
                    |> List.tryPick (function
                        | BWeight(p, None, _, _) -> Some p
                        | _ -> None)

                match elseB, unguarded with
                | Some(p, _, _), Some u ->
                    err
                        p
                        Codes.ElseAfterUnguardedWeight
                        $"`else` can never fire: the branch at line %d{u.Line} has no guard, so it's always eligible"
                | _ -> Result.Ok(Production(h, ProdWeighted(weights, elseSucc), rulePos))

let private ruleBody (h: Head) (rulePos: Pos) : P<Rule> =
    fun s ->
        if isArrowAt s then
            (arrow >>. actualString .>> semicolon |>> fun succ -> Production(h, ProdSimple succ, rulePos)) s
        elif peekName s = "as" then
            (keyword "as" >>. actualString .>> semicolon
             |>> fun succ -> Interpretation(h, InterpSimple succ, rulePos))
                s
        elif s.Peek() = ':' then
            s.Skip()

            let r =
                (ws
                 >>. (many1 rawBranch <??> "`when` or `weight`")
                 .>>. opt elseBranch
                 .>> semicolon)
                    s

            if r.Status <> Ok then
                Reply(r.Status, r.Error)
            else
                let branches, elseB = r.Result

                match buildBranches h rulePos branches elseB with
                | Result.Ok rule -> Reply(rule)
                | Result.Error d -> Reply(FatalError, otherError (box d))
        else
            moduleStop "`->`, `as` or `:` after the rule's head" false s

let private rule: P<Rule> =
    getPos .>>. head >>= fun (p, h) -> ruleBody h p

// Prelude ---------------------------------------------------------------------

/// The keyword that starts a prelude statement. If what follows can only
/// continue a rule head, the keyword is being used as a module name.
let private preludeKeyword (k: string) : P<Pos> =
    fun s ->
        let p = toPos s.Position
        s.Skip k.Length
        let r = ws s

        if r.Status <> Ok then
            Reply(r.Status, r.Error)
        elif isArrowAt s || s.Peek() = '<' || s.Peek() = '>' || s.Peek() = '(' then
            fatalAt p Codes.ReservedName (keywordAsName k)
        else
            Reply(p)

/// The `:` that starts a module string (`axiom:`, `ignore:`).
let private colonAfter (k: string) : P<unit> =
    fun s ->
        if s.Peek() = ':' then
            s.Skip()
            ws s
        elif s.Peek() = '=' then
            fatal Codes.WrongSeparator "use `:` here: `:` introduces a module string, `=` an expression" s
        else
            fatal Codes.Syntax $"unexpected %s{describeHere s}; expected `:` after `%s{k}`" s

/// The `=` that starts an expression (`let`, settings entries).
let private equalsAfter (what: string) : P<unit> =
    fun s ->
        if s.Peek() = '=' then
            s.Skip()
            ws s
        elif s.Peek() = ':' then
            fatal Codes.WrongSeparator "use `=` here: `=` introduces an expression, `:` a module string" s
        else
            fatal Codes.Syntax $"unexpected %s{describeHere s}; expected `=` after %s{what}" s

/// The `;` after an expression. A name, `->`, `}` or the end of the file
/// can't continue an expression, so a `;` is probably missing.
let private exprSemicolon: P<unit> =
    fun s ->
        let name = peekName s

        if s.Peek() = ';' then
            s.Skip()
            ws s
        elif keywords.Contains name && not (statementAhead s) then
            fatal Codes.MisplacedKeyword (misplacedKeyword name "an expression") s
        elif s.IsEndOfStream || s.Peek() = '}' || isArrowAt s || nameLength s > 0 then
            fatal Codes.MissingSemicolon $"missing `;`? unexpected %s{describeHere s}" s
        else
            fatal Codes.Syntax $"unexpected %s{describeHere s}; expected `;`" s

let private letStatement: P<Item> =
    preludeKeyword "let"
    >>= fun p ->
        identOrWild Bind "a constant name"
        >>= fun (n, _) -> equalsAfter $"`%s{n}`" >>. expr .>> exprSemicolon |>> fun e -> Let(n, e, p)

/// One `key = expr;` entry. Fails without consuming input at anything but
/// a setting name.
let private settingEntry: P<SettingEntry> =
    fun s ->
        let p = toPos s.Position
        let name = peekName s

        match SettingKey.tryParse name with
        | Some key ->
            s.Skip name.Length
            (ws >>. equalsAfter $"`%s{name}`" >>. expr .>> exprSemicolon |>> fun e -> (key, e, p)) s
        | None -> Reply(Error, NoErrorMessages)

/// The `}` that ends a settings block, with a helpful error for whatever
/// stopped the entries instead.
let private closeBrace (blockPos: Pos) : P<unit> =
    fun s ->
        let p = toPos s.Position
        let name = peekName s

        if s.Peek() = '}' then
            s.Skip()
            ws s
        elif name = "seed" && nameThenSeparator s then
            fatalAt
                p
                Codes.UnknownSetting
                "`seed` isn't a setting: the random seed is a run parameter (`--seed` on the command line)"
        elif name <> "" && not (keywords.Contains name) && nameThenSeparator s then
            let known = SettingKey.all |> List.map (fun k -> $"`%s{SettingKey.name k}`") |> String.concat ", "
            fatalAt p Codes.UnknownSetting $"unknown setting `%s{name}`; the settings are %s{known}"
        elif s.Peek() = '#' then
            fatalAt p Codes.ReservedChar "`#` has no meaning; it's reserved"
        elif s.IsEndOfStream || name <> "" || isArrowAt s then
            fatalAt
                p
                Codes.MissingBrace
                $"missing `}}`? unexpected %s{describeHere s} in the `settings` block at line %d{blockPos.Line}"
        else
            fatalAt p Codes.Syntax $"unexpected %s{describeHere s}; expected a setting or `}}`"

let private settingsBlock: P<Item> =
    preludeKeyword "settings"
    >>= fun p -> punct '{' >>. many settingEntry .>> closeBrace p |>> fun entries -> Settings(entries, p)

let private ignoreName: P<string * Pos> =
    fun s ->
        if s.Peek() = '[' || s.Peek() = ']' then
            fatal Codes.BracketNotAllowed "brackets can't be ignored" s
        else
            moduleNameTok s

let private ignoreStatement: P<Item> =
    preludeKeyword "ignore"
    >>= fun p ->
        colonAfter "ignore" >>. many ignoreName .>> semicolon
        >>= fun names _ ->
            if names.IsEmpty then
                fatalAt p Codes.EmptyIgnore "`ignore:` needs at least one module; leave it out if nothing is ignored"
            else
                Reply(Ignore(names, p))

let private axiomStatement: P<Item> =
    preludeKeyword "axiom"
    >>= fun p -> colonAfter "axiom" >>. actualString .>> semicolon |>> fun a -> Axiom(a, p)

// File ------------------------------------------------------------------------

/// A prelude statement or a rule. Their order is checked later (A.3).
let private item: P<Item> =
    fun s ->
        match peekName s with
        | "settings" -> settingsBlock s
        | "let" -> letStatement s
        | "ignore" -> ignoreStatement s
        | "axiom" -> axiomStatement s
        | _ -> (rule |>> RuleItem) s

let private file: P<File> = skipTrivia >>. manyTill item eof

/// Finds our own diagnostic in an FParsec error, if there is one.
let rec private findDiagnostic (msgs: ErrorMessageList) =
    ErrorMessageList.ToSortedArray msgs
    |> Array.tryPick (fun m ->
        match m with
        | :? ErrorMessage.Other as o ->
            match o.Data with
            | :? Diagnostic as d -> Some d
            | _ -> None
        | :? ErrorMessage.NestedError as n -> findDiagnostic n.Messages
        | :? ErrorMessage.CompoundError as c -> findDiagnostic c.NestedErrorMessages
        | _ -> None)

let private describeAt (source: string) (index: int) =
    if index >= source.Length then
        "end of file"
    else
        let c = source[index]

        if c = '\n' || c = '\r' then
            "end of line"
        elif Unicode.nameStartLength source index > 0 then
            let mutable j = index + Unicode.nameStartLength source index

            while Unicode.nameCharLength source j > 0 do
                j <- j + Unicode.nameCharLength source j

            $"`%s{source.Substring(index, j - index)}`"
        else
            $"`%c{c}`"

let private toDiagnostic (source: string) (err: ParserError) =
    match findDiagnostic err.Messages with
    | Some d -> d
    | None ->
        let msgs = ErrorMessageList.ToSortedArray err.Messages

        let expectedLabels =
            msgs
            |> Array.choose (fun m ->
                match m with
                | :? ErrorMessage.Expected as e -> Some e.Label
                | :? ErrorMessage.ExpectedString as e -> Some $"`%s{e.String}`"
                | _ -> None)
            |> Array.distinct
            |> List.ofArray

        let expecting =
            match expectedLabels with
            | [] -> ""
            | [ one ] -> $"; expected %s{one}"
            | many ->
                let init = many |> List.take (many.Length - 1) |> String.concat ", "
                $"; expected %s{init} or %s{List.last many}"

        let here = describeAt source (int err.Position.Index)

        Diagnostic.error Codes.Syntax (Some(toPos err.Position)) $"unexpected %s{here}%s{expecting}"

/// Parses a RubberPlant source file. Stops at the first syntax error.
let parse (source: string) : Result<File, Diagnostic list> =
    let source =
        if source.StartsWith '\uFEFF' then
            source.Substring 1
        else
            source

    match runParserOnString file () "" source with
    | Success(result, _, _) -> Result.Ok result
    | Failure(_, err, _) -> Result.Error [ toDiagnostic source err ]

/// Parses a single expression (for tests and tools).
let parseExpr (source: string) : Result<Expr, Diagnostic list> =
    match runParserOnString (skipTrivia >>. expr .>> eof) () "" source with
    | Success(result, _, _) -> Result.Ok result
    | Failure(_, err, _) -> Result.Error [ toDiagnostic source err ]
