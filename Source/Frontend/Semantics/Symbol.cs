using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
public record Symbol(string Name, Type Type)
{
    public static readonly IntSymbol Int = new();
    public static readonly FloatSymbol Float = new();
    public static readonly StringSymbol String = new();
    public static readonly BoolSymbol Bool = new();
    public static readonly VoidSymbol Void = new();
    public static readonly ObjectSymbol Object = new();
    public static readonly AnyFunctionSymbol AnyFunction = new();
    public static readonly Symbol Empty = new("empty", typeof(byte));
    public static readonly AnyArraySymbol AnyArray = new();
    public static readonly NullLiteralSymbol Null = new();
    public static readonly NullSymbol NullObject = new(Object);

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
    public readonly bool IsValueType = Type.IsValueType;

    static readonly Dictionary<Type, ClassSymbol> classSymbolsByType = [];

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

        if (this is NullSymbol nsl && other is NullSymbol nsr)
            return nsl.Inner.SymbolCheck(nsr.Inner);

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

        if (this is NullSymbol && other is NullLiteralSymbol)
            return;

        if (IsNonStorage || other is NullLiteralSymbol)
            throw new Error($"Cannot convert '{other}' to '{this}'", position);

        if (SymbolCheck(other))
            return;

        if (SymbolCheck(Object) || this is NullSymbol nullSymbol1 &&
            nullSymbol1.Inner.SymbolCheck(Object))
            return;

        if (SymbolCheck(Float) && other.SymbolCheck(Int))
            return;

        if (this is NullSymbol nullSymbol &&
            nullSymbol.Inner.SymbolCheck(other))
            return;

        throw new Error($"Cannot implicitly convert '{other}' to '{this}'", position);
    }

    public bool CanBeAssignedBy(Symbol other, IPosition position)
    {
        try
        {
            ThrowIfCantBeAssignedBy(other, position);
            return true;
        }
        catch (Error)
        {
            return false;
        }
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

        if (this is NullSymbol nullSymbol1 && nullSymbol1.Inner.SymbolCheck(Object) ||
            other is NullSymbol nullSymbol3 && nullSymbol3.Inner.SymbolCheck(Object))
            return;

        if ((this is NullSymbol nullSymbol && nullSymbol.Inner.SymbolCheck(other)) ||
            (other is NullSymbol nullSymbol2 && nullSymbol2.Inner.SymbolCheck(this)))
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

        if (this is NullSymbol nullSymbol)
            return $"{nullSymbol.Inner}?";

        return Name;
    }

    public static Symbol TypeToSymbol(Type type, string? functionName = null)
    {
        var nullType = Nullable.GetUnderlyingType(type);
        if (nullType is not null)
            return new NullSymbol(TypeToSymbol(nullType));

        if (!type.IsEnum && (Type.GetTypeCode(type) is
            TypeCode.SByte or TypeCode.Byte or
            TypeCode.Int16 or TypeCode.UInt16 or
            TypeCode.Int32 or TypeCode.UInt32 or
            TypeCode.Int64))
            return Int;

        if (type == typeof(float) || type == typeof(double))
            return Float;

        if (type.IsSZArray)
        {
            var elementType = type.GetElementType()!;
            return new ArraySymbol(TypeToSymbol(elementType));
        }

        if (type == typeof(Array))
            return AnyArray;

        if (type == typeof(Delegate) || type == typeof(MulticastDelegate))
            return AnyFunction;

        if (type == typeof(void))
            return Void;

        if (type == typeof(bool))
            return Bool;

        if (type == typeof(string))
            return String;

        if (type == typeof(object))
            return Object;

        if (typeof(Delegate).IsAssignableFrom(type))
        {
            var invoke = type.GetMethod("Invoke")!;

            var returnSymbol = TypeToSymbol(invoke.ReturnType);

            List<Symbol> parameters = [];
            foreach (var param in invoke.GetParameters())
                parameters.Add(TypeToSymbol(param.ParameterType));

            return new FunctionSymbol(returnSymbol, parameters, functionName is not null ? functionName : "function");
        }

        if (type.IsClass)
        {
            if (classSymbolsByType.TryGetValue(type, out var cached))
                return cached;

            string name = type.Name;

            var fields = type.GetFields(
                BindingFlags.Instance | BindingFlags.Public
            );

            var constructor = type.GetConstructor([]);

            var classSymbol = new ClassSymbol(name, [], type, constructor);

            classSymbolsByType.Add(type, classSymbol);

            foreach (var field in fields)
            {
                var symbol = TypeToSymbol(field.FieldType);
                bool isConst = field.IsInitOnly || field.IsLiteral;
                var classField = new ClassField(symbol, isConst, field);
                classSymbol.Fields.Add(field.Name, classField);
            }

            return classSymbol;
        }

        throw new InvalidOperationException(functionName is not null
            ? $"Function: {functionName}"
            : ""
            + $"Failed to convert '{type}' to a valid Gelmai symbol");
    }

    public static string GetTypeName(Type type)
    {
        return TypeToSymbol(type).ToString();
    }
}

public record IntSymbol() : Symbol("int", typeof(long));
public record FloatSymbol() : Symbol("float", typeof(double));
public record StringSymbol() : Symbol("string", typeof(string));
public record BoolSymbol() : Symbol("bool", typeof(bool));
public record VoidSymbol() : Symbol("void", typeof(void));
public record ObjectSymbol() : Symbol("object", typeof(object));
public record ArraySymbol(Symbol Inner) : Symbol("array", Inner.Type.MakeArrayType());
public record FunctionSymbol(Symbol ReturnSymbol, List<Symbol> ParameterSymbols, string Name = "function")
    : Symbol(Name, FunctionTypes.GetFunctionType(ReturnSymbol, ParameterSymbols))
{
    public int ParameterCount => ParameterSymbols.Count;
}
public record AnyFunctionSymbol() : Symbol("()", typeof(Delegate));
public record AnyArraySymbol() : Symbol("[]", typeof(Array));
public record NullSymbol(Symbol Inner) : Symbol(
        "?",
        Inner.IsValueType
            ? typeof(Nullable<>).MakeGenericType(Inner.Type)
            : Inner.Type
        );
public record NullLiteralSymbol() : Symbol("null", typeof(object));
public record ClassField(Symbol Symbol, bool IsConst, FieldInfo FieldInfo);
public record ClassSymbol(string Name, Dictionary<string, ClassField> Fields, Type Type, ConstructorInfo? Constructor) : Symbol(Name, Type);