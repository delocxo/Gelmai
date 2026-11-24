public static class Core
{
    public static bool HasValue(object? obj)
    {
        return obj is not null;
    }
}