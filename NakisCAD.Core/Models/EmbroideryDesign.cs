namespace NakisCAD.Core.Models;

public class EmbroideryDesign
{
    public string Name { get; set; } = "Untitled";
    public List<StitchCommand> Stitches { get; set; } = new();
    public List<ColorRGB> ColorPalette { get; set; } = new();
    public List<int> ColorChangeIndices { get; set; } = new();

    public int TotalStitches => Stitches.Count;
    public int TotalColorChanges => ColorChangeIndices.Count;

    public double WidthMm { get; set; }
    public double HeightMm { get; set; }

    public List<Point2D> GetAbsolutePoints()
    {
        var points = new List<Point2D>();
        double cx = 0, cy = 0;
        foreach (var s in Stitches)
        {
            cx += s.DeltaX / 10.0;
            cy += s.DeltaY / 10.0;
            points.Add(new Point2D(cx, cy));
        }
        return points;
    }
}
