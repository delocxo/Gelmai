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

string[] StrToArray(string str)
{
    int length = StrLength(str);
    string[] array = new string[length];
    int i = 0;
    while (i < length)
    {
        array[i] = str[i];
        i = i + 1;
    }
    return array;
}

string[] array = StrToArray("Hello, World!");

for (string x : array) {
    PrintLine(x);
}

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