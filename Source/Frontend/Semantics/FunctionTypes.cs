using System.Linq.Expressions;

static class FunctionTypes
{
    public static Type GetFunctionType(Symbol returnSymbol, List<Symbol> parameters)
    {
        Type[] types = [.. parameters.Select(p => p.Type), returnSymbol.Type];
        return Expression.GetDelegateType(types);
    }
}