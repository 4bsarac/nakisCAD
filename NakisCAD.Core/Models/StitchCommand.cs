namespace NakisCAD.Core.Models;

public enum StitchType
{
    Normal = 0,
    Jump = 1,
    ColorChange = 2,
    Sequin = 3,
    End = 4
}

public class StitchCommand
{
    public short DeltaX { get; set; }
    public short DeltaY { get; set; }
    public StitchType Type { get; set; }

    public StitchCommand() { }

    public StitchCommand(short dx, short dy, StitchType type)
    {
        DeltaX = dx;
        DeltaY = dy;
        Type = type;
    }

    public override string ToString() =>
        $"[{Type}] dX={DeltaX}, dY={DeltaY}";
}
