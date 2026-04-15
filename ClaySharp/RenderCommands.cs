namespace ClaySharp;

public struct RenderCommand
{
    public RenderCommandType Type;
    public ulong ElementId;
    public RectF Bounds;
    public ClayColor Color;
    public Thickness Thickness;
    public CornerRadius CornerRadius;
    public TextSlice Text;
    public TextStyle TextStyle;
    public RectF SourceRegion;
    public bool UseSourceRegion;
    public object? Payload;
}