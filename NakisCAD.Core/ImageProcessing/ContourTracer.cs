using OpenCvSharp;
using NakisCAD.Core.Models;

namespace NakisCAD.Core.ImageProcessing;

/// <summary>
/// Kontur izleme: findContours + hierarchy + RDP simplifikasyon
/// </summary>
public static class ContourTracer
{
    /// <summary>
    /// Tek renk maskesinden konturlari cikar (dis ve ic konturler)
    /// </summary>
    public static ContourResult Trace(Mat mask, double simplifyTolerance = 1.0)
    {
        var result = new ContourResult();

        // Bulaniklik ve threshold
        var blurred = new Mat();
        Cv2.GaussianBlur(mask, blurred, new Size(3, 3), 0);

        // Konturlari bul - hierarchy ile
        Cv2.FindContours(blurred, out var contours, out var hierarchy,
            RetrievalModes.Tree, ContourApproximationModes.ApproxSimple);

        if (contours.Length == 0)
        {
            blurred.Dispose();
            return result;
        }

        // Hierarchy'den dis/ic konturleri ayir
        // hierarchy[i] = [next, prev, child, parent]
        for (int i = 0; i < contours.Length; i++)
        {
            var contour = contours[i];
            double area = Cv2.ContourArea(contour);

            // Cok kucuk konturlari atla
            if (area < 20) continue;

            // RDP simplification
            double epsilon = simplifyTolerance * Cv2.ArcLength(contour, true) / 100.0;
            if (epsilon < 0.5) epsilon = 0.5;
            var simplified = Cv2.ApproxPolyDP(contour, epsilon, true);

            // Bounding rect
            var boundingRect = Cv2.BoundingRect(simplified);

            // Hierarchical bilgi
            int parentIdx = hierarchy[i].Parent;
            int childIdx = hierarchy[i].Child;
            bool isOuter = parentIdx < 0; // Root = dis kontur

            var contourInfo = new ContourInfo
            {
                Points = simplified.Select(p => new Point2D(p.X, p.Y)).ToList(),
                Area = area,
                IsClosed = true,
                BoundingRect = boundingRect,
                HierarchyLevel = isOuter ? 0 : 1,
                OpenCvPoints = simplified
            };

            if (isOuter)
            {
                result.OuterContours.Add(contourInfo);
            }
            else
            {
                result.InnerContours.Add(contourInfo);
            }

            result.AllContours.Add(contourInfo);
        }

        // Dis konturlere ic konturleri bagla (parent-child iliskisi)
        foreach (var outer in result.OuterContours)
        {
            foreach (var inner in result.InnerContours)
            {
                // Ic kontur, dis konturun bounding rect'inde mi?
                if (outer.BoundingRect.Contains(inner.BoundingRect.X, inner.BoundingRect.Y))
                {
                    // Point-in-polygon testi
                    if (IsPointInsidePolygon(inner.Points[0], outer.Points))
                    {
                        outer.Holes.Add(inner);
                    }
                }
            }
        }

        blurred.Dispose();
        return result;
    }

    /// <summary>
    /// Bir noktanin polygon icinde olup olmadigini kontrol et (Ray casting)
    /// </summary>
    private static bool IsPointInsidePolygon(Point2D point, List<Point2D> polygon)
    {
        if (polygon.Count < 3) return false;

        bool inside = false;
        int j = polygon.Count - 1;

        for (int i = 0; i < polygon.Count; i++)
        {
            if ((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y) &&
                point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) /
                          (polygon[j].Y - polygon[i].Y) + polygon[i].X)
            {
                inside = !inside;
            }
            j = i;
        }

        return inside;
    }

    /// <summary>
    /// Renkli goruntuden belirli bir renk icin mask + kontur cikar
    /// </summary>
    public static ContourResult TraceColor(Mat input, QuantizedColor color, double simplifyTolerance = 1.0)
    {
        var mask = ColorQuantizer.CreateColorMask(input, color);
        var result = Trace(mask, simplifyTolerance);
        mask.Dispose();
        return result;
    }
}

public class ContourResult
{
    public List<ContourInfo> OuterContours { get; set; } = new();
    public List<ContourInfo> InnerContours { get; set; } = new();
    public List<ContourInfo> AllContours { get; set; } = new();
}

public class ContourInfo
{
    public List<Point2D> Points { get; set; } = new();
    public double Area { get; set; }
    public bool IsClosed { get; set; }
    public OpenCvSharp.Rect BoundingRect { get; set; }
    public int HierarchyLevel { get; set; }
    public List<ContourInfo> Holes { get; set; } = new();
    public OpenCvSharp.Point[]? OpenCvPoints { get; set; }

    /// <summary>Ortalama genislik (mm)</summary>
    public double AverageWidth => Math.Min(BoundingRect.Width, BoundingRect.Height);
}
