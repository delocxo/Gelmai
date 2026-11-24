using System.Reflection;

public static partial class Natives
{
    public static Dictionary<string, (FunctionSymbol Symbol, MethodInfo Method)> Methods { get; } = new()
    {
    };
}