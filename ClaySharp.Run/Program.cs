using System.Numerics;
using ClaySharp;
using ClaySharp.Raylib;
using Raylib_cs;

const ulong IncrementButtonId = 1;
const ulong ToggleOverlayButtonId = 2;
const ulong ScrollPanelId = 3;
const ulong ToastId = 4;
const int DefaultWindowWidth = 1280;
const int DefaultWindowHeight = 800;
const float DefaultMaxScroll = 520f;
const string DefaultSnapshotPath = "ClaySharp.Run.snapshot.png";

var snapshotPath = ParseSnapshotPath(args);
var snapshotMode = snapshotPath is not null;

Raylib.SetConfigFlags(snapshotMode ? ConfigFlags.HiddenWindow : ConfigFlags.ResizableWindow);
Raylib.InitWindow(DefaultWindowWidth, DefaultWindowHeight, "ClaySharp.Run");
if (!snapshotMode)
{
    Raylib.SetTargetFPS(60);
}

{
    var fontPath = Path.Combine(AppContext.BaseDirectory, "OpenSans-Regular.ttf");
    using var fontAsset = LoadUiFontAsset(fontPath);

    using var context = new ClayContext(initialElementCapacity: 512, initialLeafCapacity: 256, initialRenderCommandCapacity: 2048, initialLineCapacity: 1024);
    using var measurer = new RaylibTextMeasurer(_ => fontAsset.Face);
    using var renderer = new ClayRaylibRenderer(_ => fontAsset.Face);

    var state = new DemoState(DefaultMaxScroll);

    if (snapshotMode)
    {
        var viewport = new Vector2(DefaultWindowWidth, DefaultWindowHeight);
        BuildUi(context, measurer, viewport, state, hoveredId: 0UL);
        var exportedPath = ExportSnapshot(renderer, context.RenderCommands, snapshotPath!, DefaultWindowWidth, DefaultWindowHeight);
        Console.WriteLine($"Snapshot exported to {exportedPath}");
    }
    else
    {
        RunInteractiveLoop(context, measurer, renderer, state);
    }
}

Raylib.CloseWindow();

static RaylibFontAsset LoadUiFontAsset(string fontPath)
{
    if (!File.Exists(fontPath))
    {
        return RaylibFontAsset.Wrap(Raylib.GetFontDefault());
    }

    var codepoints = RaylibFontAsset.CreateCodepointRange(32, 255);

    try
    {
        return RaylibFontAsset.LoadSdf(fontPath, 48, codepoints);
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Falling back to regular font rendering: {exception.Message}");
        var fallbackFont = Raylib.LoadFontEx(fontPath, 48, codepoints, codepoints.Length);
        Raylib.SetTextureFilter(fallbackFont.Texture, TextureFilter.Bilinear);
        return RaylibFontAsset.Wrap(fallbackFont, ownsFont: true);
    }
}

static void RunInteractiveLoop(ClayContext context, ITextMeasurer measurer, ClayRaylibRenderer renderer, DemoState state)
{
    while (!Raylib.WindowShouldClose())
    {
        var mouse = Raylib.GetMousePosition();
        var hoveredId = context.TryHitTest(mouse, out var hitId) ? hitId : 0UL;

        if (context.TryGetBounds(ScrollPanelId, out var scrollBounds) && scrollBounds.Contains(mouse))
        {
            state.ScrollOffset = Math.Clamp(state.ScrollOffset - (Raylib.GetMouseWheelMove() * 36f), 0f, state.MaxScroll);
        }

        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            if (hoveredId == IncrementButtonId)
            {
                state.Counter++;
            }

            if (hoveredId == ToggleOverlayButtonId)
            {
                state.ToastVisible = !state.ToastVisible;
            }
        }

        var viewport = new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
        BuildUi(context, measurer, viewport, state, hoveredId);

        Raylib.BeginDrawing();
        Raylib.ClearBackground(GetCanvasBackground());
        renderer.Render(context.RenderCommands);
        Raylib.EndDrawing();
    }
}

static void BuildUi(ClayContext context, ITextMeasurer measurer, Vector2 viewport, DemoState state, ulong hoveredId)
{
    context.BeginLayout(viewport, measurer);

    using (context.Element(new ElementStyle(
        layout: new LayoutConfig(
            axis: LayoutAxis.Vertical,
            sizing: ElementSizing.Grow(),
            padding: new Thickness(24f),
            gap: 18f),
        box: new BoxStyle(new ClayColor(244, 239, 231)))))
    {
        using (context.Element(new ElementStyle(
            layout: new LayoutConfig(
                axis: LayoutAxis.Horizontal,
                sizing: new ElementSizing(SizeSpec.Grow(), SizeSpec.Fit()),
                padding: new Thickness(22f),
                gap: 16f,
                crossAlignment: Alignment.Center),
            box: new BoxStyle(new ClayColor(252, 249, 244), new BorderStyle(new Thickness(1f), new ClayColor(205, 193, 176)), new CornerRadius(18f)))))
        {
            using (context.Element(new ElementStyle(
                layout: new LayoutConfig(axis: LayoutAxis.Vertical, sizing: new ElementSizing(SizeSpec.Grow(), SizeSpec.Fit()), gap: 6f))))
            {
                context.Text(
                    "ClaySharp",
                    new TextElementStyle(
                        ElementStyle.Leaf(new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit())),
                        new TextStyle(34f, new ClayColor(32, 28, 24), letterSpacing: 1f, wrap: false)));

                context.Text(
                    "Immediate-mode layout with pooled buffers, wrapped text, clipping, and a flat render command stream.",
                    new TextElementStyle(
                        new ElementStyle(layout: new LayoutConfig(sizing: new ElementSizing(SizeSpec.Grow(), SizeSpec.Fit()))),
                        new TextStyle(18f, new ClayColor(84, 74, 62), lineHeight: 24f, wrap: true)));
            }

            using (context.Element(ButtonStyle(ToggleOverlayButtonId, hoveredId == ToggleOverlayButtonId, new ClayColor(60, 89, 84), new ClayColor(242, 245, 240), new Vector2(188f, 54f))))
            {
                context.Text(
                    state.ToastVisible ? "Hide Overlay" : "Show Overlay",
                    new TextElementStyle(
                        new ElementStyle(layout: new LayoutConfig(sizing: ElementSizing.Grow(), padding: new Thickness(16f, 14f, 16f, 14f))),
                        new TextStyle(18f, new ClayColor(242, 245, 240), horizontalAlignment: Alignment.Center, wrap: false)));
            }
        }

        using (context.Element(new ElementStyle(
            layout: new LayoutConfig(axis: LayoutAxis.Horizontal, sizing: ElementSizing.Grow(), gap: 18f),
            box: default)))
        {
            using (context.Element(new ElementStyle(
                layout: new LayoutConfig(
                    axis: LayoutAxis.Vertical,
                    sizing: new ElementSizing(SizeSpec.Grow(), SizeSpec.Grow()),
                    padding: new Thickness(20f),
                    gap: 16f),
                box: new BoxStyle(new ClayColor(252, 249, 244), new BorderStyle(new Thickness(1f), new ClayColor(205, 193, 176)), new CornerRadius(22f)))))
            {
                using (context.Element(new ElementStyle(layout: new LayoutConfig(axis: LayoutAxis.Horizontal, sizing: new ElementSizing(SizeSpec.Grow(), SizeSpec.Fit()), gap: 12f))))
                {
                    SummaryCard(context, "Counter", state.Counter.ToString(), new ClayColor(115, 145, 121));
                    SummaryCard(context, "Scroll", $"{state.ScrollOffset:0}px", new ClayColor(183, 123, 89));
                    SummaryCard(context, "Nodes", context.ElementCount.ToString(), new ClayColor(89, 112, 144));
                }

                using (context.Element(new ElementStyle(layout: new LayoutConfig(axis: LayoutAxis.Horizontal, sizing: new ElementSizing(SizeSpec.Grow(), SizeSpec.Fit()), gap: 14f))))
                {
                    using (context.Element(ButtonStyle(IncrementButtonId, hoveredId == IncrementButtonId, new ClayColor(28, 31, 39), new ClayColor(239, 239, 235), new Vector2(220f, 60f))))
                    {
                        context.Text(
                            "Increment Counter",
                            new TextElementStyle(
                                new ElementStyle(layout: new LayoutConfig(sizing: ElementSizing.Grow(), padding: new Thickness(18f, 16f, 18f, 16f))),
                                new TextStyle(20f, new ClayColor(239, 239, 235), horizontalAlignment: Alignment.Center, wrap: false)));
                    }

                    context.Text(
                        "The list below is clipped by a scissor region and offset with a user-managed scroll value.",
                        new TextElementStyle(
                            new ElementStyle(layout: new LayoutConfig(sizing: new ElementSizing(SizeSpec.Grow(), SizeSpec.Fit()))),
                            new TextStyle(17f, new ClayColor(84, 74, 62), lineHeight: 24f, wrap: true)));
                }

                using (context.Element(new ElementStyle(
                    id: ScrollPanelId,
                    layout: new LayoutConfig(
                        axis: LayoutAxis.Vertical,
                        sizing: new ElementSizing(SizeSpec.Grow(), SizeSpec.Grow()),
                        padding: new Thickness(18f),
                        gap: 12f,
                        clipContent: true,
                        scrollOffset: new Vector2(0f, state.ScrollOffset)),
                    box: new BoxStyle(new ClayColor(247, 242, 234), new BorderStyle(new Thickness(1f), new ClayColor(224, 212, 194)), new CornerRadius(18f)))))
                {
                    for (var index = 0; index < 14; index++)
                    {
                        using (context.Element(new ElementStyle(
                            layout: new LayoutConfig(
                                axis: LayoutAxis.Vertical,
                                sizing: new ElementSizing(SizeSpec.Grow(), SizeSpec.Fit()),
                                padding: new Thickness(16f),
                                gap: 8f),
                            box: new BoxStyle(
                                index % 2 == 0 ? new ClayColor(255, 252, 247) : new ClayColor(242, 235, 224),
                                new BorderStyle(new Thickness(1f), new ClayColor(220, 208, 190)),
                                new CornerRadius(14f),
                                index == state.Counter % 14 ? new ClayColor(255, 255, 255, 22) : ClayColor.Transparent))))
                        {
                            context.Text(
                                $"Panel {index + 1}",
                                new TextElementStyle(
                                    ElementStyle.Leaf(new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit())),
                                    new TextStyle(20f, new ClayColor(38, 33, 28), wrap: false)));
                            context.Text(
                                "The layout engine measures width first, wraps text once widths are final, then resolves heights and positions in later passes.",
                                new TextElementStyle(
                                    new ElementStyle(layout: new LayoutConfig(sizing: new ElementSizing(SizeSpec.Grow(), SizeSpec.Fit()))),
                                    new TextStyle(16f, new ClayColor(96, 86, 74), lineHeight: 22f, wrap: true)));
                        }
                    }
                }
            }

            using (context.Element(new ElementStyle(
                layout: new LayoutConfig(
                    axis: LayoutAxis.Vertical,
                    sizing: new ElementSizing(SizeSpec.Fixed(280f), SizeSpec.Grow()),
                    padding: new Thickness(20f),
                    gap: 14f),
                box: new BoxStyle(new ClayColor(44, 53, 68), new BorderStyle(new Thickness(1f), new ClayColor(73, 89, 110)), new CornerRadius(22f)))))
            {
                context.Text(
                    "Renderer Notes",
                    new TextElementStyle(
                        ElementStyle.Leaf(new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit())),
                        new TextStyle(22f, new ClayColor(244, 240, 231), wrap: false)));
                context.Text(
                    "This demo resolves wrapped text in ClaySharp, then feeds a flat stream of rectangle, border, text, scissor, overlay, image, and custom commands into the Raylib painter.",
                    new TextElementStyle(
                        new ElementStyle(layout: new LayoutConfig(sizing: new ElementSizing(SizeSpec.Grow(), SizeSpec.Fit()))),
                        new TextStyle(17f, new ClayColor(205, 214, 222), lineHeight: 24f, wrap: true)));
                context.Text(
                    "Interaction is derived from the previous frame's layout via hit-testing on element bounds.",
                    new TextElementStyle(
                        new ElementStyle(layout: new LayoutConfig(sizing: new ElementSizing(SizeSpec.Grow(), SizeSpec.Fit()))),
                        new TextStyle(17f, new ClayColor(205, 214, 222), lineHeight: 24f, wrap: true)));
            }
        }

        if (state.ToastVisible)
        {
            using (context.Element(new ElementStyle(
                id: ToastId,
                layout: new LayoutConfig(
                    axis: LayoutAxis.Vertical,
                    sizing: new ElementSizing(SizeSpec.Fixed(320f), SizeSpec.Fit()),
                    padding: new Thickness(18f),
                    gap: 8f,
                    positionMode: PositionMode.Absolute,
                    absolutePosition: new AbsolutePosition(Alignment.End, Alignment.Start, -8f, 84f)),
                box: new BoxStyle(new ClayColor(28, 31, 39, 245), new BorderStyle(new Thickness(1f), new ClayColor(122, 138, 167)), new CornerRadius(18f), new ClayColor(255, 255, 255, 18)))))
            {
                context.Text(
                    "Absolute Overlay",
                    new TextElementStyle(
                        ElementStyle.Leaf(new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit())),
                        new TextStyle(20f, new ClayColor(242, 245, 240), wrap: false)));
                context.Text(
                    "This panel is removed from normal flow and anchored against the root container with an absolute position.",
                    new TextElementStyle(
                        new ElementStyle(layout: new LayoutConfig(sizing: new ElementSizing(SizeSpec.Grow(), SizeSpec.Fit()))),
                        new TextStyle(16f, new ClayColor(205, 214, 222), lineHeight: 22f, wrap: true)));
            }
        }
    }

    context.EndLayout();
}

static string ExportSnapshot(ClayRaylibRenderer renderer, ReadOnlySpan<RenderCommand> commands, string outputPath, int width, int height)
{
    var resolvedPath = Path.GetFullPath(string.IsNullOrWhiteSpace(outputPath) ? DefaultSnapshotPath : outputPath);
    var directory = Path.GetDirectoryName(resolvedPath);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }

    var target = Raylib.LoadRenderTexture(width, height);
    try
    {
        Raylib.BeginTextureMode(target);
        try
        {
            Raylib.ClearBackground(GetCanvasBackground());
            renderer.Render(commands);
        }
        finally
        {
            Raylib.EndTextureMode();
        }

        var image = Raylib.LoadImageFromTexture(target.Texture);
        try
        {
            Raylib.ImageFlipVertical(ref image);
            if (!Raylib.ExportImage(image, resolvedPath))
            {
                throw new IOException($"Failed to export snapshot to '{resolvedPath}'.");
            }
        }
        finally
        {
            Raylib.UnloadImage(image);
        }
    }
    finally
    {
        Raylib.UnloadRenderTexture(target);
    }

    return resolvedPath;
}

static string? ParseSnapshotPath(string[] args)
{
    string? snapshotPath = null;

    for (var index = 0; index < args.Length; index++)
    {
        var argument = args[index];
        if (argument.StartsWith("--snapshot=", StringComparison.OrdinalIgnoreCase))
        {
            var value = argument["--snapshot=".Length..].Trim();
            snapshotPath = string.IsNullOrWhiteSpace(value) ? DefaultSnapshotPath : value;
            continue;
        }

        if (!argument.Equals("--snapshot", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            snapshotPath = args[++index];
        }
        else
        {
            snapshotPath = DefaultSnapshotPath;
        }
    }

    return snapshotPath;
}

static Color GetCanvasBackground() => new(236, 229, 220, 255);

static ElementStyle ButtonStyle(ulong id, bool hovered, ClayColor background, ClayColor foreground, Vector2 size)
{
    var overlay = hovered ? new ClayColor(255, 255, 255, 28) : ClayColor.Transparent;
    return new ElementStyle(
        id,
        new LayoutConfig(axis: LayoutAxis.Vertical, sizing: ElementSizing.Fixed(size.X, size.Y)),
        new BoxStyle(background, new BorderStyle(new Thickness(1f), foreground), new CornerRadius(16f), overlay));
}

static void SummaryCard(ClayContext context, string title, string value, ClayColor accent)
{
    using (context.Element(new ElementStyle(
        layout: new LayoutConfig(
            axis: LayoutAxis.Vertical,
            sizing: new ElementSizing(SizeSpec.Grow(), SizeSpec.Fit()),
            padding: new Thickness(16f),
            gap: 6f),
        box: new BoxStyle(new ClayColor(255, 252, 247), new BorderStyle(new Thickness(1f), new ClayColor(220, 208, 190)), new CornerRadius(16f), new ClayColor(accent.R, accent.G, accent.B, 18)))))
    {
        context.Text(
            title,
            new TextElementStyle(
                ElementStyle.Leaf(new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit())),
                new TextStyle(16f, new ClayColor(96, 86, 74), wrap: false)));
        context.Text(
            value,
            new TextElementStyle(
                ElementStyle.Leaf(new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit())),
                new TextStyle(26f, new ClayColor(38, 33, 28), wrap: false)));
    }
}

sealed class DemoState
{
    public DemoState(float maxScroll)
    {
        MaxScroll = maxScroll;
    }

    public int Counter { get; set; }

    public bool ToastVisible { get; set; }

    public float ScrollOffset { get; set; }

    public float MaxScroll { get; }
}
