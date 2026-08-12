namespace NakisCAD.Core.DST;

using NakisCAD.Core.Models;

public class DstReader
{
    private const int HEADER_SIZE = 512;
    private const int STITCH_SIZE = 3;

    public DstHeader Header { get; private set; } = new();
    public List<StitchCommand> Stitches { get; private set; } = new();

    public EmbroideryDesign Read(string filePath)
    {
        byte[] data = File.ReadAllBytes(filePath);
        return Read(data);
    }

    public EmbroideryDesign Read(byte[] data)
    {
        if (data.Length < HEADER_SIZE)
            throw new InvalidDataException("File too small for DST header");

        Header = DstHeader.Parse(data);
        Stitches.Clear();

        int bodyLength = data.Length - HEADER_SIZE;
        int stitchCount = bodyLength / STITCH_SIZE;

        double cx = 0, cy = 0;

        for (int i = 0; i < stitchCount; i++)
        {
            int offset = HEADER_SIZE + i * STITCH_SIZE;
            byte b1 = data[offset];
            byte b2 = data[offset + 1];
            byte b3 = data[offset + 2];

            var (dx, dy, type) = DstCodec.Decode(b1, b2, b3);

            Stitches.Add(new StitchCommand(dx, dy, type));
        }

        var design = new EmbroideryDesign
        {
            Name = Header.DesignLabel,
            Stitches = Stitches,
            WidthMm = Header.WidthMm,
            HeightMm = Header.HeightMm
        };

        return design;
    }
}
