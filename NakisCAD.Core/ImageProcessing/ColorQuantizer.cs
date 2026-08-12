using OpenCvSharp;

namespace NakisCAD.Core.ImageProcessing;

/// <summary>
/// K-Means ile renk kuantizasyonu - dogru Mat reshape kullanimi
/// </summary>
public static class ColorQuantizer
{
    public static QuantizeResult Quantize(Mat input, int colorCount = 8)
    {
        int width = input.Width;
        int height = input.Height;

        // Goruntuyu 32FC3'e donustur ve yeniden boyutlandir (N x 3 matris)
        Mat samples = new Mat();
        if (input.Channels() == 1)
        {
            var bgr = new Mat();
            Cv2.CvtColor(input, bgr, ColorConversionCodes.GRAY2BGR);
            bgr.ConvertTo(samples, MatType.CV_32FC3);
            bgr.Dispose();
        }
        else
        {
            input.ConvertTo(samples, MatType.CV_32FC3);
        }

        // N x 3 matrisine yeniden sekillendir (her satir bir piksel: B G R)
        Mat reshaped = samples.Reshape(3, width * height); // 3 kanal, N satir

        // K-Means calistir
        var labelsMat = new Mat();
        var centersMat = new Mat();
        var termCriteria = new TermCriteria(CriteriaTypes.Eps | CriteriaTypes.MaxIter, 100, 1.0);

        Cv2.Kmeans(
            reshaped,
            colorCount,
            labelsMat,
            termCriteria,
            10,
            KMeansFlags.RandomCenters,
            centersMat
        );

        // Labels'int[] olarak oku
        int pixelCount = width * height;
        var labels = new int[pixelCount];
        for (int i = 0; i < pixelCount; i++)
            labels[i] = labelsMat.At<int>(i);

        // Renk merkezlerini oku ve BGR'ye donustur
        var palette = new List<QuantizedColor>();
        var colorPixelCounts = new int[colorCount];

        for (int i = 0; i < pixelCount; i++)
            colorPixelCounts[labels[i]]++;

        for (int i = 0; i < colorCount; i++)
        {
            float b = centersMat.At<float>(i, 0);
            float g = centersMat.At<float>(i, 1);
            float r = centersMat.At<float>(i, 2);

            byte bByte = (byte)Math.Clamp(b, 0, 255);
            byte gByte = (byte)Math.Clamp(g, 0, 255);
            byte rByte = (byte)Math.Clamp(r, 0, 255);

            palette.Add(new QuantizedColor
            {
                Index = i,
                BGR = new Vec3b(bByte, gByte, rByte),
                RGB = System.Drawing.Color.FromArgb(rByte, gByte, bByte),
                Lab = new Vec3b(0, 0, 0),
                PixelCount = colorPixelCounts[i]
            });
        }

        // Piksel sayisina gore sirala
        palette = palette.OrderByDescending(c => c.PixelCount).ToList();
        for (int i = 0; i < palette.Count; i++)
            palette[i].SortedIndex = i;

        // Kuantize edilmis goruntu olustur
        var quantized = new Mat(height, width, MatType.CV_8UC3);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int label = labels[y * width + x];
                var bgr = palette.First(p => p.Index == label).BGR;
                quantized.Set(y, x, bgr);
            }
        }

        // Temizlik
        samples.Dispose();
        reshaped.Dispose();
        labelsMat.Dispose();
        centersMat.Dispose();

        return new QuantizeResult
        {
            QuantizedImage = quantized,
            Palette = palette,
            Labels = labels,
            Width = width,
            Height = height
        };
    }

    public static Mat CreateColorMask(Mat quantized, QuantizedColor color, int tolerance = 10)
    {
        var mask = new Mat();
        var lower = new Scalar(
            Math.Max(0, color.BGR.Item0 - tolerance),
            Math.Max(0, color.BGR.Item1 - tolerance),
            Math.Max(0, color.BGR.Item2 - tolerance));
        var upper = new Scalar(
            Math.Min(255, color.BGR.Item0 + tolerance),
            Math.Min(255, color.BGR.Item1 + tolerance),
            Math.Min(255, color.BGR.Item2 + tolerance));
        Cv2.InRange(quantized, lower, upper, mask);

        // Morfolojik temizleme
        var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel, iterations: 2);
        Cv2.MorphologyEx(mask, mask, MorphTypes.Open, kernel, iterations: 1);
        kernel.Dispose();

        return mask;
    }
}

public class QuantizeResult
{
    public Mat QuantizedImage { get; set; } = new();
    public List<QuantizedColor> Palette { get; set; } = new();
    public int[] Labels { get; set; } = Array.Empty<int>();
    public int Width { get; set; }
    public int Height { get; set; }
}

public class QuantizedColor
{
    public int Index { get; set; }
    public int SortedIndex { get; set; }
    public Vec3b BGR { get; set; }
    public Vec3b Lab { get; set; }
    public System.Drawing.Color RGB { get; set; }
    public int PixelCount { get; set; }
    public double Percentage { get; set; }
}
