using OpenCvSharp;
using NakisCAD.Core.Models;

namespace NakisCAD.Core.ImageProcessing;

/// <summary>
/// Sekil siniflandirma: Distance Transform ile genislik analizi
/// Satin (ince kolon) vs Tatami (genis alan) vs Run (cizgi)
/// </summary>
public static class ShapeClassifier
{
    /// <summary>
    /// Kontur icin dikiş stilini belirle (Distance Transform tabanli)
    /// </summary>
    public static StitchStyle Classify(ContourInfo contour, Mat? mask = null, ClassifyOptions? options = null)
    {
        options ??= new ClassifyOptions();

        double area = contour.Area;

        // Cok kucuk alan -> Run
        if (area < options.MinAreaForFill)
            return StitchStyle.Run;

        // Distance Transform ile ortalama genislik
        double avgWidth = 0;
        if (mask != null)
        {
            avgWidth = MeasureWidthWithDT(mask, contour);
        }
        else
        {
            avgWidth = MeasureWidthSampling(contour);
        }

        // Ince sekil (< 10mm) -> Satin
        if (avgWidth < options.SatinMaxWidth)
            return StitchStyle.Satin;

        // Genis alan -> Tatami
        return StitchStyle.Tatami;
    }

    /// <summary>
    /// Distance Transform ile ortalama genislik olcme (en dogru yontem)
    /// </summary>
    public static double MeasureWidthWithDT(Mat mask, ContourInfo contour)
    {
        // Distance Transform hesapla
        var dist = new Mat();
        Cv2.DistanceTransform(mask, dist, DistanceTypes.L2, DistanceTransformMasks.Mask5);

        // Kontur icindeki pikseller icin ortalama genislik
        double totalWidth = 0;
        int count = 0;

        // Kontur icindeki pikselleri tara
        var rect = contour.BoundingRect;
        int margin = 2;
        int startX = Math.Max(0, rect.X - margin);
        int startY = Math.Max(0, rect.Y - margin);
        int endX = Math.Min(mask.Width, rect.X + rect.Width + margin);
        int endY = Math.Min(mask.Height, rect.Y + rect.Height + margin);

        for (int y = startY; y < endY; y += 2) // her 2 pikselde bir
        {
            for (int x = startX; x < endX; x += 2)
            {
                if (mask.At<byte>(y, x) > 0)
                {
                    double d = dist.At<float>(y, x);
                    if (d > 0)
                    {
                        totalWidth += d * 2; // Yari cap -> tam genislik
                        count++;
                    }
                }
            }
        }

        dist.Dispose();

        if (count == 0) return 0;

        // Piksel -> mm donusumu (yaklasik: 96 DPI varsayim)
        double avgPixels = totalWidth / count;
        return avgPixels * 0.264; // 1 piksel ~= 0.264mm (96 DPI)
    }

    /// <summary>
    /// Sampling yontemi ile genislik olcme (fallback)
    /// </summary>
    public static double MeasureWidthSampling(ContourInfo contour)
    {
        if (contour.Points.Count < 4) return 0;

        int sampleCount = Math.Min(16, contour.Points.Count);
        double totalWidth = 0;
        int validSamples = 0;

        for (int i = 0; i < sampleCount; i++)
        {
            int idx = (int)((double)i / sampleCount * contour.Points.Count);
            var p = contour.Points[idx];

            // Noktanin karsisindaki en yakin noktayi bul
            double minDist = double.MaxValue;
            for (int j = 0; j < contour.Points.Count; j++)
            {
                if (Math.Abs(j - idx) < contour.Points.Count / 8) continue;
                double dist = p.DistanceTo(contour.Points[j]);
                if (dist < minDist)
                    minDist = dist;
            }

            if (minDist < double.MaxValue && minDist > 0)
            {
                totalWidth += minDist;
                validSamples++;
            }
        }

        if (validSamples == 0) return 0;

        double avgPixels = totalWidth / validSamples;
        return avgPixels * 0.264; // Piksel -> mm
    }

    /// <summary>
    /// Genislik dagilimina gore dikiş parametrelerini belirle
    /// </summary>
    public static StitchParams GetStitchParams(double averageWidth, ClassifyOptions? options = null)
    {
        options ??= new ClassifyOptions();

        if (averageWidth < options.SatinMaxWidth)
        {
            // SATIN - ince kolon, zigzag
            return new StitchParams
            {
                Type = StitchStyle.Satin,
                StitchLength = 2.5,
                Density = Math.Max(0.3, averageWidth * 0.1), // Genislige orantili
                Angle = 0,
                UnderlayType = UnderlayType.CenterRun
            };
        }
        else
        {
            // TATAMI - genis alan, paralel satirlar
            return new StitchParams
            {
                Type = StitchStyle.Tatami,
                StitchLength = 5.0,
                Density = 0.4,
                Angle = 45,
                UnderlayType = UnderlayType.DoubleRun
            };
        }
    }
}

public class ClassifyOptions
{
    /// <summary>Satin icin maksimum genislik (mm)</summary>
    public double SatinMaxWidth { get; set; } = 10.0;

    /// <summary>Run icin maksimum genislik (mm)</summary>
    public double RunMaxWidth { get; set; } = 2.0;

    /// <summary>Dolgu icin minimum alan (mm^2)</summary>
    public double MinAreaForFill { get; set; } = 50.0;
}

public enum StitchStyle
{
    Run,     // Tek hat dikişi
    Satin,   // Zigzag (ince kolonlar)
    Tatami   // Dolgu (genis alanlar)
}

public enum UnderlayType
{
    None,
    CenterRun,
    EdgeRun,
    DoubleRun,
    ZigzagUnderlay
}

public class StitchParams
{
    public StitchStyle Type { get; set; }
    public double StitchLength { get; set; } = 2.5;
    public double Density { get; set; } = 0.4;
    public double Angle { get; set; } = 0;
    public UnderlayType UnderlayType { get; set; }
}
