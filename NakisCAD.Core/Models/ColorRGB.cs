namespace NakisCAD.Core.Models;

/// <summary>
/// Renk bilgisi (RGB)
/// </summary>
public struct ColorRGB : IEquatable<ColorRGB>
{
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }

    public ColorRGB(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
    }

    public ColorRGB(int r, int g, int b)
    {
        R = (byte)Math.Clamp(r, 0, 255);
        G = (byte)Math.Clamp(g, 0, 255);
        B = (byte)Math.Clamp(b, 0, 255);
    }

    public static ColorRGB Black => new(0, 0, 0);
    public static ColorRGB White => new(255, 255, 255);
    public static ColorRGB Red => new(255, 0, 0);
    public static ColorRGB Green => new(0, 255, 0);
    public static ColorRGB Blue => new(0, 0, 255);

    public bool Equals(ColorRGB other) => R == other.R && G == other.G && B == other.B;
    public override bool Equals(object? obj) => obj is ColorRGB c && Equals(c);
    public override int GetHashCode() => HashCode.Combine(R, G, B);
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";

    public static bool operator ==(ColorRGB left, ColorRGB right) => left.Equals(right);
    public static bool operator !=(ColorRGB left, ColorRGB right) => !left.Equals(right);
}
