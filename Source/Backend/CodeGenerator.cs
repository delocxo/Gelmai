using System.Diagnostics.SymbolStore;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;

class CodeGenerator
{
    public PersistedAssemblyBuilder Assembly { get; }
    public ModuleBuilder ModuleBuilder { get; }
    public TypeBuilder TypeBuilder { get; }
    public Dictionary<string, ISymbolDocumentWriter> Documents { get; } = [];
    public Dictionary<Local, FieldBuilder> Globals { get; } = [];
    public Dictionary<FunctionStmt, (BoundFunctionStmt Bound, MethodBuilder Builder, FunctionSymbol Symbol)> Methods { get; } = [];

    public CodeGenerator()
    {
        Assembly = new PersistedAssemblyBuilder(
            new("GelmaiGenerated"),
            typeof(object).Assembly
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
        TypeBuilder.CreateType();

        foreach (var symbol in Symbol.Symbols)
            if (symbol.Value is ClassSymbol classSymbol)
                if (classSymbol.Type is TypeBuilder typeBuilder)
                    typeBuilder.CreateType();

        var metadata = Assembly.GenerateMetadata(
            out BlobBuilder ilStream,
            out BlobBuilder fieldData,
            out MetadataBuilder pbMetadata
        );

        var pdbBlob = new BlobBuilder();

        var pdbbBuilder = new PortablePdbBuilder(
            pbMetadata,
            metadata.GetRowCounts(),
            default
        );

        var pdbId = pdbbBuilder.Serialize(pdbBlob);

        var debugDirectory = new DebugDirectoryBuilder();

        debugDirectory.AddCodeViewEntry(
            "Gelmai.pdb",
            pdbId,
            pdbbBuilder.FormatVersion
        );

        var peBuilder = new ManagedPEBuilder(
            header: PEHeaderBuilder.CreateLibraryHeader(),
            metadataRootBuilder: new MetadataRootBuilder(metadata),
            ilStream: ilStream,
            mappedFieldData: fieldData,
            debugDirectoryBuilder: debugDirectory
        );

        var peBlob = new BlobBuilder();

        peBuilder.Serialize(peBlob);

        using var asmStream = new MemoryStream();
        using var pdbStream = new MemoryStream();

        peBlob.WriteContentTo(asmStream);
        pdbBlob.WriteContentTo(pdbStream);

        asmStream.Position = 0;
        pdbStream.Position = 0;

        var assembly = AssemblyLoadContext.Default.LoadFromStream(
            asmStream,
            pdbStream
        );

        var entry = assembly
            .GetType("GelmaiProgram")!
            .GetMethod("Main")!
            .CreateDelegate<Func<long>>();

        return entry;
    }
}