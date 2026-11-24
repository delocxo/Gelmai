public static class Stdio
{
    public static void Print(object? value)
    {
        Console.Write(value);
    }

    public static void PrintF(string format, object?[]? values)
    {
        Console.Write(format, values);
    }

    public static void PrintLine(object? value)
    {
        Console.WriteLine(value);
    }

    public static void PrintLineF(string format, object?[]? values)
    {
        Console.WriteLine(format, values);
    }

    public static void Error(object? value)
    {
        Console.Error.Write(value);
    }

    public static void ErrorF(string format, object?[] values)
    {
        Console.Error.Write(format, values);
    }

    public static void ErrorLine(object? value)
    {
        Console.Error.WriteLine(value);
    }

    public static void ErrorLineF(string format, object?[] values)
    {
        Console.Error.WriteLine(format, values);
    }

    public static string? ReadLine()
    {
        return Console.ReadLine();
    }

    public static string? ReadLineF(string format, object?[]? values)
    {
        Console.Write(format, values);
        return Console.ReadLine();
    }
}