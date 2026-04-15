namespace ClaySharp;

public readonly struct TextStyle
{
    public TextStyle(
        float fontSize,
        ClayColor color,
        int fontId = 0,
        float letterSpacing = 0f,
        float lineHeight = 0f,
        Alignment horizontalAlignment = Alignment.Start,
        bool wrap = true)
    {
        FontSize = fontSize;
        Color = color;
        FontId = fontId;
        LetterSpacing = letterSpacing;
        LineHeight = lineHeight;
        HorizontalAlignment = horizontalAlignment;
        Wrap = wrap;
    }

    public int FontId { get; }

    public float FontSize { get; }

    public float LetterSpacing { get; }

    public float LineHeight { get; }

    public ClayColor Color { get; }

    public Alignment HorizontalAlignment { get; }

    public bool Wrap { get; }
}

public readonly struct TextElementStyle
{
    public TextElementStyle(ElementStyle element, TextStyle text)
    {
        Element = element;
        Text = text;
    }

    public ElementStyle Element { get; }

    public TextStyle Text { get; }
}

public readonly struct TextSlice
{
    public TextSlice(string? text, int start, int length)
    {
        Text = text;
        Start = start;
        Length = length;
    }

    public string? Text { get; }

    public int Start { get; }

    public int Length { get; }

    public ReadOnlySpan<char> Span => Text is null ? ReadOnlySpan<char>.Empty : Text.AsSpan(Start, Length);
}

public interface ITextMeasurer
{
    float MeasureWidth(ReadOnlySpan<char> text, in TextStyle style);

    int FitCharacters(ReadOnlySpan<char> text, float maxWidth, in TextStyle style, out float measuredWidth);

    float GetLineHeight(in TextStyle style);
}