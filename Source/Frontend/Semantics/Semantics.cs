global using Resolved = (Expr Expr, Symbol Symbol);
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Intrinsics.Arm;
using System.Security.Cryptography.X509Certificates;

partial class Semantics
{
    public static Dictionary<string, (BoundFunctionStmt Bound, FunctionSymbol Symbol)> BoundFunctions = [];
    static Dictionary<string, FunctionStmt> functions = [];
    static Symbol currentReturnSymbol = Symbol.Int;

    int loopDepth = 0;

    public List<Stmt> Check(List<Stmt> ast)
    {
        List<Stmt> stmts = [];

        ast.ForEach(stmt =>
        {
            if (stmt is FunctionStmt functionStmt)
            {
                ThrowIfNotFreeName(functionStmt.Name, functionStmt);

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
                    ThrowIfNotFreeName(variableStmt.Name, variableStmt);
                    var symbol = ResolveSymbol(variableStmt.Symbol);
                    var resolved = CheckExpr(variableStmt.Expr);
                    Scopes.Declare(variableStmt.Name, symbol, resolved.Symbol, variableStmt);
                    return new BoundVariableStmt(variableStmt.Name, symbol, resolved, variableStmt.Position);
                }

            case BareStmt bareStmt:
                {
                    var resolved = CheckExpr(bareStmt.Expr);
                    return new BareStmt(resolved.Expr, bareStmt.Position, resolved.Symbol);
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

            case IfStmt ifStmt:
                {
                    List<IfBranch> newBranches = [];

                    ifStmt.IfBranches.ForEach(branch =>
                    {
                        var resolved = CheckExpr(branch.Expr);
                        resolved.Symbol.ThrowIfNotSymbol(Symbol.Bool, ifStmt);

                        List<Stmt> body = [];

                        var newBranch = new IfBranch(resolved.Expr, body);
                        newBranches.Add(newBranch);

                        Scopes.BeginScope();

                        body.AddRange(Check(branch.Stmts));

                        Scopes.Save(newBranch);

                        Scopes.EndScope();
                    });

                    List<Stmt>? elseBody = null;
                    if (ifStmt.ElseBody != null)
                    {
                        Scopes.BeginScope();

                        elseBody = Check(ifStmt.ElseBody);

                        Scopes.Save(elseBody);

                        Scopes.EndScope();
                    }

                    return new IfStmt(newBranches, elseBody, ifStmt.Position);
                }

            case WhileStmt whileStmt:
                {
                    var branch = whileStmt.Branch;

                    var resolved = CheckExpr(branch.Expr);
                    resolved.Symbol.ThrowIfNotSymbol(Symbol.Bool, whileStmt);

                    List<Stmt> body = [];
                    var newBranch = new IfBranch(resolved.Expr, body);

                    Scopes.BeginScope();

                    loopDepth++;

                    body.AddRange(Check(branch.Stmts));

                    loopDepth--;

                    Scopes.Save(newBranch);

                    Scopes.EndScope();

                    return new WhileStmt(newBranch, whileStmt.Position);
                }

            case BreakStmt:
                if (loopDepth < 1)
                    throw new Error($"Break cannot be used outside a loop", stmt);
                return stmt;

            case ContinueStmt:
                if (loopDepth < 1)
                    throw new Error($"Continue cannot be used outside a loop", stmt);
                return stmt;

            case ForStmt forStmt:
                {
                    Scopes.BeginScope();

                    var symbol = ResolveSymbol(forStmt.IteratorSymbol);

                    var local = Scopes.Declare(forStmt.Iterator, symbol, null, forStmt);

                    var resolved = CheckExpr(forStmt.Iterable);

                    if (resolved.Symbol is not ArraySymbol arraySymbol)
                        throw new Error($"Cannot use '{resolved.Symbol}' as an iterable", forStmt);

                    Scopes.Assign(local.Name, arraySymbol.Inner, forStmt);

                    loopDepth++;

                    var body = Check(forStmt.Body);

                    loopDepth--;

                    Scopes.Save(body);

                    Scopes.EndScope();

                    return new BoundForStmt(
                        forStmt.Iterator,
                        symbol,
                        resolved.Expr,
                        resolved.Symbol,
                        body,
                        forStmt.Position
                    );
                }

            case ClassStmt classStmt:
                if (!Scopes.AtGlobalLevel)
                    throw new Error($"Cannot declare class '{classStmt.Name}' at the local level", classStmt);
                return classStmt;

            case TryCatchStmt tryCatchStmt:
                {
                    List<Stmt> tryBody = [];

                    Scopes.BeginScope();

                    tryBody.AddRange(Check(tryCatchStmt.TryBody));

                    Scopes.Save(tryBody);

                    Scopes.EndScope();

                    List<Stmt> catchBody = [];

                    Scopes.BeginScope();

                    Local local = Scopes.Declare(tryCatchStmt.CatchName, Symbol.Object, null, tryCatchStmt);

                    catchBody.AddRange(Check(tryCatchStmt.CatchBody));

                    Scopes.Save(catchBody);

                    Scopes.EndScope();

                    return new TryCatchStmt(tryBody, catchBody, tryCatchStmt.CatchName, tryCatchStmt.Position);
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

                    if (Natives.Methods.TryGetValue(nameExpr.Name, out var native))
                        return (nameExpr, native.Symbol);

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

            case IsExpr isExpr:
                {
                    var resolved = CheckExpr(isExpr.Target);
                    var symbol = ResolveSymbol(isExpr.Symbol);
                    return (new BoundIsExpr(resolved, symbol), Symbol.Bool);
                }

            case IndexExpr indexExpr:
                {
                    var target = CheckExpr(indexExpr.Target);

                    var index = CheckExpr(indexExpr.Index);
                    index.Symbol.ThrowIfNotSymbol(Symbol.Int, indexExpr);

                    if (target.Symbol is ArraySymbol arraySymbol)
                    {
                        return (
                            new BoundIndexExpr(
                                target.Expr,
                                index.Expr,
                                arraySymbol
                            ), arraySymbol.Inner
                        );
                    }

                    if (target.Symbol.SymbolCheck(Symbol.String))
                    {
                        return (
                            new BoundIndexExpr(
                                target.Expr,
                                index.Expr,
                                Symbol.String
                            ),
                            Symbol.String
                        );
                    }

                    throw new Error($"Cannot index '{target.Symbol}'", indexExpr);
                }

            case IndexAssignExpr indexExpr:
                {
                    var target = CheckExpr(indexExpr.Target);

                    var index = CheckExpr(indexExpr.Index);
                    index.Symbol.ThrowIfNotSymbol(Symbol.Int, indexExpr);

                    var resolved = CheckExpr(indexExpr.Expr);

                    if (target.Symbol is not ArraySymbol arraySymbol)
                        throw new Error($"Cannot index '{target.Symbol}'", indexExpr);

                    arraySymbol.Inner.ThrowIfCantBeAssignedBy(resolved.Symbol, indexExpr);

                    return (
                        new BoundIndexAssignExpr(
                            target.Expr,
                            index.Expr,
                            resolved,
                            arraySymbol.Inner
                        ),
                        arraySymbol.Inner
                    );
                }

            case NullExpr nullExpr:
                return (nullExpr, Symbol.Null);

            case ConstructorExpr constructorExpr:
                {
                    var symbol = ResolveSymbol(constructorExpr.Target);

                    if (symbol is not ClassSymbol classSymbol)
                        throw new Error($"Cannot construct '{symbol}'", constructorExpr);

                    if (classSymbol.Constructor is null)
                        throw new Error($"'{classSymbol.Name}' has no public parameterless constructor", constructorExpr);

                    List<BoundMemberAssignExpr> braceInitializations = [];

                    var bound = new BoundConstructorExpr(classSymbol, constructorExpr.Position, braceInitializations);

                    braceInitializations.AddRange(constructorExpr.BraceInitializationValues
                        .Select(x =>
                        {
                            var memberExpr = new MemberAssignExpr(
                                constructorExpr with { BraceInitializationValues = [] },
                                x.Name,
                                x.Expr
                            );

                            var boundAssign = (BoundMemberAssignExpr)CheckExpr(memberExpr).Expr;

                            return boundAssign;
                        }));

                    return (bound, classSymbol);
                }

            case MemberExpr memberExpr:
                {
                    var target = CheckExpr(memberExpr.Target);

                    if (target.Symbol is not ClassSymbol classSymbol)
                        throw new Error($"'{target.Symbol}' cannot be member accessed", memberExpr);

                    if (!classSymbol.Fields.TryGetValue(memberExpr.MemberName, out var field))
                        throw new Error($"Class '{classSymbol}' does not contain field '{memberExpr.MemberName}'", memberExpr);

                    return (new BoundMemberExpr(target.Expr, field, memberExpr.Position), field.Symbol);
                }

            case MemberAssignExpr memberExpr:
                {
                    var target = CheckExpr(memberExpr.Target);

                    if (target.Symbol is not ClassSymbol classSymbol)
                        throw new Error($"'{target.Symbol}' cannot be member accessed", memberExpr);

                    if (!classSymbol.Fields.TryGetValue(memberExpr.MemberName, out var field))
                        throw new Error($"Class '{classSymbol}' does not contain field '{memberExpr.MemberName}'", memberExpr);

                    if (!memberExpr.BraceInitialization && (field.IsConst || field.FieldInfo.IsInitOnly))
                        throw new Error($"Field '{memberExpr.MemberName}' from '{classSymbol.Name}' cannot be modified", memberExpr);

                    var value = CheckExpr(memberExpr.Expr);

                    field.Symbol.ThrowIfCantBeAssignedBy(value.Symbol, memberExpr);

                    return (new BoundMemberAssignExpr(target.Expr, field, value), field.Symbol);
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

            case AstAnyArraySymbol:
                return Symbol.AnyArray;

            case AstNullSymbol astNullSymbol:
                return new NullSymbol(ResolveSymbol(astNullSymbol.InnerSymbol));
        }

        throw new Error($"Invalid Symbol: {astSymbol.GetType().Name}", astSymbol);
    }

    void ThrowIfNotFreeName(string name, IPosition position)
    {
        if (functions.ContainsKey(name) ||
            Natives.Methods.ContainsKey(name))
            throw new Error($"'{name}' is an already existent function", position);
    }

    public void RegisterUserTypes(List<Stmt> ast, ModuleBuilder moduleBuilder)
    {
        // First Pass
        ast.ForEach(stmt =>
        {
            if (stmt is ClassStmt classStmt)
            {
                if (Symbol.Symbols.ContainsKey(classStmt.Name))
                    throw new Error($"Class '{classStmt.Name}' is an already existent type", classStmt);

                var type = moduleBuilder.DefineType(classStmt.Name, TypeAttributes.Public | TypeAttributes.Class);

                var construtor = type.DefineConstructor(
                    MethodAttributes.Public,
                    CallingConventions.Standard,
                    []
                );

                var ctorIl = construtor.GetILGenerator();
                ctorIl.Emit(OpCodes.Ldarg_0);
                ctorIl.Emit(OpCodes.Call, typeof(object).GetConstructor([])!);
                ctorIl.Emit(OpCodes.Ret);

                var classSymbol = new ClassSymbol(classStmt.Name, [], type, construtor);

                Symbol.Symbols.Add(classStmt.Name, classSymbol);
            }
        });

        // Second Pass
        ast.ForEach(stmt =>
        {
            if (stmt is ClassStmt classStmt)
            {
                var classSymbol = (ClassSymbol)Symbol.Symbols[classStmt.Name];
                var typeBuilder = (TypeBuilder)classSymbol.Type;

                HashSet<string> existingFields = [];

                classStmt.Fields.ForEach(field =>
                {
                    if (!existingFields.Add(field.Name))
                        throw new Error($"Field '{field.Name}' is a duplicate from class '{classSymbol.Name}'", field.Symbol);

                    var symbol = ResolveSymbol(field.Symbol);

                    // Why not resuse my existing const check?
                    bool isConst = new Local(field.Name, symbol) { IsTopLevel = false }.IsConst;

                    var fieldBuilder = typeBuilder.DefineField(
                        field.Name,
                        symbol.Type,
                        FieldAttributes.Public
                    );

                    classSymbol.Fields[field.Name] = new(symbol, isConst, fieldBuilder);
                });
            }
        });
    }
}
