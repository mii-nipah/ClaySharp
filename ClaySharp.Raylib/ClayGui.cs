using System.Numerics;
using ClaySharp;
using Raylib_cs;
using RL = Raylib_cs.Raylib;

namespace ClaySharp.Raylib;

public sealed class ClayGui
{
    private const float DefaultTransitionDuration = 0.22f;
    private const float DefaultTransitionOffset = 14f;
    private const float ScrollTranslationTolerance = 1f;
    private const int RetainedStateTtlFrames = 180;
    private const ulong AutomaticIdentityNamespace = 0xC1A94E1D9F42A6B7UL;
    private const ulong HashOffsetBasis = 14695981039346656037UL;
    private const ulong HashPrime = 1099511628211UL;

    private readonly ClayContext _context;
    private readonly ITextMeasurer _textMeasurer;
    private readonly HashSet<ulong> _trackedElementIds = [];
    private readonly Dictionary<ulong, RectF> _previousBounds = [];
    private readonly Dictionary<ulong, RectF> _previousFlowContentBounds = [];
    private readonly Dictionary<ulong, ScrollState> _scrollStates = [];
    private readonly Dictionary<ulong, TransitionState> _transitionStates = [];
    private readonly Dictionary<ulong, int> _transitionCommandIndices = [];
    private readonly Dictionary<ulong, ulong> _automaticIdentityCounts = [];
    private readonly List<float> _verticalScrollTranslations = [];

    private Vector2 _viewport;
    private ulong _hoveredElementId;
    private bool _leftPressed;
    private Vector2 _mouseWheelMove;
    private float _frameDeltaTime;
    private int _frameGeneration;
    private RenderCommand[] _animatedRenderCommands = Array.Empty<RenderCommand>();
    private int _animatedRenderCommandCount;

    public ClayGui(ClayContext context, ITextMeasurer textMeasurer, ClayRaylibRenderer renderer)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _textMeasurer = textMeasurer ?? throw new ArgumentNullException(nameof(textMeasurer));
        _ = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _viewport = new Vector2(RL.GetScreenWidth(), RL.GetScreenHeight());
    }

    public int ElementCount => _context.ElementCount;

    public ReadOnlySpan<RenderCommand> RenderCommands => _animatedRenderCommands.AsSpan(0, _animatedRenderCommandCount);

    public void SetViewport(Vector2 viewport)
    {
        _viewport = viewport;
    }

    public void Begin()
    {
        _frameGeneration++;
        _frameDeltaTime = MathF.Max(0f, RL.GetFrameTime());

        var mousePosition = RL.GetMousePosition();
        _hoveredElementId = _context.TryHitTest(mousePosition, out var elementId) ? elementId : 0UL;
        _leftPressed = RL.IsMouseButtonPressed(MouseButton.Left);
        _mouseWheelMove = RL.GetMouseWheelMoveV();
        _verticalScrollTranslations.Clear();

        CapturePreviousElementMetrics();
        _trackedElementIds.Clear();

        _automaticIdentityCounts.Clear();

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
        BuildAnimatedRenderCommands();
        CleanupRetainedState();
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
        Vector2? scrollOffset = null,
        int? zIndex = null,
        bool? transitionEnabled = null)
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
                scrollOffset ?? layout.ScrollOffset,
                zIndex ?? layout.ZIndex,
                transitionEnabled ?? layout.TransitionEnabled);
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

    private ulong ClaimAutomaticIdentityOrdinal(ulong semanticHash)
    {
        var ordinal = _automaticIdentityCounts.TryGetValue(semanticHash, out var currentOrdinal)
            ? currentOrdinal + 1UL
            : 1UL;
        _automaticIdentityCounts[semanticHash] = ordinal;
        return ordinal;
    }

    private bool IsHovered(ulong elementId) => elementId != 0 && elementId == _hoveredElementId;

    private bool IsClicked(ulong elementId) => elementId != 0 && elementId == _hoveredElementId && _leftPressed;

    private bool IsMouseOver(ulong elementId)
    {
        if (elementId == 0 || !_previousBounds.TryGetValue(elementId, out var bounds))
        {
            return false;
        }

        var mouse = RL.GetMousePosition();
        return mouse.X >= bounds.X && mouse.X <= bounds.X + bounds.Width
            && mouse.Y >= bounds.Y && mouse.Y <= bounds.Y + bounds.Height;
    }

    private void TrackElementId(ulong elementId)
    {
        if (elementId != 0)
        {
            _trackedElementIds.Add(elementId);
        }
    }

    private void ReplaceTrackedElementId(ulong previousId, ulong nextId)
    {
        if (previousId != 0 && previousId != nextId)
        {
            _trackedElementIds.Remove(previousId);
        }

        TrackElementId(nextId);
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

    private ulong ComposeAutomaticIdentity(in ElementStyle style, ulong ordinal)
    {
        var hash = ComputeAutomaticIdentityHash(in style);
        hash = HashValue(hash, AutomaticIdentityNamespace);
        hash = HashValue(hash, ordinal);
        return hash == 0 ? 1UL : hash;
    }

    private static ulong ComputeAutomaticIdentityHash(in ElementStyle style)
    {
        var hash = HashOffsetBasis;
        hash = HashValue(hash, (ulong)style.Layout.Axis);
        hash = HashValue(hash, (ulong)style.Layout.MainAlignment);
        hash = HashValue(hash, (ulong)style.Layout.CrossAlignment);
        hash = HashValue(hash, (ulong)style.Layout.PositionMode);
        hash = HashValue(hash, style.Layout.ClipContent ? 1UL : 0UL);
        hash = HashValue(hash, (ulong)(uint)style.Layout.ZIndex);
        hash = HashSizeSpec(hash, style.Layout.Sizing.Width);
        hash = HashSizeSpec(hash, style.Layout.Sizing.Height);
        hash = HashFloat(hash, style.Layout.Padding.Left);
        hash = HashFloat(hash, style.Layout.Padding.Top);
        hash = HashFloat(hash, style.Layout.Padding.Right);
        hash = HashFloat(hash, style.Layout.Padding.Bottom);
        hash = HashFloat(hash, style.Layout.Gap);
        hash = HashValue(hash, (ulong)style.Layout.AbsolutePosition.HorizontalAnchor);
        hash = HashValue(hash, (ulong)style.Layout.AbsolutePosition.VerticalAnchor);
        hash = HashFloat(hash, style.Layout.AspectRatio);
        hash = HashFloat(hash, style.Box.Border.Widths.Left);
        hash = HashFloat(hash, style.Box.Border.Widths.Top);
        hash = HashFloat(hash, style.Box.Border.Widths.Right);
        hash = HashFloat(hash, style.Box.Border.Widths.Bottom);
        hash = HashFloat(hash, style.Box.CornerRadius.TopLeft);
        hash = HashFloat(hash, style.Box.CornerRadius.TopRight);
        hash = HashFloat(hash, style.Box.CornerRadius.BottomRight);
        hash = HashFloat(hash, style.Box.CornerRadius.BottomLeft);
        return hash;
    }

    private static ulong HashSizeSpec(ulong hash, in SizeSpec spec)
    {
        hash = HashValue(hash, (ulong)spec.Mode);
        hash = HashFloat(hash, spec.Value);
        hash = HashFloat(hash, spec.Min);
        hash = HashFloat(hash, spec.Max);
        return hash;
    }

    private static ulong HashFloat(ulong hash, float value)
    {
        return HashValue(hash, (ulong)(uint)BitConverter.SingleToUInt32Bits(value));
    }

    private static ulong HashValue(ulong hash, ulong value)
    {
        hash ^= value;
        hash *= HashPrime;
        return hash;
    }

    private void RegisterAnimatedElement(ulong elementId, float durationSeconds)
    {
        if (elementId == 0)
        {
            return;
        }

        if (!_transitionStates.TryGetValue(elementId, out var state))
        {
            state = new TransitionState();
            _transitionStates[elementId] = state;
        }

        state.DurationSeconds = durationSeconds > 0f ? durationSeconds : DefaultTransitionDuration;
    }

    private ScrollState GetScrollState(ulong elementId)
    {
        return _scrollStates.TryGetValue(elementId, out var state) ? state : default;
    }

    private void SetScrollState(ulong elementId, ScrollState state)
    {
        state.LastSeenGeneration = _frameGeneration;
        _scrollStates[elementId] = state;
    }

    private void RegisterVerticalScrollTranslation(float translationY)
    {
        if (MathF.Abs(translationY) <= 0.001f)
        {
            return;
        }

        _verticalScrollTranslations.Add(translationY);
    }

    private float GetVerticalWheelOffset(float step)
    {
        var wheelDelta = _mouseWheelMove.Y;
        if (MathF.Abs(wheelDelta) <= float.Epsilon)
        {
            return 0f;
        }

        if (MathF.Abs(wheelDelta) < 1f)
        {
            wheelDelta *= 3f;
        }

        return -wheelDelta * step;
    }

    private void BuildAnimatedRenderCommands()
    {
        var commands = _context.RenderCommands;
        EnsureAnimatedCommandCapacity(commands.Length + EstimateExitCommandCapacity());
        _animatedRenderCommandCount = 0;
        _transitionCommandIndices.Clear();

        foreach (ref readonly var command in commands)
        {
            var output = command;
            if (command.TransitionId != 0)
            {
                output = ApplyTransition(command);
            }

            _animatedRenderCommands[_animatedRenderCommandCount++] = output;
        }

        foreach (var pair in _transitionCommandIndices)
        {
            if (_transitionStates.TryGetValue(pair.Key, out var state))
            {
                state.Count = pair.Value;
                state.IsExiting = false;
            }
        }

        EmitExitingTransitions();
    }

    private int EstimateExitCommandCapacity()
    {
        var total = 0;
        foreach (var pair in _transitionStates)
        {
            if (pair.Value.LastSeenGeneration == _frameGeneration || pair.Value.Count == 0)
            {
                continue;
            }

            total += pair.Value.Count;
        }

        return total;
    }

    private void EmitExitingTransitions()
    {
        if (_transitionStates.Count == 0)
        {
            return;
        }

        Span<ulong> completed = stackalloc ulong[Math.Min(_transitionStates.Count, 32)];
        List<ulong>? overflow = null;
        var completedCount = 0;

        foreach (var pair in _transitionStates)
        {
            var transitionId = pair.Key;
            var state = pair.Value;
            if (state.LastSeenGeneration == _frameGeneration || state.Count == 0)
            {
                continue;
            }

            if (!state.IsExiting)
            {
                state.EnsureCapacity(state.Count);
                for (var index = 0; index < state.Count; index++)
                {
                    state.ExitTargets[index] = CreateExitCommand(state.Commands[index]);
                }

                state.IsExiting = true;
            }

            EnsureAnimatedCommandCapacity(_animatedRenderCommandCount + state.Count);

            var factor = ComputeTransitionFactor(state.DurationSeconds);
            var settled = true;
            for (var index = 0; index < state.Count; index++)
            {
                var current = state.Commands[index];
                var exitTarget = state.ExitTargets[index];
                var output = InterpolateCommand(current, exitTarget, factor);
                state.Commands[index] = output;
                _animatedRenderCommands[_animatedRenderCommandCount++] = output;
                settled &= IsTransitionSettled(output, exitTarget);
            }

            if (!settled)
            {
                continue;
            }

            state.Count = 0;
            if (completedCount < completed.Length)
            {
                completed[completedCount++] = transitionId;
            }
            else
            {
                overflow ??= [];
                overflow.Add(transitionId);
            }
        }

        for (var index = 0; index < completedCount; index++)
        {
            _transitionStates.Remove(completed[index]);
        }

        if (overflow is not null)
        {
            foreach (var transitionId in overflow)
            {
                _transitionStates.Remove(transitionId);
            }
        }
    }

    private RenderCommand ApplyTransition(in RenderCommand target)
    {
        if (!_transitionStates.TryGetValue(target.TransitionId, out var state))
        {
            state = new TransitionState { DurationSeconds = DefaultTransitionDuration, LastSeenGeneration = 0 };
            _transitionStates[target.TransitionId] = state;
        }

        var nextIndex = _transitionCommandIndices.TryGetValue(target.TransitionId, out var currentIndex)
            ? currentIndex
            : 0;
        _transitionCommandIndices[target.TransitionId] = nextIndex + 1;

        if (state.LastSeenGeneration != _frameGeneration)
        {
            var seenPreviousFrame = state.LastSeenGeneration == (_frameGeneration - 1);
            state.LastSeenGeneration = _frameGeneration;
            state.FrameIsEntering = !seenPreviousFrame && _frameGeneration > 1;
            state.FrameScrollTranslationResolved = false;
            state.FrameScrollTranslationY = 0f;
        }

        state.IsExiting = false;
        state.EnsureCapacity(nextIndex + 1);

        var factor = ComputeTransitionFactor(state.DurationSeconds);

        RenderCommand blended;
        if (state.FrameIsEntering)
        {
            // Enter transition: interpolate everything (slide + fade in)
            var start = CreateEnterCommand(target);
            if (nextIndex < state.Count && CanInterpolate(state.Commands[nextIndex], start))
            {
                start = state.Commands[nextIndex];
            }

            blended = InterpolateCommand(start, target, factor);
        }
        else if (nextIndex < state.Count && CanInterpolate(state.Commands[nextIndex], target))
        {
            var previous = state.Commands[nextIndex];
            var scrollTranslationY = ResolveFrameScrollTranslation(state, nextIndex, in previous, in target);
            blended = InterpolateOngoingCommand(previous, target, factor, scrollTranslationY);
        }
        else
        {
            blended = target;
        }

        state.Commands[nextIndex] = blended;
        return blended;
    }

    private float ResolveFrameScrollTranslation(TransitionState state, int commandIndex, in RenderCommand previous, in RenderCommand target)
    {
        if (state.FrameScrollTranslationResolved)
        {
            return state.FrameScrollTranslationY;
        }

        state.FrameScrollTranslationResolved = true;
        if (commandIndex != 0 || _verticalScrollTranslations.Count == 0 || !CommandHasBounds(target.Type))
        {
            return 0f;
        }

        if (!NearlyEqual(previous.Bounds.X, target.Bounds.X))
        {
            return 0f;
        }

        var boundsDeltaY = target.Bounds.Y - previous.Bounds.Y;
        foreach (var scrollTranslationY in _verticalScrollTranslations)
        {
            if (!NearlyEqual(boundsDeltaY, scrollTranslationY, ScrollTranslationTolerance))
            {
                continue;
            }

            state.FrameScrollTranslationY = boundsDeltaY;
            return state.FrameScrollTranslationY;
        }

        return 0f;
    }

    private float ComputeTransitionFactor(float durationSeconds)
    {
        if (durationSeconds <= 0f || _frameDeltaTime <= 0f)
        {
            return 1f;
        }

        return 1f - MathF.Exp(-_frameDeltaTime * (5f / durationSeconds));
    }

    internal static float AdvanceScrollOffset(float currentOffset, float targetOffset, float damping, float deltaTime)
    {
        if (damping <= 0f || deltaTime <= 0f)
        {
            return targetOffset;
        }

        var factor = 1f - MathF.Exp(-deltaTime * damping);
        var nextOffset = currentOffset + ((targetOffset - currentOffset) * factor);
        return MathF.Abs(targetOffset - nextOffset) <= 0.01f ? targetOffset : nextOffset;
    }

    private static bool CanInterpolate(in RenderCommand previous, in RenderCommand target)
    {
        return previous.Type == target.Type;
    }

    private static RenderCommand InterpolateCommand(in RenderCommand previous, in RenderCommand target, float factor)
    {
        if (factor >= 0.999f)
        {
            return target;
        }

        return new RenderCommand
        {
            Type = target.Type,
            ElementId = target.ElementId,
            TransitionId = target.TransitionId,
            Bounds = Lerp(previous.Bounds, target.Bounds, factor),
            Color = Lerp(previous.Color, target.Color, factor),
            Thickness = Lerp(previous.Thickness, target.Thickness, factor),
            CornerRadius = Lerp(previous.CornerRadius, target.CornerRadius, factor),
            Text = target.Text,
            TextStyle = Lerp(previous.TextStyle, target.TextStyle, factor),
            SourceRegion = Lerp(previous.SourceRegion, target.SourceRegion, factor),
            UseSourceRegion = target.UseSourceRegion,
            Payload = target.Payload,
        };
    }

    internal static RenderCommand InterpolateOngoingCommand(
        in RenderCommand previous,
        in RenderCommand target,
        float factor,
        float verticalScrollTranslationY)
    {
        var adjustedPrevious = previous;
        if (CommandHasBounds(target.Type) && MathF.Abs(verticalScrollTranslationY) > 0.001f)
        {
            adjustedPrevious.Bounds = Offset(adjustedPrevious.Bounds, 0f, verticalScrollTranslationY);
        }

        return InterpolateCommand(in adjustedPrevious, in target, factor);
    }

    private static bool CommandHasBounds(RenderCommandType type)
    {
        return type != RenderCommandType.ScissorEnd && type != RenderCommandType.OverlayEnd;
    }

    private static RectF Offset(in RectF bounds, float offsetX, float offsetY)
    {
        return new RectF(bounds.X + offsetX, bounds.Y + offsetY, bounds.Width, bounds.Height);
    }

    private static RenderCommand CreateEnterCommand(in RenderCommand target)
    {
        return CreateTransitionPose(target, 0f, DefaultTransitionOffset);
    }

    private static RenderCommand CreateExitCommand(in RenderCommand current)
    {
        return CreateTransitionPose(current, 0f, -DefaultTransitionOffset);
    }

    private static RenderCommand CreateTransitionPose(in RenderCommand source, float alphaScale, float offsetY)
    {
        return new RenderCommand
        {
            Type = source.Type,
            ElementId = source.ElementId,
            TransitionId = source.TransitionId,
            Bounds = new RectF(source.Bounds.X, source.Bounds.Y + offsetY, source.Bounds.Width, source.Bounds.Height),
            Color = ScaleAlpha(source.Color, alphaScale),
            Thickness = source.Thickness,
            CornerRadius = source.CornerRadius,
            Text = source.Text,
            TextStyle = ScaleAlpha(source.TextStyle, alphaScale),
            SourceRegion = source.SourceRegion,
            UseSourceRegion = source.UseSourceRegion,
            Payload = source.Payload,
        };
    }

    private static ClayColor ScaleAlpha(ClayColor color, float alphaScale)
    {
        return new ClayColor(color.R, color.G, color.B, (byte)Math.Clamp(MathF.Round(color.A * alphaScale), 0f, 255f));
    }

    private static TextStyle ScaleAlpha(in TextStyle style, float alphaScale)
    {
        return new TextStyle(
            style.FontSize,
            ScaleAlpha(style.Color, alphaScale),
            style.FontId,
            style.LetterSpacing,
            style.LineHeight,
            style.HorizontalAlignment,
            style.Wrap);
    }

    private static bool IsTransitionSettled(in RenderCommand current, in RenderCommand target)
    {
        return NearlyEqual(current.Bounds.X, target.Bounds.X)
            && NearlyEqual(current.Bounds.Y, target.Bounds.Y)
            && NearlyEqual(current.Bounds.Width, target.Bounds.Width)
            && NearlyEqual(current.Bounds.Height, target.Bounds.Height)
            && current.Color.A == target.Color.A
            && current.TextStyle.Color.A == target.TextStyle.Color.A;
    }

    private static bool NearlyEqual(float left, float right)
    {
        return MathF.Abs(left - right) <= 0.5f;
    }

    private static bool NearlyEqual(float left, float right, float tolerance)
    {
        return MathF.Abs(left - right) <= tolerance;
    }

    private void CleanupRetainedState()
    {
        CleanupScrollStates();
        CleanupTransitionStates();
    }

    private void CleanupScrollStates()
    {
        if (_scrollStates.Count == 0)
        {
            return;
        }

        Span<ulong> toRemove = stackalloc ulong[Math.Min(_scrollStates.Count, 32)];
        List<ulong>? overflow = null;
        var removeCount = 0;

        foreach (var pair in _scrollStates)
        {
            if (_frameGeneration - pair.Value.LastSeenGeneration <= RetainedStateTtlFrames)
            {
                continue;
            }

            if (removeCount < toRemove.Length)
            {
                toRemove[removeCount++] = pair.Key;
            }
            else
            {
                overflow ??= [];
                overflow.Add(pair.Key);
            }
        }

        for (var index = 0; index < removeCount; index++)
        {
            _scrollStates.Remove(toRemove[index]);
        }

        if (overflow is not null)
        {
            foreach (var key in overflow)
            {
                _scrollStates.Remove(key);
            }
        }
    }

    private void CleanupTransitionStates()
    {
        if (_transitionStates.Count == 0)
        {
            return;
        }

        Span<ulong> toRemove = stackalloc ulong[Math.Min(_transitionStates.Count, 32)];
        List<ulong>? overflow = null;
        var removeCount = 0;

        foreach (var pair in _transitionStates)
        {
            if (_frameGeneration - pair.Value.LastSeenGeneration <= RetainedStateTtlFrames)
            {
                continue;
            }

            if (removeCount < toRemove.Length)
            {
                toRemove[removeCount++] = pair.Key;
            }
            else
            {
                overflow ??= [];
                overflow.Add(pair.Key);
            }
        }

        for (var index = 0; index < removeCount; index++)
        {
            _transitionStates.Remove(toRemove[index]);
        }

        if (overflow is not null)
        {
            foreach (var key in overflow)
            {
                _transitionStates.Remove(key);
            }
        }
    }

    private void EnsureAnimatedCommandCapacity(int required)
    {
        if (_animatedRenderCommands.Length >= required)
        {
            return;
        }

        Array.Resize(ref _animatedRenderCommands, Math.Max(required, Math.Max(_animatedRenderCommands.Length * 2, 8)));
    }

    private static RectF Lerp(in RectF from, in RectF to, float factor)
    {
        return new RectF(
            Lerp(from.X, to.X, factor),
            Lerp(from.Y, to.Y, factor),
            Lerp(from.Width, to.Width, factor),
            Lerp(from.Height, to.Height, factor));
    }

    private static Thickness Lerp(in Thickness from, in Thickness to, float factor)
    {
        return new Thickness(
            Lerp(from.Left, to.Left, factor),
            Lerp(from.Top, to.Top, factor),
            Lerp(from.Right, to.Right, factor),
            Lerp(from.Bottom, to.Bottom, factor));
    }

    private static CornerRadius Lerp(in CornerRadius from, in CornerRadius to, float factor)
    {
        return new CornerRadius(
            Lerp(from.TopLeft, to.TopLeft, factor),
            Lerp(from.TopRight, to.TopRight, factor),
            Lerp(from.BottomRight, to.BottomRight, factor),
            Lerp(from.BottomLeft, to.BottomLeft, factor));
    }

    private static ClayColor Lerp(ClayColor from, ClayColor to, float factor)
    {
        return new ClayColor(
            LerpByte(from.R, to.R, factor),
            LerpByte(from.G, to.G, factor),
            LerpByte(from.B, to.B, factor),
            LerpByte(from.A, to.A, factor));
    }

    private static TextStyle Lerp(in TextStyle from, in TextStyle to, float factor)
    {
        return new TextStyle(
            Lerp(from.FontSize, to.FontSize, factor),
            Lerp(from.Color, to.Color, factor),
            to.FontId,
            Lerp(from.LetterSpacing, to.LetterSpacing, factor),
            Lerp(from.LineHeight, to.LineHeight, factor),
            to.HorizontalAlignment,
            to.Wrap);
    }

    private static float Lerp(float from, float to, float factor) => from + ((to - from) * factor);

    private static byte LerpByte(byte from, byte to, float factor)
    {
        return (byte)Math.Clamp(MathF.Round(Lerp(from, to, factor)), 0f, 255f);
    }

    private sealed class TransitionState
    {
        public RenderCommand[] Commands = Array.Empty<RenderCommand>();
        public RenderCommand[] ExitTargets = Array.Empty<RenderCommand>();
        public int Count;
        public float DurationSeconds = DefaultTransitionDuration;
        public int LastSeenGeneration;
        public bool IsExiting;
        public bool FrameIsEntering;
        public bool FrameScrollTranslationResolved;
        public float FrameScrollTranslationY;

        public void EnsureCapacity(int required)
        {
            if (Commands.Length >= required && ExitTargets.Length >= required)
            {
                return;
            }

            var capacity = Math.Max(required, Math.Max(Commands.Length * 2, 4));
            Array.Resize(ref Commands, capacity);
            Array.Resize(ref ExitTargets, capacity);
        }
    }

    private struct ScrollState
    {
        public float Offset;
        public float TargetOffset;
        public int LastSeenGeneration;
    }

    private enum ElementIdentityMode
    {
        None,
        Automatic,
        Explicit,
    }

    public struct ElementBuilder : IDisposable
    {
        private readonly ClayGui _gui;
        private ElementStyle _style;
        private ulong _assignedId;
        private ulong _identityOrdinal;
        private ElementIdentityMode _identityMode;
        private bool _disposed;

        internal ElementBuilder(ClayGui gui, ElementStyle style)
        {
            _gui = gui;
            _style = style;
            _assignedId = style.Id;
            _identityOrdinal = 0;
            _identityMode = style.Id != 0 ? ElementIdentityMode.Explicit : ElementIdentityMode.None;
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

        public ElementBuilder Key(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("A stable key is required.", nameof(key));
            }

            SetExplicitId(ClayId.FromString(key));
            return this;
        }

        public ElementBuilder Key(ulong key)
        {
            if (key == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(key), "A non-zero key is required.");
            }

            SetExplicitId(key);
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

        public ElementBuilder ZIndex(int zIndex)
        {
            _style = new ElementStyle(_style.Id, WithLayout(_style.Layout, zIndex: zIndex), _style.Box);
            Apply();
            return this;
        }

        public ElementBuilder Floating(int zIndex = 1)
        {
            _style = new ElementStyle(_style.Id, WithLayout(_style.Layout, positionMode: ClaySharp.PositionMode.Absolute, zIndex: zIndex), _style.Box);
            Apply();
            return this;
        }

        public ElementBuilder Animated(float durationSeconds = DefaultTransitionDuration)
        {
            var elementId = EnsureInteractionId();
            _style = new ElementStyle(_style.Id, WithLayout(_style.Layout, transitionEnabled: true), _style.Box);
            Apply();
            _gui.RegisterAnimatedElement(elementId, durationSeconds);
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

        public ElementBuilder ScrollableY(float step = 56f, float damping = 14f)
        {
            return ScrollableYCore(out _, step, damping);
        }

        public ElementBuilder ScrollableY(out float offset, float step = 56f, float damping = 14f)
        {
            return ScrollableYCore(out offset, step, damping);
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

        private void SetExplicitId(ulong elementId)
        {
            var previousId = _assignedId;
            _assignedId = elementId;
            _identityMode = ElementIdentityMode.Explicit;
            _style = new ElementStyle(_assignedId, _style.Layout, _style.Box);
            _gui.ReplaceTrackedElementId(previousId, _assignedId);
            Apply();
        }

        private ulong EnsureAutomaticId()
        {
            if (_identityMode == ElementIdentityMode.Explicit && _assignedId != 0)
            {
                _gui.TrackElementId(_assignedId);
                return _assignedId;
            }

            if (_identityMode == ElementIdentityMode.None)
            {
                _identityMode = ElementIdentityMode.Automatic;
                var semanticHash = ComputeAutomaticIdentityHash(in _style);
                _identityOrdinal = _gui.ClaimAutomaticIdentityOrdinal(semanticHash);
                var nextId = _gui.ComposeAutomaticIdentity(in _style, _identityOrdinal);
                var previousId = _assignedId;
                _assignedId = nextId;
                _style = new ElementStyle(_assignedId, _style.Layout, _style.Box);
                _gui.ReplaceTrackedElementId(previousId, _assignedId);
            }

            _gui.TrackElementId(_assignedId);
            return _assignedId;
        }

        private ulong EnsureInteractionId()
        {
            return EnsureAutomaticId();
        }

        private void EnsureScrollId()
        {
            _ = EnsureAutomaticId();
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

            var previousOffset = offset;

            var wheelOffset = _gui.IsMouseOver(_assignedId)
                ? _gui.GetVerticalWheelOffset(step)
                : 0f;
            if (MathF.Abs(wheelOffset) > 0.001f)
            {
                offset += wheelOffset;
            }

            offset = Math.Clamp(offset, 0f, resolvedMaxOffset);
            _gui.RegisterVerticalScrollTranslation(previousOffset - offset);

            var scrollOffset = new Vector2(_style.Layout.ScrollOffset.X, offset);
            _style = new ElementStyle(_style.Id, WithLayout(_style.Layout, scrollOffset: scrollOffset), _style.Box);
            Apply();
            return this;
        }

        private ElementBuilder ScrollableYCore(out float offset, float step, float damping)
        {
            EnsureScrollId();

            var state = _gui.GetScrollState(_assignedId);
            var maxOffset = GetMaxScrollOffset();
            var previousOffset = state.Offset;

            _style = new ElementStyle(_style.Id, WithLayout(_style.Layout, clipContent: true), _style.Box);

            var wheelOffset = _gui.IsMouseOver(_assignedId)
                ? _gui.GetVerticalWheelOffset(step)
                : 0f;

            state.TargetOffset += wheelOffset;

            if (maxOffset <= 0f)
            {
                state.Offset = 0f;
                state.TargetOffset = 0f;
            }
            else
            {
                state.TargetOffset = Math.Clamp(state.TargetOffset, 0f, maxOffset);
                state.Offset = Math.Clamp(state.Offset, 0f, maxOffset);
                state.Offset = Math.Clamp(AdvanceScrollOffset(state.Offset, state.TargetOffset, damping, _gui._frameDeltaTime), 0f, maxOffset);
            }

            _gui.RegisterVerticalScrollTranslation(previousOffset - state.Offset);
            _gui.SetScrollState(_assignedId, state);
            offset = state.Offset;

            var scrollOffset = new Vector2(_style.Layout.ScrollOffset.X, state.Offset);
            _style = new ElementStyle(_style.Id, WithLayout(_style.Layout, scrollOffset: scrollOffset, clipContent: true), _style.Box);
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
