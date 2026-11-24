using System.Reflection;
using System.Runtime.Loader;

public static class DllLoader
{
    public static HashSet<string> LoadedDlls = [];

    public static void LoadDll(string path, Position position)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new Error("Dll path cannot be empty", position);

        path = Path.GetFullPath(path);

        if (!Path.Exists(path + ".dll"))
            if (!Path.Exists(path))
                throw new Error($"Dll path: '{path}' does not exist", position);

        if (!LoadedDlls.Add(path))
            throw new Error($"Dll path: '{path}' has already been loaded", position);

        try
        {
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);

            foreach (var type in assembly.GetExportedTypes())
            {
                if (type.IsClass &&
                    !type.ContainsGenericParameters &&
                    !(type.IsAbstract && type.IsSealed) &&
                    Symbol.TypeToSymbol(type) is ClassSymbol classSymbol)
                {
                    if (!Symbol.Symbols.TryAdd(classSymbol.Name, classSymbol))
                        throw new Error($"Dll type '{classSymbol.Name}' is a duplicate", position);
                }

                foreach (var method in type.GetMethods(
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (method.IsSpecialName || method.ContainsGenericParameters)
                        continue;

                    if (Natives.Methods.ContainsKey(method.Name))
                        throw new Error($"Dll function '{method.Name}' is a duplicate", position);

                    var returnSymbol = Symbol.TypeToSymbol(method.ReturnType, method.Name);
                    var parameters = method.GetParameters()
                        .Select(p => Symbol.TypeToSymbol(p.ParameterType, method.Name))
                        .ToList();

                    var symbol = new FunctionSymbol(returnSymbol, parameters, method.Name);

                    Natives.Methods.Add(method.Name, (symbol, method));
                }
            }
        }
        catch (Exception ex)
        {
            throw new Error($"Failed to load dll: '{path}': {ex.Message}");
        }
    }
}