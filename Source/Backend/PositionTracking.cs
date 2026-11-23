using System.Reflection.Emit;

public static class PositionTracking
{
    public static ThreadLocal<Position> Current = new(() => Position.Protocal);

    public static bool Debug = true;

    public static void EmitPosition(ILGenerator iLGenerator, IPosition position)
    {
        if (!Debug)
            return;

        Position pos = position.Position;

        iLGenerator.Emit(OpCodes.Ldc_I4, pos.Line);
        iLGenerator.Emit(OpCodes.Ldc_I4, pos.Column);
        iLGenerator.Emit(OpCodes.Ldstr, pos.Source);

        iLGenerator.Emit(
            OpCodes.Call,
            typeof(PositionTracking).GetMethod(nameof(SetPosition))!
        );
    }

    public static void SetPosition(int line, int column, string source)
    {
        Current.Value = new(line, column, source);
    }
}