using System.Reflection;

public static partial class Natives
{
    public static Dictionary<string, (FunctionSymbol Symbol, MethodInfo Method)> Methods { get; } = new()
    {
        {
            "Print",
            (
                new(Symbol.Void, [Symbol.Object], "Print"),
                typeof(Console).GetMethod(
                    nameof(Console.Write),
                    [typeof(object)]
                )!
            )
        },
        {
            "PrintLine",
            (
                new(Symbol.Void, [Symbol.Object], "PrintLine"),
                typeof(Console).GetMethod(
                    nameof(Console.WriteLine),
                    [typeof(object)]
                )!
            )
        },
        {
            "StrLength",
            (
                new(Symbol.Int, [Symbol.String], "StrLength"),
                typeof(Natives).GetMethod(nameof(StrLength))!
            )
        },
        {
            "ArrLength",
            (
                new(Symbol.Int, [Symbol.AnyArraySymbol], "ArrLength"),
                typeof(Natives).GetMethod(nameof(ArrLength))!
            )
        },
        {
            "ReadLine",
            (
                new(Symbol.String, []),
                typeof(Natives).GetMethod(nameof(ReadLine))!
            )
        },
        {
            "ToString",
            (
                new (Symbol.String, [Symbol.Object]),
                typeof(Natives).GetMethod(nameof(ObjToString))!
            )
        }
    };

    public static long StrLength(string str)
    {
        return str.Length;
    }

    public static long ArrLength(Array array)
    {
        return array.Length;
    }

    public static string ReadLine()
    {
        return Console.ReadLine() ?? "";
    }

    public static string ObjToString(object? obj)
    {
        if (obj is null)
            return "";
        return obj.ToString() ?? "";
    }
}