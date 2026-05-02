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

    const float sidebarWidth = 200f;
    const float hoverTriggerZone = 12f;
    var mouseX = Raylib.GetMousePosition().X;
    var sidebarTargetExpanded = mouseX < hoverTriggerZone + (sidebarWidth * state.SidebarReveal);
    var dt = MathF.Max(0f, Raylib.GetFrameTime());
    var revealSpeed = 8f * dt;
    state.SidebarReveal = sidebarTargetExpanded
        ? MathF.Min(1f, state.SidebarReveal + revealSpeed)
        : MathF.Max(0f, state.SidebarReveal - revealSpeed);

    using (clui.Element().Grow().HorizontalLayout())
    {
        if (state.ActivePage == 0)
            DashboardPage(clui, state);
        else
            WidgetGalleryPage(clui, state);
    }

    if (state.SidebarReveal > 0.001f)
    {
        var sidebarOffset = -sidebarWidth * (1f - state.SidebarReveal);
        var sidebarAlpha = (byte)MathF.Round(255f * state.SidebarReveal);

        var screenHeight = (float)Raylib.GetScreenHeight();
        using (clui.Element()
            .Key("sidebar-panel")
            .Animated(0.12f)
            .Floating(50)
            .AbsolutePosition(new AbsolutePosition(Alignment.Start, Alignment.Start, sidebarOffset, 0f))
            .BackgroundColor(new ClayColor(22, 24, 30, sidebarAlpha))
            .Border(new Thickness(0f, 0f, 1f, 0f), new ClayColor(60, 65, 80, sidebarAlpha))
            .Padding(0f, 12f)
            .Gap(4f)
            .Size(new Vector2(sidebarWidth, screenHeight))
            .VerticalLayout())
        {
            using (clui.Element()
                .Padding(18f, 16f)
                .Gap(0f)
                .GrowHorizontal()
                .FitVertical()
                .HorizontalLayout()
                .CrossAlignment(Alignment.Center))
            {
                clui.Text("CS",
                    new TextElementStyle(
                        ElementStyle.Leaf(new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit())),
                        new TextStyle(22f, new ClayColor(160, 180, 255, sidebarAlpha), letterSpacing: 2f, wrap: false)));
            }

            using (clui.Element()
                .BackgroundColor(new ClayColor(50, 54, 68, sidebarAlpha))
                .GrowHorizontal()
                .FitVertical()
                .Size(new Vector2(0f, 1f))) { }

            SidebarTab(clui, "Dashboard", 0, state, sidebarAlpha);
            SidebarTab(clui, "Widget Gallery", 1, state, sidebarAlpha);

            using (clui.Element().Grow()) { }

            using (clui.Element()
                .BackgroundColor(new ClayColor(50, 54, 68, sidebarAlpha))
                .GrowHorizontal()
                .FitVertical()
                .Size(new Vector2(0f, 1f))) { }

            using (clui.Element()
                .Padding(18f, 12f)
                .GrowHorizontal()
                .FitVertical())
            {
                clui.Text("Hover left edge to reveal",
                    new TextElementStyle(
                        ElementStyle.Leaf(new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit())),
                        new TextStyle(12f, new ClayColor(90, 96, 115, sidebarAlpha), wrap: false)));
            }
        }
    }

    if (state.SidebarReveal < 0.05f)
    {
        var pulseAlpha = (byte)MathF.Round(40f + 20f * MathF.Sin((float)Raylib.GetTime() * 3f));
        using (clui.Element()
            .Key("sidebar-hint")
            .Floating(49)
            .AbsolutePosition(new AbsolutePosition(Alignment.Start, Alignment.Start, 0f, 0f))
            .BackgroundColor(new ClayColor(130, 150, 220, pulseAlpha))
            .Size(new Vector2(3f, Raylib.GetScreenHeight())))
        { }
    }

    clui.End();
}

static void SidebarTab(ClayGui clui, string label, int pageIndex, DemoState state, byte alpha)
{
    var isActive = state.ActivePage == pageIndex;
    var activeBg = new ClayColor(45, 50, 72, alpha);
    var hoverBg = new ClayColor(36, 40, 56, alpha);
    var normalBg = ClayColor.Transparent;
    var activeAccent = new ClayColor(130, 160, 255, alpha);
    var normalText = new ClayColor(170, 175, 190, alpha);

    using (clui.Element()
        .Animated(0.14f)
        .BackgroundColor(isActive ? activeBg : normalBg)
        .Hovered(out var hovered)
        .OverlayColor(hovered && !isActive ? hoverBg : ClayColor.Transparent)
        .Clicked(out var clicked)
        .CornerRadius(10f)
        .Padding(18f, 12f)
        .Gap(10f)
        .GrowHorizontal()
        .FitVertical()
        .HorizontalLayout()
        .CrossAlignment(Alignment.Center))
    {
        if (clicked)
            state.ActivePage = pageIndex;

        if (isActive)
        {
            using (clui.Element()
                .BackgroundColor(activeAccent)
                .CornerRadius(2f)
                .Size(new Vector2(3f, 18f)))
            { }
        }

        clui.Text(label,
            new TextElementStyle(
                ElementStyle.Leaf(new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit())),
                new TextStyle(15f, isActive ? activeAccent : normalText, wrap: false)));
    }
}

static void DashboardPage(ClayGui clui, DemoState state)
{
    var badgePhase = 0.5f + (0.5f * MathF.Sin((float)Raylib.GetTime() * 2.2f));
    var badgeOffsetY = state.ToastVisible ? 188f : 132f;
    var badgeColor = new ClayColor(
        LerpByte(190, 236, badgePhase),
        LerpByte(132, 168, badgePhase),
        LerpByte(96, 124, badgePhase));

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
                .Animated(0.18f)
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
                    NiceSummaryCard(clui, "Scroll", $"{state.LastObservedScrollOffset:0}px", new ClayColor(183, 123, 89));
                    NiceSummaryCard(clui, "Nodes", clui.ElementCount.ToString(), new ClayColor(89, 112, 144));
                }

                using(clui.Element()
                    .Gap(14f)
                    .GrowHorizontal()
                    .FitVertical()
                    .HorizontalLayout())
                {
                    using(clui.Element()
                        .Animated(0.16f)
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
                        "The list below uses ClayGui.ScrollableY with retained momentum and automatic semantic identity instead of a user-managed ref float.",
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
                    .ScrollableY(out var scrollOffset)
                    .VerticalLayout())
                {
                    state.LastObservedScrollOffset = scrollOffset;

                    for (var index = 0; index < 14; index++)
                    {
                        using(clui.Element()
                            .Animated(0.18f)
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
                    "This build extracts floating absolute elements into a z-sorted render pass, so stacked overlays paint on top without inheriting parent clipping or scroll offsets.",
                    new TextElementStyle(
                        new ElementStyle(layout: new LayoutConfig(sizing: new ElementSizing(SizeSpec.Grow(), SizeSpec.Fit()))),
                        new TextStyle(17f, new ClayColor(205, 214, 222), lineHeight: 24f, wrap: true)));
                clui.Text(
                    "Animated containers retain their previous render commands and ease toward the latest layout, which gives immediate-mode code smooth motion without manual tween bookkeeping.",
                    new TextElementStyle(
                        new ElementStyle(layout: new LayoutConfig(sizing: new ElementSizing(SizeSpec.Grow(), SizeSpec.Fit()))),
                        new TextStyle(17f, new ClayColor(205, 214, 222), lineHeight: 24f, wrap: true)));
                clui.Text(
                    "Retained ids are derived automatically from each element's semantic shape plus invocation order, so basically you don't need manual keys or ids",
                    new TextElementStyle(
                        new ElementStyle(layout: new LayoutConfig(sizing: new ElementSizing(SizeSpec.Grow(), SizeSpec.Fit()))),
                        new TextStyle(17f, new ClayColor(205, 214, 222), lineHeight: 24f, wrap: true)));
            }
        }
    }

    using(clui.Element()
        .Animated(0.35f)
        .Floating(8)
        .AbsolutePosition(new AbsolutePosition(Alignment.End, Alignment.Start, -36f, badgeOffsetY))
        .BackgroundColor(new ClayColor(badgeColor.R, badgeColor.G, badgeColor.B, 240))
        .Border(new Thickness(1f), new ClayColor(255, 246, 230, 210))
        .CornerRadius(999f)
        .Padding(14f, 10f)
        .Gap(6f)
        .FitHorizontal()
        .FitVertical()
        .HorizontalLayout()
        .CrossAlignment(Alignment.Center))
    {
        clui.Text(
            state.ToastVisible ? "z-index 8" : "floating chip",
            new TextElementStyle(
                ElementStyle.Leaf(new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit())),
                new TextStyle(16f, new ClayColor(42, 31, 24), wrap: false)));
    }

    if (state.ToastVisible)
    {
        using(clui.Element()
            .Animated(0.28f)
            .Floating(20)
            .AbsolutePosition(new AbsolutePosition(Alignment.End, Alignment.Start, -8f, 116f))
            .Color(new ClayColor(28, 31, 39, 245))
            .Border(new Thickness(1f), new ClayColor(122, 138, 167))
            .CornerRadius(18f)
            .OverlayColor(new ClayColor(255, 255, 255, 18))
            .Padding(18f)
            .Gap(8f)
            .FitHorizontal(320f)
            .VerticalLayout())
        {
            clui.Text(
                "Animated Floating Overlay",
                new TextElementStyle(
                    ElementStyle.Leaf(new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit())),
                    new TextStyle(20f, new ClayColor(242, 245, 240), wrap: false)));
            clui.Text(
                "This panel is mounted conditionally. Its enter and exit motions come from the retained transition state.",
                new TextElementStyle(
                    new ElementStyle(layout: new LayoutConfig(sizing: new ElementSizing(SizeSpec.Grow(), SizeSpec.Fit()))),
                    new TextStyle(16f, new ClayColor(205, 214, 222), lineHeight: 22f, wrap: true)));
        }
    }
}

static void WidgetGalleryPage(ClayGui clui, DemoState state)
{
    var time = (float)Raylib.GetTime();

    using (clui.Element()
        .Color(new ClayColor(18, 18, 26))
        .Padding(28f)
        .Gap(22f)
        .Grow()
        .ScrollableY()
        .VerticalLayout())
    {
        using (clui.Element()
            .GrowHorizontal()
            .FitVertical()
            .Gap(6f)
            .VerticalLayout())
        {
            clui.Text("Widget Gallery",
                new TextElementStyle(
                    ElementStyle.Leaf(new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit())),
                    new TextStyle(32f, new ClayColor(230, 235, 255), letterSpacing: 0.8f, wrap: false)));
            clui.Text("Interactive components built with ClaySharp's immediate-mode API.",
                new TextElementStyle(
                    new ElementStyle(layout: new LayoutConfig(sizing: new ElementSizing(SizeSpec.Grow(), SizeSpec.Fit()))),
                    new TextStyle(16f, new ClayColor(120, 128, 155), lineHeight: 22f, wrap: true)));
        }

        using (clui.Element()
            .BackgroundColor(new ClayColor(26, 28, 38))
            .Border(new Thickness(1f), new ClayColor(50, 54, 72))
            .CornerRadius(18f)
            .Padding(20f)
            .Gap(14f)
            .GrowHorizontal()
            .FitVertical()
            .VerticalLayout())
        {
            clui.Text("Color Palette",
                new TextElementStyle(
                    ElementStyle.Leaf(new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit())),
                    new TextStyle(20f, new ClayColor(200, 210, 240), wrap: false)));

            using (clui.Element()
                .Gap(10f)
                .GrowHorizontal()
                .FitVertical()
                .HorizontalLayout())
            {
                ReadOnlySpan<(string name, ClayColor color)> swatches =
                [
                    ("Rose", new ClayColor(244, 114, 131)),
                    ("Amber", new ClayColor(245, 180, 70)),
                    ("Lime", new ClayColor(132, 204, 96)),
                    ("Teal", new ClayColor(72, 199, 190)),
                    ("Sky", new ClayColor(96, 165, 250)),
                    ("Violet", new ClayColor(167, 139, 250)),
                    ("Fuchsia", new ClayColor(232, 121, 249)),
                    ("Slate", new ClayColor(148, 163, 184)),
                ];

                for (var i = 0; i < swatches.Length; i++)
                {
                    var (name, color) = swatches[i];
                    var displayColor = state.Toggle2 ? WarmShift(color) : color;
                    var selected = state.SelectedSwatch == i;
                    var borderColor = selected ? new ClayColor(255, 255, 255, 200) : new ClayColor(60, 65, 82);
                    var scale = selected ? 1.05f : 1f;
                    _ = scale; // layout doesn't support scale, use overlay instead

                    using (clui.Element()
                        .Animated(0.14f)
                        .BackgroundColor(displayColor)
                        .Border(new Thickness(selected ? 2f : 1f), borderColor)
                        .CornerRadius(12f)
                        .Hovered(out var swatchHovered)
                        .OverlayColor(swatchHovered ? new ClayColor(255, 255, 255, 40) : ClayColor.Transparent)
                        .Clicked(out var swatchClicked)
                        .Padding(10f, 28f)
                        .Gap(4f)
                        .GrowHorizontal()
                        .FitVertical()
                        .VerticalLayout()
                        .CrossAlignment(Alignment.Center))
                    {
                        if (swatchClicked) state.SelectedSwatch = i;

                        clui.Text(name,
                            new TextElementStyle(
                                ElementStyle.Leaf(new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit())),
                                new TextStyle(13f, new ClayColor(0, 0, 0, 160), wrap: false)));
                    }
                }
            }

            if (state.SelectedSwatch >= 0)
            {
                ReadOnlySpan<(string name, ClayColor color)> swatchesInfo =
                [
                    ("Rose", new ClayColor(244, 114, 131)),
                    ("Amber", new ClayColor(245, 180, 70)),
                    ("Lime", new ClayColor(132, 204, 96)),
                    ("Teal", new ClayColor(72, 199, 190)),
                    ("Sky", new ClayColor(96, 165, 250)),
                    ("Violet", new ClayColor(167, 139, 250)),
                    ("Fuchsia", new ClayColor(232, 121, 249)),
                    ("Slate", new ClayColor(148, 163, 184)),
                ];
                var sel = swatchesInfo[state.SelectedSwatch];
                var infoColor = state.Toggle2
                    ? WarmShift(sel.color)
                    : sel.color;
                using (clui.Element()
                    .Animated(0.16f)
                    .BackgroundColor(new ClayColor(34, 38, 52))
                    .CornerRadius(10f)
                    .Padding(14f, 10f)
                    .Gap(8f)
                    .GrowHorizontal()
                    .FitVertical()
                    .HorizontalLayout()
                    .CrossAlignment(Alignment.Center))
                {
                    using (clui.Element()
                        .Animated(0.16f)
                        .BackgroundColor(infoColor)
                        .CornerRadius(6f)
                        .Size(new Vector2(24f, 24f))) { }

                    clui.Text($"{sel.name}  —  rgb({infoColor.R}, {infoColor.G}, {infoColor.B})",
                        new TextElementStyle(
                            ElementStyle.Leaf(new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit())),
                            new TextStyle(14f, new ClayColor(180, 190, 215), wrap: false)));
                }
            }
        }

        using (clui.Element()
            .Gap(16f)
            .GrowHorizontal()
            .FitVertical()
            .HorizontalLayout())
        {
            using (clui.Element()
                .BackgroundColor(new ClayColor(26, 28, 38))
                .Border(new Thickness(1f), new ClayColor(50, 54, 72))
                .CornerRadius(18f)
                .Padding(20f)
                .Gap(14f)
                .GrowHorizontal()
                .FitVertical()
                .VerticalLayout())
            {
                clui.Text("Toggle Switches",
                    new TextElementStyle(
                        ElementStyle.Leaf(new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit())),
                        new TextStyle(20f, new ClayColor(200, 210, 240), wrap: false)));

                ToggleRow(clui, "Animate progress bars", ref state.Toggle1);
                ToggleRow(clui, "Warm color palette", ref state.Toggle2);
                ToggleRow(clui, "Show notifications", ref state.Toggle3);
            }

            using (clui.Element()
                .BackgroundColor(new ClayColor(26, 28, 38))
                .Border(new Thickness(1f), new ClayColor(50, 54, 72))
                .CornerRadius(18f)
                .Padding(20f)
                .Gap(14f)
                .GrowHorizontal()
                .FitVertical()
                .VerticalLayout())
            {
                clui.Text("Progress Indicators",
                    new TextElementStyle(
                        ElementStyle.Leaf(new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit())),
                        new TextStyle(20f, new ClayColor(200, 210, 240), wrap: false)));

                var animMul = state.Toggle1 ? 1f : 0f;
                ProgressBar(clui, "Build", 0.5f + 0.5f * MathF.Sin(time * 0.8f * animMul), new ClayColor(96, 165, 250));
                ProgressBar(clui, "Tests", 0.5f + 0.5f * MathF.Sin(time * 1.2f * animMul + 1f), new ClayColor(132, 204, 96));
                ProgressBar(clui, "Deploy", 0.5f + 0.5f * MathF.Sin(time * 0.5f * animMul + 2f), new ClayColor(245, 180, 70));

                if (!state.Toggle1)
                {
                    clui.Text("Animations paused by toggle",
                        new TextElementStyle(
                            ElementStyle.Leaf(new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit())),
                            new TextStyle(12f, new ClayColor(110, 115, 140), wrap: false)));
                }
            }
        }

        using (clui.Element()
            .BackgroundColor(new ClayColor(26, 28, 38))
            .Border(new Thickness(1f), new ClayColor(50, 54, 72))
            .CornerRadius(18f)
            .Padding(20f)
            .Gap(12f)
            .GrowHorizontal()
            .FitVertical()
            .VerticalLayout())
        {
            clui.Text("Typography Scale",
                new TextElementStyle(
                    ElementStyle.Leaf(new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit())),
                    new TextStyle(20f, new ClayColor(200, 210, 240), wrap: false)));

            var typeColor = state.Toggle2
                ? new ClayColor(255, 225, 200)
                : new ClayColor(220, 225, 245);

            ReadOnlySpan<(string label, float size)> typeSamples =
            [
                ("Display — 36px", 36f),
                ("Heading — 28px", 28f),
                ("Title — 22px", 22f),
                ("Body — 16px", 16f),
                ("Caption — 13px", 13f),
                ("Overline — 11px", 11f),
            ];

            for (var i = 0; i < typeSamples.Length; i++)
            {
                var (label, size) = typeSamples[i];
                var alphaFade = (byte)(255 - i * 22);
                using (clui.Element()
                    .Padding(8f, 6f)
                    .GrowHorizontal()
                    .FitVertical()
                    .HorizontalLayout()
                    .CrossAlignment(Alignment.Center))
                {
                    clui.Text(label,
                        new TextElementStyle(
                            ElementStyle.Leaf(new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit())),
                            new TextStyle(size, new ClayColor(typeColor.R, typeColor.G, typeColor.B, alphaFade), wrap: false)));
                }
            }
        }

        if (state.Toggle3)
        {
            using (clui.Element()
                .Animated(0.22f)
                .BackgroundColor(new ClayColor(26, 28, 38))
                .Border(new Thickness(1f), new ClayColor(50, 54, 72))
                .CornerRadius(18f)
                .Padding(20f)
                .Gap(10f)
                .GrowHorizontal()
                .FitVertical()
                .VerticalLayout())
            {
                clui.Text("Notification Feed",
                    new TextElementStyle(
                        ElementStyle.Leaf(new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit())),
                        new TextStyle(20f, new ClayColor(200, 210, 240), wrap: false)));

                NotificationItem(clui, "System", "Layout engine updated to v2.4 with z-sorted passes.", new ClayColor(96, 165, 250), "2m ago");
                NotificationItem(clui, "Build", "All 47 tests passing. Coverage at 94%.", new ClayColor(132, 204, 96), "5m ago");
                NotificationItem(clui, "Warning", "Font atlas approaching 2048px limit.", new ClayColor(245, 180, 70), "12m ago");
                NotificationItem(clui, "Deploy", "Snapshot exported successfully to disk.", new ClayColor(167, 139, 250), "1h ago");
            }
        }
    }
}

static void ToggleRow(ClayGui clui, string label, ref bool value)
{
    using (clui.Element()
        .GrowHorizontal()
        .FitVertical()
        .Padding(4f, 8f)
        .HorizontalLayout()
        .CrossAlignment(Alignment.Center))
    {
        clui.Text(label,
            new TextElementStyle(
                new ElementStyle(layout: new LayoutConfig(sizing: new ElementSizing(SizeSpec.Grow(), SizeSpec.Fit()))),
                new TextStyle(15f, new ClayColor(180, 185, 200), wrap: false)));

        var trackColor = value ? new ClayColor(80, 140, 255) : new ClayColor(55, 60, 78);
        var knobOffset = value ? 20f : 2f;

        using (clui.Element()
            .Animated(0.16f)
            .BackgroundColor(trackColor)
            .CornerRadius(12f)
            .Hovered(out var trackHovered)
            .OverlayColor(trackHovered ? new ClayColor(255, 255, 255, 20) : ClayColor.Transparent)
            .Clicked(out var toggled)
            .Size(new Vector2(44f, 24f)))
        {
            if (toggled) value = !value;

            using (clui.Element()
                .PositionMode(PositionMode.Absolute)
                .AbsolutePosition(new AbsolutePosition(Alignment.Start, Alignment.Start, knobOffset, 2f))
                .BackgroundColor(new ClayColor(240, 242, 250))
                .CornerRadius(10f)
                .Size(new Vector2(20f, 20f)))
            { }
        }
    }
}

static void ProgressBar(ClayGui clui, string label, float progress, ClayColor color)
{
    progress = Math.Clamp(progress, 0f, 1f);
    var pct = (int)(progress * 100f);

    using (clui.Element()
        .Gap(6f)
        .GrowHorizontal()
        .FitVertical()
        .VerticalLayout())
    {
        using (clui.Element()
            .GrowHorizontal()
            .FitVertical()
            .HorizontalLayout())
        {
            clui.Text(label,
                new TextElementStyle(
                    new ElementStyle(layout: new LayoutConfig(sizing: new ElementSizing(SizeSpec.Grow(), SizeSpec.Fit()))),
                    new TextStyle(14f, new ClayColor(160, 168, 190), wrap: false)));
            clui.Text($"{pct}%",
                new TextElementStyle(
                    ElementStyle.Leaf(new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit())),
                    new TextStyle(14f, new ClayColor(160, 168, 190), wrap: false)));
        }

        using (clui.Element()
            .BackgroundColor(new ClayColor(40, 44, 60))
            .CornerRadius(4f)
            .GrowHorizontal()
            .FitVertical()
            .Size(new Vector2(0f, 8f))
            .ClipContent()
            .HorizontalLayout())
        {
            using (clui.Element()
                .Animated(0.2f)
                .BackgroundColor(color)
                .CornerRadius(4f)
                .FitHorizontal(MathF.Max(4f, progress * 400f))
                .GrowVertical())
            { }
        }
    }
}

static void NotificationItem(ClayGui clui, string category, string message, ClayColor accent, string timestamp)
{
    using (clui.Element()
        .Animated(0.16f)
        .BackgroundColor(new ClayColor(32, 36, 50))
        .Border(new Thickness(1f), new ClayColor(46, 50, 66))
        .CornerRadius(12f)
        .Hovered(out var hovered)
        .OverlayColor(hovered ? new ClayColor(255, 255, 255, 10) : ClayColor.Transparent)
        .Padding(14f, 12f)
        .Gap(10f)
        .GrowHorizontal()
        .FitVertical()
        .HorizontalLayout()
        .CrossAlignment(Alignment.Center))
    {
        using (clui.Element()
            .BackgroundColor(accent)
            .CornerRadius(5f)
            .Size(new Vector2(10f, 10f)))
        { }

        using (clui.Element()
            .Gap(2f)
            .GrowHorizontal()
            .FitVertical()
            .VerticalLayout())
        {
            using (clui.Element()
                .GrowHorizontal()
                .FitVertical()
                .HorizontalLayout())
            {
                clui.Text(category,
                    new TextElementStyle(
                        new ElementStyle(layout: new LayoutConfig(sizing: new ElementSizing(SizeSpec.Grow(), SizeSpec.Fit()))),
                        new TextStyle(14f, accent, wrap: false)));
                clui.Text(timestamp,
                    new TextElementStyle(
                        ElementStyle.Leaf(new ElementSizing(SizeSpec.Fit(), SizeSpec.Fit())),
                        new TextStyle(12f, new ClayColor(90, 96, 115), wrap: false)));
            }

            clui.Text(message,
                new TextElementStyle(
                    new ElementStyle(layout: new LayoutConfig(sizing: new ElementSizing(SizeSpec.Grow(), SizeSpec.Fit()))),
                    new TextStyle(14f, new ClayColor(160, 168, 190), lineHeight: 20f, wrap: true)));
        }
    }
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
        .Animated(0.18f)
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

static byte LerpByte(byte from, byte to, float factor)
{
    return (byte)Math.Clamp(MathF.Round(from + ((to - from) * factor)), 0f, 255f);
}

static ClayColor WarmShift(ClayColor c)
{
    return new ClayColor(
        (byte)Math.Min(255, c.R + 30),
        (byte)Math.Max(0, c.G - 20),
        (byte)Math.Max(0, c.B - 50),
        c.A);
}

sealed class DemoState
{
    public int Counter { get; set; }

    public bool ToastVisible = true;

    public float LastObservedScrollOffset;

    public int ActivePage;
    public float SidebarReveal; // 0..1 animated reveal factor

    public bool Toggle1 = true;
    public bool Toggle2;
    public bool Toggle3 = true;
    public float SliderValue = 0.62f;
    public int SelectedSwatch = 3;
}
