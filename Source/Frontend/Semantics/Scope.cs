class Local
{
    public Local(string name, Symbol symbol)
    {
        Name = name;
        Symbol = symbol;
        IsConst = name.Any(char.IsLetter) &&
            name.All(c => c == '_' || char.IsDigit(c) || char.IsUpper(c));
    }

    public string Name { get; }
    public Symbol Symbol { get; }
    public bool IsConst { get; }
    public required bool IsTopLevel { get; set; }
}

static class Scopes
{
    public static Stack<Dictionary<string, Local>> ScopesData { get; set; } = new([new()]);
    public static Dictionary<string, Local> Scope => ScopesData.Peek();
    public static Dictionary<object, Stack<Dictionary<string, Local>>> SavedScopes =
        new(ReferenceEqualityComparer.Instance);

    public static bool AtGlobalLevel => ScopesData.Count == 1 /* || FunctionLevel > 0*/;
    public static int FunctionLevel = 0;

    public static Local Declare(string name, Symbol symbol, Symbol? valueSymbol, IPosition position)
    {
        if (Scope.ContainsKey(name))
            throw new Error($"Local '{name}' has already been declared", position);

        symbol.ThrowIfNotValidStorage(position);

        if (valueSymbol != null)
        {
            symbol.ThrowIfCantBeAssignedBy(valueSymbol, position);
        }

        Local local = new Local(name, symbol)
        {
            IsTopLevel = AtGlobalLevel
        };

        Scope.Add(name, local);

        return local;
    }

    public static Local Assign(string name, Symbol valueSymbol, IPosition position)
    {
        Local local = Resolve(name, position);

        if (local.IsConst)
            throw new Error($"Cannot assign to const local '{name}'", position);

        local.Symbol.ThrowIfCantBeAssignedBy(valueSymbol, position);

        return local;
    }

    public static bool TryResolve(string name, out Local? local)
    {
        foreach (var scope in ScopesData)
            if (scope.TryGetValue(name, out local))
                return true;
        local = null;
        return false;
    }

    public static Local Resolve(string name, Position position)
    {
        if (!TryResolve(name, out var local))
            throw new Error($"Local '{name}' does not exist", position);
        return local!;
    }

    public static Local Resolve(string name, IPosition position)
    {
        return Resolve(name, position.Position);
    }

    public static void BeginScope() => ScopesData.Push(new());
    public static void EndScope() => ScopesData.Pop();

    public static void Save(object owner)
    {
        SavedScopes[owner] =
            new(ScopesData.Reverse());
    }

    public static void Restore(object owner)
    {
        ScopesData =
            new(SavedScopes[owner].Reverse());
    }
}