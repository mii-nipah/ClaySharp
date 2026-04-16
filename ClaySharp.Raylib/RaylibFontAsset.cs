using System.Text;
using Raylib_cs;

namespace ClaySharp.Raylib;

public readonly struct RaylibFontFace
{
    public RaylibFontFace(Font font)
    {
        Font = font;
        Shader = default;
        UsesShader = false;
    }

    public RaylibFontFace(Font font, Shader shader)
    {
        Font = font;
        Shader = shader;
        UsesShader = true;
    }

    public Font Font { get; }

    public Shader Shader { get; }

    public bool UsesShader { get; }
}

public sealed class RaylibFontAsset : IDisposable
{
    private const int DefaultGlyphCount = 95;
    private const int SdfAtlasPackMethod = 1;
    private readonly bool _ownsFont;
    private readonly bool _ownsShader;
    private bool _disposed;

    private const string SdfFragmentShaderSource = """
#version 330

in vec2 fragTexCoord;
in vec4 fragColor;

uniform sampler2D texture0;
uniform vec4 colDiffuse;

out vec4 finalColor;

void main()
{
    float distanceFromOutline = texture(texture0, fragTexCoord).a - 0.5;
    float distanceChangePerFragment = length(vec2(dFdx(distanceFromOutline), dFdy(distanceFromOutline)));
    float alpha = smoothstep(-distanceChangePerFragment, distanceChangePerFragment, distanceFromOutline);
    finalColor = vec4(fragColor.rgb, fragColor.a * alpha);
}
""";

    private RaylibFontAsset(RaylibFontFace face, bool ownsFont, bool ownsShader)
    {
        Face = face;
        _ownsFont = ownsFont;
        _ownsShader = ownsShader;
    }

    public RaylibFontFace Face { get; private set; }

    public Font Font => Face.Font;

    public Shader Shader => Face.Shader;

    public bool UsesShader => Face.UsesShader;

    public static RaylibFontAsset Wrap(Font font, bool ownsFont = false) => new(new RaylibFontFace(font), ownsFont, ownsShader: false);

    public unsafe static RaylibFontAsset LoadSdf(string fontPath, int baseSize = 64, ReadOnlySpan<int> codepoints = default)
    {
        if (string.IsNullOrWhiteSpace(fontPath))
        {
            throw new ArgumentException("Font path must be provided.", nameof(fontPath));
        }

        if (baseSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baseSize), baseSize, "Base size must be greater than zero.");
        }

        var fileData = File.ReadAllBytes(fontPath);
        var requestedGlyphCount = codepoints.Length;
        var resolvedGlyphCount = requestedGlyphCount > 0 ? requestedGlyphCount : DefaultGlyphCount;

        GlyphInfo* glyphs = null;
        Rectangle* recs = null;
        var atlas = default(Image);
        var font = default(Font);
        var shader = default(Shader);

        try
        {
            fixed (byte* fileDataPointer = fileData)
            fixed (int* codepointPointer = codepoints)
            {
                glyphs = Raylib_cs.Raylib.LoadFontData(
                    fileDataPointer,
                    fileData.Length,
                    baseSize,
                    requestedGlyphCount == 0 ? null : codepointPointer,
                    requestedGlyphCount,
                    FontType.Sdf);
            }

            if (glyphs == null)
            {
                throw new InvalidOperationException($"Failed to generate SDF glyphs from '{fontPath}'.");
            }

            atlas = Raylib_cs.Raylib.GenImageFontAtlas(glyphs, &recs, requestedGlyphCount, baseSize, 0, SdfAtlasPackMethod);
            if (atlas.Data == null || recs == null)
            {
                throw new InvalidOperationException($"Failed to generate an SDF atlas for '{fontPath}'.");
            }

            font = new Font
            {
                BaseSize = baseSize,
                GlyphCount = resolvedGlyphCount,
                GlyphPadding = 0,
                Texture = Raylib_cs.Raylib.LoadTextureFromImage(atlas),
                Recs = recs,
                Glyphs = glyphs
            };

            if (font.Texture.Id == 0)
            {
                throw new InvalidOperationException($"Failed to upload the SDF atlas texture for '{fontPath}'.");
            }

            Raylib_cs.Raylib.SetTextureFilter(font.Texture, TextureFilter.Bilinear);

            shader = LoadSdfShader();
            if (!Raylib_cs.Raylib.IsShaderValid(shader))
            {
                throw new InvalidOperationException("Failed to compile the SDF text shader.");
            }

            return new RaylibFontAsset(new RaylibFontFace(font, shader), ownsFont: true, ownsShader: true);
        }
        catch
        {
            if (shader.Id != 0)
            {
                Raylib_cs.Raylib.UnloadShader(shader);
            }

            if (font.Recs != null)
            {
                Raylib_cs.Raylib.UnloadFont(font);
            }
            else if (glyphs != null)
            {
                Raylib_cs.Raylib.UnloadFontData(glyphs, resolvedGlyphCount);
            }

            throw;
        }
        finally
        {
            if (atlas.Data != null)
            {
                Raylib_cs.Raylib.UnloadImage(atlas);
            }
        }
    }

    public static int[] CreateCodepointRange(int startInclusive, int endInclusive)
    {
        if (startInclusive > endInclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(startInclusive), startInclusive, "Range start must be less than or equal to the range end.");
        }

        var count = checked(endInclusive - startInclusive + 1);
        var codepoints = new int[count];
        for (var index = 0; index < count; index++)
        {
            codepoints[index] = startInclusive + index;
        }

        return codepoints;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_ownsShader && Face.UsesShader && Face.Shader.Id != 0)
        {
            Raylib_cs.Raylib.UnloadShader(Face.Shader);
        }

        if (_ownsFont && Face.Font.Texture.Id != 0)
        {
            Raylib_cs.Raylib.UnloadFont(Face.Font);
        }

        Face = default;
        _disposed = true;
    }

    private unsafe static Shader LoadSdfShader()
    {
        var fragmentShaderBytes = Encoding.UTF8.GetBytes(SdfFragmentShaderSource + "\0");
        fixed (byte* fragmentShaderPointer = fragmentShaderBytes)
        {
            return Raylib_cs.Raylib.LoadShaderFromMemory((sbyte*)null, (sbyte*)fragmentShaderPointer);
        }
    }
}
