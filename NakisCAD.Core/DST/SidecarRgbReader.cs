namespace NakisCAD.Core.DST;

using NakisCAD.Core.Models;

/// <summary>
/// Reads .RGB sidecar files for DST color palette information.
/// The .RGB file stores RGB values for each color change in the design.
/// </summary>
public static class SidecarRgbReader
{
    public static List<ColorRGB> Read(string dstFilePath)
    {
        string rgbPath = Path.ChangeExtension(dstFilePath, ".RGB");
        if (!File.Exists(rgbPath))
            return new List<ColorRGB>();

        return Parse(rgbPath);
    }

    public static List<ColorRGB> Parse(string rgbFilePath)
    {
        var colors = new List<ColorRGB>();
        byte[] data = File.ReadAllBytes(rgbFilePath);

        // Each color entry is 3 bytes (R, G, B)
        for (int i = 0; i + 2 < data.Length; i += 3)
        {
            colors.Add(new ColorRGB(data[i], data[i + 1], data[i + 2]));
        }

        return colors;
    }

    public static void Write(string dstFilePath, List<ColorRGB> colors)
    {
        string rgbPath = Path.ChangeExtension(dstFilePath, ".RGB");
        byte[] data = new byte[colors.Count * 3];

        for (int i = 0; i < colors.Count; i++)
        {
            data[i * 3] = colors[i].R;
            data[i * 3 + 1] = colors[i].G;
            data[i * 3 + 2] = colors[i].B;
        }

        File.WriteAllBytes(rgbPath, data);
    }
}
