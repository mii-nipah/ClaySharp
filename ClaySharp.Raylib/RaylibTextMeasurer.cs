using ClaySharp;
using Raylib_cs;

namespace ClaySharp.Raylib;

public sealed class RaylibTextMeasurer : ITextMeasurer, IDisposable
{
    private readonly Func<int, Font> _fontResolver;
    private readonly Utf8ScratchBuffer _buffer;

    public RaylibTextMeasurer(Func<int, Font> fontResolver, int initialBufferBytes = 256)
    {
        _fontResolver = fontResolver ?? throw new ArgumentNullException(nameof(fontResolver));
        _buffer = new Utf8ScratchBuffer(initialBufferBytes);
    }

    public unsafe float MeasureWidth(ReadOnlySpan<char> text, in TextStyle style)
    {
        if (text.IsEmpty)
        {
            return 0f;
        }

        var font = ResolveFont(style.FontId);
        var fontSize = EffectiveFontSize(in style);
        var letterSpacing = style.LetterSpacing;
        return _buffer.WithCString(text, ptr => Raylib_cs.Raylib.MeasureTextEx(font, ptr, fontSize, letterSpacing).X);
    }

    public int FitCharacters(ReadOnlySpan<char> text, float maxWidth, in TextStyle style, out float measuredWidth)
    {
        if (text.IsEmpty || maxWidth <= 0f)
        {
            measuredWidth = 0f;
            return 0;
        }

        var low = 1;
        var high = text.Length;
        var best = 0;
        var bestWidth = 0f;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var width = MeasureWidth(text[..middle], in style);
            if (width <= maxWidth + 0.01f)
            {
                best = middle;
                bestWidth = width;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        measuredWidth = bestWidth;
        return best;
    }

    public unsafe float GetLineHeight(in TextStyle style)
    {
        if (style.LineHeight > 0f)
        {
            return style.LineHeight;
        }

        var font = ResolveFont(style.FontId);
        var fontSize = EffectiveFontSize(in style);
        var letterSpacing = style.LetterSpacing;
        return _buffer.WithCString("Ag".AsSpan(), ptr => Raylib_cs.Raylib.MeasureTextEx(font, ptr, fontSize, letterSpacing).Y);
    }

    public void Dispose()
    {
        _buffer.Dispose();
    }

    private Font ResolveFont(int fontId) => _fontResolver(fontId);

    private static float EffectiveFontSize(in TextStyle style) => style.FontSize > 0f ? style.FontSize : 16f;
}
