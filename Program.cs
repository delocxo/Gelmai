using System.Runtime.InteropServices;
using LLVMSharp.Interop;

LLVM.InitializeNativeTarget();
LLVM.InitializeNativeAsmPrinter();
LLVM.InitializeNativeAsmParser();

using var context = LLVMContextRef.Create();
using var module = context.CreateModuleWithName("Gelmai");
using var builder = context.CreateBuilder();
