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

use "nlib/Stdio.dll";

class Profile
{
    string nickname;
    int level;
}

class Player
{
    string name;
    Profile profile;
    bool active;
}

Player player = new Player {
    name = "Mira",
    profile = new Profile {
        nickname = "Byte",
        level = 7
    },
    active = true
};

PrintLine(player.name);
PrintLine(player.profile.nickname);
PrintLine(player.profile.level);
PrintLine(player.active);

player.profile.level = player.profile.level + 1;
PrintLine(player.profile.level);

Profile empty = new Profile {};
PrintLine(empty.level);
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