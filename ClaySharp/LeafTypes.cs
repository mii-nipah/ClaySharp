using System.Numerics;

namespace ClaySharp;

public readonly struct ImageElementStyle
{
    public ImageElementStyle(
        ElementStyle element,
        Vector2 intrinsicSize,
        object? source,
        ClayColor tint,
        bool preserveAspectRatio = true,
        RectF sourceRegion = default,
        bool useSourceRegion = false)
    {
        Element = element;
        IntrinsicSize = intrinsicSize;
        Source = source;
        Tint = tint;
        PreserveAspectRatio = preserveAspectRatio;
        SourceRegion = sourceRegion;
        UseSourceRegion = useSourceRegion;
    }

    public ElementStyle Element { get; }

    public Vector2 IntrinsicSize { get; }

    public object? Source { get; }

    public ClayColor Tint { get; }

    public bool PreserveAspectRatio { get; }

    public RectF SourceRegion { get; }

    public bool UseSourceRegion { get; }
}

public readonly struct CustomElementStyle
{
    public CustomElementStyle(ElementStyle element, Vector2 preferredSize, object? payload)
    {
        Element = element;
        PreferredSize = preferredSize;
        Payload = payload;
    }

    public ElementStyle Element { get; }

    public Vector2 PreferredSize { get; }

    public object? Payload { get; }
}