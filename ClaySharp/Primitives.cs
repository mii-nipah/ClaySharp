using System.Numerics;

namespace ClaySharp;

public enum LayoutAxis
{
    Horizontal,
    Vertical,
}

public enum Alignment
{
    Start,
    Center,
    End,
    Stretch,
}

public enum SizeMode
{
    Fit,
    Grow,
    Fixed,
}

public enum PositionMode
{
    Flow,
    Absolute,
}

public enum ElementKind
{
    Container,
    Text,
    Image,
    Custom,
}

public enum RenderCommandType
{
    Rectangle,
    Border,
    Text,
    Image,
    Custom,
    ScissorStart,
    ScissorEnd,
    OverlayStart,
    OverlayEnd,
}

public readonly struct ClayColor
{
    public static readonly ClayColor Transparent = new(0, 0, 0, 0);
    public static readonly ClayColor White = new(255, 255, 255, 255);
    public static readonly ClayColor Black = new(0, 0, 0, 255);

    public ClayColor(byte r, byte g, byte b, byte a = 255)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public byte R { get; }

    public byte G { get; }

    public byte B { get; }

    public byte A { get; }

    public bool IsVisible => A > 0;

    public static ClayColor Rgba(byte r, byte g, byte b, byte a = 255) => new(r, g, b, a);
}

public readonly struct Thickness
{
    public Thickness(float uniform)
        : this(uniform, uniform, uniform, uniform)
    {
    }

    public Thickness(float left, float top, float right, float bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public float Left { get; }

    public float Top { get; }

    public float Right { get; }

    public float Bottom { get; }

    public float Horizontal => Left + Right;

    public float Vertical => Top + Bottom;

    public static Thickness All(float value) => new(value);

    public static Thickness Symmetric(float horizontal, float vertical) => new(horizontal, vertical, horizontal, vertical);
}

public readonly struct CornerRadius
{
    public CornerRadius(float uniform)
        : this(uniform, uniform, uniform, uniform)
    {
    }

    public CornerRadius(float topLeft, float topRight, float bottomRight, float bottomLeft)
    {
        TopLeft = topLeft;
        TopRight = topRight;
        BottomRight = bottomRight;
        BottomLeft = bottomLeft;
    }

    public float TopLeft { get; }

    public float TopRight { get; }

    public float BottomRight { get; }

    public float BottomLeft { get; }

    public bool IsZero => TopLeft <= 0f && TopRight <= 0f && BottomRight <= 0f && BottomLeft <= 0f;

    public bool TryGetUniform(out float radius)
    {
        radius = TopLeft;
        return NearlyEqual(TopLeft, TopRight)
            && NearlyEqual(TopLeft, BottomRight)
            && NearlyEqual(TopLeft, BottomLeft);
    }

    private static bool NearlyEqual(float left, float right) => MathF.Abs(left - right) <= 0.01f;
}

public readonly struct RectF
{
    public RectF(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public float X { get; }

    public float Y { get; }

    public float Width { get; }

    public float Height { get; }

    public float Right => X + Width;

    public float Bottom => Y + Height;

    public bool Contains(Vector2 point)
    {
        return point.X >= X
            && point.Y >= Y
            && point.X <= Right
            && point.Y <= Bottom;
    }

    public RectF Deflate(in Thickness thickness)
    {
        var width = MathF.Max(0f, Width - thickness.Horizontal);
        var height = MathF.Max(0f, Height - thickness.Vertical);
        return new RectF(X + thickness.Left, Y + thickness.Top, width, height);
    }
}