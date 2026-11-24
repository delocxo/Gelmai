internal enum TokenType
{
    String, Number, Identifier,

    True, False, New, Return, Is, If, Else,
    While, Break, Continue, Null,

    Add, Sub, Mul, Div, Mod,
    IsEqual, NotEqual, Less, Greater,
    LessEq, GreaterEq, And, Or, Bang,
    BitwiseNot, BitwiseLeftShift, BitwiseRightShift,
    BitwiseAnd, BitwiseOr, BitwiseXor,

    Equal, Semicolon, LeftBracket, RightBracket,
    LeftBrace, RightBrace, LeftParen, RightParen,
    Comma, Period, Hash, Arrow, At, Hook,

    Eof
}

internal record Token(TokenType TokenType, string Lexeme, Position Position);