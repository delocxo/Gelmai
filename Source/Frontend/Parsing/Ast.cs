using System;
using System.Collections.Generic;
using System.Text;

abstract record Expr(Position Position) : IPosition;
record IntExpr(long Value, Position Position) : Expr(Position);
record FloatExpr(double Value, Position Position) : Expr(Position);
record StringExpr(string Value, Position Position) : Expr(Position);
record BoolExpr(bool Value, Position Position) : Expr(Position);
record NameExpr(string Name, Position Position) : Expr(Position);
record LoadFunctionExpr(FunctionStmt FunctionStmt) : Expr(FunctionStmt.Position);
record UnaryExpr(Expr Right, TokenType Op, Position Position) : Expr(Position);
record BinaryExpr(Expr Left, Expr Right, TokenType Op, Position Position) : Expr(Position);
record BoundBinaryExpr(Resolved Left, Resolved Right, TokenType Op, Position Position) : Expr(Position);
record AssignExpr(string Name, Expr Expr, Position Position, Symbol ExprSymbol) : Expr(Position);
record CastExpr(AstSymbol Symbol, Expr Target, Position Position) : Expr(Position);
record BoundCastExpr(Symbol TargetSymbol, Expr Expr, Symbol SourceSymbol, Position Position) : Expr(Position);
record ArrayExpr(AstSymbol Symbol, Expr? Size, List<Expr> Items, Position Position) : Expr(Position);
record BoundArrayExpr(ArraySymbol Symbol, Expr? Size, List<Resolved> Items, Position Position) : Expr(Position);
record CallExpr(Expr Target, List<Expr> Args) : Expr(Target.Position);
record BoundCallExpr(Expr Target, List<(Resolved First, Symbol Second)> Args, Type Type) : Expr(Target.Position);

abstract record AstSymbol(Position Position) : IPosition;
record AstNameSymbol(string Name, Position Position) : AstSymbol(Position);
record AstArraySymbol(AstSymbol InnerSymbol) : AstSymbol(InnerSymbol.Position);
record AstFunctionSymbol(AstSymbol ReturnSymbol, List<AstSymbol> ParameterSymbols) : AstSymbol(ReturnSymbol.Position);
record AstAnyFunctionSymbol(Position Position) : AstSymbol(Position);

abstract record Stmt(Position Position) : IPosition;
record VariableStmt(string Name, AstSymbol Symbol, Expr Expr, Position Position) : Stmt(Position);
record BoundVariableStmt(string Name, Symbol Symbol, Resolved Resolved, Position Position) : Stmt(Position);
record BareStmt(Expr Expr, Position Position) : Stmt(Position);
record AstParameter(AstSymbol Symbol, string Name);
record BoundParameter(Symbol Symbol, string Name);
record FunctionStmt(AstSymbol ReturnSymbol, string Name, List<AstParameter> Parameters, List<Stmt> Body) : Stmt(ReturnSymbol.Position);
record BoundFunctionStmt(Symbol ReturnSymbol, string Name, List<BoundParameter> Parameters, List<Stmt> Body, Position Position) : Stmt(Position);
record ReturnStmt(Expr? Expr, Position Position, Symbol ReturnSymbol, Symbol ExpSymbol) : Stmt(Position);