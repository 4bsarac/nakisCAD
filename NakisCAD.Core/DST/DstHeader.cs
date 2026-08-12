namespace NakisCAD.Core.DST;

public class DstHeader
{
    public const int HEADER_SIZE = 512;
    public const int LABEL_MAX_CHARS = 16;

    public string DesignLabel { get; set; } = "";
    public int TotalStitches { get; set; }
    public int ColorChanges { get; set; }
    public int PositiveX { get; set; }
    public int NegativeX { get; set; }
    public int PositiveY { get; set; }
    public int NegativeY { get; set; }
    public int Ax { get; set; }
    public int Ay { get; set; }
    public int Mx { get; set; }
    public int My { get; set; }

    public double WidthMm => (PositiveX + Math.Abs(NegativeX)) / 10.0;
    public double HeightMm => (PositiveY + Math.Abs(NegativeY)) / 10.0;

    public static DstHeader Parse(byte[] data)
    {
        if (data.Length < HEADER_SIZE)
            throw new InvalidDataException("DST header must be at least 512 bytes");

        var h = new DstHeader();
        h.DesignLabel = ReadAsciiField(data, 0, 16).Trim();
        h.TotalStitches = ReadIntField(data, 17, 7);
        h.ColorChanges = ReadIntField(data, 25, 3);
        h.PositiveX = ReadIntField(data, 29, 5);
        h.NegativeX = ReadIntField(data, 35, 5);
        h.PositiveY = ReadIntField(data, 41, 5);
        h.NegativeY = ReadIntField(data, 47, 5);
        h.Ax = ReadIntField(data, 53, 6);
        h.Ay = ReadIntField(data, 60, 6);
        h.Mx = ReadIntField(data, 67, 6);
        h.My = ReadIntField(data, 74, 6);
        return h;
    }

    public byte[] Serialize()
    {
        byte[] data = new byte[HEADER_SIZE];
        for (int i = 0; i < data.Length; i++) data[i] = 0x20;

        WriteAsciiField(data, 0, "LA:" + DesignLabel);
        WriteAsciiField(data, 17, "ST:" + TotalStitches.ToString("D7"));
        WriteAsciiField(data, 25, "CO:" + ColorChanges.ToString("D3"));
        WriteAsciiField(data, 29, "+X:" + PositiveX.ToString("D5"));
        WriteAsciiField(data, 35, "-X:" + Math.Abs(NegativeX).ToString("D5"));
        WriteAsciiField(data, 41, "+Y:" + PositiveY.ToString("D5"));
        WriteAsciiField(data, 47, "-Y:" + Math.Abs(NegativeY).ToString("D5"));
        WriteAsciiField(data, 53, "AX:" + Ax.ToString("D6"));
        WriteAsciiField(data, 60, "AY:" + Ay.ToString("D6"));
        WriteAsciiField(data, 67, "MX:" + Mx.ToString("D6"));
        WriteAsciiField(data, 74, "MY:" + My.ToString("D6"));

        return data;
    }

    private static string ReadAsciiField(byte[] data, int offset, int length)
    {
        return System.Text.Encoding.ASCII.GetString(data, offset, length);
    }

    private static int ReadIntField(byte[] data, int offset, int length)
    {
        string s = ReadAsciiField(data, offset, length);
        s = new string(s.Where(c => char.IsDigit(c) || c == '-' || c == '+').ToArray());
        if (string.IsNullOrEmpty(s)) return 0;
        return int.TryParse(s, out int v) ? v : 0;
    }

    private static void WriteAsciiField(byte[] data, int offset, string value)
    {
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(value);
        int len = Math.Min(bytes.Length, data.Length - offset);
        Array.Copy(bytes, 0, data, offset, len);
    }
}
