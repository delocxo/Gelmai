using System;
using System.Collections.Generic;
using System.Text;

class Lexer
{
    int _i;
    string _code;
    Position _currentPos;

    static Dictionary<string, TokenType> s_keywords = new Dictionary<string, TokenType>()
    {
        { "true", TokenType.True },
        { "false", TokenType.False },
        { "new", TokenType.New },
        { "return", TokenType.Return }
    };

    static Dictionary<string, TokenType> s_symbols = new Dictionary<string, TokenType>()
        {
            { "+", TokenType.Add },
            { "-", TokenType.Sub },
            { "*", TokenType.Mul },
            { "/", TokenType.Div },
            { "%", TokenType.Mod },
            { "==", TokenType.IsEqual },
            { "!=", TokenType.NotEqual },
            { "<", TokenType.Less },
            { ">", TokenType.Greater },
            { "<=", TokenType.LessEq },
            { ">=", TokenType.GreaterEq },
            { "=", TokenType.Equal },
            { ";", TokenType.Semicolon },
            { "[", TokenType.LeftBracket },
            { "]", TokenType.RightBracket },
            { "{", TokenType.LeftBrace },
            { "}", TokenType.RightBrace },
            { "&&", TokenType.And },
            { "||", TokenType.Or },
            { "!", TokenType.Bang },
            { "(", TokenType.LeftParen },
            { ")", TokenType.RightParen },
            { ",", TokenType.Comma },
            { ".", TokenType.Period },
            { "#", TokenType.Hash },
            // { "~", TokenType.BitwiseNot },
            // { "<<", TokenType.BitwiseLeftShift },
            // { ">>", TokenType.BitwiseRightShift },
            // { "&", TokenType.BitwiseAnd },
            // { "^", TokenType.BitwiseXor },
            // { "|", TokenType.BitwiseOr },
            { "=>", TokenType.Arrow },
            { "@", TokenType.At }
        };

    public static string? GetKeywordFromType(TokenType type)
    {
        KeyValuePair<string, TokenType>? value = s_keywords.FirstOrDefault(x => x.Value == type);
        if (value == null)
            return null;
        return value.Value.Key;
    }

    public static string? GetSymbolFromType(TokenType type)
    {
        KeyValuePair<string, TokenType>? value = s_symbols.FirstOrDefault(x => x.Value == type);
        if (value == null)
            return null;
        return value.Value.Key;
    }

    public Lexer(string code, string source)
    {
        _code = code;
        _currentPos = new Position(1, 1, source);
    }

    public List<Token> Lex()
    {
        List<Token> tokens = new List<Token>();

        while (NotAtEnd())
        {
            if (char.IsWhiteSpace(Char()))
            {
                Next();
                continue;
            }

            if (NotAtEnd(1))
            {
                string doubleChar = $"{Char(0)}{Char(1)}";
                if (doubleChar == "//")
                {
                    Next();
                    Next();
                    while (NotAtEnd() && Char() != '\n')
                        Next();
                    continue;
                }
                if (s_symbols.TryGetValue(doubleChar, out TokenType value))
                {
                    tokens.Add(new Token(value, doubleChar, _currentPos));
                    Next();
                    Next();
                    continue;
                }
            }

            if (s_symbols.TryGetValue(Char().ToString(), out TokenType otherValue))
            {
                tokens.Add(new Token(otherValue, Char().ToString(), _currentPos));
                Next();
                continue;
            }

            if (Char() == '"')
            {
                tokens.Add(LexString());
                continue;
            }

            if (char.IsLetter(Char()) || Char() == '_')
            {
                tokens.Add(LexAlpha());
                continue;
            }

            if (char.IsDigit(Char()))
            {
                tokens.Add(LexNumber());
                continue;
            }

            throw new Error($"Unexpected character '{Char()}'", _currentPos);
        }

        tokens.Add(new Token(TokenType.Eof, "End of File", _currentPos));

        return tokens;
    }

    Token LexString()
    {
        Position position = _currentPos;

        Next();

        StringBuilder sb = new StringBuilder();

        while (NotAtEnd() && Char() != '"')
        {
            if (Char() == '\\')
            {
                sb.Append(LexEscape());
                continue;
            }
            sb.Append(Char());
            Next();
        }

        if (!NotAtEnd())
            throw new Error("Unterminated string", _currentPos);

        Next();

        return new Token(TokenType.String, sb.ToString(), position);
    }

    Token LexAlpha()
    {
        Position position = _currentPos;

        StringBuilder sb = new StringBuilder();

        while (NotAtEnd() && (char.IsLetterOrDigit(Char()) || Char() == '_'))
        {
            sb.Append(Char());
            Next();
        }

        string value = sb.ToString();

        if (s_keywords.TryGetValue(value, out TokenType keyword))
            return new Token(keyword, value, position);

        return new Token(TokenType.Identifier, value, position);
    }

    Token LexNumber()
    {
        Position position = _currentPos;

        StringBuilder sb = new StringBuilder();
        bool hasDecimal = false;

        while (NotAtEnd() && (char.IsDigit(Char()) || Char() == '.'))
        {
            if (Char() == '.')
            {
                if (hasDecimal)
                    throw new Error("Number already contains a decimal", _currentPos);

                if (!NotAtEnd(1) || !char.IsDigit(Char(1)))
                    throw new Error("Expected number after decimal", _currentPos);

                hasDecimal = true;
            }
            sb.Append(Char());
            Next();
        }

        string number = sb.ToString();
        int zeros = 0;

        if (NotAtEnd(1))
        {
            string suffix = $"{char.ToLowerInvariant(Char())}{char.ToLowerInvariant(Char(1))}";

            zeros = suffix switch
            {
                "qa" => 15,
                "qi" => 18,
                _ => 0
            };

            if (zeros != 0)
            {
                Next();
                Next();
            }
        }

        if (zeros == 0 && NotAtEnd())
        {
            zeros = char.ToLowerInvariant(Char()) switch
            {
                'k' => 3,
                'm' => 6,
                'b' => 9,
                't' => 12,
                _ => 0
            };

            if (zeros != 0)
                Next();
        }

        if (zeros != 0)
            number = ApplySuffix(number, zeros);

        return new Token(TokenType.Number, number, position);
    }

    string ApplySuffix(string number, int zeros)
    {
        int dot = number.IndexOf('.');

        if (dot == -1)
            return number + new string('0', zeros);

        string digits = number.Remove(dot, 1);
        int newDot = dot + zeros;

        return newDot >= digits.Length
            ? digits.PadRight(newDot, '0')
            : digits.Insert(newDot, ".");
    }

    char LexEscape()
    {
        Position escapePos = _currentPos;

        Next();

        if (!NotAtEnd())
            throw new Error("Unterminated escape sequence", escapePos);

        char escape = Char() switch
        {
            'n' => '\n',
            't' => '\t',
            'a' => '\a',
            '0' => '\0',
            '"' => '\"',
            '\\' => '\\',
            'f' => '\f',
            'v' => '\v',
            'b' => '\b',
            'e' => '\e',
            'r' => '\r',
            _ => Char(),
        };

        Next();

        return escape;
    }

    char Char(int dst = 0) => _code[_i + dst];
    bool NotAtEnd(int dst = 0) => _i + dst < _code.Length;

    void Next()
    {
        if (Char() == '\n')
        {
            _currentPos.Line++;
            _currentPos.Column = 1;
        }
        else
            _currentPos.Column++;
        _i++;
    }
}
