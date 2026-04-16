using System.Numerics;
using ClaySharp;
using Raylib_cs;
using RL = Raylib_cs.Raylib;

namespace ClaySharp.Raylib;

public sealed class ClayGui
{
    private const ulong FirstInteractionId = 1024UL;
    private const ulong FirstLegacyScrollId = 3UL;

    private readonly ClayContext _context;
    private readonly ITextMeasurer _textMeasurer;
    private readonly HashSet<ulong> _trackedElementIds = [];
    private readonly Dictionary<ulong, RectF> _previousBounds = [];
    private readonly Dictionary<ulong, RectF> _previousFlowContentBounds = [];

    private Vector2 _viewport;
    private ulong _hoveredElementId;
    private bool _leftPressed;
    private float _mouseWheelMove;
    private ulong _nextInteractionId;
    private bool _legacyScrollAssigned;

    public ClayGui(ClayContext context, ITextMeasurer textMeasurer, ClayRaylibRenderer renderer)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _textMeasurer = textMeasurer ?? throw new ArgumentNullException(nameof(textMeasurer));
        _ = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _viewport = new Vector2(RL.GetScreenWidth(), RL.GetScreenHeight());
        _nextInteractionId = FirstInteractionId;
    }

    public int ElementCount => _context.ElementCount;

    public ReadOnlySpan<RenderCommand> RenderCommands => _context.RenderCommands;

    public void SetViewport(Vector2 viewport)
    {
        _viewport = viewport;
    }

    public void Begin()
    {
        var mousePosition = RL.GetMousePosition();
        _hoveredElementId = _context.TryHitTest(mousePosition, out var elementId) ? elementId : 0UL;
        _leftPressed = RL.IsMouseButtonPressed(MouseButton.Left);
        _mouseWheelMove = RL.GetMouseWheelMove();

        CapturePreviousElementMetrics();
        _trackedElementIds.Clear();

        _nextInteractionId = FirstInteractionId;
        _legacyScrollAssigned = false;

        var viewport = _viewport;
        if (viewport.X <= 0f || viewport.Y <= 0f)
        {
            viewport = new Vector2(RL.GetScreenWidth(), RL.GetScreenHeight());
        }

        _context.BeginLayout(viewport, _textMeasurer);
    }

    public void End()
    {
        _context.EndLayout();
    }

    public ElementBuilder Element()
    {
        var style = new ElementStyle(layout: new LayoutConfig());
        _context.OpenElement(style);
        return new ElementBuilder(this, style);
    }

    public TextBuilder Text(string text)
    {
        return new TextBuilder(this, text ?? string.Empty);
    }

    public void Text(string text, in TextElementStyle style)
    {
        _context.Text(text, style);
    }

    private static LayoutConfig WithLayout(
        in LayoutConfig layout,
        LayoutAxis? axis = null,
        ElementSizing? sizing = null,
        Thickness? padding = null,
        float? gap = null,
        Alignment? mainAlignment = null,
        Alignment? crossAlignment = null,
        PositionMode? positionMode = null,
        AbsolutePosition? absolutePosition = null,
        float? aspectRatio = null,
        bool? clipContent = null,
        Vector2? scrollOffset = null)
    {
        return new LayoutConfig(
            axis ?? layout.Axis,
            sizing ?? layout.Sizing,
            padding ?? layout.Padding,
            gap ?? layout.Gap,
            mainAlignment ?? layout.MainAlignment,
            crossAlignment ?? layout.CrossAlignment,
            positionMode ?? layout.PositionMode,
            absolutePosition ?? layout.AbsolutePosition,
            aspectRatio ?? layout.AspectRatio,
            clipContent ?? layout.ClipContent,
            scrollOffset ?? layout.ScrollOffset);
    }

    private static BoxStyle WithBox(
        in BoxStyle box,
        ClayColor? backgroundColor = null,
        BorderStyle? border = null,
        CornerRadius? cornerRadius = null,
        ClayColor? overlayColor = null)
    {
        return new BoxStyle(
            backgroundColor ?? box.BackgroundColor,
            border ?? box.Border,
            cornerRadius ?? box.CornerRadius,
            overlayColor ?? box.OverlayColor);
    }

    private ulong AllocateInteractionId() => _nextInteractionId++;

    private ulong AllocateScrollId()
    {
        if (!_legacyScrollAssigned)
        {
            _legacyScrollAssigned = true;
            return FirstLegacyScrollId;
        }

        return AllocateInteractionId();
    }

    private bool IsHovered(ulong elementId) => elementId != 0 && elementId == _hoveredElementId;

    private bool IsClicked(ulong elementId) => elementId != 0 && elementId == _hoveredElementId && _leftPressed;

    private void TrackElementId(ulong elementId)
    {
        if (elementId != 0)
        {
            _trackedElementIds.Add(elementId);
        }
    }

    private void CapturePreviousElementMetrics()
    {
        _previousBounds.Clear();
        _previousFlowContentBounds.Clear();

        foreach (var elementId in _trackedElementIds)
        {
            if (_context.TryGetBounds(elementId, out var bounds))
            {
                _previousBounds[elementId] = bounds;
            }

            if (_context.TryGetFlowContentBounds(elementId, out var contentBounds))
            {
                _previousFlowContentBounds[elementId] = contentBounds;
            }
        }
    }

    private bool TryGetPreviousBounds(ulong elementId, out RectF bounds)
    {
        return _previousBounds.TryGetValue(elementId, out bounds);
    }

    private bool TryGetPreviousFlowContentBounds(ulong elementId, out RectF bounds)
    {
        return _previousFlowContentBounds.TryGetValue(elementId, out bounds);
    }

    public struct ElementBuilder : IDisposable
    {
        private readonly ClayGui _gui;
        private ElementStyle _style;
        private ulong _assignedId;
        private bool _disposed;

        internal ElementBuilder(ClayGui gui, ElementStyle style)
        {
            _gui = gui;
            _style = style;
            _assignedId = style.Id;
            _disposed = false;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _gui._context.CloseCurrentElement();
        }

        public ElementBuilder Color(byte r, byte g, byte b, byte a = 255)
        {
            return BackgroundColor(new ClayColor(r, g, b, a));
        }

        public ElementBuilder Color(ClayColor color)
        {
            return BackgroundColor(color);
        }

        public ElementBuilder BackgroundColor(ClayColor color)
        {
            _style = new ElementStyle(_style.Id, _style.Layout, WithBox(_style.Box, backgroundColor: color));
            Apply();
            return this;
        }

        public ElementBuilder Border(Thickness widths, ClayColor color)
        {
            _style = new ElementStyle(_style.Id, _style.Layout, WithBox(_style.Box, border: new BorderStyle(widths, color)));
            Apply();
            return this;
        }

        public ElementBuilder CornerRadius(float radius)
        {
            _style = new ElementStyle(_style.Id, _style.Layout, WithBox(_style.Box, cornerRadius: new CornerRadius(radius)));
            Apply();
            return this;
        }

        public ElementBuilder Padding(float uniform)
        {
            return Padding(new Thickness(uniform));
        }

        public ElementBuilder Padding(float horizontal, float vertical)
        {
            return Padding(new Thickness(horizontal, vertical, horizontal, vertical));
        }

        public ElementBuilder Gap(float gap)
        {
            _style = new ElementStyle(_style.Id, WithLayout(_style.Layout, gap: gap), _style.Box);
            Apply();
            return this;
        }

        public ElementBuilder Grow()
        {
            _style = new ElementStyle(_style.Id, WithLayout(_style.Layout, sizing: ElementSizing.Grow()), _style.Box);
            Apply();
            return this;
        }

        public ElementBuilder GrowHorizontal()
        {
            var sizing = new ElementSizing(SizeSpec.Grow(), _style.Layout.Sizing.Height);
            _style = new ElementStyle(_style.Id, WithLayout(_style.Layout, sizing: sizing), _style.Box);
            Apply();
            return this;
        }

        public ElementBuilder GrowVertical()
        {
            var sizing = new ElementSizing(_style.Layout.Sizing.Width, SizeSpec.Grow());
            _style = new ElementStyle(_style.Id, WithLayout(_style.Layout, sizing: sizing), _style.Box);
            Apply();
            return this;
        }

        public ElementBuilder FitHorizontal()
        {
            var sizing = new ElementSizing(SizeSpec.Fit(), _style.Layout.Sizing.Height);
            _style = new ElementStyle(_style.Id, WithLayout(_style.Layout, sizing: sizing), _style.Box);
            Apply();
            return this;
        }

        public ElementBuilder FitHorizontal(float width)
        {
            var sizing = new ElementSizing(SizeSpec.Fixed(width), _style.Layout.Sizing.Height);
            _style = new ElementStyle(_style.Id, WithLayout(_style.Layout, sizing: sizing), _style.Box);
            Apply();
            return this;
        }

        public ElementBuilder FitVertical()
        {
            var sizing = new ElementSizing(_style.Layout.Sizing.Width, SizeSpec.Fit());
            _style = new ElementStyle(_style.Id, WithLayout(_style.Layout, sizing: sizing), _style.Box);
            Apply();
            return this;
        }

        public ElementBuilder HorizontalLayout()
        {
            _style = new ElementStyle(_style.Id, WithLayout(_style.Layout, axis: LayoutAxis.Horizontal), _style.Box);
            Apply();
            return this;
        }

        public ElementBuilder VerticalLayout()
        {
            _style = new ElementStyle(_style.Id, WithLayout(_style.Layout, axis: LayoutAxis.Vertical), _style.Box);
            Apply();
            return this;
        }

        public ElementBuilder CrossAlignment(Alignment alignment)
        {
            _style = new ElementStyle(_style.Id, WithLayout(_style.Layout, crossAlignment: alignment), _style.Box);
            Apply();
            return this;
        }

        public ElementBuilder Hovered(out bool hovered)
        {
            hovered = _gui.IsHovered(EnsureInteractionId());
            return this;
        }

        public ElementBuilder OverlayColor(ClayColor color)
        {
            _style = new ElementStyle(_style.Id, _style.Layout, WithBox(_style.Box, overlayColor: color));
            Apply();
            return this;
        }

        public ElementBuilder Clicked(out bool clicked)
        {
            clicked = _gui.IsClicked(EnsureInteractionId());
            return this;
        }

        public ElementBuilder Size(Vector2 size)
        {
            _style = new ElementStyle(_style.Id, WithLayout(_style.Layout, sizing: ElementSizing.Fixed(size.X, size.Y)), _style.Box);
            Apply();
            return this;
        }

        public ElementBuilder ClipContent()
        {
            _style = new ElementStyle(_style.Id, WithLayout(_style.Layout, clipContent: true), _style.Box);
            Apply();
            return this;
        }

        public ElementBuilder ScrollOffset(Vector2 offset)
        {
            EnsureScrollId();
            _style = new ElementStyle(_style.Id, WithLayout(_style.Layout, scrollOffset: offset), _style.Box);
            Apply();
            return this;
        }

        public ElementBuilder ScrollY(ref float offset, float step = 36f)
        {
            return ScrollYCore(ref offset, null, step);
        }

        public ElementBuilder ScrollY(ref float offset, float maxOffset, float step = 36f)
        {
            return ScrollYCore(ref offset, MathF.Max(0f, maxOffset), step);
        }

        public ElementBuilder PositionMode(PositionMode mode)
        {
            _style = new ElementStyle(_style.Id, WithLayout(_style.Layout, positionMode: mode), _style.Box);
            Apply();
            return this;
        }

        public ElementBuilder AbsolutePosition(AbsolutePosition position)
        {
            _style = new ElementStyle(_style.Id, WithLayout(_style.Layout, absolutePosition: position), _style.Box);
            Apply();
            return this;
        }

        private ElementBuilder Padding(Thickness padding)
        {
            _style = new ElementStyle(_style.Id, WithLayout(_style.Layout, padding: padding), _style.Box);
            Apply();
            return this;
        }

        private ulong EnsureInteractionId()
        {
            if (_assignedId != 0)
            {
                _gui.TrackElementId(_assignedId);
                return _assignedId;
            }

            _assignedId = _gui.AllocateInteractionId();
            _style = new ElementStyle(_assignedId, _style.Layout, _style.Box);
            _gui.TrackElementId(_assignedId);
            Apply();
            return _assignedId;
        }

        private void EnsureScrollId()
        {
            if (_assignedId != 0)
            {
                _gui.TrackElementId(_assignedId);
                return;
            }

            _assignedId = _gui.AllocateScrollId();
            _style = new ElementStyle(_assignedId, _style.Layout, _style.Box);
            _gui.TrackElementId(_assignedId);
            Apply();
        }

        private void Apply()
        {
            _gui._context.UpdateCurrentElementStyle(_style);
        }

        private ElementBuilder ScrollYCore(ref float offset, float? maxOffset, float step)
        {
            EnsureScrollId();

            var resolvedMaxOffset = GetMaxScrollOffset();
            if (maxOffset.HasValue)
            {
                resolvedMaxOffset = MathF.Min(resolvedMaxOffset, maxOffset.Value);
            }

            if (_gui.IsHovered(_assignedId) && MathF.Abs(_gui._mouseWheelMove) > 0.001f)
            {
                offset -= _gui._mouseWheelMove * step;
            }

            offset = Math.Clamp(offset, 0f, resolvedMaxOffset);

            var scrollOffset = new Vector2(_style.Layout.ScrollOffset.X, offset);
            _style = new ElementStyle(_style.Id, WithLayout(_style.Layout, scrollOffset: scrollOffset), _style.Box);
            Apply();
            return this;
        }

        private float GetMaxScrollOffset()
        {
            if (!_gui.TryGetPreviousBounds(_assignedId, out var bounds))
            {
                return 0f;
            }

            if (!_gui.TryGetPreviousFlowContentBounds(_assignedId, out var contentBounds))
            {
                return 0f;
            }

            var visibleHeight = MathF.Max(0f, bounds.Height - _style.Layout.Padding.Vertical);
            return MathF.Max(0f, contentBounds.Height - visibleHeight);
        }
    }

    public struct TextBuilder
    {
        private readonly ClayGui _gui;
        private readonly string _text;
        private TextStyle _style;

        internal TextBuilder(ClayGui gui, string text)
        {
            _gui = gui;
            _text = text;
            _style = new TextStyle(16f, ClayColor.Black);
        }

        public TextBuilder FontSize(float fontSize)
        {
            _style = new TextStyle(fontSize, _style.Color, _style.FontId, _style.LetterSpacing, _style.LineHeight, _style.HorizontalAlignment, _style.Wrap);
            return this;
        }

        public TextBuilder Color(ClayColor color)
        {
            _style = new TextStyle(_style.FontSize, color, _style.FontId, _style.LetterSpacing, _style.LineHeight, _style.HorizontalAlignment, _style.Wrap);
            return this;
        }

        public TextBuilder LetterSpacing(float letterSpacing)
        {
            _style = new TextStyle(_style.FontSize, _style.Color, _style.FontId, letterSpacing, _style.LineHeight, _style.HorizontalAlignment, _style.Wrap);
            return this;
        }

        public TextBuilder LineHeight(float lineHeight)
        {
            _style = new TextStyle(_style.FontSize, _style.Color, _style.FontId, _style.LetterSpacing, lineHeight, _style.HorizontalAlignment, _style.Wrap);
            return this;
        }

        public TextBuilder HorizontalAlignment(Alignment alignment)
        {
            _style = new TextStyle(_style.FontSize, _style.Color, _style.FontId, _style.LetterSpacing, _style.LineHeight, alignment, _style.Wrap);
            return this;
        }

        public void Wrap()
        {
            Emit(wrap: true);
        }

        public void NoWrap()
        {
            Emit(wrap: false);
        }

        private void Emit(bool wrap)
        {
            var style = new TextStyle(_style.FontSize, _style.Color, _style.FontId, _style.LetterSpacing, _style.LineHeight, _style.HorizontalAlignment, wrap);
            var width = wrap || style.HorizontalAlignment != Alignment.Start ? SizeSpec.Grow() : SizeSpec.Fit();
            var element = new ElementStyle(layout: new LayoutConfig(sizing: new ElementSizing(width, SizeSpec.Fit())));
            _gui._context.Text(_text, new TextElementStyle(element, style));
        }
    }
}
