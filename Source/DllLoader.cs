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
            Natives.RegisterAssembly(assembly, position);
        }
        catch (Exception ex)
        {
            throw new Error($"Failed to load dll: '{path}': {ex.Message}");
        }
    }
}