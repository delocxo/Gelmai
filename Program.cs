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
    var lexer = new Lexer(
"""

int[] values = new int[5] { };
values[0] = 100;

""", "main.gel");
    var tokens = lexer.Lex();

    var parser = new Parser(tokens);
    var ast = parser.Parse();

    var semantics = new Semantics();
    ast = semantics.Check(ast);

    var codegen = new CodeGenerator();
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
catch (Exception ex)
{
    var position = PositionTracking.Current.Value!;

    if (PositionTracking.Debug)
        Console.Error.WriteLine($"Error: {position}: {ex.Message}");
    else
        Console.Error.WriteLine($"Error: {ex.Message}");

    Console.Error.WriteLine(ex.StackTrace);

    return 1;
}