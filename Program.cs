// using System.Runtime.InteropServices;
// using LLVMSharp.Interop;

// LLVM.InitializeNativeTarget();
// LLVM.InitializeNativeAsmPrinter();
// LLVM.InitializeNativeAsmParser();

// using var context = LLVMContextRef.Create();
// using var module = context.CreateModuleWithName("Gelmai");
// using var builder = context.CreateBuilder();

using System.Linq.Expressions;

try
{
    Natives.RegisterStaticMethods(typeof(Core), null);
    Natives.RegisterSymbol(typeof(Exception));
    Natives.RegisterSymbol(typeof(PanicException));

    var lexer = new Lexer(
"""

use "nlib/Stdio.dll";

PanicException

""", "main.gel");
    var tokens = lexer.Lex();

    var parser = new Parser(tokens);
    parser.ScanForKnownUserTypes();
    var ast = parser.Parse();

    var codegen = new CodeGenerator();

    var semantics = new Semantics();
    semantics.RegisterUserTypes(ast, codegen.ModuleBuilder);
    ast = semantics.Check(ast);

    codegen.GenerateMain(ast);

    var @delegate = codegen.GetDelegate();
    long exitCode = @delegate();

    return (int)exitCode;
}
catch (Error err)
{
    err.Print();
    Console.Error.WriteLine(err.StackTrace);
    return 1;
}

class MyClass
{
    public int hello;
    public readonly string yo = "";
    public const float hi = 0;
}