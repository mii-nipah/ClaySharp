using System.Numerics;
using ClaySharp;

namespace ClaySharp.Tests;

public sealed class LayoutEngineTests
{
    [Fact]
    public void HorizontalGrow_DistributesRemainingWidthSmallestFirst()
    {
        using var context = new ClayContext();
        var measurer = new MonospaceTextMeasurer();

        context.BeginLayout(new Vector2(400f, 200f), measurer);
        using (context.Element(new ElementStyle(layout: new LayoutConfig(axis: LayoutAxis.Horizontal, sizing: ElementSizing.Fixed(300f, 100f), gap: 10f))))
        {
            context.Box(new ElementStyle(1, new LayoutConfig(sizing: new ElementSizing(SizeSpec.Grow(40f), SizeSpec.Fixed(20f)))));
            context.Box(new ElementStyle(2, new LayoutConfig(sizing: new ElementSizing(SizeSpec.Grow(80f), SizeSpec.Fixed(20f)))));
            context.Box(new ElementStyle(3, new LayoutConfig(sizing: ElementSizing.Fixed(30f, 20f))));
        }

        context.EndLayout();

        Assert.True(context.TryGetBounds(1, out var first));
        Assert.True(context.TryGetBounds(2, out var second));
        Assert.True(context.TryGetBounds(3, out var third));
        Assert.Equal(125f, first.Width, 2);
        Assert.Equal(125f, second.Width, 2);
        Assert.Equal(30f, third.Width, 2);
        Assert.Equal(135f, second.X - first.X, 2);
    }

    [Fact]
    public void HorizontalOverflow_ShrinksLargestFirstRespectingMinimums()
    {
        using var context = new ClayContext();
        var measurer = new MonospaceTextMeasurer();

        context.BeginLayout(new Vector2(400f, 200f), measurer);
        using (context.Element(new ElementStyle(layout: new LayoutConfig(axis: LayoutAxis.Horizontal, sizing: ElementSizing.Fixed(160f, 100f), gap: 10f))))
        {
            context.Custom(new CustomElementStyle(
                new ElementStyle(10, new LayoutConfig(sizing: new ElementSizing(SizeSpec.Fit(50f), SizeSpec.Fixed(20f)))),
                new Vector2(120f, 20f),
                null));
            context.Custom(new CustomElementStyle(
                new ElementStyle(11, new LayoutConfig(sizing: new ElementSizing(SizeSpec.Fit(40f), SizeSpec.Fixed(20f)))),
                new Vector2(80f, 20f),
                null));
        }

        context.EndLayout();

        Assert.True(context.TryGetBounds(10, out var first));
        Assert.True(context.TryGetBounds(11, out var second));
        Assert.Equal(75f, first.Width, 2);
        Assert.Equal(75f, second.Width, 2);
    }

    [Fact]
    public void TextWrapping_UsesResolvedWidthBeforeHeight()
    {
        using var context = new ClayContext();
        var measurer = new MonospaceTextMeasurer();

        context.BeginLayout(new Vector2(200f, 200f), measurer);
        using (context.Element(new ElementStyle(layout: new LayoutConfig(axis: LayoutAxis.Vertical, sizing: ElementSizing.Fixed(70f, 100f)))))
        {
            context.Text(
                "abcdefghi",
                new TextElementStyle(
                    new ElementStyle(20, new LayoutConfig(sizing: new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit()))),
                    new TextStyle(16f, ClayColor.Black, lineHeight: 20f, wrap: true)));
        }

        context.EndLayout();

        Assert.True(context.TryGetBounds(20, out var textBounds));
        Assert.Equal(70f, textBounds.Width, 2);
        Assert.Equal(40f, textBounds.Height, 2);
        var textCommandCount = 0;
        foreach (var command in context.RenderCommands)
        {
            if (command.Type == RenderCommandType.Text)
            {
                textCommandCount++;
            }
        }

        Assert.Equal(2, textCommandCount);
    }

    [Fact]
    public void Alignment_AppliesMainAndCrossAxisOffsets()
    {
        using var context = new ClayContext();
        var measurer = new MonospaceTextMeasurer();

        context.BeginLayout(new Vector2(300f, 200f), measurer);
        using (context.Element(new ElementStyle(layout: new LayoutConfig(axis: LayoutAxis.Horizontal, sizing: ElementSizing.Fixed(200f, 100f), mainAlignment: Alignment.Center, crossAlignment: Alignment.End))))
        {
            context.Box(new ElementStyle(30, new LayoutConfig(sizing: ElementSizing.Fixed(50f, 20f))));
        }

        context.EndLayout();

        Assert.True(context.TryGetBounds(30, out var bounds));
        Assert.Equal(75f, bounds.X, 2);
        Assert.Equal(80f, bounds.Y, 2);
    }

    [Fact]
    public void AbsoluteChildren_DoNotAffectFitSizingAndClipCommandsAreEmitted()
    {
        using var context = new ClayContext();
        var measurer = new MonospaceTextMeasurer();

        context.BeginLayout(new Vector2(300f, 200f), measurer);
        using (context.Element(new ElementStyle(
            40,
            new LayoutConfig(axis: LayoutAxis.Vertical, sizing: new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit()), clipContent: true),
            new BoxStyle(new ClayColor(200, 200, 200)))))
        {
            context.Box(new ElementStyle(41, new LayoutConfig(sizing: ElementSizing.Fixed(20f, 10f))));
            context.Box(new ElementStyle(
                42,
                new LayoutConfig(
                    sizing: ElementSizing.Fixed(100f, 100f),
                    positionMode: PositionMode.Absolute,
                    absolutePosition: new AbsolutePosition(Alignment.End, Alignment.End))));
        }

        context.EndLayout();

        Assert.True(context.TryGetBounds(40, out var parentBounds));
        Assert.Equal(20f, parentBounds.Width, 2);
        Assert.Equal(10f, parentBounds.Height, 2);
        var hasScissorStart = false;
        var hasScissorEnd = false;
        foreach (var command in context.RenderCommands)
        {
            hasScissorStart |= command.Type == RenderCommandType.ScissorStart && command.ElementId == 40;
            hasScissorEnd |= command.Type == RenderCommandType.ScissorEnd && command.ElementId == 40;
        }

        Assert.True(hasScissorStart);
        Assert.True(hasScissorEnd);
    }

    [Fact]
    public void ClipContainers_AllowMainAxisOverflowWithoutShrinkingChildren()
    {
        using var context = new ClayContext();
        var measurer = new MonospaceTextMeasurer();

        context.BeginLayout(new Vector2(300f, 200f), measurer);
        using (context.Element(new ElementStyle(
            50,
            new LayoutConfig(axis: LayoutAxis.Vertical, sizing: ElementSizing.Fixed(100f, 60f), clipContent: true),
            new BoxStyle(new ClayColor(210, 210, 210)))))
        {
            context.Custom(new CustomElementStyle(
                new ElementStyle(51, new LayoutConfig(sizing: new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit()))),
                new Vector2(100f, 40f),
                null));
            context.Custom(new CustomElementStyle(
                new ElementStyle(52, new LayoutConfig(sizing: new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit()))),
                new Vector2(100f, 40f),
                null));
        }

        context.EndLayout();

        Assert.True(context.TryGetBounds(50, out var parentBounds));
        Assert.True(context.TryGetBounds(51, out var firstBounds));
        Assert.True(context.TryGetBounds(52, out var secondBounds));
        Assert.Equal(60f, parentBounds.Height, 2);
        Assert.Equal(40f, firstBounds.Height, 2);
        Assert.Equal(40f, secondBounds.Height, 2);
        Assert.Equal(40f, secondBounds.Y - firstBounds.Y, 2);
    }

    [Fact]
    public void FlowContentBounds_UnionFlowChildrenAndIgnoreAbsoluteChildren()
    {
        using var context = new ClayContext();
        var measurer = new MonospaceTextMeasurer();

        context.BeginLayout(new Vector2(300f, 200f), measurer);
        using (context.Element(new ElementStyle(
            60,
            new LayoutConfig(
                axis: LayoutAxis.Vertical,
                sizing: ElementSizing.Fixed(120f, 80f),
                padding: new Thickness(10f),
                gap: 5f,
                clipContent: true,
                scrollOffset: new Vector2(0f, 25f)),
            new BoxStyle(new ClayColor(220, 220, 220)))))
        {
            context.Box(new ElementStyle(61, new LayoutConfig(sizing: ElementSizing.Fixed(100f, 30f))));
            context.Box(new ElementStyle(62, new LayoutConfig(sizing: ElementSizing.Fixed(100f, 40f))));
            context.Box(new ElementStyle(
                63,
                new LayoutConfig(
                    sizing: ElementSizing.Fixed(100f, 100f),
                    positionMode: PositionMode.Absolute,
                    absolutePosition: new AbsolutePosition(Alignment.End, Alignment.End))));
        }

        context.EndLayout();

        Assert.True(context.TryGetFlowContentBounds(60, out var contentBounds));
        Assert.Equal(100f, contentBounds.Width, 2);
        Assert.Equal(75f, contentBounds.Height, 2);
    }

    private sealed class MonospaceTextMeasurer : ITextMeasurer
    {
        public float MeasureWidth(ReadOnlySpan<char> text, in TextStyle style) => text.Length * 10f;

        public int FitCharacters(ReadOnlySpan<char> text, float maxWidth, in TextStyle style, out float measuredWidth)
        {
            var fit = Math.Clamp((int)MathF.Floor(maxWidth / 10f), 0, text.Length);
            measuredWidth = fit * 10f;
            return fit;
        }

        public float GetLineHeight(in TextStyle style) => style.LineHeight > 0f ? style.LineHeight : 20f;
    }
}
