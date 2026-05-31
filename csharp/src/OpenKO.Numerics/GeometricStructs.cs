namespace OpenKO.Numerics;

/// <summary>Port of <c>_SIZE</c> (MathUtils/GeometricStructs.h) — replacement for MFC CSize.</summary>
public struct Size
{
    public int Cx;
    public int Cy;

    public Size(int cx, int cy)
    {
        Cx = cx;
        Cy = cy;
    }
}

/// <summary>Port of <c>_POINT</c> — replacement for MFC CPoint.</summary>
public struct Point
{
    public int X;
    public int Y;

    public Point(int x, int y)
    {
        X = x;
        Y = y;
    }
}

/// <summary>Port of <c>_RECT</c> — replacement for MFC CRect.</summary>
public struct Rect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public Rect(int left, int top, int right, int bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public readonly bool Contains(Point p)
        => p.X >= Left && p.X <= Right && p.Y >= Top && p.Y <= Bottom;
}
