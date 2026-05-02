# ClaySharp

ClaySharp is an immediate-mode UI layout library for .NET. It builds a layout tree each frame, resolves sizes and positions, then emits a flat span of renderer-agnostic render commands for rectangles, borders, text, images, clipping, overlays, and custom payloads.

The repository currently ships two packages:

- `ClaySharp`: core layout engine and render command model.
- `ClaySharp.Raylib`: Raylib-cs text measurement, rendering, font assets, and a retained-state GUI helper layer.

`ClaySharp.Run` is a sample app, and `ClaySharp.Tests` is the test project. They are intentionally not packable.

## Install

```sh
dotnet add package ClaySharp
dotnet add package ClaySharp.Raylib
```

Install only `ClaySharp` if you want to provide your own renderer and `ITextMeasurer` implementation.

## Core Usage

```csharp
using System.Numerics;
using ClaySharp;

using var context = new ClayContext();

context.BeginLayout(new Vector2(1280, 720), textMeasurer);

using (context.Element(ElementStyle.Container(
    ElementSizing.Grow(),
    padding: Thickness.All(24),
    gap: 12,
    background: ClayColor.Rgba(24, 26, 32))))
{
    context.Text(
        "Hello from ClaySharp",
        new TextElementStyle(
            ElementStyle.Leaf(ElementSizing.Fit()),
            new TextStyle(24, ClayColor.White, wrap: false)));
}

context.EndLayout();

foreach (ref readonly var command in context.RenderCommands)
{
    // Draw with your renderer of choice.
}
```

## Raylib Usage

```csharp
using ClaySharp;
using ClaySharp.Raylib;
using Raylib_cs;

Raylib.InitWindow(1280, 720, "ClaySharp");

using var context = new ClayContext();
using var measurer = new RaylibTextMeasurer(_ => Raylib.GetFontDefault());
using var renderer = new ClayRaylibRenderer(_ => Raylib.GetFontDefault());
var gui = new ClayGui(context, measurer, renderer);

while (!Raylib.WindowShouldClose())
{
    gui.Begin();

    using (gui.Element()
        .Grow()
        .Padding(24)
        .Gap(12)
        .VerticalLayout())
    {
        gui.Text("Hello from Raylib");
    }

    gui.End();

    Raylib.BeginDrawing();
    Raylib.ClearBackground(Color.Black);
    renderer.Render(gui.RenderCommands);
    Raylib.EndDrawing();
}

Raylib.CloseWindow();
```

Raylib GPU assets such as custom fonts, shaders, and texture-backed font atlases should be disposed before `Raylib.CloseWindow()`.

## Development

```sh
dotnet restore ClaySharp.slnx
dotnet build ClaySharp.slnx
dotnet test ClaySharp.Tests/ClaySharp.Tests.csproj --no-build
```

Create local NuGet packages with:

```sh
dotnet pack ClaySharp/ClaySharp.csproj -c Release -o artifacts/packages
dotnet pack ClaySharp.Raylib/ClaySharp.Raylib.csproj -c Release -o artifacts/packages
```

Package metadata is centralized in `Directory.Build.props`. Update its `Version` value before publishing a new release.
