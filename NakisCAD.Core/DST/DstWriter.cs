namespace NakisCAD.Core.DST;

using NakisCAD.Core.Models;

public class DstWriter
{
    public void Write(string filePath, EmbroideryDesign design)
    {
        byte[] data = Serialize(design);
        File.WriteAllBytes(filePath, data);
    }

    public byte[] Serialize(EmbroideryDesign design)
    {
        var header = new DstHeader
        {
            DesignLabel = design.Name,
            TotalStitches = design.TotalStitches,
            ColorChanges = design.TotalColorChanges
        };

        // Calculate bounding box from stitches
        double cx = 0, cy = 0;
        double minX = 0, maxX = 0, minY = 0, maxY = 0;

        foreach (var s in design.Stitches)
        {
            cx += s.DeltaX / 10.0;
            cy += s.DeltaY / 10.0;
            if (cx < minX) minX = cx;
            if (cx > maxX) maxX = cx;
            if (cy < minY) minY = cy;
            if (cy > maxY) maxY = cy;
        }

        header.PositiveX = (int)(maxX * 10);
        header.NegativeX = (int)(minX * 10);
        header.PositiveY = (int)(maxY * 10);
        header.NegativeY = (int)(minY * 10);

        byte[] headerBytes = header.Serialize();

        // Encode stitches
        byte[] stitchData = new byte[design.Stitches.Count * 3];
        for (int i = 0; i < design.Stitches.Count; i++)
        {
            var s = design.Stitches[i];
            var (b1, b2, b3) = DstCodec.Encode(s.DeltaX, s.DeltaY, s.Type);
            stitchData[i * 3] = b1;
            stitchData[i * 3 + 1] = b2;
            stitchData[i * 3 + 2] = b3;
        }

        // Concatenate header + stitch data
        byte[] result = new byte[HEADER_SIZE + stitchData.Length];
        Array.Copy(headerBytes, 0, result, 0, HEADER_SIZE);
        Array.Copy(stitchData, 0, result, HEADER_SIZE, stitchData.Length);

        return result;
    }

    private const int HEADER_SIZE = 512;
}
