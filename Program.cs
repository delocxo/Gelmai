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

use "Dlls/Stdio.dll";

PrintF("{0}", new object[] {"Hello, World!"});

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