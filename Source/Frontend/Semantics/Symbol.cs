using System.Linq.Expressions;
public record Symbol(string Name, Type Type, bool IsValueType)
{
    public static readonly IntSymbol Int = new();
    public static readonly FloatSymbol Float = new();
    public static readonly StringSymbol String = new();
    public static readonly BoolSymbol Bool = new();
    public static readonly VoidSymbol Void = new();
    public static readonly ObjectSymbol Object = new();
    public static readonly AnyFunctionSymbol AnyFunction = new();
    public static readonly Symbol Empty = new("empty", typeof(byte), false);
    public static readonly AnyArraySymbol AnyArraySymbol = new();

    public static readonly Dictionary<string, Symbol> Symbols = new()
    {
        { "int", Int },
        { "float", Float },
        { "string", String },
        { "bool", Bool },
        { "void", Void },
        { "object", Object }
    };

    public bool IsNonStorage => SymbolCheck(Void) || this is AnyFunctionSymbol || this is AnyArraySymbol;
    public bool IsArray => this is ArraySymbol || this is AnyArraySymbol;
    public bool IsFunction => this is FunctionSymbol || this is AnyFunctionSymbol;

    public bool SymbolCheck(Symbol other)
    {
        if (this is ArraySymbol asl && other is ArraySymbol asr)
            return asl.Inner.SymbolCheck(asr.Inner);

        if (this is FunctionSymbol fsl && other is FunctionSymbol fsr)
            return fsl.ParameterSymbols.Count == fsr.ParameterSymbols.Count &&
                fsl.ParameterSymbols
                    .Zip(fsr.ParameterSymbols)
                    .All(x => x.First.SymbolCheck(x.Second)) &&
                fsl.ReturnSymbol.SymbolCheck(fsr.ReturnSymbol);

        if (CrossCheck<FunctionSymbol, AnyFunctionSymbol>(other))
            return true;

        if (CrossCheck<ArraySymbol, AnyArraySymbol>(other))
            return true;

        return this == other;
    }

    public void ThrowIfNotValidStorage(IPosition position)
    {
        if (IsNonStorage)
            throw new Error($"'{this}' cannot be used as a storage type", position);
    }

    public bool CrossCheck<T1, T2>(Symbol other)
        where T1 : Symbol where T2 : Symbol
    {
        return (this is T1 && other is T2) ||
            (this is T2 && other is T1);
    }

    public void ThrowIfCantBeAssignedBy(Symbol other, IPosition position)
    {
        if (this is AnyArraySymbol && other.IsArray)
            return;

        if (this is AnyFunctionSymbol && other.IsFunction)
            return;

        if (IsNonStorage)
            throw new Error($"Cannot convert '{other}' to '{this}'", position);

        if (SymbolCheck(other))
            return;

        if (SymbolCheck(Object))
            return;

        if (SymbolCheck(Float) && other.SymbolCheck(Int))
            return;

        throw new Error($"Cannot implicitly convert '{other}' to '{this}'", position);
    }

    public bool CanBeAssignedBy(Symbol other)
    {
        if (IsNonStorage || other.IsNonStorage)
            return false;

        if (SymbolCheck(other))
            return true;

        // Can accept any type
        if (SymbolCheck(Object))
            return true;

        if (SymbolCheck(Float) && other.SymbolCheck(Int))
            return true;

        return false;
    }

    public void ThrowIfCantCastFrom(Symbol other, IPosition position)
    {
        if (SymbolCheck(Object) && other is AnyArraySymbol)
            return;

        if (IsNonStorage || other.IsNonStorage)
            throw new Error($"Cannot convert '{this}' to '{other}'", position);

        if (SymbolCheck(other))
            return;

        if (SymbolCheck(Object) || other.SymbolCheck(Object))
            return;

        if ((SymbolCheck(Int) && other.SymbolCheck(Float)) ||
            (SymbolCheck(Float) && other.SymbolCheck(Int)))
            return;

        throw new Error($"Cannot explicitly convert '{this}' to '{other}'", position);
    }

    public void ThrowIfNotSymbol(Symbol other, IPosition position)
    {
        if (!SymbolCheck(other))
            throw new Error($"Expected '{other}', got '{this}'", position);
    }

    public void ThrowIfNotNumber(IPosition position)
    {
        if (!IsNumber())
            throw new Error($"Expected a numeric, got '{this}'", position);
    }

    public bool IsNumber()
    {
        return SymbolCheck(Int) || SymbolCheck(Float);
    }

    public sealed override string ToString()
    {
        if (this is ArraySymbol arraySymbol)
            return $"{arraySymbol.Inner}[]";

        if (this is FunctionSymbol functionSymbol)
            return $"{functionSymbol.ReturnSymbol} {Name}({string.Join(", ", functionSymbol.ParameterSymbols)})";

        return Name;
    }
}

public record IntSymbol() : Symbol("int", typeof(long), true);
public record FloatSymbol() : Symbol("float", typeof(double), true);
public record StringSymbol() : Symbol("string", typeof(string), false);
public record BoolSymbol() : Symbol("bool", typeof(bool), true);
public record VoidSymbol() : Symbol("void", typeof(void), false);
public record ObjectSymbol() : Symbol("object", typeof(object), false);
public record ArraySymbol(Symbol Inner) : Symbol("array", Inner.Type.MakeArrayType(), false);
public record FunctionSymbol(Symbol ReturnSymbol, List<Symbol> ParameterSymbols, string Name = "function")
    : Symbol(Name, FunctionTypes.GetFunctionType(ReturnSymbol, ParameterSymbols), false)
{
    public int ParameterCount => ParameterSymbols.Count;
}
public record AnyFunctionSymbol() : Symbol("()", typeof(Delegate), false);
public record AnyArraySymbol() : Symbol("[]", typeof(Array), false);