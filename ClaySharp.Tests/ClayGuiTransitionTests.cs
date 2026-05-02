using ClaySharp;
using ClaySharp.Raylib;

namespace ClaySharp.Tests;

public sealed class ClayGuiTransitionTests
{
    [Fact]
    public void OngoingTransitions_ApplyScrollTranslationBeforeInterpolatingBounds()
    {
        var previous = new RenderCommand
        {
            Type = RenderCommandType.Rectangle,
            ElementId = 12,
            TransitionId = 12,
            Bounds = new RectF(10f, 100f, 80f, 20f),
            Color = new ClayColor(10, 20, 30, 40),
        };
        var target = new RenderCommand
        {
            Type = RenderCommandType.Rectangle,
            ElementId = 12,
            TransitionId = 12,
            Bounds = new RectF(30f, 44f, 120f, 20f),
            Color = new ClayColor(100, 110, 120, 255),
        };

        var command = ClayGui.InterpolateOngoingCommand(previous, target, 0.5f, -56f);

        Assert.Equal(20f, command.Bounds.X, 2);
        Assert.Equal(44f, command.Bounds.Y, 2);
        Assert.Equal(100f, command.Bounds.Width, 2);
        Assert.Equal(20f, command.Bounds.Height, 2);
        Assert.Equal(55, command.Color.R);
        Assert.Equal(65, command.Color.G);
        Assert.Equal(75, command.Color.B);
        Assert.Equal(148, command.Color.A);
    }

    [Fact]
    public void OngoingTransitions_IgnoreScrollTranslationForCommandEndMarkers()
    {
        var previous = new RenderCommand
        {
            Type = RenderCommandType.OverlayEnd,
            TransitionId = 8,
            Bounds = new RectF(0f, 0f, 0f, 0f),
        };
        var target = new RenderCommand
        {
            Type = RenderCommandType.OverlayEnd,
            TransitionId = 8,
            Bounds = new RectF(0f, 0f, 0f, 0f),
        };

        var command = ClayGui.InterpolateOngoingCommand(previous, target, 0.5f, -56f);

        Assert.Equal(0f, command.Bounds.Y, 2);
    }
}
