using NakisCAD.Core.DST;
using NakisCAD.Core.Models;
using NakisCAD.Core.ImageProcessing;
using OpenCvSharp;

namespace NakisCAD.Core.Digitizing;

public class DigitizingPipeline
{
    public DigitizingOptions Options { get; set; } = new();
    public event Action<string>? OnProgress;

    public EmbroideryDesign Process(string imagePath)
    {
        Log("=== OTOMATIK DIGITIZING BASLADI ===");
        Log("1. Goruntu yukleniyor...");
        var input = Cv2.ImRead(imagePath, ImreadModes.Color);
        if (input.Empty())
            throw new FileNotFoundException("Goruntu yuklenemedi: " + imagePath);

        Log($"   Orijinal boyut: {input.Width}x{input.Height} piksel");
        var result = ProcessImage(input);
        input.Dispose();
        return result;
    }

    public EmbroideryDesign ProcessImage(Mat input)
    {
        int imgHeight = input.Height;

        // ===== 1. ARKA PLAN TEMIZLEME (Bug #2 cozumu) =====
        Log("2. Arka plan temizleniyor...");
        var bgMask = CreateBackgroundMask(input);
        var bgRemoved = new Mat();
        input.CopyTo(bgRemoved, bgMask);

        // ===== 2. HEDEF BOYUTA OLCEKLE =====
        double targetWidthMm = Options.TargetWidthMm;
        double targetHeightMm = Options.TargetHeightMm;
        double imgAspect = (double)bgRemoved.Width / bgRemoved.Height;
        double targetAspect = targetWidthMm / targetHeightMm;

        double finalWidthMm, finalHeightMm;
        if (imgAspect > targetAspect)
        {
            finalWidthMm = targetWidthMm;
            finalHeightMm = targetWidthMm / imgAspect;
        }
        else
        {
            finalHeightMm = targetHeightMm;
            finalWidthMm = targetHeightMm * imgAspect;
        }

        double mmToPixel = Options.ResolutionPxPerMm;
        int targetWidthPx = (int)(finalWidthMm * mmToPixel);
        int targetHeightPx = (int)(finalHeightMm * mmToPixel);

        Log($"3. Olcekleniyor: {targetWidthPx}x{targetHeightPx} piksel = {finalWidthMm:F1}x{finalHeightMm:F1}mm");

        var scaled = new Mat();
        Cv2.Resize(bgRemoved, scaled, new Size(targetWidthPx, targetHeightPx), 0, 0, InterpolationFlags.Area);

        // ===== 3. RENK KUANTIZASYONU =====
        Log($"4. Renk kuantizasyonu ({Options.ColorCount} renk)...");
        var quantizeResult = ColorQuantizer.Quantize(scaled, Options.ColorCount);

        var foregroundColors = quantizeResult.Palette
            .Where(c => !IsBackgroundColor(c))
            .ToList();

        Log($"   {foregroundColors.Count} on plan rengi tespit edildi");
        int totalPixels = quantizeResult.Width * quantizeResult.Height;
        foreach (var color in foregroundColors)
        {
            double pct = (double)color.PixelCount / totalPixels * 100;
            color.Percentage = pct;
            Log($"   RGB({color.RGB.R},{color.RGB.G},{color.RGB.B}) - %{pct:F1}");
        }

        // ===== 4. HER RENK ICIN KONTURLAR + DOLGU =====
        var allStitches = new List<StitchCommand>();
        var palette = new List<ColorRGB>();
        Point2D lastExitPoint = new Point2D(0, 0);

        foreach (var color in foregroundColors)
        {
            if (color.Percentage < Options.MinColorPercentage)
                continue;

            var mask = ColorQuantizer.CreateColorMask(quantizeResult.QuantizedImage, color);

            Cv2.FindContours(mask, out var contours, out var hierarchy,
                RetrievalModes.Tree, ContourApproximationModes.ApproxSimple);

            if (contours.Length == 0)
            {
                mask.Dispose();
                continue;
            }

            Log($"5.{palette.Count + 1} Renk RGB({color.RGB.R},{color.RGB.G},{color.RGB.B}): {contours.Length} kontur");

            if (allStitches.Count > 0)
                allStitches.Add(new StitchCommand(0, 0, StitchType.ColorChange));

            var sortedContours = SortContoursByProximity(contours, lastExitPoint, hierarchy);

            foreach (var contour in sortedContours)
            {
                double area = Cv2.ContourArea(contour);
                if (area < 50) continue;

                // Konturu simplify et
                double epsilon = Options.SimplifyTolerance * Cv2.ArcLength(contour, true) / 100.0;
                if (epsilon < 0.5) epsilon = 0.5;
                var simplified = Cv2.ApproxPolyDP(contour, epsilon, true);

                // MM koordinatlarina cevir + Y EKSENI TERSLEY (Bug #3 cozumu)
                var mmPoints = simplified
                    .Select(p => new Point2D(
                        p.X / mmToPixel,
                        finalHeightMm - (p.Y / mmToPixel)))  // Y = H - Y
                    .ToList();

                if (mmPoints.Count < 3) continue;

                var mmContour = new ContourInfo
                {
                    Points = mmPoints,
                    Area = area / (mmToPixel * mmToPixel),
                    IsClosed = true
                };

                double width = MeasureContourWidth(mmContour);
                var stitchParams = GetStitchParams(width);

                // Entry point + jump
                var entryPoint = mmPoints[0];
                short jumpDx = (short)Math.Round((entryPoint.X - lastExitPoint.X) * DST_SCALE);
                short jumpDy = (short)Math.Round((entryPoint.Y - lastExitPoint.Y) * DST_SCALE);

                if (Math.Abs(jumpDx) > 1 || Math.Abs(jumpDy) > 1)
                    allStitches.Add(new StitchCommand(jumpDx, jumpDy, StitchType.Jump));

                // Dikis uret
                var stitchCommands = StitchGenerator.Generate(
                    mmContour, stitchParams.Type, stitchParams.StitchLength,
                    stitchParams.Density, stitchParams.Angle, mmToPixel);
                allStitches.AddRange(stitchCommands);

                lastExitPoint = mmPoints[^1];
                Log($"   Alan={mmContour.Area:F1}mm2, Genislik={width:F1}mm -> {stitchParams.Type}");
            }

            palette.Add(new ColorRGB(color.RGB.R, color.RGB.G, color.RGB.B));
            mask.Dispose();
        }

        // ===== 5. BITIS =====
        allStitches.Add(new StitchCommand(0, 0, StitchType.End));

        // ===== 6. ISTATISTIKLER =====
        int normalCount = allStitches.Count(s => s.Type == StitchType.Normal);
        int jumpCount = allStitches.Count(s => s.Type == StitchType.Jump);
        int colorChangeCount = allStitches.Count(s => s.Type == StitchType.ColorChange);

        Log($"=== PIPELINE TAMAMLANDI ===");
        Log($"Toplam dikiş: {normalCount:N0}");
        Log($"Atlama (Jump): {jumpCount:N0}");
        Log($"Renk degisimi: {colorChangeCount}");

        var design = new EmbroideryDesign
        {
            Name = "AutoDigitized",
            Stitches = allStitches,
            ColorPalette = palette
        };

        for (int i = 0; i < allStitches.Count; i++)
            if (allStitches[i].Type == StitchType.ColorChange)
                design.ColorChangeIndices.Add(i);

        // Boyut hesabi (sadece normal dikişler)
        var normalPoints = new List<Point2D>();
        var currentPos = new Point2D(0, 0);
        foreach (var cmd in allStitches)
        {
            if (cmd.Type == StitchType.Normal || cmd.Type == StitchType.Jump)
            {
                currentPos = new Point2D(currentPos.X + cmd.DeltaX / DST_SCALE, currentPos.Y + cmd.DeltaY / DST_SCALE);
                if (cmd.Type == StitchType.Normal)
                    normalPoints.Add(currentPos);
            }
        }
        if (normalPoints.Count > 0)
        {
            design.WidthMm = normalPoints.Max(p => p.X) - normalPoints.Min(p => p.X);
            design.HeightMm = normalPoints.Max(p => p.Y) - normalPoints.Min(p => p.Y);
        }

        Log($"Boyut: {design.WidthMm:F1} x {design.HeightMm:F1} mm");

        scaled.Dispose();
        bgRemoved.Dispose();
        bgMask.Dispose();
        quantizeResult.QuantizedImage.Dispose();

        return design;
    }

    /// <summary>
    /// Arka plan maskesi olustur - Beyaz arka plan kapat
    /// </summary>
    private Mat CreateBackgroundMask(Mat input)
    {
        // 1. Beyaz arka planı sil (Beyaz pikselleri arka plan olarak say)
        var hsv = new Mat();
        Cv2.CvtColor(input, hsv, ColorConversionCodes.BGR2HSV);

        var bgMask = new Mat();
        // Beyaz: Hue=0, Saturation=0, Value>=200
        var lowerWhite = new Scalar(0, 0, 200);
        var upperWhite = new Scalar(180, 255, 255);
        Cv2.InRange(hsv, lowerWhite, upperWhite, bgMask);

        // 2. Arka plan'ı gizle (hata)
        var fgMask = new Mat();
        Cv2.BitwiseNot(bgMask, fgMask);

        // 3. Histogram ile daha iyi arka plan tespit
        var small = new Mat();
        Cv2.Resize(input, small, new Size(200, 200));
        var smallHsv = new Mat();
        Cv2.CvtColor(small, smallHsv, ColorConversionCodes.BGR2HSV);

        var hist = new Mat();
        Cv2.CalcHist(new[] { smallHsv }, new[] { 0 }, null, hist, 1,
            new[] { 180 }, new[] { new Rangef(0, 180) });

        Cv2.MinMaxLoc(hist, out _, out _, out _, out var maxLoc);
        int bgHue = maxLoc.Y;

        // 4. Siyah piksellerini ve arka plan rengini birlikte silsin
        var histBgMask = new Mat();
        Cv2.InRange(hsv, new Scalar(Math.Max(0, bgHue - 10), 20, 20),
            new Scalar(Math.Min(179, bgHue + 10), 255, 255), histBgMask);

        // Her iki maskeyi birleştir (Beyaz + Siyah = arka plan)
        var combined = new Mat();
        Cv2.BitwiseOr(bgMask, histBgMask, combined);

        // Tam arka planı al (yani bgMask'in tam karşılığı)
        var finalMask = new Mat();
        Cv2.BitwiseNot(combined, finalMask);

        // Morfolojik temizlik
        var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(5, 5));
        Cv2.MorphologyEx(finalMask, finalMask, MorphTypes.Open, kernel, iterations: 2);
        Cv2.MorphologyEx(finalMask, finalMask, MorphTypes.Close, kernel, iterations: 1);

        hsv.Dispose();
        bgMask.Dispose();
        fgMask.Dispose();
        small.Dispose();
        smallHsv.Dispose();
        hist.Dispose();
        histBgMask.Dispose();
        combined.Dispose();
        finalMask.Dispose();
        kernel.Dispose();

        Log("   Arka plan temizlendi");
        return finalMask;
    }

    private bool IsBackgroundColor(QuantizedColor color)
    {
        byte brightness = (byte)((color.RGB.R + color.RGB.G + color.RGB.B) / 3);
        return brightness < 30;
    }

    private List<OpenCvSharp.Point[]> SortContoursByProximity(
        OpenCvSharp.Point[][] contours, Point2D startPoint, HierarchyIndex[] hierarchy)
    {
        if (contours.Length <= 1)
            return contours.ToList();

        var remaining = contours.Select(c => (contour: c, center: GetCentroid(c))).ToList();
        var sorted = new List<OpenCvSharp.Point[]>();
        var current = startPoint;

        while (remaining.Count > 0)
        {
            int nearestIdx = 0;
            double nearestDist = double.MaxValue;
            for (int i = 0; i < remaining.Count; i++)
            {
                double dist = current.DistanceTo(remaining[i].center);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearestIdx = i;
                }
            }
            sorted.Add(remaining[nearestIdx].contour);
            current = remaining[nearestIdx].center;
            remaining.RemoveAt(nearestIdx);
        }
        return sorted;
    }

    private Point2D GetCentroid(OpenCvSharp.Point[] contour)
    {
        double cx = 0, cy = 0;
        foreach (var p in contour) { cx += p.X; cy += p.Y; }
        return new Point2D(cx / contour.Length, cy / contour.Length);
    }

    private double MeasureContourWidth(ContourInfo contour)
    {
        if (contour.Points.Count < 4) return 0;
        int sampleCount = Math.Min(8, contour.Points.Count);
        double totalWidth = 0;
        int validSamples = 0;

        for (int i = 0; i < sampleCount; i++)
        {
            int idx = (int)((double)i / sampleCount * contour.Points.Count);
            var p = contour.Points[idx];
            double minDist = double.MaxValue;
            for (int j = 0; j < contour.Points.Count; j++)
            {
                if (Math.Abs(j - idx) < contour.Points.Count / 6) continue;
                double dist = p.DistanceTo(contour.Points[j]);
                if (dist < minDist) minDist = dist;
            }
            if (minDist < double.MaxValue && minDist > 0)
            {
                totalWidth += minDist;
                validSamples++;
            }
        }
        return validSamples > 0 ? totalWidth / validSamples : 0;
    }

    private StitchParams GetStitchParams(double widthMm)
    {
        if (widthMm < 8)
        {
            return new StitchParams
            {
                Type = StitchStyle.Satin,
                StitchLength = 2.5,
                Density = Math.Max(0.3, widthMm * 0.1),
                Angle = 0
            };
        }
        else if (widthMm < 20)
        {
            return new StitchParams
            {
                Type = StitchStyle.Satin,
                StitchLength = 3.0,
                Density = 0.5,
                Angle = 0
            };
        }
        else
        {
            return new StitchParams
            {
                Type = StitchStyle.Tatami,
                StitchLength = 4.0,
                Density = 0.4,
                Angle = 45
            };
        }
    }

    private void Log(string message)
    {
        OnProgress?.Invoke(message);
        System.Diagnostics.Debug.WriteLine(message);
    }

    private const double DST_SCALE = 10.0;
}

public class DigitizingOptions
{
    public double TargetWidthMm { get; set; } = 100.0;
    public double TargetHeightMm { get; set; } = 100.0;
    public double ResolutionPxPerMm { get; set; } = 8.0;
    public int ColorCount { get; set; } = 6;
    public double SimplifyTolerance { get; set; } = 1.0;
    public double MinColorPercentage { get; set; } = 2.0;
    public double JumpThresholdMm { get; set; } = 5.0;
    public double MinStitchLengthMm { get; set; } = 0.4;
    public ClassifyOptions Classify { get; set; } = new();
}
