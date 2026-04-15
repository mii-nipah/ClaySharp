using System.Numerics;

namespace ClaySharp;

public readonly struct SizeSpec
{
    private SizeSpec(SizeMode mode, float value, float min, float max)
    {
        Mode = mode;
        Value = value;
        Min = min;
        Max = max;
    }

    public SizeMode Mode { get; }

    public float Value { get; }

    public float Min { get; }

    public float Max { get; }

    public float EffectiveMax => Mode == SizeMode.Fixed
        ? Value
        : Max <= 0f
            ? float.PositiveInfinity
            : Max;

    public float Clamp(float value)
    {
        if (Mode == SizeMode.Fixed)
        {
            return Value;
        }

        var clamped = MathF.Max(Min, value);
        var max = EffectiveMax;
        if (!float.IsPositiveInfinity(max))
        {
            clamped = MathF.Min(max, clamped);
        }

        return clamped;
    }

    public static SizeSpec Fit(float min = 0f, float max = 0f) => new(SizeMode.Fit, 0f, min, max);

    public static SizeSpec Grow(float min = 0f, float max = 0f) => new(SizeMode.Grow, 0f, min, max);

    public static SizeSpec Fixed(float value) => new(SizeMode.Fixed, value, value, value);
}

public readonly struct ElementSizing
{
    public ElementSizing(SizeSpec width, SizeSpec height)
    {
        Width = width;
        Height = height;
    }

    public SizeSpec Width { get; }

    public SizeSpec Height { get; }

    public static ElementSizing FitContent => new(SizeSpec.Fit(), SizeSpec.Fit());

    public static ElementSizing Grow() => new(SizeSpec.Grow(), SizeSpec.Grow());

    public static ElementSizing Fixed(float width, float height) => new(SizeSpec.Fixed(width), SizeSpec.Fixed(height));
}

public readonly struct AbsolutePosition
{
    public AbsolutePosition(Alignment horizontalAnchor, Alignment verticalAnchor, float offsetX = 0f, float offsetY = 0f)
    {
        HorizontalAnchor = horizontalAnchor;
        VerticalAnchor = verticalAnchor;
        OffsetX = offsetX;
        OffsetY = offsetY;
    }

    public Alignment HorizontalAnchor { get; }

    public Alignment VerticalAnchor { get; }

    public float OffsetX { get; }

    public float OffsetY { get; }
}

public readonly struct LayoutConfig
{
    public LayoutConfig(
        LayoutAxis axis = LayoutAxis.Vertical,
        ElementSizing sizing = default,
        Thickness padding = default,
        float gap = 0f,
        Alignment mainAlignment = Alignment.Start,
        Alignment crossAlignment = Alignment.Start,
        PositionMode positionMode = PositionMode.Flow,
        AbsolutePosition absolutePosition = default,
        float aspectRatio = 0f,
        bool clipContent = false,
        Vector2 scrollOffset = default)
    {
        Axis = axis;
        Sizing = sizing.Width.Mode == 0 && sizing.Height.Mode == 0 && sizing.Width.Value == 0f && sizing.Height.Value == 0f
            ? ElementSizing.FitContent
            : sizing;
        Padding = padding;
        Gap = gap;
        MainAlignment = mainAlignment;
        CrossAlignment = crossAlignment;
        PositionMode = positionMode;
        AbsolutePosition = absolutePosition;
        AspectRatio = aspectRatio;
        ClipContent = clipContent;
        ScrollOffset = scrollOffset;
    }

    public LayoutAxis Axis { get; }

    public ElementSizing Sizing { get; }

    public Thickness Padding { get; }

    public float Gap { get; }

    public Alignment MainAlignment { get; }

    public Alignment CrossAlignment { get; }

    public PositionMode PositionMode { get; }

    public AbsolutePosition AbsolutePosition { get; }

    public float AspectRatio { get; }

    public bool ClipContent { get; }

    public Vector2 ScrollOffset { get; }

    public LayoutConfig WithAspectRatio(float aspectRatio)
    {
        return new LayoutConfig(
            Axis,
            Sizing,
            Padding,
            Gap,
            MainAlignment,
            CrossAlignment,
            PositionMode,
            AbsolutePosition,
            aspectRatio,
            ClipContent,
            ScrollOffset);
    }
}

public readonly struct BorderStyle
{
    public BorderStyle(Thickness widths, ClayColor color)
    {
        Widths = widths;
        Color = color;
    }

    public Thickness Widths { get; }

    public ClayColor Color { get; }

    public bool HasVisibleBorder => Color.IsVisible
        && (Widths.Left > 0f || Widths.Top > 0f || Widths.Right > 0f || Widths.Bottom > 0f);
}

public readonly struct BoxStyle
{
    public BoxStyle(ClayColor backgroundColor, BorderStyle border = default, CornerRadius cornerRadius = default, ClayColor overlayColor = default)
    {
        BackgroundColor = backgroundColor;
        Border = border;
        CornerRadius = cornerRadius;
        OverlayColor = overlayColor;
    }

    public ClayColor BackgroundColor { get; }

    public BorderStyle Border { get; }

    public CornerRadius CornerRadius { get; }

    public ClayColor OverlayColor { get; }

    public bool HasBackground => BackgroundColor.IsVisible;

    public bool HasOverlay => OverlayColor.IsVisible;
}

public readonly struct ElementStyle
{
    public ElementStyle(ulong id = 0, LayoutConfig layout = default, BoxStyle box = default)
    {
        Id = id;
        Layout = layout;
        Box = box;
    }

    public ulong Id { get; }

    public LayoutConfig Layout { get; }

    public BoxStyle Box { get; }

    public ElementStyle WithAspectRatio(float aspectRatio) => new(Id, Layout.WithAspectRatio(aspectRatio), Box);

    public static ElementStyle Container(
        ElementSizing sizing,
        LayoutAxis axis = LayoutAxis.Vertical,
        Thickness padding = default,
        float gap = 0f,
        ClayColor background = default,
        BorderStyle border = default,
        CornerRadius cornerRadius = default,
        Alignment mainAlignment = Alignment.Start,
        Alignment crossAlignment = Alignment.Start,
        bool clipContent = false,
        Vector2 scrollOffset = default,
        ulong id = 0)
    {
        return new ElementStyle(
            id,
            new LayoutConfig(axis, sizing, padding, gap, mainAlignment, crossAlignment, PositionMode.Flow, default, 0f, clipContent, scrollOffset),
            new BoxStyle(background, border, cornerRadius));
    }

    public static ElementStyle Leaf(
        ElementSizing sizing,
        Thickness padding = default,
        ClayColor background = default,
        BorderStyle border = default,
        CornerRadius cornerRadius = default,
        PositionMode positionMode = PositionMode.Flow,
        AbsolutePosition absolutePosition = default,
        float aspectRatio = 0f,
        ulong id = 0)
    {
        return new ElementStyle(
            id,
            new LayoutConfig(LayoutAxis.Vertical, sizing, padding, 0f, Alignment.Start, Alignment.Start, positionMode, absolutePosition, aspectRatio),
            new BoxStyle(background, border, cornerRadius));
    }
}