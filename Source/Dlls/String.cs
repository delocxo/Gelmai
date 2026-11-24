public static class GelmaiString
{
    public static string? ToStr(object? value)
    {
        if (value is null)
            return null;
        return value.ToString();
    }

    public static string? Format(string format, object[] values)
    {
        return string.Format(format, values);
    }
}