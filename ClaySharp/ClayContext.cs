using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace ClaySharp;

public sealed class ClayContext : IDisposable
{
    private const float Epsilon = 0.01f;

    private LayoutNode[] _nodes;
    private TextNodeData[] _textNodes;
    private ImageNodeData[] _imageNodes;
    private CustomNodeData[] _customNodes;
    private WrappedTextLine[] _wrappedTextLines;
    private RenderCommand[] _renderCommands;
    private int[] _openElementStack;
    private int[] _childScratch;
    private TraversalFrame[] _traversalStack;

    private int _nodeCount;
    private int _textNodeCount;
    private int _imageNodeCount;
    private int _customNodeCount;
    private int _wrappedTextLineCount;
    private int _renderCommandCount;
    private int _openElementCount;
    private bool _isBuilding;
    private Vector2 _viewportSize;
    private ITextMeasurer? _textMeasurer;

    public ClayContext(
        int initialElementCapacity = 256,
        int initialLeafCapacity = 128,
        int initialRenderCommandCapacity = 512,
        int initialLineCapacity = 512)
    {
        _nodes = ArrayPool<LayoutNode>.Shared.Rent(Math.Max(initialElementCapacity, 8));
        _textNodes = ArrayPool<TextNodeData>.Shared.Rent(Math.Max(initialLeafCapacity, 8));
        _imageNodes = ArrayPool<ImageNodeData>.Shared.Rent(Math.Max(initialLeafCapacity, 8));
        _customNodes = ArrayPool<CustomNodeData>.Shared.Rent(Math.Max(initialLeafCapacity, 8));
        _wrappedTextLines = ArrayPool<WrappedTextLine>.Shared.Rent(Math.Max(initialLineCapacity, 8));
        _renderCommands = ArrayPool<RenderCommand>.Shared.Rent(Math.Max(initialRenderCommandCapacity, 8));
        _openElementStack = ArrayPool<int>.Shared.Rent(Math.Max(initialElementCapacity, 8));
        _childScratch = ArrayPool<int>.Shared.Rent(Math.Max(initialElementCapacity, 8));
        _traversalStack = ArrayPool<TraversalFrame>.Shared.Rent(Math.Max(initialElementCapacity, 8));
    }

    public ReadOnlySpan<RenderCommand> RenderCommands => _renderCommands.AsSpan(0, _renderCommandCount);

    public int ElementCount => Math.Max(0, _nodeCount - 1);

    public void Dispose()
    {
        Return(_nodes);
        Return(_textNodes);
        Return(_imageNodes);
        Return(_customNodes);
        Return(_wrappedTextLines);
        Return(_renderCommands);
        Return(_openElementStack);
        Return(_childScratch);
        Return(_traversalStack);
    }

    public void BeginLayout(Vector2 viewportSize, ITextMeasurer textMeasurer)
    {
        if (textMeasurer is null)
        {
            throw new ArgumentNullException(nameof(textMeasurer));
        }

        ReleaseReferences();

        _viewportSize = viewportSize;
        _textMeasurer = textMeasurer;
        _nodeCount = 0;
        _textNodeCount = 0;
        _imageNodeCount = 0;
        _customNodeCount = 0;
        _wrappedTextLineCount = 0;
        _renderCommandCount = 0;
        _openElementCount = 0;
        _isBuilding = true;

        EnsureNodeCapacity(1);
        EnsureOpenStackCapacity(1);

        _nodes[0] = new LayoutNode
        {
            Kind = ElementKind.Container,
            ParentIndex = -1,
            FirstChildIndex = -1,
            LastChildIndex = -1,
            NextSiblingIndex = -1,
            ChildCount = 0,
            DataIndex = -1,
            FirstWrappedLineIndex = -1,
            WrappedLineCount = 0,
            Style = new ElementStyle(layout: new LayoutConfig(sizing: ElementSizing.Fixed(viewportSize.X, viewportSize.Y))),
            ResolvedWidth = viewportSize.X,
            ResolvedHeight = viewportSize.Y,
            AbsoluteX = 0f,
            AbsoluteY = 0f,
        };

        _nodeCount = 1;
        _openElementStack[0] = 0;
        _openElementCount = 1;
    }

    public ElementScope Element(in ElementStyle style)
    {
        EnsureBuilding();
        var index = AddNode(ElementKind.Container, style, -1);
        PushOpenElement(index);
        return new ElementScope(this);
    }

    public void Box(in ElementStyle style)
    {
        EnsureBuilding();
        AddNode(ElementKind.Container, style, -1);
    }

    public void Text(string text, in TextElementStyle style)
    {
        EnsureBuilding();
        var dataIndex = AddText(text, style.Text);
        AddNode(ElementKind.Text, style.Element, dataIndex);
    }

    public void Image(in ImageElementStyle style)
    {
        EnsureBuilding();
        var elementStyle = style.PreserveAspectRatio && style.IntrinsicSize.Y > 0f && style.Element.Layout.AspectRatio <= 0f
            ? style.Element.WithAspectRatio(style.IntrinsicSize.X / style.IntrinsicSize.Y)
            : style.Element;
        var dataIndex = AddImage(style);
        AddNode(ElementKind.Image, elementStyle, dataIndex);
    }

    public void Custom(in CustomElementStyle style)
    {
        EnsureBuilding();
        var dataIndex = AddCustom(style);
        AddNode(ElementKind.Custom, style.Element, dataIndex);
    }

    public void EndLayout()
    {
        EnsureBuilding();
        if (_openElementCount != 1)
        {
            throw new InvalidOperationException("All opened elements must be closed before ending layout.");
        }

        _isBuilding = false;

        ResolveWidths();
        WrapText();
        ResolveHeights();
        PositionElements();
        EmitRenderCommands();
    }

    public bool TryGetBounds(ulong elementId, out RectF bounds)
    {
        for (var index = 1; index < _nodeCount; index++)
        {
            ref readonly var node = ref _nodes[index];
            if (node.Style.Id != elementId)
            {
                continue;
            }

            bounds = new RectF(node.AbsoluteX, node.AbsoluteY, node.ResolvedWidth, node.ResolvedHeight);
            return true;
        }

        bounds = default;
        return false;
    }

    public bool TryHitTest(Vector2 point, out ulong elementId)
    {
        for (var index = _nodeCount - 1; index >= 1; index--)
        {
            ref readonly var node = ref _nodes[index];
            if (node.Style.Id == 0)
            {
                continue;
            }

            var bounds = new RectF(node.AbsoluteX, node.AbsoluteY, node.ResolvedWidth, node.ResolvedHeight);
            if (bounds.Contains(point))
            {
                elementId = node.Style.Id;
                return true;
            }
        }

        elementId = 0;
        return false;
    }

    private void ResolveWidths()
    {
        for (var index = _nodeCount - 1; index >= 0; index--)
        {
            ResolveNodeWidthBottomUp(index);
        }

        for (var index = 0; index < _nodeCount; index++)
        {
            ResolveNodeWidthTopDown(index);
        }
    }

    private void ResolveHeights()
    {
        for (var index = _nodeCount - 1; index >= 0; index--)
        {
            ResolveNodeHeightBottomUp(index);
        }

        for (var index = 0; index < _nodeCount; index++)
        {
            ResolveNodeHeightTopDown(index);
        }
    }

    private void ResolveNodeWidthBottomUp(int nodeIndex)
    {
        ref var node = ref _nodes[nodeIndex];
        if (nodeIndex == 0)
        {
            node.ResolvedWidth = _viewportSize.X;
            return;
        }

        var spec = node.Style.Layout.Sizing.Width;
        if (spec.Mode == SizeMode.Fixed)
        {
            node.ResolvedWidth = spec.Value;
            return;
        }

        var aspectRatio = GetAspectRatio(nodeIndex);
        if (aspectRatio > 0f && node.Style.Layout.Sizing.Height.Mode == SizeMode.Fixed)
        {
            node.ResolvedWidth = spec.Clamp(node.Style.Layout.Sizing.Height.Value * aspectRatio);
            return;
        }

        var fitWidth = GetFitWidth(nodeIndex);
        node.ResolvedWidth = spec.Clamp(fitWidth);
    }

    private void ResolveNodeHeightBottomUp(int nodeIndex)
    {
        ref var node = ref _nodes[nodeIndex];
        if (nodeIndex == 0)
        {
            node.ResolvedHeight = _viewportSize.Y;
            return;
        }

        var spec = node.Style.Layout.Sizing.Height;
        if (spec.Mode == SizeMode.Fixed)
        {
            node.ResolvedHeight = spec.Value;
            return;
        }

        var aspectRatio = GetAspectRatio(nodeIndex);
        if (aspectRatio > 0f && node.ResolvedWidth > 0f)
        {
            node.ResolvedHeight = spec.Clamp(node.ResolvedWidth / aspectRatio);
            return;
        }

        var fitHeight = GetFitHeight(nodeIndex);
        node.ResolvedHeight = spec.Clamp(fitHeight);
    }

    private void ResolveNodeWidthTopDown(int parentIndex)
    {
        ref readonly var parent = ref _nodes[parentIndex];
        if (parent.ChildCount == 0)
        {
            return;
        }

        EnsureChildScratch(parent.ChildCount);
        var children = _childScratch.AsSpan(0, parent.ChildCount);
        var flowCount = CollectFlowChildren(parentIndex, children);
        if (flowCount == 0)
        {
            return;
        }

        var flowChildren = children.Slice(0, flowCount);
        var availableCross = MathF.Max(0f, parent.ResolvedWidth - parent.Style.Layout.Padding.Horizontal);

        if (parent.Style.Layout.Axis == LayoutAxis.Horizontal)
        {
            var availableMain = MathF.Max(0f, availableCross - ComputeGapContribution(flowCount, parent.Style.Layout.Gap));
            var used = SumSizes(flowChildren, isWidth: true);
            var remaining = availableMain - used;
            if (remaining > Epsilon)
            {
                var growCount = CompactChildren(flowChildren, (node, isWidth) => GetSizeSpec(node, isWidth).Mode == SizeMode.Grow, isWidth: true);
                if (growCount > 0)
                {
                    DistributeGrowth(flowChildren.Slice(0, growCount), remaining, isWidth: true);
                }
            }
            else if (remaining < -Epsilon)
            {
                var shrinkCount = CompactChildren(flowChildren, (node, isWidth) => GetSizeSpec(node, isWidth).Mode != SizeMode.Fixed, isWidth: true);
                if (shrinkCount > 0)
                {
                    DistributeShrink(flowChildren.Slice(0, shrinkCount), -remaining, isWidth: true);
                }
            }
        }
        else
        {
            foreach (var childIndex in flowChildren)
            {
                ApplyCrossAxisConstraint(parentIndex, childIndex, availableCross, isWidth: true);
            }
        }
    }

    private void ResolveNodeHeightTopDown(int parentIndex)
    {
        ref readonly var parent = ref _nodes[parentIndex];
        if (parent.ChildCount == 0)
        {
            return;
        }

        EnsureChildScratch(parent.ChildCount);
        var children = _childScratch.AsSpan(0, parent.ChildCount);
        var flowCount = CollectFlowChildren(parentIndex, children);
        if (flowCount == 0)
        {
            return;
        }

        var flowChildren = children.Slice(0, flowCount);
        var availableCross = MathF.Max(0f, parent.ResolvedHeight - parent.Style.Layout.Padding.Vertical);

        if (parent.Style.Layout.Axis == LayoutAxis.Vertical)
        {
            var availableMain = MathF.Max(0f, availableCross - ComputeGapContribution(flowCount, parent.Style.Layout.Gap));
            var used = SumSizes(flowChildren, isWidth: false);
            var remaining = availableMain - used;
            if (remaining > Epsilon)
            {
                var growCount = CompactChildren(flowChildren, (node, isWidth) => GetSizeSpec(node, isWidth).Mode == SizeMode.Grow, isWidth: false);
                if (growCount > 0)
                {
                    DistributeGrowth(flowChildren.Slice(0, growCount), remaining, isWidth: false);
                }
            }
            else if (remaining < -Epsilon)
            {
                var shrinkCount = CompactChildren(flowChildren, (node, isWidth) => GetSizeSpec(node, isWidth).Mode != SizeMode.Fixed, isWidth: false);
                if (shrinkCount > 0)
                {
                    DistributeShrink(flowChildren.Slice(0, shrinkCount), -remaining, isWidth: false);
                }
            }
        }
        else
        {
            foreach (var childIndex in flowChildren)
            {
                ApplyCrossAxisConstraint(parentIndex, childIndex, availableCross, isWidth: false);
            }
        }
    }

    private void WrapText()
    {
        var measurer = RequireTextMeasurer();
        for (var nodeIndex = 1; nodeIndex < _nodeCount; nodeIndex++)
        {
            ref var node = ref _nodes[nodeIndex];
            if (node.Kind != ElementKind.Text)
            {
                continue;
            }

            ref readonly var textNode = ref _textNodes[node.DataIndex];
            var text = textNode.Text ?? string.Empty;
            var availableWidth = MathF.Max(0f, node.ResolvedWidth - node.Style.Layout.Padding.Horizontal);
            node.FirstWrappedLineIndex = _wrappedTextLineCount;
            node.WrappedLineCount = 0;

            if (text.Length == 0)
            {
                AddWrappedLine(nodeIndex, 0, 0, 0f);
                continue;
            }

            var start = 0;
            while (start <= text.Length)
            {
                var lineEnd = text.IndexOf('\n', start);
                if (lineEnd < 0)
                {
                    lineEnd = text.Length;
                }

                var segmentLength = lineEnd - start;
                if (!textNode.Style.Wrap)
                {
                    var width = segmentLength == 0 ? 0f : measurer.MeasureWidth(text.AsSpan(start, segmentLength), in textNode.Style);
                    AddWrappedLine(nodeIndex, start, segmentLength, width);
                }
                else
                {
                    WrapParagraph(nodeIndex, text, start, segmentLength, availableWidth, in textNode.Style);
                }

                if (lineEnd == text.Length)
                {
                    break;
                }

                start = lineEnd + 1;
                if (start == text.Length)
                {
                    AddWrappedLine(nodeIndex, start, 0, 0f);
                    break;
                }
            }
        }
    }

    private void PositionElements()
    {
        _nodes[0].AbsoluteX = 0f;
        _nodes[0].AbsoluteY = 0f;

        for (var parentIndex = 0; parentIndex < _nodeCount; parentIndex++)
        {
            PositionChildren(parentIndex);
        }
    }

    private void PositionChildren(int parentIndex)
    {
        ref readonly var parent = ref _nodes[parentIndex];
        if (parent.ChildCount == 0)
        {
            return;
        }

        EnsureChildScratch(parent.ChildCount);
        var children = _childScratch.AsSpan(0, parent.ChildCount);
        var flowCount = CollectFlowChildren(parentIndex, children);
        var availableWidth = MathF.Max(0f, parent.ResolvedWidth - parent.Style.Layout.Padding.Horizontal);
        var availableHeight = MathF.Max(0f, parent.ResolvedHeight - parent.Style.Layout.Padding.Vertical);
        var contentOriginX = parent.AbsoluteX + parent.Style.Layout.Padding.Left - parent.Style.Layout.ScrollOffset.X;
        var contentOriginY = parent.AbsoluteY + parent.Style.Layout.Padding.Top - parent.Style.Layout.ScrollOffset.Y;

        if (flowCount > 0)
        {
            var flowChildren = children.Slice(0, flowCount);
            var contentSize = parent.Style.Layout.Axis == LayoutAxis.Horizontal
                ? SumSizes(flowChildren, isWidth: true) + ComputeGapContribution(flowCount, parent.Style.Layout.Gap)
                : SumSizes(flowChildren, isWidth: false) + ComputeGapContribution(flowCount, parent.Style.Layout.Gap);

            var remainingMain = parent.Style.Layout.Axis == LayoutAxis.Horizontal
                ? availableWidth - contentSize
                : availableHeight - contentSize;
            var cursor = GetAlignmentOffset(parent.Style.Layout.MainAlignment, remainingMain);

            foreach (var childIndex in flowChildren)
            {
                ref var child = ref _nodes[childIndex];
                if (parent.Style.Layout.Axis == LayoutAxis.Horizontal)
                {
                    var cross = GetAlignmentOffset(parent.Style.Layout.CrossAlignment, availableHeight - child.ResolvedHeight);
                    child.AbsoluteX = contentOriginX + cursor;
                    child.AbsoluteY = contentOriginY + cross;
                    cursor += child.ResolvedWidth + parent.Style.Layout.Gap;
                }
                else
                {
                    var cross = GetAlignmentOffset(parent.Style.Layout.CrossAlignment, availableWidth - child.ResolvedWidth);
                    child.AbsoluteX = contentOriginX + cross;
                    child.AbsoluteY = contentOriginY + cursor;
                    cursor += child.ResolvedHeight + parent.Style.Layout.Gap;
                }
            }
        }

        for (var childIndex = parent.FirstChildIndex; childIndex >= 0; childIndex = _nodes[childIndex].NextSiblingIndex)
        {
            ref var child = ref _nodes[childIndex];
            if (child.Style.Layout.PositionMode != PositionMode.Absolute)
            {
                continue;
            }

            child.AbsoluteX = contentOriginX
                + GetAlignmentOffset(child.Style.Layout.AbsolutePosition.HorizontalAnchor, availableWidth - child.ResolvedWidth)
                + child.Style.Layout.AbsolutePosition.OffsetX;
            child.AbsoluteY = contentOriginY
                + GetAlignmentOffset(child.Style.Layout.AbsolutePosition.VerticalAnchor, availableHeight - child.ResolvedHeight)
                + child.Style.Layout.AbsolutePosition.OffsetY;
        }
    }

    private void EmitRenderCommands()
    {
        _renderCommandCount = 0;
        EnsureTraversalCapacity(_nodeCount);
        _traversalStack[0] = new TraversalFrame { NodeIndex = 0, NextChildIndex = -1, Entered = false };
        var stackCount = 1;

        while (stackCount > 0)
        {
            ref var frame = ref _traversalStack[stackCount - 1];
            if (!frame.Entered)
            {
                frame.Entered = true;
                frame.NextChildIndex = _nodes[frame.NodeIndex].FirstChildIndex;
                EmitNodeStart(frame.NodeIndex, ref frame);
                continue;
            }

            if (frame.NextChildIndex >= 0)
            {
                var child = frame.NextChildIndex;
                frame.NextChildIndex = _nodes[child].NextSiblingIndex;
                _traversalStack[stackCount++] = new TraversalFrame { NodeIndex = child, NextChildIndex = -1, Entered = false };
                continue;
            }

            EmitNodeEnd(frame.NodeIndex, in frame);
            stackCount--;
        }
    }

    private void EmitNodeStart(int nodeIndex, ref TraversalFrame frame)
    {
        if (nodeIndex == 0)
        {
            return;
        }

        ref readonly var node = ref _nodes[nodeIndex];
        var bounds = new RectF(node.AbsoluteX, node.AbsoluteY, node.ResolvedWidth, node.ResolvedHeight);
        var contentBounds = bounds.Deflate(node.Style.Layout.Padding);

        if (node.Style.Box.HasOverlay)
        {
            EmitCommand(new RenderCommand
            {
                Type = RenderCommandType.OverlayStart,
                ElementId = node.Style.Id,
                Bounds = bounds,
                Color = node.Style.Box.OverlayColor,
            });
            frame.OverlayOpened = true;
        }

        if (node.Style.Box.HasBackground)
        {
            EmitCommand(new RenderCommand
            {
                Type = RenderCommandType.Rectangle,
                ElementId = node.Style.Id,
                Bounds = bounds,
                Color = node.Style.Box.BackgroundColor,
                CornerRadius = node.Style.Box.CornerRadius,
            });
        }

        if (node.Style.Box.Border.HasVisibleBorder)
        {
            EmitCommand(new RenderCommand
            {
                Type = RenderCommandType.Border,
                ElementId = node.Style.Id,
                Bounds = bounds,
                Color = node.Style.Box.Border.Color,
                Thickness = node.Style.Box.Border.Widths,
                CornerRadius = node.Style.Box.CornerRadius,
            });
        }

        if (node.Style.Layout.ClipContent)
        {
            EmitCommand(new RenderCommand
            {
                Type = RenderCommandType.ScissorStart,
                ElementId = node.Style.Id,
                Bounds = contentBounds,
            });
            frame.ScissorOpened = true;
        }

        switch (node.Kind)
        {
            case ElementKind.Text:
                EmitTextCommands(nodeIndex, contentBounds);
                break;
            case ElementKind.Image:
                EmitImageCommand(nodeIndex, contentBounds);
                break;
            case ElementKind.Custom:
                EmitCustomCommand(nodeIndex, contentBounds);
                break;
        }
    }

    private void EmitNodeEnd(int nodeIndex, in TraversalFrame frame)
    {
        if (nodeIndex == 0)
        {
            return;
        }

        if (frame.ScissorOpened)
        {
            EmitCommand(new RenderCommand
            {
                Type = RenderCommandType.ScissorEnd,
                ElementId = _nodes[nodeIndex].Style.Id,
            });
        }

        if (frame.OverlayOpened)
        {
            EmitCommand(new RenderCommand
            {
                Type = RenderCommandType.OverlayEnd,
                ElementId = _nodes[nodeIndex].Style.Id,
            });
        }
    }

    private void EmitTextCommands(int nodeIndex, RectF contentBounds)
    {
        var measurer = RequireTextMeasurer();
        ref readonly var node = ref _nodes[nodeIndex];
        ref readonly var textNode = ref _textNodes[node.DataIndex];
        var lineHeight = measurer.GetLineHeight(in textNode.Style);

        for (var lineIndex = 0; lineIndex < node.WrappedLineCount; lineIndex++)
        {
            ref readonly var line = ref _wrappedTextLines[node.FirstWrappedLineIndex + lineIndex];
            if (line.Length == 0)
            {
                continue;
            }

            var drawX = contentBounds.X + GetAlignmentOffset(textNode.Style.HorizontalAlignment, contentBounds.Width - line.Width);
            var drawY = contentBounds.Y + (lineIndex * lineHeight);
            EmitCommand(new RenderCommand
            {
                Type = RenderCommandType.Text,
                ElementId = node.Style.Id,
                Bounds = new RectF(drawX, drawY, line.Width, lineHeight),
                Color = textNode.Style.Color,
                Text = new TextSlice(textNode.Text, line.Start, line.Length),
                TextStyle = textNode.Style,
            });
        }
    }

    private void EmitImageCommand(int nodeIndex, RectF contentBounds)
    {
        ref readonly var node = ref _nodes[nodeIndex];
        ref readonly var imageNode = ref _imageNodes[node.DataIndex];
        EmitCommand(new RenderCommand
        {
            Type = RenderCommandType.Image,
            ElementId = node.Style.Id,
            Bounds = contentBounds,
            Color = imageNode.Tint,
            Payload = imageNode.Source,
            SourceRegion = imageNode.SourceRegion,
            UseSourceRegion = imageNode.UseSourceRegion,
        });
    }

    private void EmitCustomCommand(int nodeIndex, RectF contentBounds)
    {
        ref readonly var node = ref _nodes[nodeIndex];
        ref readonly var customNode = ref _customNodes[node.DataIndex];
        EmitCommand(new RenderCommand
        {
            Type = RenderCommandType.Custom,
            ElementId = node.Style.Id,
            Bounds = contentBounds,
            Payload = customNode.Payload,
        });
    }

    private void WrapParagraph(int nodeIndex, string text, int start, int length, float maxWidth, in TextStyle style)
    {
        var measurer = RequireTextMeasurer();
        if (length == 0)
        {
            AddWrappedLine(nodeIndex, start, 0, 0f);
            return;
        }

        var current = start;
        var end = start + length;
        while (current < end)
        {
            var remaining = end - current;
            if (maxWidth <= 0f)
            {
                var width = measurer.MeasureWidth(text.AsSpan(current, 1), in style);
                AddWrappedLine(nodeIndex, current, 1, width);
                current++;
                continue;
            }

            var fit = measurer.FitCharacters(text.AsSpan(current, remaining), maxWidth, in style, out var measuredWidth);
            if (fit <= 0)
            {
                fit = 1;
                measuredWidth = measurer.MeasureWidth(text.AsSpan(current, 1), in style);
            }

            var lineLength = Math.Min(fit, remaining);
            if (current + lineLength < end)
            {
                var wrapLength = FindWrapLength(text, current, lineLength);
                if (wrapLength > 0)
                {
                    lineLength = wrapLength;
                    measuredWidth = measurer.MeasureWidth(text.AsSpan(current, lineLength), in style);
                }
            }

            var trimmedLength = TrimTrailingWhitespace(text, current, lineLength);
            if (trimmedLength == 0)
            {
                trimmedLength = Math.Min(1, lineLength);
                measuredWidth = measurer.MeasureWidth(text.AsSpan(current, trimmedLength), in style);
            }

            AddWrappedLine(nodeIndex, current, trimmedLength, measuredWidth);

            current += lineLength;
            while (current < end && char.IsWhiteSpace(text[current]))
            {
                current++;
            }
        }
    }

    private float GetFitWidth(int nodeIndex)
    {
        ref readonly var node = ref _nodes[nodeIndex];
        var padding = node.Style.Layout.Padding.Horizontal;

        if (node.Kind == ElementKind.Text)
        {
            return padding + MeasureLongestLine(_textNodes[node.DataIndex]);
        }

        if (node.Kind == ElementKind.Image)
        {
            return padding + _imageNodes[node.DataIndex].IntrinsicSize.X;
        }

        if (node.Kind == ElementKind.Custom)
        {
            return padding + _customNodes[node.DataIndex].PreferredSize.X;
        }

        if (node.ChildCount == 0)
        {
            return padding;
        }

        var flowCount = 0;
        var sum = 0f;
        var max = 0f;
        for (var childIndex = node.FirstChildIndex; childIndex >= 0; childIndex = _nodes[childIndex].NextSiblingIndex)
        {
            ref readonly var child = ref _nodes[childIndex];
            if (child.Style.Layout.PositionMode != PositionMode.Flow)
            {
                continue;
            }

            flowCount++;
            if (node.Style.Layout.Axis == LayoutAxis.Horizontal)
            {
                sum += child.ResolvedWidth;
            }
            else
            {
                max = MathF.Max(max, child.ResolvedWidth);
            }
        }

        return padding + (node.Style.Layout.Axis == LayoutAxis.Horizontal
            ? sum + ComputeGapContribution(flowCount, node.Style.Layout.Gap)
            : max);
    }

    private float GetFitHeight(int nodeIndex)
    {
        ref readonly var node = ref _nodes[nodeIndex];
        var padding = node.Style.Layout.Padding.Vertical;

        if (node.Kind == ElementKind.Text)
        {
            var lineHeight = RequireTextMeasurer().GetLineHeight(in _textNodes[node.DataIndex].Style);
            return padding + (Math.Max(1, node.WrappedLineCount) * lineHeight);
        }

        if (node.Kind == ElementKind.Image)
        {
            return padding + _imageNodes[node.DataIndex].IntrinsicSize.Y;
        }

        if (node.Kind == ElementKind.Custom)
        {
            return padding + _customNodes[node.DataIndex].PreferredSize.Y;
        }

        if (node.ChildCount == 0)
        {
            return padding;
        }

        var flowCount = 0;
        var sum = 0f;
        var max = 0f;
        for (var childIndex = node.FirstChildIndex; childIndex >= 0; childIndex = _nodes[childIndex].NextSiblingIndex)
        {
            ref readonly var child = ref _nodes[childIndex];
            if (child.Style.Layout.PositionMode != PositionMode.Flow)
            {
                continue;
            }

            flowCount++;
            if (node.Style.Layout.Axis == LayoutAxis.Vertical)
            {
                sum += child.ResolvedHeight;
            }
            else
            {
                max = MathF.Max(max, child.ResolvedHeight);
            }
        }

        return padding + (node.Style.Layout.Axis == LayoutAxis.Vertical
            ? sum + ComputeGapContribution(flowCount, node.Style.Layout.Gap)
            : max);
    }

    private float MeasureLongestLine(in TextNodeData textNode)
    {
        var text = textNode.Text ?? string.Empty;
        if (text.Length == 0)
        {
            return 0f;
        }

        var measurer = RequireTextMeasurer();
        var maxWidth = 0f;
        var start = 0;
        while (start <= text.Length)
        {
            var lineEnd = text.IndexOf('\n', start);
            if (lineEnd < 0)
            {
                lineEnd = text.Length;
            }

            var length = lineEnd - start;
            if (length > 0)
            {
                maxWidth = MathF.Max(maxWidth, measurer.MeasureWidth(text.AsSpan(start, length), in textNode.Style));
            }

            if (lineEnd == text.Length)
            {
                break;
            }

            start = lineEnd + 1;
        }

        return maxWidth;
    }

    private void ApplyCrossAxisConstraint(int parentIndex, int childIndex, float availableSize, bool isWidth)
    {
        ref var child = ref _nodes[childIndex];
        var spec = GetSizeSpec(childIndex, isWidth);
        if (spec.Mode == SizeMode.Fixed)
        {
            return;
        }

        var target = GetResolvedSize(childIndex, isWidth);
        if (spec.Mode == SizeMode.Grow || _nodes[parentIndex].Style.Layout.CrossAlignment == Alignment.Stretch)
        {
            target = availableSize;
        }
        else if (target > availableSize)
        {
            target = availableSize;
        }

        SetResolvedSize(childIndex, isWidth, spec.Clamp(target));
    }

    private void DistributeGrowth(Span<int> childIndices, float remaining, bool isWidth)
    {
        while (remaining > Epsilon)
        {
            var activeCount = CompactChildren(childIndices, (node, axis) =>
            {
                var spec = GetSizeSpec(node, axis);
                return spec.Mode == SizeMode.Grow && GetResolvedSize(node, axis) < spec.EffectiveMax - Epsilon;
            }, isWidth);
            if (activeCount == 0)
            {
                return;
            }

            var active = childIndices.Slice(0, activeCount);
            SortChildrenBySize(active, isWidth, ascending: true);
            var smallest = GetResolvedSize(active[0], isWidth);
            var groupCount = 1;
            while (groupCount < active.Length && NearlyEqual(GetResolvedSize(active[groupCount], isWidth), smallest))
            {
                groupCount++;
            }

            var nextSize = float.PositiveInfinity;
            for (var index = groupCount; index < active.Length; index++)
            {
                var candidate = GetResolvedSize(active[index], isWidth);
                if (candidate > smallest + Epsilon)
                {
                    nextSize = candidate;
                    break;
                }
            }

            var idealStep = float.IsPositiveInfinity(nextSize)
                ? remaining / groupCount
                : MathF.Min(nextSize - smallest, remaining / groupCount);
            if (idealStep <= Epsilon)
            {
                return;
            }

            var consumed = 0f;
            for (var index = 0; index < groupCount; index++)
            {
                var childIndex = active[index];
                var current = GetResolvedSize(childIndex, isWidth);
                var max = GetSizeSpec(childIndex, isWidth).EffectiveMax;
                var delta = MathF.Min(idealStep, max - current);
                if (delta <= Epsilon)
                {
                    continue;
                }

                SetResolvedSize(childIndex, isWidth, current + delta);
                consumed += delta;
            }

            if (consumed <= Epsilon)
            {
                return;
            }

            remaining -= consumed;
        }
    }

    private void DistributeShrink(Span<int> childIndices, float overflow, bool isWidth)
    {
        while (overflow > Epsilon)
        {
            var activeCount = CompactChildren(childIndices, (node, axis) =>
            {
                var spec = GetSizeSpec(node, axis);
                return spec.Mode != SizeMode.Fixed && GetResolvedSize(node, axis) > spec.Min + Epsilon;
            }, isWidth);
            if (activeCount == 0)
            {
                return;
            }

            var active = childIndices.Slice(0, activeCount);
            SortChildrenBySize(active, isWidth, ascending: false);
            var largest = GetResolvedSize(active[0], isWidth);
            var groupCount = 1;
            while (groupCount < active.Length && NearlyEqual(GetResolvedSize(active[groupCount], isWidth), largest))
            {
                groupCount++;
            }

            var nextSize = float.NegativeInfinity;
            for (var index = groupCount; index < active.Length; index++)
            {
                var candidate = GetResolvedSize(active[index], isWidth);
                if (candidate < largest - Epsilon)
                {
                    nextSize = candidate;
                    break;
                }
            }

            var idealStep = float.IsNegativeInfinity(nextSize)
                ? overflow / groupCount
                : MathF.Min(largest - nextSize, overflow / groupCount);
            if (idealStep <= Epsilon)
            {
                return;
            }

            var consumed = 0f;
            for (var index = 0; index < groupCount; index++)
            {
                var childIndex = active[index];
                var current = GetResolvedSize(childIndex, isWidth);
                var min = GetSizeSpec(childIndex, isWidth).Min;
                var delta = MathF.Min(idealStep, current - min);
                if (delta <= Epsilon)
                {
                    continue;
                }

                SetResolvedSize(childIndex, isWidth, current - delta);
                consumed += delta;
            }

            if (consumed <= Epsilon)
            {
                return;
            }

            overflow -= consumed;
        }
    }

    private int CompactChildren(Span<int> childIndices, Func<int, bool, bool> predicate, bool isWidth)
    {
        var count = 0;
        for (var index = 0; index < childIndices.Length; index++)
        {
            var childIndex = childIndices[index];
            if (!predicate(childIndex, isWidth))
            {
                continue;
            }

            childIndices[count++] = childIndex;
        }

        return count;
    }

    private void SortChildrenBySize(Span<int> childIndices, bool isWidth, bool ascending)
    {
        for (var index = 1; index < childIndices.Length; index++)
        {
            var candidate = childIndices[index];
            var candidateSize = GetResolvedSize(candidate, isWidth);
            var insertion = index - 1;
            while (insertion >= 0)
            {
                var currentSize = GetResolvedSize(childIndices[insertion], isWidth);
                var shouldMove = ascending ? candidateSize < currentSize : candidateSize > currentSize;
                if (!shouldMove)
                {
                    break;
                }

                childIndices[insertion + 1] = childIndices[insertion];
                insertion--;
            }

            childIndices[insertion + 1] = candidate;
        }
    }

    private int CollectFlowChildren(int parentIndex, Span<int> destination)
    {
        var count = 0;
        for (var childIndex = _nodes[parentIndex].FirstChildIndex; childIndex >= 0; childIndex = _nodes[childIndex].NextSiblingIndex)
        {
            if (_nodes[childIndex].Style.Layout.PositionMode != PositionMode.Flow)
            {
                continue;
            }

            destination[count++] = childIndex;
        }

        return count;
    }

    private int AddNode(ElementKind kind, in ElementStyle style, int dataIndex)
    {
        EnsureNodeCapacity(_nodeCount + 1);
        var index = _nodeCount++;
        _nodes[index] = new LayoutNode
        {
            Kind = kind,
            ParentIndex = CurrentParentIndex,
            FirstChildIndex = -1,
            LastChildIndex = -1,
            NextSiblingIndex = -1,
            ChildCount = 0,
            Style = style,
            DataIndex = dataIndex,
            FirstWrappedLineIndex = -1,
            WrappedLineCount = 0,
        };
        AddChild(CurrentParentIndex, index);
        return index;
    }

    private int AddText(string text, in TextStyle style)
    {
        EnsureTextCapacity(_textNodeCount + 1);
        _textNodes[_textNodeCount] = new TextNodeData { Text = text, Style = style };
        return _textNodeCount++;
    }

    private int AddImage(in ImageElementStyle style)
    {
        EnsureImageCapacity(_imageNodeCount + 1);
        _imageNodes[_imageNodeCount] = new ImageNodeData
        {
            Source = style.Source,
            IntrinsicSize = style.IntrinsicSize,
            Tint = style.Tint,
            SourceRegion = style.SourceRegion,
            UseSourceRegion = style.UseSourceRegion,
            PreserveAspectRatio = style.PreserveAspectRatio,
        };
        return _imageNodeCount++;
    }

    private int AddCustom(in CustomElementStyle style)
    {
        EnsureCustomCapacity(_customNodeCount + 1);
        _customNodes[_customNodeCount] = new CustomNodeData { PreferredSize = style.PreferredSize, Payload = style.Payload };
        return _customNodeCount++;
    }

    private void AddWrappedLine(int nodeIndex, int start, int length, float width)
    {
        EnsureWrappedLineCapacity(_wrappedTextLineCount + 1);
        _wrappedTextLines[_wrappedTextLineCount++] = new WrappedTextLine
        {
            NodeIndex = nodeIndex,
            Start = start,
            Length = length,
            Width = width,
        };
        _nodes[nodeIndex].WrappedLineCount++;
    }

    private void EmitCommand(in RenderCommand command)
    {
        EnsureRenderCommandCapacity(_renderCommandCount + 1);
        _renderCommands[_renderCommandCount++] = command;
    }

    private void AddChild(int parentIndex, int childIndex)
    {
        ref var parent = ref _nodes[parentIndex];
        if (parent.FirstChildIndex < 0)
        {
            parent.FirstChildIndex = childIndex;
        }
        else
        {
            _nodes[parent.LastChildIndex].NextSiblingIndex = childIndex;
        }

        parent.LastChildIndex = childIndex;
        parent.ChildCount++;
    }

    private void PushOpenElement(int nodeIndex)
    {
        EnsureOpenStackCapacity(_openElementCount + 1);
        _openElementStack[_openElementCount++] = nodeIndex;
    }

    private void CloseElement()
    {
        if (_openElementCount <= 1)
        {
            throw new InvalidOperationException("Cannot close the implicit root element.");
        }

        _openElementCount--;
    }

    private void EnsureBuilding()
    {
        if (!_isBuilding)
        {
            throw new InvalidOperationException("BeginLayout must be called before building a UI tree.");
        }
    }

    private ITextMeasurer RequireTextMeasurer() => _textMeasurer ?? throw new InvalidOperationException("A text measurer is required for layout.");

    private int CurrentParentIndex => _openElementStack[_openElementCount - 1];

    private void EnsureNodeCapacity(int required)
    {
        if (_nodes.Length >= required)
        {
            return;
        }

        Resize(ref _nodes, required);
    }

    private void EnsureTextCapacity(int required)
    {
        if (_textNodes.Length >= required)
        {
            return;
        }

        Resize(ref _textNodes, required);
    }

    private void EnsureImageCapacity(int required)
    {
        if (_imageNodes.Length >= required)
        {
            return;
        }

        Resize(ref _imageNodes, required);
    }

    private void EnsureCustomCapacity(int required)
    {
        if (_customNodes.Length >= required)
        {
            return;
        }

        Resize(ref _customNodes, required);
    }

    private void EnsureWrappedLineCapacity(int required)
    {
        if (_wrappedTextLines.Length >= required)
        {
            return;
        }

        Resize(ref _wrappedTextLines, required);
    }

    private void EnsureRenderCommandCapacity(int required)
    {
        if (_renderCommands.Length >= required)
        {
            return;
        }

        Resize(ref _renderCommands, required);
    }

    private void EnsureOpenStackCapacity(int required)
    {
        if (_openElementStack.Length >= required)
        {
            return;
        }

        Resize(ref _openElementStack, required);
    }

    private void EnsureChildScratch(int required)
    {
        if (_childScratch.Length >= required)
        {
            return;
        }

        Resize(ref _childScratch, required);
    }

    private void EnsureTraversalCapacity(int required)
    {
        if (_traversalStack.Length >= required)
        {
            return;
        }

        Resize(ref _traversalStack, required);
    }

    private void ReleaseReferences()
    {
        _textNodes.AsSpan(0, _textNodeCount).Clear();
        _imageNodes.AsSpan(0, _imageNodeCount).Clear();
        _customNodes.AsSpan(0, _customNodeCount).Clear();
        _renderCommands.AsSpan(0, _renderCommandCount).Clear();
    }

    private static void Resize<T>(ref T[] buffer, int required)
    {
        var replacement = ArrayPool<T>.Shared.Rent(Math.Max(required, buffer.Length * 2));
        Array.Copy(buffer, replacement, buffer.Length);
        Return(buffer);
        buffer = replacement;
    }

    private static void Return<T>(T[] buffer)
    {
        ArrayPool<T>.Shared.Return(buffer, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
    }

    private static float ComputeGapContribution(int childCount, float gap) => childCount > 1 ? ((childCount - 1) * gap) : 0f;

    private float SumSizes(ReadOnlySpan<int> childIndices, bool isWidth)
    {
        var total = 0f;
        foreach (var childIndex in childIndices)
        {
            total += GetResolvedSize(childIndex, isWidth);
        }

        return total;
    }

    private float GetResolvedSize(int nodeIndex, bool isWidth) => isWidth
        ? _nodes[nodeIndex].ResolvedWidth
        : _nodes[nodeIndex].ResolvedHeight;

    private SizeSpec GetSizeSpec(int nodeIndex, bool isWidth) => isWidth
        ? _nodes[nodeIndex].Style.Layout.Sizing.Width
        : _nodes[nodeIndex].Style.Layout.Sizing.Height;

    private void SetResolvedSize(int nodeIndex, bool isWidth, float value)
    {
        if (isWidth)
        {
            _nodes[nodeIndex].ResolvedWidth = value;
        }
        else
        {
            _nodes[nodeIndex].ResolvedHeight = value;
        }
    }

    private float GetAspectRatio(int nodeIndex)
    {
        var ratio = _nodes[nodeIndex].Style.Layout.AspectRatio;
        if (ratio > 0f)
        {
            return ratio;
        }

        if (_nodes[nodeIndex].Kind == ElementKind.Image)
        {
            ref readonly var image = ref _imageNodes[_nodes[nodeIndex].DataIndex];
            if (image.PreserveAspectRatio && image.IntrinsicSize.Y > 0f)
            {
                return image.IntrinsicSize.X / image.IntrinsicSize.Y;
            }
        }

        return 0f;
    }

    private static float GetAlignmentOffset(Alignment alignment, float remainingSpace)
    {
        return alignment switch
        {
            Alignment.Center => remainingSpace * 0.5f,
            Alignment.End => remainingSpace,
            _ => 0f,
        };
    }

    private static bool NearlyEqual(float left, float right) => MathF.Abs(left - right) <= Epsilon;

    private static int FindWrapLength(string text, int start, int maxLength)
    {
        for (var index = maxLength - 1; index > 0; index--)
        {
            if (char.IsWhiteSpace(text[start + index]))
            {
                return index;
            }
        }

        return 0;
    }

    private static int TrimTrailingWhitespace(string text, int start, int length)
    {
        var trimmed = length;
        while (trimmed > 0 && char.IsWhiteSpace(text[start + trimmed - 1]))
        {
            trimmed--;
        }

        return trimmed;
    }

    private struct LayoutNode
    {
        public ElementKind Kind;
        public int ParentIndex;
        public int FirstChildIndex;
        public int LastChildIndex;
        public int NextSiblingIndex;
        public int ChildCount;
        public int DataIndex;
        public int FirstWrappedLineIndex;
        public int WrappedLineCount;
        public ElementStyle Style;
        public float ResolvedWidth;
        public float ResolvedHeight;
        public float AbsoluteX;
        public float AbsoluteY;
    }

    private struct TextNodeData
    {
        public string? Text;
        public TextStyle Style;
    }

    private struct ImageNodeData
    {
        public object? Source;
        public Vector2 IntrinsicSize;
        public ClayColor Tint;
        public RectF SourceRegion;
        public bool UseSourceRegion;
        public bool PreserveAspectRatio;
    }

    private struct CustomNodeData
    {
        public Vector2 PreferredSize;
        public object? Payload;
    }

    private struct WrappedTextLine
    {
        public int NodeIndex;
        public int Start;
        public int Length;
        public float Width;
    }

    private struct TraversalFrame
    {
        public int NodeIndex;
        public int NextChildIndex;
        public bool Entered;
        public bool ScissorOpened;
        public bool OverlayOpened;
    }

    public ref struct ElementScope
    {
        private ClayContext? _context;

        internal ElementScope(ClayContext context)
        {
            _context = context;
        }

        public void Dispose()
        {
            var context = _context;
            if (context is null)
            {
                return;
            }

            _context = null;
            context.CloseElement();
        }
    }
}
