namespace NakisCAD.Core.DST;

using NakisCAD.Core.Models;

/// <summary>
/// Tajima DST 3-byte stitch codec.
/// Encodes/decodes dX,dY values using powers of 3 (1,3,9,27,81).
/// </summary>
public static class DstCodec
{
    // Byte1 bit layout for X: b0=+1, b1=-1, b2=+9, b3=-9, b4=x-9, b5=x+9, b6=x-1, b7=x+1
    // But per the DST spec, the actual X contributions per bit are:
    // Byte 1: bit0=+1, bit1=-1, bit2=+9, bit3=-9
    // Byte 2: bit0=+3, bit1=-3, bit2=+27, bit3=-27
    // Byte 3: bit2=+81, bit3=-81

    private static readonly int[] Weights = { 1, 3, 9, 27, 81 };

    /// <summary>
    /// Decode a 3-byte stitch into (dX, dY, stitchType).
    /// </summary>
    public static (short dX, short dY, StitchType type) Decode(byte b1, byte b2, byte b3)
    {
        // Extract X components
        int dx = 0;
        dx += ((b1 & 0x01) != 0) ? +1 : 0;
        dx += ((b1 & 0x02) != 0) ? -1 : 0;
        dx += ((b1 & 0x04) != 0) ? +9 : 0;
        dx += ((b1 & 0x08) != 0) ? -9 : 0;
        dx += ((b2 & 0x01) != 0) ? +3 : 0;
        dx += ((b2 & 0x02) != 0) ? -3 : 0;
        dx += ((b2 & 0x04) != 0) ? +27 : 0;
        dx += ((b2 & 0x08) != 0) ? -27 : 0;
        dx += ((b3 & 0x04) != 0) ? +81 : 0;
        dx += ((b3 & 0x08) != 0) ? -81 : 0;

        // Extract Y components
        int dy = 0;
        dy += ((b1 & 0x80) != 0) ? +1 : 0;
        dy += ((b1 & 0x40) != 0) ? -1 : 0;
        dy += ((b1 & 0x20) != 0) ? +9 : 0;
        dy += ((b1 & 0x10) != 0) ? -9 : 0;
        dy += ((b2 & 0x80) != 0) ? +3 : 0;
        dy += ((b2 & 0x40) != 0) ? -3 : 0;
        dy += ((b2 & 0x20) != 0) ? +27 : 0;
        dy += ((b2 & 0x10) != 0) ? -27 : 0;
        dy += ((b3 & 0x20) != 0) ? +81 : 0;
        dy += ((b3 & 0x10) != 0) ? -81 : 0;

        // Extract control bits c0 (bit7) and c1 (bit6) from byte 3
        bool c0 = (b3 & 0x80) != 0;
        bool c1 = (b3 & 0x40) != 0;

        StitchType type = (c0, c1) switch
        {
            (false, false) => StitchType.Normal,
            (true, false) => StitchType.Jump,
            (true, true) => StitchType.ColorChange,
            (false, true) => StitchType.Sequin,
        };

        return ((short)dx, (short)dy, type);
    }

    /// <summary>
    /// Encode (dX, dY, stitchType) into 3 bytes.
    /// Uses greedy decomposition with powers of 3.
    /// </summary>
    public static (byte b1, byte b2, byte b3) Encode(short dX, short dY, StitchType type)
    {
        // Clamp to valid range
        int dx = Math.Clamp((int)dX, -121, 121);
        int dy = Math.Clamp((int)dY, -121, 121);

        // Decompose each axis into powers of 3
        int[] dxParts = Decompose(dx);
        int[] dyParts = Decompose(dy);

        byte b1 = 0, b2 = 0, b3 = 0;

        // Byte 1: X weights 1,3 and Y weights 1,3 (but DST puts Y on upper nibble)
        // X bits in byte 1: bit0=+1, bit1=-1, bit2=+9, bit3=-9
        if (dxParts[0] == 1) b1 |= 0x01;
        if (dxParts[0] == -1) b1 |= 0x02;
        if (dxParts[1] == 1) b1 |= 0x04;
        if (dxParts[1] == -1) b1 |= 0x08;

        // Y bits in byte 1: bit7=+1, bit6=-1, bit5=+9, bit4=-9
        if (dyParts[0] == 1) b1 |= 0x80;
        if (dyParts[0] == -1) b1 |= 0x40;
        if (dyParts[1] == 1) b1 |= 0x20;
        if (dyParts[1] == -1) b1 |= 0x10;

        // Byte 2: X weights 3,27 and Y weights 3,27
        // X bits in byte 2: bit0=+3, bit1=-3, bit2=+27, bit3=-27
        if (dxParts[2] == 1) b2 |= 0x01;
        if (dxParts[2] == -1) b2 |= 0x02;
        if (dxParts[3] == 1) b2 |= 0x04;
        if (dxParts[3] == -1) b2 |= 0x08;

        // Y bits in byte 2: bit7=+3, bit6=-3, bit5=+27, bit4=-27
        if (dyParts[2] == 1) b2 |= 0x80;
        if (dyParts[2] == -1) b2 |= 0x40;
        if (dyParts[3] == 1) b2 |= 0x20;
        if (dyParts[3] == -1) b2 |= 0x10;

        // Byte 3: X weight 81, Y weight 81, control bits
        // X bits: bit2=+81, bit3=-81
        if (dxParts[4] == 1) b3 |= 0x04;
        if (dxParts[4] == -1) b3 |= 0x08;

        // Y bits: bit5=+81, bit4=-81
        if (dyParts[4] == 1) b3 |= 0x20;
        if (dyParts[4] == -1) b3 |= 0x10;

        // Control bits
        bool c0 = type == StitchType.Jump || type == StitchType.ColorChange || type == StitchType.End;
        bool c1 = type == StitchType.Sequin || type == StitchType.ColorChange || type == StitchType.End;

        if (c0) b3 |= 0x80;
        if (c1) b3 |= 0x40;

        // Byte 3 bits 0,1 must always be 1
        b3 |= 0x01;
        b3 |= 0x02;

        return (b1, b2, b3);
    }

    /// <summary>
    /// Decompose integer d into 5 signed components for weights [1,3,9,27,81].
    /// Each component is -1, 0, or +1.
    /// </summary>
    private static int[] Decompose(int d)
    {
        int[] result = new int[5];
        int remaining = d;

        for (int i = 4; i >= 0; i--)
        {
            int w = Weights[i];
            int threshold = (w + 1) / 2;

            if (remaining >= threshold)
            {
                result[i] = 1;
                remaining -= w;
            }
            else if (remaining <= -threshold)
            {
                result[i] = -1;
                remaining += w;
            }
            else
            {
                result[i] = 0;
            }
        }

        return result;
    }
}
