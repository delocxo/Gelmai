public static class GelmaiArray
{
    public static int ArrLength(Array array)
    {
        return array.Length;
    }

    public static void ArrCopy(Array source, int sourceIndex, Array dest, int destIndex, int count)
    {
        Array.Copy(source, sourceIndex, dest, destIndex, count);
    }

    public static int ArrIndexOf(Array array, object? value)
    {
        return Array.IndexOf(array, value);
    }

    public static void ArrReverse(Array array)
    {
        Array.Reverse(array);
    }

    public static void ArrClear(Array array)
    {
        Array.Clear(array);
    }
}