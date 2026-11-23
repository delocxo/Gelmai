using System.Reflection;
using System.Reflection.Emit;

class CodeGenerator
{
    public AssemblyBuilder Assembly { get; }
    public ModuleBuilder ModuleBuilder { get; }
    public TypeBuilder TypeBuilder { get; }
    public Dictionary<Local, FieldBuilder> Globals { get; } = [];
    public Dictionary<FunctionStmt, (BoundFunctionStmt Bound, MethodBuilder Builder, FunctionSymbol Symbol)> Methods { get; } = [];

    public CodeGenerator()
    {
        Assembly = AssemblyBuilder.DefineDynamicAssembly(
            new("Gelmai"),
            AssemblyBuilderAccess.Run
        );

        ModuleBuilder = Assembly.DefineDynamicModule("Gelmai");

        TypeBuilder = ModuleBuilder.DefineType(
            "GelmaiProgram",
            TypeAttributes.Public
        );
    }

    public void GenerateMain(List<Stmt> ast)
    {
        foreach (var stmt in ast)
        {
            if (stmt is FunctionStmt functionStmt)
            {
                if (!Semantics.BoundFunctions.TryGetValue(functionStmt.Name, out var bound))
                    continue;

                var symbol = bound.Symbol;

                var method = TypeBuilder.DefineMethod(
                    functionStmt.Name,
                    MethodAttributes.Public | MethodAttributes.Static,
                    symbol.ReturnSymbol.Type,
                    symbol.ParameterSymbols
                        .Select(x => x.Type)
                        .ToArray()
                );

                Methods[functionStmt] = (bound.Bound, method, symbol);
            }
        }

        var main = TypeBuilder.DefineMethod(
            "Main",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(long),
            []
        );

        var il = main.GetILGenerator();

        var emitter = new ILEmitter(il, this);
        emitter.EmitStmts(ast);

        il.Emit(OpCodes.Ldc_I8, 0L);
        il.Emit(OpCodes.Ret);
    }

    public FieldBuilder DefineGlobal(Local local)
    {
        var global = TypeBuilder.DefineField(
            local.Name,
            local.Symbol.Type,
            FieldAttributes.Public | FieldAttributes.Static
        );

        Globals[local] = global;
        return global;
    }

    public Func<long> GetDelegate()
    {
        Type program = TypeBuilder.CreateType()!;

        var entry = program
            .GetMethod("Main")!
            .CreateDelegate<Func<long>>();

        return entry;
    }
}