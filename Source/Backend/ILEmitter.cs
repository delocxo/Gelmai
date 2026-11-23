using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using static System.Reflection.Emit.OpCodes;

class ILEmitter
{
    public Dictionary<Local, LocalBuilder> Locals { get; } = [];
    CodeGenerator codeGenerator { get; }
    ILGenerator il { get; }

    Stack<(Label End, Label Start)> loopStack = [];

    public ILEmitter(ILGenerator iLGenerator, CodeGenerator codeGenerator)
    {
        il = iLGenerator;
        this.codeGenerator = codeGenerator;
    }

    public void EmitStmts(List<Stmt> stmts)
    {
        stmts.ForEach(EmitStmt);
    }

    void EmitStmt(Stmt stmt)
    {
        switch (stmt)
        {
            case BoundVariableStmt variableStmt:
                {
                    Local local = Scopes.Resolve(variableStmt.Name, variableStmt);

                    EmitExpr(variableStmt.Resolved);

                    HandleImplicitConversion(variableStmt.Symbol, variableStmt.Resolved.Symbol);

                    if (local.IsTopLevel)
                    {
                        var global = codeGenerator.DefineGlobal(local);
                        il.Emit(Stsfld, global);
                        break;
                    }

                    var localBuilder = il.DeclareLocal(variableStmt.Symbol.Type);
                    Locals[local] = localBuilder;
                    il.Emit(Stloc, localBuilder);

                    break;
                }

            case BareStmt bareStmt:
                {
                    EmitExpr(bareStmt.Expr);
                    if (!bareStmt.Symbol.SymbolCheck(Symbol.Void))
                        il.Emit(Pop);
                    break;
                }

            case ReturnStmt returnStmt:
                {
                    if (returnStmt.Expr != null)
                    {
                        EmitExpr(returnStmt.Expr);
                        HandleImplicitConversion(returnStmt.ReturnSymbol, returnStmt.ExpSymbol);
                    }
                    il.Emit(Ret);
                    break;
                }

            case FunctionStmt functionStmt:
                {
                    if (!Semantics.BoundFunctions.ContainsKey(functionStmt.Name))
                        return;

                    var previous = Scopes.ScopesData;

                    Scopes.Restore(functionStmt);

                    var method = codeGenerator.Methods[functionStmt];

                    var bound = method.Bound;

                    var ilGenerator = method.Builder.GetILGenerator();
                    var emitter = new ILEmitter(ilGenerator, codeGenerator);

                    for (int i = 0; i < bound.Parameters.Count; i++)
                    {
                        var param = bound.Parameters[i];
                        ilGenerator.Emit(Ldarg, i);

                        var local = ilGenerator.DeclareLocal(param.Symbol.Type);
                        emitter.Locals[Scopes.Resolve(param.Name, Position.Protocal)] = local;
                        ilGenerator.Emit(Stloc, local);
                    }

                    emitter.EmitStmts(bound.Body);

                    Scopes.ScopesData = previous;

                    break;
                }

            case IfStmt ifStmt:
                {
                    Label endLabel = il.DefineLabel();

                    ifStmt.IfBranches.ForEach(branch =>
                    {
                        Label falseLabel = il.DefineLabel();

                        EmitExpr(branch.Expr);

                        il.Emit(Brfalse, falseLabel);

                        var previous = Scopes.ScopesData;

                        Scopes.Restore(branch);

                        EmitStmts(branch.Stmts);

                        il.Emit(Br, endLabel);

                        Scopes.ScopesData = previous;

                        il.MarkLabel(falseLabel);
                    });

                    if (ifStmt.ElseBody != null)
                    {
                        var previous = Scopes.ScopesData;

                        Scopes.Restore(ifStmt.ElseBody);

                        EmitStmts(ifStmt.ElseBody);

                        Scopes.ScopesData = previous;
                    }

                    il.MarkLabel(endLabel);

                    break;
                }

            case WhileStmt whileStmt:
                {
                    var branch = whileStmt.Branch;

                    Label start = il.DefineLabel();
                    Label end = il.DefineLabel();

                    il.MarkLabel(start);

                    EmitExpr(branch.Expr);

                    il.Emit(Brfalse, end);

                    var previous = Scopes.ScopesData;

                    Scopes.Restore(branch);

                    loopStack.Push((end, start));

                    EmitStmts(branch.Stmts);

                    loopStack.Pop();

                    Scopes.ScopesData = previous;

                    il.Emit(Br, start);

                    il.MarkLabel(end);

                    break;
                }

            case BreakStmt breakStmt:
                {
                    var endLabel = loopStack.Peek().End;
                    il.Emit(Br, endLabel);
                    break;
                }

            case ContinueStmt:
                {
                    var startLabel = loopStack.Peek().Start;
                    il.Emit(Br, startLabel);
                    break;
                }
        }
    }

    void EmitExpr(Expr expr)
    {
        switch (expr)
        {
            case IntExpr intExpr:
                {
                    il.Emit(Ldc_I8, intExpr.Value);
                    break;
                }

            case FloatExpr floatExpr:
                {
                    il.Emit(Ldc_R8, floatExpr.Value);
                    break;
                }

            case StringExpr stringExpr:
                {
                    il.Emit(Ldstr, stringExpr.Value);
                    break;
                }

            case BoolExpr boolExpr:
                {
                    il.Emit(boolExpr.Value ? Ldc_I4_1 : Ldc_I4_0);
                    break;
                }

            case NameExpr nameExpr:
                {
                    var method = codeGenerator.Methods
                        .FirstOrDefault(x => x.Key.Name == nameExpr.Name);

                    if (method.Key != null)
                    {
                        var tuple = method.Value;
                        LoadFunction(tuple.Builder, tuple.Symbol.Type);
                        break;
                    }

                    if (Natives.Methods.TryGetValue(nameExpr.Name, out var native))
                    {
                        LoadFunction(native.Method, native.Symbol.Type);
                        break;
                    }

                    Local local = Scopes.Resolve(nameExpr.Name, nameExpr);

                    if (local.IsTopLevel)
                    {
                        var global = codeGenerator.Globals[local];
                        il.Emit(Ldsfld, global);
                        break;
                    }

                    var localBuilder = Locals[local];
                    il.Emit(Ldloc, localBuilder);
                    break;
                }

            case UnaryExpr unaryExpr:
                {
                    EmitExpr(unaryExpr.Right);

                    if (unaryExpr.Op is TokenType.Sub)
                    {
                        il.Emit(Neg);
                        break;
                    }

                    il.Emit(Ldc_I4_0);
                    il.Emit(Ceq);

                    break;
                }

            case BoundBinaryExpr boundBinaryExpr:
                {
                    EmitBinary(boundBinaryExpr);
                    break;
                }

            case BoundCastExpr boundCastExpr:
                {
                    var source = boundCastExpr.SourceSymbol;
                    var target = boundCastExpr.TargetSymbol;

                    EmitExpr(boundCastExpr.Expr);

                    if (source.SymbolCheck(target))
                        break;

                    if (source.SymbolCheck(Symbol.Int) &&
                        target.SymbolCheck(Symbol.Float))
                    {
                        il.Emit(Conv_R8);
                        break;
                    }

                    if (source.SymbolCheck(Symbol.Float) &&
                        target.SymbolCheck(Symbol.Int))
                    {
                        il.Emit(Conv_I8);
                        break;
                    }

                    if (target.SymbolCheck(Symbol.Object))
                    {
                        if (source.IsValueType)
                        {
                            il.Emit(Box, source.Type);
                            break;
                        }

                        break;
                    }

                    if (source.SymbolCheck(Symbol.Object))
                    {
                        PositionTracking.EmitPosition(il, boundCastExpr);
                        if (target.IsValueType)
                            il.Emit(Unbox_Any, target.Type);
                        else
                            il.Emit(Castclass, target.Type);
                    }

                    break;
                }

            case BoundArrayExpr boundArrayExpr:
                {
                    var symbol = boundArrayExpr.Symbol;

                    if (boundArrayExpr.Size != null)
                    {
                        EmitExpr(boundArrayExpr.Size);
                        il.Emit(Conv_I);
                    }
                    else
                        il.Emit(Ldc_I4, boundArrayExpr.Items.Count);

                    PositionTracking.EmitPosition(il, boundArrayExpr);
                    il.Emit(Newarr, symbol.Inner.Type);

                    for (int i = 0; i < boundArrayExpr.Items.Count; i++)
                    {
                        il.Emit(Dup);

                        var item = boundArrayExpr.Items[i];

                        il.Emit(Ldc_I4, i);

                        EmitExpr(item);

                        HandleImplicitConversion(symbol.Inner, item.Symbol);

                        PositionTracking.EmitPosition(il, item.Expr);
                        il.Emit(Stelem, symbol.Inner.Type);
                    }

                    break;
                }

            case AssignExpr assignExpr:
                {
                    Local local = Scopes.Resolve(assignExpr.Name, assignExpr);

                    EmitExpr(assignExpr.Expr);

                    HandleImplicitConversion(local.Symbol, assignExpr.ExprSymbol);

                    il.Emit(Dup);

                    if (local.IsTopLevel)
                    {
                        il.Emit(Stsfld, codeGenerator.Globals[local]);
                        break;
                    }

                    il.Emit(Stloc, Locals[local]);
                    break;
                }

            case BoundCallExpr boundCallExpr:
                {
                    EmitExpr(boundCallExpr.Target);

                    foreach (var arg in boundCallExpr.Args)
                    {
                        var (resolvedArg, param) = arg;

                        EmitExpr(resolvedArg.Expr);

                        HandleImplicitConversion(param, resolvedArg.Symbol);
                    }

                    PositionTracking.EmitPosition(il, boundCallExpr);

                    il.Emit(Callvirt, boundCallExpr.Type.GetMethod("Invoke")!);

                    break;
                }

            case BoundIsExpr boundIsExpr:
                {
                    var resolved = boundIsExpr.Resolved;

                    EmitExpr(resolved);

                    if (resolved.Symbol.IsValueType)
                        il.Emit(Box, resolved.Symbol.Type);

                    il.Emit(Isinst, boundIsExpr.Symbol.Type);
                    il.Emit(Ldnull);
                    il.Emit(Cgt_Un);

                    break;
                }

            case BoundIndexExpr boundIndexExpr:
                {
                    if (boundIndexExpr.Symbol is ArraySymbol arraySymbol)
                    {
                        EmitExpr(boundIndexExpr.Target);

                        EmitExpr(boundIndexExpr.Index);

                        il.Emit(Conv_I);

                        PositionTracking.EmitPosition(il, boundIndexExpr);

                        il.Emit(Ldelem, arraySymbol.Inner.Type);

                        break;
                    }

                    EmitExpr(boundIndexExpr.Target);

                    EmitExpr(boundIndexExpr.Index);

                    il.Emit(Conv_I4);

                    il.Emit(Ldc_I4_1);

                    PositionTracking.EmitPosition(il, boundIndexExpr);

                    il.Emit(Callvirt, typeof(string).GetMethod(
                        nameof(string.Substring),
                        [typeof(int), typeof(int)]
                    )!);

                    break;
                }

            case BoundIndexAssignExpr boundIndexExpr:
                {
                    EmitExpr(boundIndexExpr.Target);

                    EmitExpr(boundIndexExpr.Index);

                    il.Emit(Conv_I);

                    EmitExpr(boundIndexExpr.Expr);

                    HandleImplicitConversion(boundIndexExpr.Inner, boundIndexExpr.Expr.Symbol);

                    var local = il.DeclareLocal(boundIndexExpr.Inner.Type);
                    il.Emit(Stloc, local);

                    il.Emit(Ldloc, local);

                    PositionTracking.EmitPosition(il, boundIndexExpr);

                    il.Emit(Stelem, boundIndexExpr.Inner.Type);

                    il.Emit(Ldloc, local);

                    break;
                }
        }
    }

    void EmitExpr(Resolved resolved)
    {
        EmitExpr(resolved.Expr);
    }

    void EmitEquality(TokenType tokenType)
    {
        if (tokenType is TokenType.IsEqual)
            EmitEqual();
        else
            EmitNotEqual();
    }

    void EmitEqual()
    {
        il.Emit(Ceq);
    }

    void EmitNotEqual()
    {
        il.Emit(Ceq);
        il.Emit(Ldc_I4_0);
        il.Emit(Ceq);
    }

    void EmitFlip()
    {
        il.Emit(Ldc_I4_0);
        il.Emit(Ceq);
    }

    void EmitBinary(BoundBinaryExpr boundBinaryExpr)
    {
        var op = boundBinaryExpr.Op;
        var left = boundBinaryExpr.Left;
        var right = boundBinaryExpr.Right;

        if (op is TokenType.And)
        {
            Label falseLabel = il.DefineLabel();
            Label endLabel = il.DefineLabel();

            EmitExpr(left);
            il.Emit(Brfalse, falseLabel);

            EmitExpr(right);
            il.Emit(Br, endLabel);

            il.MarkLabel(falseLabel);
            il.Emit(Ldc_I4_0);

            il.MarkLabel(endLabel);
        }
        else if (op is TokenType.Or)
        {
            Label trueLabel = il.DefineLabel();
            Label endLabel = il.DefineLabel();

            EmitExpr(left);
            il.Emit(Brtrue, trueLabel);

            EmitExpr(right);
            il.Emit(Br, endLabel);

            il.MarkLabel(trueLabel);
            il.Emit(Ldc_I4_1);

            il.MarkLabel(endLabel);
        }
        else
        {
            if (op is TokenType.IsEqual or TokenType.NotEqual &&
                !left.Symbol.IsNumber() &&
                !right.Symbol.IsNumber())
            {
                EmitExpr(left);
                EmitExpr(right);

                if (left.Symbol.SymbolCheck(Symbol.String) &&
                    right.Symbol.SymbolCheck(Symbol.String))
                {
                    var stringEquals = typeof(string).GetMethod(
                        "Equals",
                        [typeof(string), typeof(string)]
                    )!;

                    il.Emit(Call, stringEquals);

                    if (op is TokenType.NotEqual)
                        EmitFlip();
                }

                else if (left.Symbol is FunctionSymbol &&
                    right.Symbol is FunctionSymbol)
                {
                    var method = typeof(Delegate).GetMethod(
                        op is TokenType.IsEqual
                            ? "op_Equality"
                            : "op_Inequality"
                    )!;

                    il.Emit(Call, method);
                }

                else
                {
                    EmitEquality(op);
                }

                return;
            }

            if (op is TokenType.Add &&
                left.Symbol.SymbolCheck(Symbol.String) &&
                right.Symbol.SymbolCheck(Symbol.String))
            {
                EmitExpr(left);
                EmitExpr(right);

                il.Emit(Call, typeof(string).GetMethod(
                    "Concat",
                    [typeof(string), typeof(string)]
                )!);

                return;
            }

            EmitExpr(left);

            bool isFloat =
                left.Symbol.SymbolCheck(Symbol.Float) &&
                right.Symbol.SymbolCheck(Symbol.Float);

            if (left.Symbol.SymbolCheck(Symbol.Int) &&
                right.Symbol.SymbolCheck(Symbol.Float))
            {
                il.Emit(Conv_R8);
                isFloat = true;
            }

            EmitExpr(right);

            if (left.Symbol.SymbolCheck(Symbol.Float) &&
                right.Symbol.SymbolCheck(Symbol.Int))
            {
                il.Emit(Conv_R8);
                isFloat = true;
            }

            if (op is TokenType.IsEqual or TokenType.NotEqual)
            {
                EmitEquality(op);
                return;
            }

            if (op is TokenType.LessEq)
            {
                il.Emit(isFloat ? Cgt_Un : Cgt);
                EmitFlip();
                return;
            }

            if (op is TokenType.GreaterEq)
            {
                il.Emit(isFloat ? Clt_Un : Clt);
                EmitFlip();
                return;
            }

            var opcode = op switch
            {
                TokenType.Less => Clt,
                TokenType.Greater => Cgt,
                TokenType.Add => Add,
                TokenType.Sub => Sub,
                TokenType.Mul => Mul,
                TokenType.Div => Div,
                TokenType.Mod => Rem,
                _ => throw new UnreachableException()
            };

            if (!isFloat && (op is TokenType.Div or TokenType.Mod))
                PositionTracking.EmitPosition(il, boundBinaryExpr);

            il.Emit(opcode);
        }
    }

    void HandleImplicitConversion(Symbol left, Symbol right)
    {
        if (left.SymbolCheck(Symbol.Float) &&
            right.SymbolCheck(Symbol.Int))
        {
            il.Emit(Conv_R8);
            return;
        }

        if (left.SymbolCheck(Symbol.Object) &&
            right.IsValueType)
        {
            il.Emit(Box, right.Type);
        }
    }

    void LoadFunction(MethodBuilder methodBuilder, Type type)
    {
        il.Emit(Ldnull);
        il.Emit(Ldftn, methodBuilder);
        il.Emit(Newobj, type.GetConstructor(
            [typeof(object), typeof(IntPtr)]
        )!);
    }

    void LoadFunction(MethodInfo methodInfo, Type type)
    {
        il.Emit(Ldnull);
        il.Emit(Ldftn, methodInfo);
        il.Emit(Newobj, type.GetConstructor(
            [typeof(object), typeof(IntPtr)]
        )!);
    }
}
