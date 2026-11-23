global using Resolved = (Expr Expr, Symbol Symbol);
using System.Runtime.Intrinsics.Arm;

partial class Semantics
{
    public static Dictionary<string, (BoundFunctionStmt Bound, FunctionSymbol Symbol)> BoundFunctions = [];
    static Dictionary<string, FunctionStmt> functions = [];
    static Symbol currentReturnSymbol = Symbol.Int;

    public List<Stmt> Check(List<Stmt> ast)
    {
        List<Stmt> stmts = [];

        ast.ForEach(stmt =>
        {
            if (stmt is FunctionStmt functionStmt)
            {
                if (!functions.TryAdd(functionStmt.Name, functionStmt))
                    throw new Error($"Function '{functionStmt.Name}' is an already existing function", functionStmt.Position);

                HashSet<string> parameters = [];

                functionStmt.Parameters.ForEach(x =>
                {
                    if (!parameters.Add(x.Name))
                        throw new Error($"'{x.Name}' is a duplicate parameter from function '{functionStmt.Name}'", x.Symbol);
                });
            }
        });

        ast.ForEach(stmt => stmts.Add(CheckStmt(stmt)));

        return stmts;
    }

    Stmt CheckStmt(Stmt stmt)
    {
        switch (stmt)
        {
            case VariableStmt variableStmt:
                {
                    var symbol = ResolveSymbol(variableStmt.Symbol);
                    var resolved = CheckExpr(variableStmt.Expr);
                    Scopes.Declare(variableStmt.Name, symbol, resolved.Symbol, variableStmt);
                    return new BoundVariableStmt(variableStmt.Name, symbol, resolved, variableStmt.Position);
                }

            case BareStmt bareStmt:
                {
                    var resolved = CheckExpr(bareStmt.Expr).Expr;
                    return new BareStmt(resolved, bareStmt.Position);
                }

            case FunctionStmt functionStmt:
                if (!Scopes.AtGlobalLevel)
                    throw new Error($"Cannot declare function '{functionStmt.Name}' at the local level", functionStmt.Position);
                return functionStmt;

            case ReturnStmt returnStmt:
                {
                    if (returnStmt.Expr is null)
                        return returnStmt;

                    var resolved = CheckExpr(returnStmt.Expr);
                    currentReturnSymbol.ThrowIfCantBeAssignedBy(resolved.Symbol, returnStmt);

                    return returnStmt with
                    {
                        Expr = resolved.Expr,
                        ReturnSymbol = currentReturnSymbol,
                        ExpSymbol = resolved.Symbol,
                    };
                }
        }

        throw new Error($"Invalid Statement: {stmt.GetType().Name}", stmt);
    }

    Resolved CheckExpr(Expr expr)
    {
        switch (expr)
        {
            case IntExpr intExpr:
                return (intExpr, Symbol.Int);

            case FloatExpr floatExpr:
                return (floatExpr, Symbol.Float);

            case StringExpr stringExpr:
                return (stringExpr, Symbol.String);

            case BoolExpr boolExpr:
                return (boolExpr, Symbol.Bool);

            case NameExpr nameExpr:
                {
                    if (functions.TryGetValue(nameExpr.Name, out var function))
                    {
                        var boundFunction = BoundFunction(function, nameExpr.Position);
                        return (nameExpr, boundFunction.Symbol);
                    }

                    return (
                        nameExpr,
                        Scopes.Resolve(nameExpr.Name, nameExpr).Symbol);
                }

            case UnaryExpr unaryExpr:
                {
                    var resolved = CheckExpr(unaryExpr.Right);

                    var newExpr = unaryExpr with { Right = resolved.Expr };

                    if (unaryExpr.Op == TokenType.Bang &&
                        resolved.Symbol.SymbolCheck(Symbol.Bool))
                        return (newExpr, Symbol.Bool);

                    if (unaryExpr.Op == TokenType.Sub &&
                        resolved.Symbol.IsNumber())
                        return (newExpr, resolved.Symbol);

                    throw new Error($"Cannot apply '{unaryExpr.Op}' to '{resolved.Symbol}", unaryExpr);
                }

            case BinaryExpr binaryExpr:
                {
                    var left = CheckExpr(binaryExpr.Left);
                    var right = CheckExpr(binaryExpr.Right);

                    var newExpr = new BoundBinaryExpr(
                        left,
                        right,
                        binaryExpr.Op,
                        binaryExpr.Position
                    );

                    if (binaryExpr.Op is TokenType.And or TokenType.Or)
                    {
                        left.Symbol.ThrowIfNotSymbol(Symbol.Bool, binaryExpr);
                        right.Symbol.ThrowIfNotSymbol(Symbol.Bool, binaryExpr);
                        return (newExpr, Symbol.Bool);
                    }

                    if (binaryExpr.Op is TokenType.Less
                        or TokenType.Greater
                        or TokenType.LessEq
                        or TokenType.GreaterEq)
                    {
                        left.Symbol.ThrowIfNotNumber(binaryExpr);
                        right.Symbol.ThrowIfNotNumber(binaryExpr);
                        return (newExpr, Symbol.Bool);
                    }

                    if (binaryExpr.Op is TokenType.IsEqual or TokenType.NotEqual)
                    {
                        if (left.Symbol.IsNumber() && right.Symbol.IsNumber())
                            return (newExpr, Symbol.Bool);
                        left.Symbol.ThrowIfNotSymbol(right.Symbol, binaryExpr);
                        return (newExpr, Symbol.Bool);
                    }

                    if (binaryExpr.Op is TokenType.Add &&
                        left.Symbol.SymbolCheck(Symbol.String) &&
                        right.Symbol.SymbolCheck(Symbol.String))
                        return (newExpr, Symbol.String);

                    if (left.Symbol.IsNumber() && right.Symbol.IsNumber())
                    {
                        if (left.Symbol.SymbolCheck(Symbol.Float) ||
                            right.Symbol.SymbolCheck(Symbol.Float))
                            return (newExpr, Symbol.Float);
                        return (newExpr, Symbol.Int);
                    }

                    throw new Error($"Cannot apply '{binaryExpr.Op}' to '{left.Symbol}' and '{right.Symbol}'", binaryExpr);
                }

            case AssignExpr assignExpr:
                {
                    var resolved = CheckExpr(assignExpr.Expr);
                    var newExpr = assignExpr with
                    {
                        Expr = resolved.Expr,
                        ExprSymbol = resolved.Symbol
                    };
                    Local local = Scopes.Assign(assignExpr.Name, resolved.Symbol, assignExpr);
                    return (newExpr, local.Symbol);
                }

            case CastExpr castExpr:
                {
                    var symbol = ResolveSymbol(castExpr.Symbol);
                    var resolved = CheckExpr(castExpr.Target);
                    resolved.Symbol.ThrowIfCantCastFrom(symbol, castExpr);
                    return (new BoundCastExpr(symbol, resolved.Expr, resolved.Symbol, castExpr.Position), symbol);
                }

            case ArrayExpr arrayExpr:
                {
                    var symbol = ResolveSymbol(arrayExpr.Symbol);

                    if (symbol is not ArraySymbol arraySymbol)
                        throw new Error($"'{symbol}' cannot be constructed", arrayExpr.Symbol.Position);

                    Resolved? size = null;
                    if (arrayExpr.Size != null)
                        size = CheckExpr(arrayExpr.Size);

                    if (size != null && !size!.Value.Symbol.SymbolCheck(Symbol.Int))
                        throw new Error($"'{symbol!}' cannot be used as an array capacity", arrayExpr);

                    List<Resolved> resolvedExprs = [];

                    arrayExpr.Items.ForEach(item =>
                    {
                        var resolved = CheckExpr(item);
                        arraySymbol.Inner.ThrowIfCantBeAssignedBy(resolved.Symbol, item);
                        resolvedExprs.Add(resolved);
                    });

                    return (
                        new BoundArrayExpr(
                            arraySymbol,
                            size != null ? size!.Value.Expr : null,
                            resolvedExprs,
                            arrayExpr.Position),
                        arraySymbol
                    );
                }

            case CallExpr callExpr:
                {
                    var target = CheckExpr(callExpr.Target);

                    var args = callExpr.Args
                        .Select(CheckExpr)
                        .ToList();

                    if (target.Symbol is not FunctionSymbol functionSymbol)
                        throw new Error($"'{target.Symbol}' is not callable", callExpr);

                    string name = callExpr.Target is NameExpr n
                        ? n.Name
                        : functionSymbol.ToString();

                    if (args.Count != functionSymbol.ParameterCount)
                        throw new Error($"Function '{name}' expects {functionSymbol.ParameterCount} argument(s), got {args.Count}", callExpr);

                    var finalArgs = args
                        .Zip(functionSymbol.ParameterSymbols)
                        .ToList();

                    finalArgs.ForEach(
                        x => x.Second.ThrowIfCantBeAssignedBy(x.First.Symbol, x.First.Expr)
                    );

                    var newExpr = new BoundCallExpr(

                        target.Expr,
                        finalArgs,
                        functionSymbol.Type
                    );

                    return (newExpr, functionSymbol.ReturnSymbol);
                }
        }

        throw new Error($"Invalid Expression: {expr.GetType().Name}", expr);
    }

    Symbol ResolveSymbol(AstSymbol astSymbol)
    {
        switch (astSymbol)
        {
            case AstNameSymbol nameSymbol:
                {
                    if (!Symbol.Symbols.TryGetValue(nameSymbol.Name, out var symbol))
                        throw new Error($"'{nameSymbol.Name}' is not a valid type", nameSymbol);

                    return symbol;
                }

            case AstArraySymbol astArraySymbol:
                return new ArraySymbol(ResolveSymbol(astArraySymbol.InnerSymbol));

            case AstFunctionSymbol astFunctionSymbol:
                {
                    var returnSymbol = ResolveSymbol(astFunctionSymbol.ReturnSymbol);

                    var parameterSymbols = astFunctionSymbol.ParameterSymbols
                        .Select(ps =>
                        {
                            var symbol = ResolveSymbol(ps);
                            symbol.ThrowIfNotValidStorage(ps);
                            return symbol;
                        }).ToList();

                    return new FunctionSymbol(returnSymbol, parameterSymbols);
                }

            case AstAnyFunctionSymbol:
                return Symbol.AnyFunction;
        }

        throw new Error($"Invalid Symbol: {astSymbol.GetType().Name}", astSymbol);
    }
}
