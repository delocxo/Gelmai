partial class Semantics
{
    public (FunctionSymbol Symbol, string Name) BoundFunction(FunctionStmt functionStmt, Position position)
    {
        if (BoundFunctions.TryGetValue(functionStmt.Name, out var bound))
            return (bound.Symbol, functionStmt.Name);

        Scopes.BeginScope();

        Semantics semantics = new Semantics();

        var returnSymbol = semantics.ResolveSymbol(functionStmt.ReturnSymbol);

        if (!returnSymbol.SymbolCheck(Symbol.Void) &&
            (functionStmt.Body.Count == 0 ||
            functionStmt.Body[^1] is not ReturnStmt returnStmt ||
            returnStmt.Expr is null))
            throw new Error($"Non-void function '{functionStmt.Name}' must have a final return with a value", functionStmt);

        var boundParameters = functionStmt.Parameters
            .Select(x =>
            {
                var symbol = ResolveSymbol(x.Symbol);
                symbol.ThrowIfNotValidStorage(x.Symbol);
                return new BoundParameter(symbol, x.Name);
            }).ToList();

        foreach (var param in boundParameters)
            Scopes.Declare(param.Name, param.Symbol, null, functionStmt);

        FunctionSymbol functionSymbol = new(
            returnSymbol,
            boundParameters
                .Select(p => p.Symbol)
                .ToList(),
            functionStmt.Name);

        List<Stmt> body = [];

        BoundFunctions[functionStmt.Name] = (new(
            returnSymbol,
            functionStmt.Name,
            boundParameters,
            body,
            functionStmt.Position
        ), functionSymbol);

        var prevReturnSymbol = currentReturnSymbol;
        currentReturnSymbol = returnSymbol;

        body.AddRange(semantics.Check(functionStmt.Body));

        currentReturnSymbol = prevReturnSymbol;

        Scopes.Save(functionStmt);

        Scopes.EndScope();

        return (functionSymbol, functionStmt.Name);
    }
}