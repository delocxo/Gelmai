using System.Reflection;

public static partial class Natives
{
    public static Dictionary<string, (FunctionSymbol Symbol, MethodInfo Method)> Methods { get; } = new()
    {
    };

    public static void RegisterMethod(MethodInfo method, Position? position)
    {
        if (method.IsSpecialName || method.ContainsGenericParameters)
            return;

        if (Methods.ContainsKey(method.Name))
            throw new Error($"Dll function '{method.Name}' is a duplicate", position);

        var returnSymbol = Symbol.TypeToSymbol(method.ReturnType, method.Name);
        var parameters = method.GetParameters()
            .Select(p => Symbol.TypeToSymbol(p.ParameterType, method.Name))
            .ToList();

        var symbol = new FunctionSymbol(returnSymbol, parameters, method.Name);

        Methods.Add(method.Name, (symbol, method));
    }

    public static void RegisterAssembly(Assembly assembly, Position? position)
    {
        foreach (var type in assembly.GetExportedTypes())
        {
            if (type.IsDefined(typeof(IncludInGelmai), inherit: false) &&
                type.IsClass &&
                !type.ContainsGenericParameters &&
                !(type.IsAbstract && type.IsSealed) &&
                Symbol.TypeToSymbol(type) is ClassSymbol classSymbol)
            {
                if (!Symbol.Symbols.TryAdd(classSymbol.Name, classSymbol))
                    throw new Error($"Dll type '{classSymbol.Name}' is a duplicate", position);
            }

            RegisterStaticMethods(type, position);
        }
    }

    public static void RegisterStaticMethods(Type type, Position? position)
    {
        foreach (var method in type.GetMethods(
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            RegisterMethod(method, position);
        }
    }

    public static void RegisterSymbol(Type type)
    {
        if (Symbol.TypeToSymbol(type) is not ClassSymbol classSymbol)
            throw new Error($"Can only register classes");

        if (!Symbol.Symbols.TryAdd(type.Name, classSymbol))
            throw new Error($"Class '{classSymbol}' is an already existing type");
    }
}

public static class Core
{
    public static bool HasValue(object? obj)
    {
        return obj is not null;
    }

    public static string TypeName(object? obj)
    {
        if (obj is null)
            return "";
        return Symbol.GetTypeName(obj.GetType());
    }

    public static void Throw(object? obj)
    {
        if (obj is string str)
            throw new Exception(str);

        if (obj is Exception exception)
            throw exception;

        if (obj is null)
            throw new Error("Cannot throw 'null'");

        throw new InvalidOperationException($"Cannot thow '{Symbol.GetTypeName(obj.GetType())}'");
    }

    public static bool IsPanic(object? obj)
    {
        return obj is PanicException;
    }
}

class PanicException : Exception
{
    public object? Value;
}
