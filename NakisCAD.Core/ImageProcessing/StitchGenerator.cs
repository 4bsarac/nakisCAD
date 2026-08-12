using NakisCAD.Core.Models;
using OpenCvSharp;

namespace NakisCAD.Core.ImageProcessing;

public static class StitchGenerator
{
    private const double DST_SCALE = 10.0;
    private const double MIN_STITCH_MM = 0.4;
    private const double MAX_STITCH_MM = 12.1;

    public static List<StitchCommand> Generate(
        ContourInfo contour,
        StitchStyle style,
        double stitchLengthMm = 2.5,
        double densityMm = 0.4,
        double angleDeg = 0,
        double pxPerMm = 8.0)
    {
        switch (style)
        {
            case StitchStyle.Run:
                return GenerateRun(contour, stitchLengthMm);
            case StitchStyle.Satin:
                return GenerateSatin(contour, stitchLengthMm, densityMm, pxPerMm);
            case StitchStyle.Tatami:
                return GenerateTatami(contour, stitchLengthMm, densityMm, angleDeg, pxPerMm);
        }
        return new List<StitchCommand>();
    }

    private static List<StitchCommand> GenerateRun(ContourInfo contour, double stitchLengthMm)
    {
        var commands = new List<StitchCommand>();
        var points = contour.Points;
        if (points.Count < 2) return commands;

        double step = Math.Max(MIN_STITCH_MM, Math.Min(stitchLengthMm, MAX_STITCH_MM));
        var prev = points[0];

        for (int i = 1; i < points.Count; i++)
        {
            var from = prev;
            var to = points[i];
            double dist = from.DistanceTo(to);

            if (dist <= step)
            {
                AddStitch(commands, from, to);
                prev = to;
            }
            else
            {
                int segments = (int)Math.Ceiling(dist / step);
                for (int s = 1; s <= segments; s++)
                {
                    double t = (double)s / segments;
                    var mid = new Point2D(
                        from.X + (to.X - from.X) * t,
                        from.Y + (to.Y - from.Y) * t);
                    AddStitch(commands, prev, mid);
                    prev = mid;
                }
            }
        }
        return commands;
    }

    // ===================== TATAMI (Pixel-level mask scan) =====================
    private static List<StitchCommand> GenerateTatami(
        ContourInfo contour, double stitchLengthMm, double densityMm,
        double angleDeg, double pxPerMm)
    {
        var commands = new List<StitchCommand>();
        var points = contour.Points;
        if (points.Count < 3) return commands;

        double rowSpacing = Math.Max(MIN_STITCH_MM, densityMm);
        double stitchStep = Math.Max(MIN_STITCH_MM, Math.Min(stitchLengthMm, MAX_STITCH_MM));
        double angleRad = angleDeg * Math.PI / 180.0;

        var fillDir = new Point2D(Math.Cos(angleRad), Math.Sin(angleRad));
        var fillNormal = new Point2D(-Math.Sin(angleRad), Math.Cos(angleRad));

        double minX = points.Min(p => p.X);
        double maxX = points.Max(p => p.X);
        double minY = points.Min(p => p.Y);
        double maxY = points.Max(p => p.Y);

        int margin = 2;
        int imgW = (int)((maxX - minX) * pxPerMm) + margin * 2;
        int imgH = (int)((maxY - minY) * pxPerMm) + margin * 2;
        if (imgW < 3 || imgH < 3) return commands;

        var cvPoints = points.Select(p =>
            new OpenCvSharp.Point(
                (int)((p.X - minX) * pxPerMm) + margin,
                (int)((p.Y - minY) * pxPerMm) + margin
            )).ToArray();

        using var mask = new Mat(imgH, imgW, MatType.CV_8UC1, Scalar.All(0));
        Cv2.DrawContours(mask, new[] { cvPoints }, 0, Scalar.All(255), -1);

        var maskData = mask.GetData();

        var allRows = new List<(double perp, List<(double x1, double x2)> segs)>();

        for (int row = 0; row < imgH; row++)
        {
            double perpY = row / pxPerMm;

            var segs = new List<(double x1, double x2)>();

            int x = 0;
            while (x < imgW)
            {
                if (maskData[row * imgW + x] > 0)
                {
                    int start = x;
                    while (x < imgW && maskData[row * imgW + x] > 0)
                        x++;
                    if (x - start > MIN_STITCH_MM)
                        segs.Add((start, x));
                }
                else
                    x++;
            }

            if (segs.Count == 0) continue;

            allRows.Add((perpY, segs));
        }

        bool forward = true;
        var prevPoint = new Point2D(double.NaN, double.NaN);

        foreach (var (perpVal, rowSegments) in allRows)
        {
            if (forward)
                rowSegments.Sort((a, b) => a.x1.CompareTo(b.x1));
            else
                rowSegments.Sort((a, b) => b.x1.CompareTo(a.x1));

            foreach (var seg in rowSegments)
            {
                double segStartX = seg.x1;
                double segEndX = seg.x2;

                var segStart = new Point2D(segStartX, perpVal);
                var segEnd = new Point2D(segEndX, perpVal);

                if (!double.IsNaN(prevPoint.X))
                {
                    double jumpDist = prevPoint.DistanceTo(segStart);
                    if (jumpDist > stitchStep * 3)
                    {
                        AddStitch(commands, prevPoint, segStart, true);
                        prevPoint = segStart;
                    }
                    else if (jumpDist > 0.1)
                    {
                        AddStitch(commands, prevPoint, segStart);
                        prevPoint = segStart;
                    }
                }

                double segLen = segStart.DistanceTo(segEnd);
                if (segLen < MIN_STITCH_MM)
                {
                    if (segLen > 0.1)
                        AddStitch(commands, segStart, segEnd);
                    prevPoint = segEnd;
                    continue;
                }

                int stitchCount = Math.Max(2, (int)Math.Round(segLen / stitchStep));

                for (int s = 0; s < stitchCount; s++)
                {
                    double t1 = (double)s / stitchCount;
                    double t2 = (double)(s + 1) / stitchCount;

                    var p1 = new Point2D(
                        segStart.X + (segEnd.X - segStart.X) * t1,
                        segStart.Y + (segEnd.Y - segStart.Y) * t1);
                    var p2 = new Point2D(
                        segStart.X + (segEnd.X - segStart.X) * t2,
                        segStart.Y + (segEnd.Y - segStart.Y) * t2);

                    AddStitch(commands, p1, p2);
                    prevPoint = p2;
                }
            }

            forward = !forward;
        }

        return commands;
    }

    // ===================== SATIN (Proper rail interpolation) =====================
    private static List<StitchCommand> GenerateSatin(
        ContourInfo contour, double stitchLengthMm, double densityMm, double pxPerMm)
    {
        var commands = new List<StitchCommand>();
        var points = contour.Points;
        if (points.Count < 4) return commands;

        double zigzagSpacing = Math.Max(MIN_STITCH_MM, densityMm);

        // En uzun kenarı bul
        int splitIdx = 0;
        double maxEdgeLen = 0;
        for (int i = 0; i < points.Count; i++)
        {
            int j = (i + 1) % points.Count;
            double len = points[i].DistanceTo(points[j]);
            if (len > maxEdgeLen)
            {
                maxEdgeLen = len;
                splitIdx = i;
            }
        }

        int halfCount = points.Count / 2;
        var leftRail = new List<Point2D>();
        var rightRail = new List<Point2D>();

        for (int i = 0; i <= halfCount; i++)
            leftRail.Add(points[(splitIdx + i) % points.Count]);

        for (int i = 0; i <= halfCount; i++)
            rightRail.Add(points[(splitIdx + halfCount + i) % points.Count]);

        rightRail.Reverse();

        if (leftRail.Count < 2 || rightRail.Count < 2)
            return GenerateSimpleZigzag(contour, stitchLengthMm);

        double leftLen = GetPathLength(leftRail);
        double rightLen = GetPathLength(rightRail);
        double maxLen = Math.Max(leftLen, rightLen);
        if (maxLen < MIN_STITCH_MM) return commands;

        int zigCount = Math.Max(2, (int)Math.Round(maxLen / zigzagSpacing));

        for (int i = 0; i < zigCount; i++)
        {
            double t = (double)i / (zigCount - 1);
            var leftPt = InterpolatePath(leftRail, t);
            var rightPt = InterpolatePath(rightRail, t);

            if (i % 2 == 0)
                AddStitch(commands, leftPt, rightPt);
            else
                AddStitch(commands, rightPt, leftPt);
        }

        return commands;
    }

    private static List<StitchCommand> GenerateSimpleZigzag(ContourInfo contour, double stitchLengthMm)
    {
        var commands = new List<StitchCommand>();
        var points = contour.Points;
        if (points.Count < 4) return commands;

        for (int i = 0; i < points.Count - 1; i += 2)
        {
            var p1 = points[i];
            var p2 = points[Math.Min(i + 1, points.Count - 1)];
            AddStitch(commands, p1, p2);
        }
        return commands;
    }

    // ===================== YARDIMCI =====================
    private static double GetPathLength(List<Point2D> path)
    {
        double length = 0;
        for (int i = 1; i < path.Count; i++)
            length += path[i - 1].DistanceTo(path[i]);
        return length;
    }

    private static Point2D InterpolatePath(List<Point2D> path, double t)
    {
        if (path.Count < 2) return path[0];
        double totalLength = GetPathLength(path);
        double targetDist = t * totalLength;
        double accumulated = 0;

        for (int i = 1; i < path.Count; i++)
        {
            double segLen = path[i - 1].DistanceTo(path[i]);
            if (accumulated + segLen >= targetDist)
            {
                double localT = segLen > 0 ? (targetDist - accumulated) / segLen : 0;
                return new Point2D(
                    path[i - 1].X + (path[i].X - path[i - 1].X) * localT,
                    path[i - 1].Y + (path[i].Y - path[i - 1].Y) * localT);
            }
            accumulated += segLen;
        }
        return path[^1];
    }

    private static void AddStitch(List<StitchCommand> commands, Point2D from, Point2D to, bool isJump = false)
    {
        short dx = (short)Math.Round((to.X - from.X) * DST_SCALE);
        short dy = (short)Math.Round((to.Y - from.Y) * DST_SCALE);

        if (dx == 0 && dy == 0) return;

        var stitchType = isJump ? StitchType.Jump : StitchType.Normal;

        if (Math.Abs(dx) > 121 || Math.Abs(dy) > 121)
        {
            int steps = Math.Max(
                (int)Math.Ceiling(Math.Abs(dx) / 121.0),
                (int)Math.Ceiling(Math.Abs(dy) / 121.0));

            for (int i = 0; i < steps; i++)
            {
                short stepDx = (short)(dx / steps);
                short stepDy = (short)(dy / steps);
                if (stepDx != 0 || stepDy != 0)
                    commands.Add(new StitchCommand(stepDx, stepDy, stitchType));
            }
        }
        else
        {
            commands.Add(new StitchCommand(dx, dy, stitchType));
        }
    }
}