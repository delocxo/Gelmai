using System;
using System.Collections.Generic;
using System.Text;

public record struct Position(int Line, int Column, string Source)
{
    public static readonly Position Protocal = new(1, 1, "<protocal>");

    public override string ToString()
    {
        return $"{Line}:{Column}:{Source}";
    }
}

public interface IPosition
{
    Position Position { get; }
}

class Error : Exception
{
    Position? _position;

    public Error(string message, Position position) : base(message)
    {
        _position = position;
    }

    public Error(string message, IPosition position) : base(message)
    {
        _position = position.Position;
    }

    public Error(string message) : base(message)
    {
        _position = null;
    }

    public void Print()
    {
        if (_position != null)
            Console.Error.WriteLine($"Error: {_position.Value}: {Message}");
        else
            Console.Error.WriteLine($"Error: {Message}");
    }
}