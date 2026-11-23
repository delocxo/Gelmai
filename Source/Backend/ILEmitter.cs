using System.Diagnostics;
using System.Reflection.Emit;
using static System.Reflection.Emit.OpCodes;

class ILEmitter
{
    public Dictionary<Local, LocalBuilder> Locals { get; } = [];
    CodeGenerator codeGenerator { get; }
    ILGenerator il { get; }

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

                        il.Emit(Ldnull);

                        il.Emit(Ldftn, tuple.Builder);

                        il.Emit(Newobj, tuple.Symbol.Type.GetConstructor(
                            [typeof(object), typeof(IntPtr)]
                        )!);

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
}
