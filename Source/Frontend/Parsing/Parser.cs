using System.Diagnostics;
using System.Globalization;
using System.IO.Pipelines;
using System.Runtime.ConstrainedExecution;

class Parser
{
    List<Token> _tokens;
    int _i = 0;

    public Parser(List<Token> tokens)
    {
        _tokens = tokens;
    }

    public List<Stmt> Parse()
    {
        List<Stmt> stmts = new List<Stmt>();

        while (Check(TokenType.Use))
        {
            var pos = PosNext();
            string path = Expect(TokenType.String);
            Expect(TokenType.Semicolon);
            DllLoader.LoadDll(path, pos);
        }

        while (NotAtEnd())
        {
            if (Check(TokenType.Use))
                throw new Error("Use must precede all other statements", CurrentPos());
            stmts.Add(ParseStmt());
        }

        return stmts;
    }

    Stmt ParseStmt()
    {
        if (IsKnownType())
            return ParseVar();

        if (Check(TokenType.Return))
            return ParseReturn();

        if (Check(TokenType.If))
            return ParseIf();

        if (Check(TokenType.While))
            return ParseWhile();

        if (Check(TokenType.Break))
        {
            var pos = PosNext();
            Expect(TokenType.Semicolon);
            return new BreakStmt(pos);
        }

        if (Check(TokenType.Continue))
        {
            var pos = PosNext();
            Expect(TokenType.Semicolon);
            return new ContinueStmt(pos);
        }

        if (Check(TokenType.For))
            return ParseFor();

        return ParseBare();
    }

    Error ThrowUnexpected()
    {
        Token token = Current();
        throw new Error($"Unexpected token '{token.TokenType}'", token.Position);
    }

    BareStmt ParseBare()
    {
        Expr expr = ParseExpr();
        Expect(TokenType.Semicolon);
        return new(expr, expr.Position, Symbol.Empty);
    }

    Stmt ParseVar()
    {
        var symbol = ParseSymbol();

        string name = ParseName();

        if (Check(TokenType.LeftParen))
            return ParseFunction(symbol, name);

        Expect(TokenType.Equal);

        Expr expr = ParseExpr();

        Expect(TokenType.Semicolon);

        return new VariableStmt(name, symbol, expr, symbol.Position);
    }

    FunctionStmt ParseFunction(AstSymbol returnSymbol, string name)
    {
        List<AstParameter> parameters = [];

        Expect(TokenType.LeftParen);

        if (!Check(TokenType.RightParen))
        {
            parameters.Add(ParseParameter());

            while (Match(TokenType.Comma))
                parameters.Add(ParseParameter());
        }

        Expect(TokenType.RightParen);

        var body = ParseBody();

        return new(returnSymbol, name, parameters, body);
    }

    ReturnStmt ParseReturn()
    {
        var pos = PosNext();

        if (Match(TokenType.Semicolon))
            return new(null, pos, Symbol.Empty, Symbol.Empty);

        Expr expr = ParseExpr();

        Expect(TokenType.Semicolon);

        return new(expr, pos, Symbol.Empty, Symbol.Empty);
    }

    IfStmt ParseIf()
    {
        var pos = PosNext();

        List<IfBranch> branches = [ParseBranch()];

        while (Check(TokenType.Else) && PeekNotAtEnd() && Peek().TokenType == TokenType.If)
        {
            Next();
            Next();
            branches.Add(ParseBranch());
        }

        List<Stmt>? elseBranch = null;

        if (Match(TokenType.Else))
            elseBranch = ParseBody();

        return new(branches, elseBranch, pos);
    }

    WhileStmt ParseWhile()
    {
        var pos = PosNext();

        var branch = ParseBranch();

        return new(branch, pos);
    }

    ForStmt ParseFor()
    {
        var pos = PosNext();

        Expect(TokenType.LeftParen);

        var symbol = ParseSymbol();

        string name = ParseName();

        Expect(TokenType.Colon);

        var iterable = ParseExpr();

        Expect(TokenType.RightParen);

        var body = ParseBody();

        return new(name, symbol, iterable, body, pos);
    }

    IfBranch ParseBranch()
    {
        Expect(TokenType.LeftParen);

        Expr condition = ParseExpr();

        Expect(TokenType.RightParen);

        var body = ParseBody();

        return new(condition, body);
    }

    bool IsKnownType(string? value = null)
    {
        if (value != null && Symbol.Symbols.ContainsKey(value))
            return true;

        if (Symbol.Symbols.ContainsKey(CurrentLexeme()))
            return true;

        if (Check(TokenType.LeftBracket) && PeekNotAtEnd() &&
            Peek().TokenType is TokenType.RightBracket)
            return true;

        if (Check(TokenType.LeftParen) && PeekNotAtEnd() &&
            Peek().TokenType is TokenType.RightParen)
            return true;

        return false;
    }

    AstSymbol ParseSymbol()
    {
        var pos = CurrentPos();

        if (Match(TokenType.LeftParen))
        {
            Expect(TokenType.RightParen);
            return new AstAnyFunctionSymbol(pos);
        }

        if (Match(TokenType.LeftBracket))
        {
            Expect(TokenType.RightBracket);
            return new AstAnyArraySymbol(pos);
        }

        string name = Expect(TokenType.Identifier);

        AstSymbol symbol = new AstNameSymbol(name, pos);

        if (Match(TokenType.LeftParen))
        {
            if (Match(TokenType.RightParen))
                symbol = new AstFunctionSymbol(symbol, []);
            else
            {
                List<AstSymbol> parameterSymbols = [ParseSymbol()];

                while (Match(TokenType.Comma))
                    parameterSymbols.Add(ParseSymbol());

                Expect(TokenType.RightParen);

                symbol = new AstFunctionSymbol(symbol, parameterSymbols);
            }
        }

        while (true)
        {
            if (Check(TokenType.LeftBracket) && PeekNotAtEnd() && Peek().TokenType == TokenType.RightBracket)
            {
                Next();
                Next();
                symbol = new AstArraySymbol(symbol);
                continue;
            }

            break;
        }

        if (Match(TokenType.Hook))
            symbol = new AstNullSymbol(symbol);

        return symbol;
    }

    AstParameter ParseParameter()
    {
        var symbol = ParseSymbol();
        string name = ParseName();
        return new(symbol, name);
    }

    string ParseName()
    {
        return Expect(TokenType.Identifier);
    }

    string ParseName(string message)
    {
        string name = Current().Lexeme;
        Eat(message, TokenType.Identifier);
        return name;
    }

    List<Stmt> ParseBody()
    {
        List<Stmt> stmts = new List<Stmt>();

        Expect(TokenType.LeftBrace);

        while (NotAtEnd() && !Check(TokenType.RightBrace))
            stmts.Add(ParseStmt());

        Expect(TokenType.RightBrace);

        return stmts;
    }

    List<string> ParseNames(TokenType end)
    {
        if (Match(end))
            return new List<string>();

        List<string> names = new List<string>()
            {
                ParseName()
            };

        while (Match(TokenType.Comma))
            names.Add(ParseName());

        Expect(end);

        return names;
    }

    List<string> ParseNames(TokenType start, TokenType end)
    {
        Expect(start);
        return ParseNames(end);
    }

    List<Expr> ParseArgs(TokenType start, TokenType end)
    {
        Expect(start);

        if (Match(end))
            return new List<Expr>();

        List<Expr> args = new List<Expr>()
            {
                ParseExpr()
            };

        while (Match(TokenType.Comma))
            args.Add(ParseExpr());

        Expect(end);

        return args;
    }

    bool Check(params TokenType[] types)
    {
        for (int i = 0; i < types.Length; i++)
            if (Current().TokenType == types[i])
                return true;
        return false;
    }

    Token Current() => _tokens[_i];
    Token Peek() => _tokens[_i + 1];
    bool NotAtEnd() => !Check(TokenType.Eof);
    bool PeekNotAtEnd() => _i + 1 < _tokens.Count;
    bool AtEnd() => Check(TokenType.Eof);
    void Next() => _i++;
    Position CurrentPos() => Current().Position;
    string CurrentLexeme() => Current().Lexeme;
    TokenType CurrentType() => Current().TokenType;

    Position PosNext()
    {
        Position pos = Current().Position;
        Next();
        return pos;
    }

    Token TokNext()
    {
        Token token = Current();
        Next();
        return token;
    }

    void Eat(string message, params TokenType[] types)
    {
        if (Check(types))
        {
            Next();
            return;
        }
        throw new Error(message, Current().Position);
    }

    bool Match(params TokenType[] types)
    {
        if (Check(types))
        {
            Next();
            return true;
        }
        return false;
    }

    string Expect(TokenType type)
    {
        if (Check(type))
        {
            string lexeme = CurrentLexeme();
            Next();
            return lexeme;
        }
        throw new Error($"Expected token '{type}', got '{CurrentType()}'", CurrentPos());
    }

    Expr ParsePrimary()
    {
        Token token = Current();

        if (Match(TokenType.Number))
        {
            if (long.TryParse(token.Lexeme, CultureInfo.InvariantCulture, out long longResult))
                return new IntExpr(longResult, token.Position);

            else if (double.TryParse(token.Lexeme, CultureInfo.InvariantCulture, out double doubleResult))
                return new FloatExpr(doubleResult, token.Position);

            throw new Error("Number literal too large", token.Position);
        }
        else if (Match(TokenType.String))
            return new StringExpr(token.Lexeme, token.Position);
        else if (Match(TokenType.True))
            return new BoolExpr(true, token.Position);
        else if (Match(TokenType.False))
            return new BoolExpr(false, token.Position);
        else if (Match(TokenType.Identifier))
        {
            return new NameExpr(token.Lexeme, token.Position);
        }
        else if (Match(TokenType.LeftParen))
        {
            Expr expr = ParseExpr();
            Expect(TokenType.RightParen);
            return expr;
        }
        else if (Match(TokenType.New))
        {
            var symbol = ParseSymbol();
            Expr? size = null;

            if (Check(TokenType.LeftBracket))
            {
                Next();

                size = ParseExpr();

                Expect(TokenType.RightBracket);

                symbol = new AstArraySymbol(symbol);
            }

            List<Expr> items = [];

            if (Check(TokenType.LeftBrace))
                items = ParseArgs(TokenType.LeftBrace, TokenType.RightBrace);

            return new ArrayExpr(symbol, size, items, token.Position);
        }
        else if (Match(TokenType.Null))
            return new NullExpr(token.Position);

        throw ThrowUnexpected();
    }

    Expr ParsePostfix()
    {
        Expr left = ParsePrimary();

        while (Check(TokenType.LeftParen, TokenType.LeftBracket, TokenType.Period, TokenType.Is))
        {
            if (Check(TokenType.LeftParen))
            {
                List<Expr> args = ParseArgs(TokenType.LeftParen, TokenType.RightParen);

                left = new CallExpr(left, args);

                continue;
            }

            if (Match(TokenType.Is))
            {
                var symbol = ParseSymbol();

                left = new IsExpr(left, symbol);

                continue;
            }

            if (Check(TokenType.LeftBracket))
            {
                Position position = Current().Position;

                Expect(TokenType.LeftBracket);

                Expr index = ParseExpr();

                Expect(TokenType.RightBracket);

                left = new IndexExpr(left, index);

                continue;
            }

            //     if (Check(TokenType.Period))
            //     {
            //         Position position = Current().Position;

            //         Next();

            //         string name = ParseName();

            //         left = new MemberExpr(left, name, position);

            //         continue;
            //     }

            break;
        }
        return left;
    }

    Expr ParseUnary()
    {
        if (Check(TokenType.LeftParen))
        {
            var pos = PosNext();

            if (!IsKnownType())
            {
                _i--;
                goto RegularUnary;
            }

            var symbol = ParseSymbol();

            Expect(TokenType.RightParen);

            Expr target = ParseUnary();

            return new CastExpr(symbol, target, pos);
        }

    RegularUnary:
        if (Check(TokenType.Sub, TokenType.Bang, TokenType.BitwiseNot /* TokenType.At */))
        {
            Token op = Current();

            Next();

            Expr right = ParseUnary();

            return new UnaryExpr(right, op.TokenType, op.Position);
        }
        return ParsePostfix();
    }

    Expr ParseTerm()
    {
        Expr left = ParseUnary();

        while (Check(TokenType.Mul, TokenType.Div, TokenType.Mod))
        {
            Token op = Current();

            Next();

            Expr right = ParseUnary();

            left = new BinaryExpr(left, right, op.TokenType, op.Position);
        }

        return left;
    }

    Expr ParseFactor()
    {
        Expr left = ParseTerm();

        while (Check(TokenType.Add, TokenType.Sub))
        {
            Token op = Current();

            Next();

            Expr right = ParseTerm();

            left = new BinaryExpr(left, right, op.TokenType, op.Position);
        }

        return left;
    }

    Expr ParseBitwiseShift()
    {
        Expr left = ParseFactor();

        while (Check(TokenType.BitwiseLeftShift, TokenType.BitwiseRightShift))
        {
            Token op = Current();

            Next();

            Expr right = ParseFactor();

            left = new BinaryExpr(left, right, op.TokenType, op.Position);
        }

        return left;
    }

    Expr ParseBitwiseAnd()
    {
        Expr left = ParseBitwiseShift();

        while (Check(TokenType.BitwiseAnd))
        {
            Token op = Current();

            Next();

            Expr right = ParseBitwiseShift();

            left = new BinaryExpr(left, right, op.TokenType, op.Position);
        }

        return left;
    }

    Expr ParseBitwiseXor()
    {
        Expr left = ParseBitwiseAnd();

        while (Check(TokenType.BitwiseXor))
        {
            Token op = Current();

            Next();

            Expr right = ParseBitwiseAnd();

            left = new BinaryExpr(left, right, op.TokenType, op.Position);
        }

        return left;
    }

    Expr ParseBitwiseOr()
    {
        Expr left = ParseBitwiseXor();

        while (Check(TokenType.BitwiseOr))
        {
            Token op = Current();

            Next();

            Expr right = ParseBitwiseXor();

            left = new BinaryExpr(left, right, op.TokenType, op.Position);
        }

        return left;
    }

    Expr ParseComparison()
    {
        Expr left = ParseBitwiseOr();

        while (Check(TokenType.Less, TokenType.Greater, TokenType.LessEq, TokenType.GreaterEq))
        {
            Token op = Current();

            Next();

            Expr right = ParseBitwiseOr();

            left = new BinaryExpr(left, right, op.TokenType, op.Position);
        }

        return left;
    }

    Expr ParseEquality()
    {
        Expr left = ParseComparison();

        while (Check(TokenType.NotEqual, TokenType.IsEqual))
        {
            Token op = Current();

            Next();

            Expr right = ParseComparison();

            left = new BinaryExpr(left, right, op.TokenType, op.Position);
        }

        return left;
    }

    Expr ParseAnd()
    {
        Expr left = ParseEquality();

        while (Check(TokenType.And))
        {
            Token op = Current();

            Next();

            Expr right = ParseEquality();

            left = new BinaryExpr(left, right, op.TokenType, op.Position);
        }

        return left;
    }

    Expr ParseOr()
    {
        Expr left = ParseAnd();

        while (Check(TokenType.Or))
        {
            Token op = Current();

            Next();

            Expr right = ParseAnd();

            left = new BinaryExpr(left, right, op.TokenType, op.Position);
        }

        return left;
    }

    Expr ParseAssign()
    {
        Expr left = ParseOr();

        if (Match(TokenType.Equal))
        {
            Expr expr = ParseAssign();

            if (left is NameExpr nameExpr)
                return new AssignExpr(nameExpr.Name, expr, left.Position, Symbol.Empty);

            if (left is IndexExpr indexExpr)
                return new IndexAssignExpr(
                    indexExpr.Target,
                    indexExpr.Index,
                    expr
                );

            throw new Error("Invalid assign target", left);
        }

        return left;
    }

    Expr ParseExpr() => ParseAssign();
}
