# Reaching SDF font rendering in `Raylib-cs`

This note is meant for a developer who wants to render scalable, sharp text in `Raylib-cs` using Signed Distance Fields (SDF), while staying grounded in the actual `raylib-cs` package and the upstream raylib workflow.

## What is directly confirmed in `raylib-cs`

These points are directly grounded in the current `raylib-cs` repository:

- `raylib-cs` targets `net6.0` and `net8.0`, and its README says it uses the official **raylib 5.5** release to build the native libraries.
- `Raylib_cs.FontType` contains `Sdf`, and the source comment says it is **"SDF font generation, requires external shader"**.
- `Raylib_cs.Raylib` exposes shader-mode functions such as `BeginShaderMode(...)` and shader-loading functions such as `LoadShaderFromMemory(...)`.
- `Raylib.Utils.cs` adds C#-friendly overloads for shader loading and shader location lookup, so loading shader code from strings is ergonomic on the C# side.

Those facts are enough to conclude that the package is aligned with the upstream raylib SDF model:

1. you need an SDF-capable font/atlas,
2. you need a custom shader while drawing that font,
3. this is not just "load TTF normally and hope it stays sharp".

## What upstream raylib confirms

The official upstream raylib example for SDF fonts shows the intended pipeline:

1. `LoadFileData(...)`
2. `LoadFontData(..., FONT_SDF, ...)`
3. `GenImageFontAtlas(...)`
4. `LoadTextureFromImage(...)`
5. `SetTextureFilter(..., TEXTURE_FILTER_BILINEAR)`
6. `BeginShaderMode(shader)`
7. `DrawTextEx(...)`
8. `EndShaderMode()`

That example also shows an important practical detail: fonts/textures must be created **after** `InitWindow(...)`, because GPU resources need an OpenGL context.

## The practical conclusion

In `Raylib-cs`, the goal should be framed like this:

> “Build or obtain an SDF font atlas, then render it through an SDF shader with `DrawTextEx`.”

Not like this:

> “Find a magic smoother `LoadFontEx` setting.”

`LoadFontEx` may still be useful for ordinary font loading, but SDF is a different pipeline.

## The safest implementation strategy

There are two realistic paths.

### Path A — Direct `Raylib-cs` SDF pipeline

Use this if your installed package surface exposes the low-level font-generation entry points expected from raylib 5.5, namely the equivalents of:

- `LoadFontData(...)`
- `GenImageFontAtlas(...)`
- `LoadTextureFromImage(...)`
- `UnloadFontData(...)`
- `DrawTextEx(...)`

This is the closest translation of the official raylib example.

#### What the flow should look like

1. Initialize window.
2. Load TTF bytes.
3. Generate glyph data with `FontType.Sdf` / `FONT_SDF` semantics.
4. Build an atlas image from the glyphs.
5. Upload atlas image into a `Texture2D`.
6. Set atlas filtering to bilinear.
7. Load the SDF shader.
8. During drawing, wrap `DrawTextEx(...)` with `BeginShaderMode(...)` / `EndShaderMode()`.
9. Release CPU-side atlas data and shader/font resources when done.

#### What matters most technically

- The atlas must be built as **SDF**, not ordinary antialiased alpha glyphs.
- The shader is not optional. `FontType.Sdf` in the package itself explicitly says it requires an external shader.
- The SDF texture should typically use **bilinear filtering**.
- The text draw call should be `DrawTextEx(...)` using your custom `Font`.
- Resource lifetime matters: upload to GPU, then free temporary CPU resources you no longer need.

#### Skeleton of the idea

```csharp
InitWindow(...);

// 1) Build or load SDF font atlas
// 2) Create Font that points to that atlas
// 3) Load SDF shader

while (!WindowShouldClose())
{
    BeginDrawing();
    ClearBackground(Color.RayWhite);

    BeginShaderMode(sdfShader);
    DrawTextEx(sdfFont, text, position, renderSize, spacing, Color.Black);
    EndShaderMode();

    EndDrawing();
}
```

That skeleton is the right mental model even before you fill in the font-generation details.

### Path B — Offline SDF atlas generation, simple runtime rendering

Use this if:

- your installed `Raylib-cs` version does not expose one of the low-level font-generation functions you need,
- or you want a simpler runtime path,
- or you want to avoid extra unsafe code and pointer-heavy setup.

In this path, you generate the SDF atlas **outside** your app, then ship:

- the atlas texture,
- the glyph metrics/rectangles,
- and the SDF fragment shader.

At runtime, your app mostly does the easy part:

- load atlas texture,
- reconstruct the `Font` metadata,
- render with `BeginShaderMode(...)` + `DrawTextEx(...)`.

This is often the most maintainable route if you care more about product code than about reproducing raylib’s full font-building pipeline inside C#.

## What I would recommend first

For a real project, I would do this in order:

1. **Confirm the package surface in your local IDE**
   - Check whether your installed `Raylib-cs` exposes the direct low-level font APIs you want.
   - Since `raylib-cs` is passively maintained, don’t assume every upstream helper will always be equally convenient from C#.

2. **Prove the shader path first**
   - Before generating fonts dynamically, make sure you can:
     - load a custom shader,
     - enter `BeginShaderMode(...)`,
     - render text with `DrawTextEx(...)`,
     - and see the shader affect the result.

3. **Only then wire in font generation**
   - This isolates “font data problems” from “shader/rendering problems”.

4. **If the direct path gets annoying, switch to offline atlas generation early**
   - That is usually the smart engineering tradeoff.

## Common mistakes to avoid

- Creating fonts/textures before `InitWindow(...)`.
- Generating a normal glyph atlas and expecting SDF-like scaling quality.
- Forgetting the shader when drawing SDF text.
- Forgetting bilinear filtering on the SDF atlas.
- Treating this as a pure C# text API problem instead of a font-atlas + shader problem.
- Spending too long trying to coerce `LoadFontEx(...)` into doing a job that belongs to `LoadFontData(..., SDF)` + atlas generation + shader rendering.

## What is definitely available to you in `Raylib-cs`

Even before you settle the font-building step, these pieces are already clearly available from the repository:

- `FontType.Sdf`
- `BeginShaderMode(...)`
- `EndShaderMode()`
- `LoadShaderFromMemory(...)`
- C# string overloads for shader loading and shader location lookup in `Raylib.Utils.cs`

That means the **rendering half** of the solution is solidly supported.

## What still needs local verification

I was able to directly verify the enum and shader-related pieces from the `raylib-cs` repository in this environment. I was not able to cleanly scrape symbol-level proof from GitHub’s browser layer here for every low-level font-building function in the binding, even though the upstream raylib 5.5 example clearly uses that pipeline.

So, the developer action item is simple:

- open your installed `Raylib-cs` package in IDE navigation,
- search for `LoadFontData`, `GenImageFontAtlas`, and `DrawTextEx`,
- then choose Path A or Path B accordingly.

That is the fastest way to avoid guessing and get to a working SDF implementation.

## Recommended next milestone

The next milestone should not be “full font system”. It should be:

- render one line of text,
- using a known SDF atlas,
- through a custom SDF shader,
- with zoomable font size,
- and visually confirm that it scales better than a normal alpha font.

Once that works, the rest is just resource management and tooling.

## References

### `raylib-cs`

- Repository README: `raylib-cs` targets `net6.0` / `net8.0` and uses the official raylib 5.5 native release.  
  https://github.com/raylib-cs/raylib-cs

- `Font.cs`: `FontType.Sdf` exists and is documented as requiring an external shader.  
  https://github.com/raylib-cs/raylib-cs/blob/main/Raylib-cs/types/Font.cs

- `Raylib.cs`: shader-mode interop and `LoadShaderFromMemory(...)`.  
  https://github.com/raylib-cs/raylib-cs/blob/main/Raylib-cs/interop/Raylib.cs

- `Raylib.Utils.cs`: C# convenience overloads for `LoadShader(...)`, `LoadShaderFromMemory(...)`, `GetShaderLocation(...)`, and `GetShaderLocationAttrib(...)`.  
  https://github.com/raylib-cs/raylib-cs/blob/main/Raylib-cs/types/Raylib.Utils.cs

### Upstream raylib

- `raylib.h`: `FONT_SDF` / `FontType` definition.  
  https://github.com/raysan5/raylib/blob/master/src/raylib.h

- Official SDF example: `text_font_sdf.c`.  
  https://github.com/raysan5/raylib/blob/master/examples/text/text_font_sdf.c

- Online example page: SDF font generation from TTF font.  
  https://www.raylib.com/examples/text/loader.html?name=text_font_sdf
