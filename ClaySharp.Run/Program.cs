
using System.Numerics;
using Raylib_cs;

Raylib.InitWindow(800, 600, "ClaySharp.Run");
Raylib.SetTargetFPS(60);
Raylib.SetWindowState(ConfigFlags.ResizableWindow | ConfigFlags.MaximizedWindow);

var font = Raylib.LoadFontEx("OpenSans-Regular.ttf", 128, null, 0);
Raylib.SetTextureFilter(font.Texture, TextureFilter.Point);

while (!Raylib.WindowShouldClose())
{
    Raylib.BeginDrawing();
    Raylib.ClearBackground(Color.SkyBlue);

    Raylib.DrawTextEx(font, "Hello, ClaySharp.Run!", new Vector2(10, 10), 32, 1, Color.Black);

    Raylib.EndDrawing();
}

Raylib.CloseWindow();
