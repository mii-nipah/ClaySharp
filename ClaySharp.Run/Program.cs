using System.Numerics;
using ClaySharp;
using ClaySharp.Raylib;
using Raylib_cs;

const int DefaultWindowWidth = 1280;
const int DefaultWindowHeight = 800;
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

    var clui = new ClayGui(context, measurer, renderer);

    var state = new DemoState();

    if (snapshotMode)
    {
        var viewport = new Vector2(DefaultWindowWidth, DefaultWindowHeight);
        clui.SetViewport(viewport);
        Game(clui, state);
        var exportedPath = ExportSnapshot(renderer, clui.RenderCommands, snapshotPath!, DefaultWindowWidth, DefaultWindowHeight);
        Console.WriteLine($"Snapshot exported to {exportedPath}");
    }
    else
    {
        RunInteractiveLoop(clui, renderer, state);
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
    var forceRegularFont = Environment.GetEnvironmentVariable("CLAYSHARP_FORCE_REGULAR_FONT") == "1";

    if (forceRegularFont)
    {
        var font = Raylib.LoadFontEx(fontPath, 48, codepoints, codepoints.Length);
        Raylib.SetTextureFilter(font.Texture, TextureFilter.Bilinear);
        return RaylibFontAsset.Wrap(font, ownsFont: true);
    }

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

static void RunInteractiveLoop(ClayGui clui, ClayRaylibRenderer renderer, DemoState state)
{
    while (!Raylib.WindowShouldClose())
    {
        var viewport = new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
        clui.SetViewport(viewport);
        Game(clui, state);

        Raylib.BeginDrawing();
        Raylib.ClearBackground(GetCanvasBackground());
        renderer.Render(clui.RenderCommands);
        Raylib.EndDrawing();
    }
}

static void Game(ClayGui clui, DemoState state)
{
    clui.Begin();

    using(clui.Element()
        .Color(244, 239, 231)
        .Padding(24f)
        .Gap(18f)
        .Grow()
        .VerticalLayout())
    {
        using(clui.Element()
            .Color(new ClayColor(252, 249, 244))
            .Border(new Thickness(1f), new ClayColor(205, 193, 176))
            .CornerRadius(18f)
            .Padding(22f)
            .Gap(16f)
            .GrowHorizontal()
            .FitVertical()
            .HorizontalLayout()
            .CrossAlignment(Alignment.Center))
        {
            using(clui.Element()
                .Gap(6f)
                .GrowHorizontal()
                .FitVertical()
                .VerticalLayout())
            {
                clui.Text(
                    "ClaySharp",
                    new TextElementStyle(
                        ElementStyle.Leaf(new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit())),
                        new TextStyle(34f, new ClayColor(32, 28, 24), letterSpacing: 1f, wrap: false)));

                clui.Text(
                    "Immediate-mode layout with pooled buffers, wrapped text, clipping, and a flat render command stream.",
                    new TextElementStyle(
                        new ElementStyle(layout: new LayoutConfig(sizing: new ElementSizing(SizeSpec.Grow(), SizeSpec.Fit()))),
                        new TextStyle(18f, new ClayColor(84, 74, 62), lineHeight: 24f, wrap: true)));
            }

            using(clui.Element()
                .BackgroundColor(new ClayColor(60, 89, 84))
                .Border(new Thickness(1f), new ClayColor(242, 245, 240))
                .CornerRadius(16f)
                .Hovered(out var overlayHovered)
                .OverlayColor(overlayHovered
                    ? new ClayColor(255, 255, 255, 28)
                    : ClayColor.Transparent)
                .Clicked(out var clicked)
                .Size(new Vector2(188f, 54f)))
            {
                if(clicked)
                    state.ToastVisible = !state.ToastVisible;
                clui.Text(
                    state.ToastVisible ? "Hide Overlay" : "Show Overlay",
                    new TextElementStyle(
                        new ElementStyle(layout: new LayoutConfig(sizing: ElementSizing.Grow(), padding: new Thickness(16f, 14f, 16f, 14f))),
                        new TextStyle(18f, new ClayColor(242, 245, 240), horizontalAlignment: Alignment.Center, wrap: false)));
            }
        }

        using(clui.Element()
            .Gap(18f)
            .Grow()
            .HorizontalLayout())
        {
            using(clui.Element()
                .Color(new ClayColor(252, 249, 244))
                .Border(new Thickness(1f), new ClayColor(205, 193, 176))
                .CornerRadius(22f)
                .Padding(20f)
                .Gap(16f)
                .Grow()
                .VerticalLayout())
            {
                using(clui.Element()
                    .Gap(12f)
                    .GrowHorizontal()
                    .FitVertical()
                    .HorizontalLayout())
                {
                    NiceSummaryCard(clui, "Counter", state.Counter.ToString(), new ClayColor(115, 145, 121));
                    NiceSummaryCard(clui, "Scroll", $"{state.ScrollOffset:0}px", new ClayColor(183, 123, 89));
                    NiceSummaryCard(clui, "Nodes", clui.ElementCount.ToString(), new ClayColor(89, 112, 144));
                }

                using(clui.Element()
                    .Gap(14f)
                    .GrowHorizontal()
                    .FitVertical()
                    .HorizontalLayout())
                {
                    using(clui.Element()
                        .BackgroundColor(new ClayColor(28, 31, 39))
                        .Border(new Thickness(1f), new ClayColor(239, 239, 235))
                        .CornerRadius(16f)
                        .Hovered(out var incrementHovered)
                        .OverlayColor(incrementHovered
                            ? new ClayColor(255, 255, 255, 28)
                            : ClayColor.Transparent)
                        .Clicked(out var incrementClicked)
                        .Size(new Vector2(220f, 60f)))
                    {
                        if (incrementClicked)
                            state.Counter++;
                        clui.Text(
                            "Increment Counter",
                            new TextElementStyle(
                                new ElementStyle(layout: new LayoutConfig(sizing: ElementSizing.Grow(), padding: new Thickness(18f, 16f, 18f, 16f))),
                                new TextStyle(20f, new ClayColor(239, 239, 235), horizontalAlignment: Alignment.Center, wrap: false)));
                    }

                    clui.Text(
                        "The list below is clipped by a scissor region and offset with a user-managed scroll value.",
                        new TextElementStyle(
                            new ElementStyle(layout: new LayoutConfig(sizing: new ElementSizing(SizeSpec.Grow(), SizeSpec.Fit()))),
                            new TextStyle(17f, new ClayColor(84, 74, 62), lineHeight: 24f, wrap: true)));
                }

                using(clui.Element()
                    .BackgroundColor(new ClayColor(247, 242, 234))
                    .Border(new Thickness(1f), new ClayColor(224, 212, 194))
                    .CornerRadius(18f)
                    .Padding(18f)
                    .Gap(12f)
                    .Grow()
                    .ClipContent()
                    .ScrollY(ref state.ScrollOffset)
                    .VerticalLayout())
                {
                    for (var index = 0; index < 14; index++)
                    {
                        using(clui.Element()
                            .Color(index % 2 == 0 ? new ClayColor(255, 252, 247) : new ClayColor(242, 235, 224))
                            .Border(new Thickness(1f), new ClayColor(220, 208, 190))
                            .CornerRadius(14f)
                            .OverlayColor(index == state.Counter % 14 ? new ClayColor(255, 255, 255, 22) : ClayColor.Transparent)
                            .Padding(16f)
                            .Gap(8f)
                            .GrowHorizontal()
                            .FitVertical()
                            .VerticalLayout())
                        {
                            clui.Text(
                                $"Panel {index + 1}",
                                new TextElementStyle(
                                    ElementStyle.Leaf(new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit())),
                                    new TextStyle(20f, new ClayColor(38, 33, 28), wrap: false)));
                            clui.Text(
                                "The layout engine measures width first, wraps text once widths are final, then resolves heights and positions in later passes.",
                                new TextElementStyle(
                                    new ElementStyle(layout: new LayoutConfig(sizing: new ElementSizing(SizeSpec.Grow(), SizeSpec.Fit()))),
                                    new TextStyle(16f, new ClayColor(96, 86, 74), lineHeight: 22f, wrap: true)));
                        }
                    }
                }
            }

            using(clui.Element()
                .Color(new ClayColor(44, 53, 68))
                .Border(new Thickness(1f), new ClayColor(73, 89, 110))
                .CornerRadius(22f)
                .Padding(20f)
                .Gap(14f)
                .FitHorizontal(280f)
                .GrowVertical()
                .VerticalLayout())
            {
                clui.Text(
                    "Renderer Notes",
                    new TextElementStyle(
                        ElementStyle.Leaf(new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit())),
                        new TextStyle(22f, new ClayColor(244, 240, 231), wrap: false)));
                clui.Text(
                    "This demo resolves wrapped text in ClaySharp, then feeds a flat stream of rectangle, border, text, scissor, overlay, image, and custom commands into the Raylib painter.",
                    new TextElementStyle(
                        new ElementStyle(layout: new LayoutConfig(sizing: new ElementSizing(SizeSpec.Grow(), SizeSpec.Fit()))),
                        new TextStyle(17f, new ClayColor(205, 214, 222), lineHeight: 24f, wrap: true)));
                clui.Text(
                    "Interaction is derived from the previous frame's layout via hit-testing on element bounds.",
                    new TextElementStyle(
                        new ElementStyle(layout: new LayoutConfig(sizing: new ElementSizing(SizeSpec.Grow(), SizeSpec.Fit()))),
                        new TextStyle(17f, new ClayColor(205, 214, 222), lineHeight: 24f, wrap: true)));
            }
        }

        if (state.ToastVisible)
        {
            using(clui.Element()
                .Color(new ClayColor(28, 31, 39, 245))
                .Border(new Thickness(1f), new ClayColor(122, 138, 167))
                .CornerRadius(18f)
                .OverlayColor(new ClayColor(255, 255, 255, 18))
                .Padding(18f)
                .Gap(8f)
                .FitHorizontal(320f)
                .PositionMode(PositionMode.Absolute)
                .AbsolutePosition(new AbsolutePosition(Alignment.End, Alignment.Start, -8f, 84f))
                .VerticalLayout())
            {
                clui.Text(
                    "Absolute Overlay",
                    new TextElementStyle(
                        ElementStyle.Leaf(new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit())),
                        new TextStyle(20f, new ClayColor(242, 245, 240), wrap: false)));
                clui.Text(
                    "This panel is removed from normal flow and anchored against the root container with an absolute position.",
                    new TextElementStyle(
                        new ElementStyle(layout: new LayoutConfig(sizing: new ElementSizing(SizeSpec.Grow(), SizeSpec.Fit()))),
                        new TextStyle(16f, new ClayColor(205, 214, 222), lineHeight: 22f, wrap: true)));
            }
        }
    }

    clui.End();
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

static void NiceSummaryCard(ClayGui clui, string title, string value, ClayColor accent)
{
    using (clui.Element()
        .BackgroundColor(new ClayColor(255, 252, 247))
        .Border(new Thickness(1f), new ClayColor(220, 208, 190))
        .CornerRadius(16f)
        .OverlayColor(new ClayColor(accent.R, accent.G, accent.B, 18))
        .Padding(16f)
        .Gap(6f)
        .GrowHorizontal()
        .FitVertical()
        .VerticalLayout())
    {
        clui.Text(
            title,
            new TextElementStyle(
                ElementStyle.Leaf(new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit())),
                new TextStyle(16f, new ClayColor(96, 86, 74), wrap: false)));
        clui.Text(
            value,
            new TextElementStyle(
                ElementStyle.Leaf(new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit())),
                new TextStyle(26f, new ClayColor(38, 33, 28), wrap: false)));
    }
}

sealed class DemoState
{
    public int Counter { get; set; }

    public bool ToastVisible;

    public float ScrollOffset;
}
