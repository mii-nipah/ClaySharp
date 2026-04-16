using System.Buffers;
using System.Numerics;
using ClaySharp;
using Raylib_cs;

namespace ClaySharp.Raylib;

public sealed class ClayRaylibRenderer : IDisposable
{
    private readonly Func<int, RaylibFontFace> _fontResolver;
    private readonly Utf8ScratchBuffer _textBuffer;
    private ClayColor[] _overlayStack;
    private RectF[] _scissorStack;
    private int _overlayCount;
    private int _scissorCount;

    public ClayRaylibRenderer(Func<int, RaylibFontFace> fontResolver, int initialOverlayDepth = 16, int initialBufferBytes = 256)
    {
        _fontResolver = fontResolver ?? throw new ArgumentNullException(nameof(fontResolver));
        _textBuffer = new Utf8ScratchBuffer(initialBufferBytes);
        _overlayStack = ArrayPool<ClayColor>.Shared.Rent(Math.Max(initialOverlayDepth, 4));
        _scissorStack = ArrayPool<RectF>.Shared.Rent(Math.Max(initialOverlayDepth, 4));
    }

    public ClayRaylibRenderer(Func<int, Font> fontResolver, int initialOverlayDepth = 16, int initialBufferBytes = 256)
        : this(fontId => new RaylibFontFace(fontResolver(fontId)), initialOverlayDepth, initialBufferBytes)
    {
    }

    public void Dispose()
    {
        _textBuffer.Dispose();
        ArrayPool<ClayColor>.Shared.Return(_overlayStack);
        ArrayPool<RectF>.Shared.Return(_scissorStack);
    }

    public void Render(ReadOnlySpan<RenderCommand> commands, Action<RenderCommand>? customRenderer = null)
    {
        _overlayCount = 0;
        _scissorCount = 0;

        foreach (ref readonly var command in commands)
        {
            switch (command.Type)
            {
                case RenderCommandType.Rectangle:
                    DrawRectangle(command.Bounds, ApplyOverlay(command.Color), command.CornerRadius);
                    break;
                case RenderCommandType.Border:
                    DrawBorder(command.Bounds, ApplyOverlay(command.Color), command.Thickness, command.CornerRadius);
                    break;
                case RenderCommandType.Text:
                    DrawText(command);
                    break;
                case RenderCommandType.Image:
                    DrawImage(command, customRenderer);
                    break;
                case RenderCommandType.Custom:
                    customRenderer?.Invoke(command);
                    break;
                case RenderCommandType.ScissorStart:
                    PushScissor(command.Bounds);
                    break;
                case RenderCommandType.ScissorEnd:
                    PopScissor();
                    break;
                case RenderCommandType.OverlayStart:
                    PushOverlay(command.Color);
                    break;
                case RenderCommandType.OverlayEnd:
                    PopOverlay();
                    break;
            }
        }

        while (_scissorCount > 0)
        {
            PopScissor();
        }
    }

    private unsafe void DrawText(in RenderCommand command)
    {
        if (command.Text.Length <= 0)
        {
            return;
        }

        var text = command.Text;
        var color = ToRaylibColor(ApplyOverlay(command.Color));
        var fontFace = _fontResolver(command.TextStyle.FontId);
        var fontSize = command.TextStyle.FontSize > 0f ? command.TextStyle.FontSize : 16f;
        var letterSpacing = command.TextStyle.LetterSpacing;
        var position = new Vector2(command.Bounds.X, command.Bounds.Y);

        _textBuffer.WithCString(text.Span, ptr =>
        {
            if (fontFace.UsesShader)
            {
                Raylib_cs.Raylib.BeginShaderMode(fontFace.Shader);
            }

            try
            {
                Raylib_cs.Raylib.DrawTextEx(fontFace.Font, ptr, position, fontSize, letterSpacing, color);
            }
            finally
            {
                if (fontFace.UsesShader)
                {
                    Raylib_cs.Raylib.EndShaderMode();
                }
            }
        });
    }

    private void DrawImage(in RenderCommand command, Action<RenderCommand>? customRenderer)
    {
        if (command.Bounds.Width <= 0f || command.Bounds.Height <= 0f)
        {
            return;
        }

        if (command.Payload is Texture2D texture)
        {
            var source = command.UseSourceRegion
                ? ToRaylibRectangle(command.SourceRegion)
                : new Raylib_cs.Rectangle(0f, 0f, texture.Width, texture.Height);

            Raylib_cs.Raylib.DrawTexturePro(
                texture,
                source,
                ToRaylibRectangle(command.Bounds),
                Vector2.Zero,
                0f,
                ToRaylibColor(ApplyOverlay(command.Color)));
            return;
        }

        customRenderer?.Invoke(command);
    }

    private void DrawRectangle(in RectF bounds, ClayColor color, CornerRadius cornerRadius)
    {
        if (!color.IsVisible || bounds.Width <= 0f || bounds.Height <= 0f)
        {
            return;
        }

        var rectangle = ToRaylibRectangle(bounds);
        if (cornerRadius.TryGetUniform(out var radius) && radius > 0.5f)
        {
            Raylib_cs.Raylib.DrawRectangleRounded(rectangle, ToRoundness(bounds, radius), 8, ToRaylibColor(color));
            return;
        }

        Raylib_cs.Raylib.DrawRectangleRec(rectangle, ToRaylibColor(color));
    }

    private void DrawBorder(in RectF bounds, ClayColor color, Thickness thickness, CornerRadius cornerRadius)
    {
        if (!color.IsVisible || bounds.Width <= 0f || bounds.Height <= 0f)
        {
            return;
        }

        var rayColor = ToRaylibColor(color);
        if (cornerRadius.TryGetUniform(out var radius)
            && NearlyEqual(thickness.Left, thickness.Top)
            && NearlyEqual(thickness.Left, thickness.Right)
            && NearlyEqual(thickness.Left, thickness.Bottom)
            && thickness.Left > 0f)
        {
            Raylib_cs.Raylib.DrawRectangleRoundedLinesEx(ToRaylibRectangle(bounds), ToRoundness(bounds, radius), 8, thickness.Left, rayColor);
            return;
        }

        if (thickness.Top > 0f)
        {
            Raylib_cs.Raylib.DrawRectangleRec(new Raylib_cs.Rectangle(bounds.X, bounds.Y, bounds.Width, thickness.Top), rayColor);
        }

        if (thickness.Bottom > 0f)
        {
            Raylib_cs.Raylib.DrawRectangleRec(new Raylib_cs.Rectangle(bounds.X, bounds.Bottom - thickness.Bottom, bounds.Width, thickness.Bottom), rayColor);
        }

        if (thickness.Left > 0f)
        {
            Raylib_cs.Raylib.DrawRectangleRec(new Raylib_cs.Rectangle(bounds.X, bounds.Y, thickness.Left, bounds.Height), rayColor);
        }

        if (thickness.Right > 0f)
        {
            Raylib_cs.Raylib.DrawRectangleRec(new Raylib_cs.Rectangle(bounds.Right - thickness.Right, bounds.Y, thickness.Right, bounds.Height), rayColor);
        }
    }

    private void PushOverlay(ClayColor color)
    {
        EnsureOverlayCapacity(_overlayCount + 1);
        _overlayStack[_overlayCount++] = color;
    }

    private void PopOverlay()
    {
        if (_overlayCount > 0)
        {
            _overlayCount--;
        }
    }

    private void PushScissor(RectF bounds)
    {
        EnsureScissorCapacity(_scissorCount + 1);
        var clipped = _scissorCount == 0 ? bounds : Intersect(_scissorStack[_scissorCount - 1], bounds);
        _scissorStack[_scissorCount++] = clipped;

        if (_scissorCount > 1)
        {
            Raylib_cs.Raylib.EndScissorMode();
        }

        ApplyScissor(clipped);
    }

    private void PopScissor()
    {
        if (_scissorCount == 0)
        {
            return;
        }

        _scissorCount--;
        Raylib_cs.Raylib.EndScissorMode();
        if (_scissorCount > 0)
        {
            ApplyScissor(_scissorStack[_scissorCount - 1]);
        }
    }

    private void ApplyScissor(RectF bounds)
    {
        var x = (int)MathF.Floor(bounds.X);
        var y = (int)MathF.Floor(bounds.Y);
        var width = (int)MathF.Ceiling(MathF.Max(0f, bounds.Width));
        var height = (int)MathF.Ceiling(MathF.Max(0f, bounds.Height));
        Raylib_cs.Raylib.BeginScissorMode(x, y, width, height);
    }

    private ClayColor ApplyOverlay(ClayColor baseColor)
    {
        var color = baseColor;
        for (var index = 0; index < _overlayCount; index++)
        {
            color = Blend(color, _overlayStack[index]);
        }

        return color;
    }

    private static ClayColor Blend(ClayColor baseColor, ClayColor overlay)
    {
        if (!overlay.IsVisible)
        {
            return baseColor;
        }

        var alpha = overlay.A / 255f;
        return new ClayColor(
            (byte)Math.Clamp(baseColor.R + ((overlay.R - baseColor.R) * alpha), 0f, 255f),
            (byte)Math.Clamp(baseColor.G + ((overlay.G - baseColor.G) * alpha), 0f, 255f),
            (byte)Math.Clamp(baseColor.B + ((overlay.B - baseColor.B) * alpha), 0f, 255f),
            baseColor.A);
    }

    private static float ToRoundness(RectF bounds, float radius)
    {
        var denominator = MathF.Max(1f, MathF.Min(bounds.Width, bounds.Height) * 0.5f);
        return Math.Clamp(radius / denominator, 0f, 1f);
    }

    private void EnsureOverlayCapacity(int required)
    {
        if (_overlayStack.Length >= required)
        {
            return;
        }

        var replacement = ArrayPool<ClayColor>.Shared.Rent(Math.Max(required, _overlayStack.Length * 2));
        Array.Copy(_overlayStack, replacement, _overlayCount);
        ArrayPool<ClayColor>.Shared.Return(_overlayStack);
        _overlayStack = replacement;
    }

    private void EnsureScissorCapacity(int required)
    {
        if (_scissorStack.Length >= required)
        {
            return;
        }

        var replacement = ArrayPool<RectF>.Shared.Rent(Math.Max(required, _scissorStack.Length * 2));
        Array.Copy(_scissorStack, replacement, _scissorCount);
        ArrayPool<RectF>.Shared.Return(_scissorStack);
        _scissorStack = replacement;
    }

    private static RectF Intersect(in RectF left, in RectF right)
    {
        var x = MathF.Max(left.X, right.X);
        var y = MathF.Max(left.Y, right.Y);
        var rightEdge = MathF.Min(left.Right, right.Right);
        var bottomEdge = MathF.Min(left.Bottom, right.Bottom);
        return new RectF(x, y, MathF.Max(0f, rightEdge - x), MathF.Max(0f, bottomEdge - y));
    }

    private static Raylib_cs.Rectangle ToRaylibRectangle(in RectF rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private static Color ToRaylibColor(ClayColor color) => new(color.R, color.G, color.B, color.A);

    private static bool NearlyEqual(float left, float right) => MathF.Abs(left - right) <= 0.01f;
}
