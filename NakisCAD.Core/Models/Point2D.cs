namespace NakisCAD.Core.Models;

/// <summary>
/// 2B nokta - milimetrik koordinatlarda
/// </summary>
public struct Point2D : IEquatable<Point2D>
{
    public double X { get; set; }
    public double Y { get; set; }

    public Point2D(double x, double y)
    {
        X = x;
        Y = y;
    }

    public static Point2D Zero => new(0, 0);

    public double DistanceTo(Point2D other)
    {
        double dx = X - other.X;
        double dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public static Point2D operator +(Point2D a, Point2D b) => new(a.X + b.X, a.Y + b.Y);
    public static Point2D operator -(Point2D a, Point2D b) => new(a.X - b.X, a.Y - b.Y);
    public static Point2D operator *(Point2D p, double s) => new(p.X * s, p.Y * s);
    public static Point2D operator *(double s, Point2D p) => new(p.X * s, p.Y * s);

    public bool Equals(Point2D other) =>
        Math.Abs(X - other.X) < 1e-6 && Math.Abs(Y - other.Y) < 1e-6;

    public override bool Equals(object? obj) => obj is Point2D p && Equals(p);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public override string ToString() => $"({X:F3}, {Y:F3})";

    public static bool operator ==(Point2D left, Point2D right) => left.Equals(right);
    public static bool operator !=(Point2D left, Point2D right) => !left.Equals(right);
}
