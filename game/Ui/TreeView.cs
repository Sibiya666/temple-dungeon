using Godot;

namespace Dialoid.Game.Ui;

/// <summary>Display state of a graph node; the panel takes it from the session, the view only paints it.</summary>
public enum TreeNodeState { Taken, Available, Unavailable }

/// <summary>
/// One node of a <see cref="TreeView"/>: position and radius in world units (data coordinates), a short text drawn next to
/// it (null: none), a badge drawn inside (e.g. the chosen attribute's letter) and the hover text.
/// </summary>
public sealed record TreeViewNode(string Id, Vector2 Position, float Radius, string? Caption, string? Badge, string Tooltip);

/// <summary>
/// Generic graph view of the debug scene (spec §13.2–13.3): edges as lines and nodes as circles painted in _Draw on a plain
/// Control. Mouse wheel (or a trackpad pinch) zooms around the cursor, a trackpad two-finger scroll pans, dragging with the left (or middle) button pans, a left click on a node
/// raises <see cref="NodeClicked"/>, a right click <see cref="NodeRightClicked"/>, hovering shows the node's text as a
/// tooltip and raises <see cref="HoveredChanged"/>.
/// The view knows no game rules: <see cref="SetGraph"/> gives the shape, <see cref="SetStates"/> the colours. Zoom and pan
/// survive state updates; the first non-empty size (or <see cref="FitToView"/>) fits the whole graph.
/// The input handlers (<see cref="ClickAt"/>, <see cref="RightClickAt"/>, <see cref="HoverAt"/>, <see cref="ZoomAt"/>,
/// <see cref="PanBy"/>) are public so the self-test drives the same code the mouse does.
/// </summary>
public partial class TreeView : Control
{
    public const float MinZoom = 0.05f;
    public const float MaxZoom = 8f;
    public const float WheelStep = 1.15f;

    /// <summary>A click hits a node within its drawn radius, but never less than this many pixels.</summary>
    public const float MinHitPixels = 7f;

    /// <summary>Mouse travel in pixels after which a press becomes a drag (pan) instead of a click.</summary>
    public const float DragThreshold = 4f;

    private const float FitMargin = 24f;

    /// <summary>Most extra room kept at each side by <see cref="FitToView"/> for side captions, pixels.</summary>
    public const float CaptionRoom = 110f;

    /// <summary>Pixels of pan per unit of a trackpad pan gesture (two-finger scroll on macOS).</summary>
    public const float PanGestureSpeed = 12f;

    /// <summary>Captions show only on nodes drawn at least this large, so a zoomed-out tree stays readable.</summary>
    public const float CaptionMinPixels = 7f;

    /// <summary>Font scale of the second caption pass (captions that found no place at the full size).</summary>
    public const float SmallCaptionScale = 0.8f;

    public static readonly Color TakenColor = new(1f, 0.78f, 0.25f);
    public static readonly Color AvailableColor = new(0.35f, 0.85f, 0.45f);
    public static readonly Color UnavailableColor = new(0.38f, 0.38f, 0.42f);
    private static readonly Color Background = new(0.08f, 0.08f, 0.1f);
    private static readonly Color EdgeColor = new(0.3f, 0.3f, 0.34f);
    private static readonly Color HoverColor = new(1f, 1f, 1f);
    private static readonly Color CaptionColor = new(0.85f, 0.85f, 0.85f);

    private readonly List<TreeViewNode> _nodes = new();
    private readonly Dictionary<string, TreeViewNode> _byId = new();
    private readonly List<(string A, string B)> _edges = new();
    private IReadOnlyDictionary<string, TreeNodeState> _states = new Dictionary<string, TreeNodeState>();
    private Rect2 _bounds;
    private bool _fitted;

    private bool _pressed;
    private bool _dragging;
    private Vector2 _pressPos;

    /// <summary>Left click on a node.</summary>
    public event Action<string>? NodeClicked;

    /// <summary>Right click on a node, with the click position in this control's coordinates.</summary>
    public event Action<string, Vector2>? NodeRightClicked;

    /// <summary>The node under the mouse changed (null: none).</summary>
    public event Action<string?>? HoveredChanged;

    public float Zoom { get; private set; } = 1f;

    /// <summary>Screen position of the world origin, in this control's coordinates.</summary>
    public Vector2 Pan { get; private set; }

    /// <summary>The node under the mouse, or null.</summary>
    public string? HoveredId { get; private set; }

    public IReadOnlyList<TreeViewNode> Nodes => _nodes;

    public override void _Ready()
    {
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.None;
        Resized += OnResized;
    }

    /// <summary>Replaces the graph. <paramref name="refit"/> fits the new graph into the view (else zoom and pan stay).</summary>
    public void SetGraph(IEnumerable<TreeViewNode> nodes, IEnumerable<(string A, string B)> edges, bool refit)
    {
        _nodes.Clear();
        _byId.Clear();
        _edges.Clear();
        foreach (var n in nodes)
        {
            _nodes.Add(n);
            _byId[n.Id] = n;
        }
        foreach (var (a, b) in edges)
            if (_byId.ContainsKey(a) && _byId.ContainsKey(b)) _edges.Add((a, b));

        _bounds = new Rect2();
        if (_nodes.Count > 0)
        {
            _bounds = new Rect2(_nodes[0].Position, Vector2.Zero);
            foreach (var n in _nodes)
                _bounds = _bounds.Merge(new Rect2(n.Position - Vector2.One * n.Radius, Vector2.One * n.Radius * 2));
        }
        if (HoveredId != null && !_byId.ContainsKey(HoveredId)) SetHovered(null);
        if (refit) _fitted = false;
        if (!_fitted) FitToView();
        QueueRedraw();
    }

    /// <summary>Replaces the node colours (a missing id is drawn as unavailable).</summary>
    public void SetStates(IReadOnlyDictionary<string, TreeNodeState> states)
    {
        _states = states;
        QueueRedraw();
    }

    public TreeNodeState StateOf(string id) => _states.TryGetValue(id, out var s) ? s : TreeNodeState.Unavailable;

    /// <summary>Fits the tree once the current layout has settled (a window that just opened or changed size).</summary>
    public void RequestFit()
    {
        _fitted = false;
        Callable.From(FitToView).CallDeferred();
    }

    /// <summary>
    /// Fits the whole graph into the control (no-op until the control has a size). Up to <see cref="CaptionRoom"/> more is kept free
    /// at the left and right for the captions of the side nodes, but never so much that captioned nodes shrink below
    /// <see cref="CaptionMinPixels"/> (then every caption would vanish).
    /// </summary>
    public void FitToView()
    {
        if (Size.X <= 1 || Size.Y <= 1 || _nodes.Count == 0) return;
        var span = new Vector2(Mathf.Max(_bounds.Size.X, 1f), Mathf.Max(_bounds.Size.Y, 1f));
        var side = FitMargin;
        var captioned = _nodes.Where(n => n.Caption != null).Select(n => n.Radius).DefaultIfEmpty(0).Min();
        if (captioned > 0)
        {
            var zoomNeeded = (CaptionMinPixels + 0.5f) / captioned;
            side = Mathf.Clamp((Size.X - span.X * zoomNeeded) / 2, FitMargin, FitMargin + CaptionRoom);
        }
        var avail = Size - new Vector2(side, FitMargin) * 2;
        Zoom = Mathf.Clamp(Mathf.Min(avail.X / span.X, avail.Y / span.Y), MinZoom, MaxZoom);
        Pan = Size / 2 - _bounds.GetCenter() * Zoom;
        _fitted = true;
        QueueRedraw();
    }

    public Vector2 WorldToLocal(Vector2 world) => world * Zoom + Pan;

    /// <summary>Where a node is drawn now, in this control's coordinates (null: unknown id).</summary>
    public Vector2? NodeLocalPosition(string id) => _byId.TryGetValue(id, out var n) ? WorldToLocal(n.Position) : null;

    /// <summary>The node whose hit circle (drawn radius, at least <see cref="MinHitPixels"/>) holds the point, nearest first.</summary>
    public string? NodeAt(Vector2 local)
    {
        string? best = null;
        float bestDist = float.MaxValue;
        foreach (var n in _nodes)
        {
            float dist = WorldToLocal(n.Position).DistanceTo(local);
            float hit = Mathf.Max(n.Radius * Zoom, MinHitPixels);
            if (dist <= hit && dist < bestDist)
            {
                best = n.Id;
                bestDist = dist;
            }
        }
        return best;
    }

    // ---- input handlers ------------------------------------------------------------------------------------------------

    /// <summary>A left click at a local point: raises <see cref="NodeClicked"/> for the node there. Returns that node or null.</summary>
    public string? ClickAt(Vector2 local)
    {
        var id = NodeAt(local);
        if (id != null) NodeClicked?.Invoke(id);
        return id;
    }

    /// <summary>A right click at a local point: raises <see cref="NodeRightClicked"/>. Returns the node or null.</summary>
    public string? RightClickAt(Vector2 local)
    {
        var id = NodeAt(local);
        if (id != null) NodeRightClicked?.Invoke(id, local);
        return id;
    }

    /// <summary>The mouse is at a local point: updates <see cref="HoveredId"/>. Returns the hovered node or null.</summary>
    public string? HoverAt(Vector2 local)
    {
        SetHovered(NodeAt(local));
        return HoveredId;
    }

    /// <summary>Multiplies the zoom by <paramref name="factor"/> keeping the world point under <paramref name="local"/> in place.</summary>
    public void ZoomAt(Vector2 local, float factor)
    {
        float zoom = Mathf.Clamp(Zoom * factor, MinZoom, MaxZoom);
        var world = (local - Pan) / Zoom;
        Zoom = zoom;
        Pan = local - world * zoom;
        _fitted = true;
        QueueRedraw();
    }

    public void PanBy(Vector2 delta)
    {
        Pan += delta;
        _fitted = true;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventPanGesture pan:
                PanBy(-pan.Delta * PanGestureSpeed);
                AcceptEvent();
                break;
            case InputEventMagnifyGesture magnify:
                if (magnify.Factor > 0) ZoomAt(magnify.Position, magnify.Factor);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true } wheel:
                ZoomAt(wheel.Position, WheelStep);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true } wheel:
                ZoomAt(wheel.Position, 1f / WheelStep);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Middle } button:
                if (button.Pressed)
                {
                    _pressed = true;
                    _dragging = button.ButtonIndex == MouseButton.Middle;
                    _pressPos = button.Position;
                }
                else if (_pressed)
                {
                    _pressed = false;
                    if (!_dragging && button.ButtonIndex == MouseButton.Left) ClickAt(button.Position);
                    _dragging = false;
                }
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false } right:
                RightClickAt(right.Position);
                AcceptEvent();
                break;
            case InputEventMouseMotion motion:
                if (_pressed)
                {
                    if (!_dragging && motion.Position.DistanceTo(_pressPos) > DragThreshold)
                    {
                        _dragging = true;
                        PanBy(motion.Position - _pressPos);
                    }
                    else if (_dragging) PanBy(motion.Relative);
                }
                HoverAt(motion.Position);
                break;
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit) SetHovered(null);
    }

    public override string _GetTooltip(Vector2 atPosition) =>
        NodeAt(atPosition) is { } id ? _byId[id].Tooltip : "";

    private void SetHovered(string? id)
    {
        if (id == HoveredId) return;
        HoveredId = id;
        QueueRedraw();
        HoveredChanged?.Invoke(id);
    }

    private void OnResized()
    {
        if (!_fitted) FitToView();
    }

    // ---- drawing -------------------------------------------------------------------------------------------------------

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Background);

        foreach (var (a, b) in _edges)
        {
            bool taken = StateOf(a) == TreeNodeState.Taken && StateOf(b) == TreeNodeState.Taken;
            DrawLine(WorldToLocal(_byId[a].Position), WorldToLocal(_byId[b].Position),
                taken ? TakenColor : EdgeColor, taken ? 3f : 1.5f, true);
        }

        var font = GetThemeDefaultFont();
        int fontSize = GetThemeDefaultFontSize();
        foreach (var n in _nodes)
        {
            var center = WorldToLocal(n.Position);
            float r = Mathf.Max(n.Radius * Zoom, 3f);
            var color = StateOf(n.Id) switch
            {
                TreeNodeState.Taken => TakenColor,
                TreeNodeState.Available => AvailableColor,
                _ => UnavailableColor,
            };
            DrawCircle(center, r, color);
            if (n.Id == HoveredId) DrawArc(center, r + 3f, 0, Mathf.Tau, 32, HoverColor, 2f, true);

            if (n.Badge != null && r >= 6f)
            {
                int badgeSize = Mathf.Clamp((int)(r * 1.3f), 8, fontSize);
                var size = font.GetStringSize(n.Badge, HorizontalAlignment.Left, -1, badgeSize);
                DrawString(font, center + new Vector2(-size.X / 2, size.Y / 4), n.Badge, HorizontalAlignment.Left, -1,
                    badgeSize, Colors.Black);
            }
        }

        foreach (var (id, rect, size) in LayoutCaptions())
        {
            var caption = _byId[id].Caption!;
            var lineHeight = font.GetHeight(size);
            // A rect two lines high holds the caption split in two (LayoutCaptions' last resort).
            var lines = rect.Size.Y > lineHeight * 1.5f && SplitCaption(caption) is var (a, b) && b.Length > 0 ? new[] { a, b } : new[] { caption };
            for (int i = 0; i < lines.Length; i++)
                DrawString(font, rect.Position + new Vector2(0, font.GetAscent(size) + i * lineHeight), lines[i], HorizontalAlignment.Left, -1,
                    size, CaptionColor);
        }
    }

    /// <summary>
    /// Where each caption goes now, in this control's coordinates. Captions are placed in a fixed order (bigger nodes first,
    /// taken before the rest, then by id) at the first of the candidate positions around the node (right, left, above,
    /// below, the diagonals, then the same sides slid along the node) whose rectangle overlaps neither an already placed
    /// caption nor any drawn node; then, slid inside the view, below or above the node, and as a last resort in two lines there
    /// (a node at the edge among dots). Captions that found no place get a second pass in a smaller font
    /// (<see cref="SmallCaptionScale"/>); one with no free position even then is left out (its text stays in the hover line
    /// and tooltip). Deterministic: the same graph, states, zoom and pan give the same layout.
    /// </summary>
    public IReadOnlyList<(string Id, Rect2 Rect, int FontSize)> LayoutCaptions()
    {
        var placed = new List<(string Id, Rect2 Rect, int FontSize)>();
        if (_nodes.Count == 0) return placed;
        var font = GetThemeDefaultFont();
        int fontSize = GetThemeDefaultFontSize();
        var circles = _nodes.Select(n => (Center: WorldToLocal(n.Position), R: Mathf.Max(n.Radius * Zoom, 3f))).ToList();
        const float gap = 3f;

        var order = _nodes.Where(n => n.Caption != null)
            .OrderByDescending(n => n.Radius)
            .ThenBy(n => StateOf(n.Id) == TreeNodeState.Taken ? 0 : 1)
            .ThenBy(n => n.Id, StringComparer.Ordinal).ToList();
        var view = new Rect2(Vector2.Zero, Size);
        foreach (var size in new[] { fontSize, Mathf.Max(8, (int)(fontSize * SmallCaptionScale)) })
        foreach (var n in order)
        {
            if (placed.Any(p => p.Id == n.Id)) continue;
            var c = WorldToLocal(n.Position);
            float r = Mathf.Max(n.Radius * Zoom, 3f);
            if (r < CaptionMinPixels) continue;
            var extent = font.GetStringSize(n.Caption, HorizontalAlignment.Left, -1, size);
            float w = extent.X, h = extent.Y;
            float d = r * 0.7071f + gap;
            float right = c.X + r + gap, left = c.X - r - gap - w, above = c.Y - r - gap - h, below = c.Y + r + gap;
            float midX = c.X - w / 2, midY = c.Y - h / 2;
            Vector2[] corners =
            {
                new(right, midY), new(left, midY), new(midX, above), new(midX, below),
                new(c.X + d, c.Y - d - h), new(c.X + d, c.Y + d), new(c.X - d - w, c.Y - d - h), new(c.X - d - w, c.Y + d),
                new(right, midY - h), new(right, midY + h), new(left, midY - h), new(left, midY + h),
                new(midX + w / 3, above), new(midX - w / 3, above), new(midX + w / 3, below), new(midX - w / 3, below),
                new(midX, above - h), new(midX, below + h),
            };
            // A node at the view's edge: its caption below or above it, slid inside the view (a centred one would be cut).
            var inside = Mathf.Clamp(midX, 0, Mathf.Max(0, Size.X - w));
            var slid = new[] { below, above, below + h, above - h, below + 2 * h, above - 2 * h }.Select(y => new Vector2(inside, y));
            bool Fits(Rect2 rect)
            {
                // A visible node's caption cut by the edge reads as a broken word («нная кожа»); off-view nodes do not matter.
                if (view.HasPoint(c) && !view.Encloses(rect)) return false;
                var padded = rect.Grow(1f);
                return !placed.Any(p => p.Rect.Intersects(padded)) && !circles.Any(o => CircleHitsRect(o.Center, o.R, padded));
            }
            var spot = corners.Concat(slid).Select(p => new Rect2(p, w, h)).Cast<Rect2?>().FirstOrDefault(rect => Fits(rect!.Value));
            // Still no place (a side node among dots): the caption in two lines, under or over the node, slid inside the view.
            if (spot == null && SplitCaption(n.Caption!) is var (first, second) && second.Length > 0)
            {
                var w2 = Mathf.Max(font.GetStringSize(first, HorizontalAlignment.Left, -1, size).X, font.GetStringSize(second, HorizontalAlignment.Left, -1, size).X);
                var x2 = Mathf.Clamp(c.X - w2 / 2, 0, Mathf.Max(0, Size.X - w2));
                spot = new[] { new Rect2(x2, below, w2, 2 * h), new Rect2(x2, c.Y - r - gap - 2 * h, w2, 2 * h) }
                    .Cast<Rect2?>().FirstOrDefault(rect => Fits(rect!.Value));
            }
            if (spot is { } found) placed.Add((n.Id, found, size));
        }
        return placed;
    }

    /// <summary>A caption cut in two at the space nearest its middle («Каменная» / «кожа»); the second part is empty without a space.</summary>
    public static (string First, string Second) SplitCaption(string caption)
    {
        var best = -1;
        for (int i = 0; i < caption.Length; i++)
            if (caption[i] == ' ' && (best < 0 || Math.Abs(i - caption.Length / 2) < Math.Abs(best - caption.Length / 2))) best = i;
        return best < 0 ? (caption, "") : (caption[..best], caption[(best + 1)..]);
    }

    /// <summary>True when the circle and the rectangle overlap.</summary>
    public static bool CircleHitsRect(Vector2 center, float radius, Rect2 rect)
    {
        var nearest = new Vector2(Mathf.Clamp(center.X, rect.Position.X, rect.End.X), Mathf.Clamp(center.Y, rect.Position.Y, rect.End.Y));
        return nearest.DistanceSquaredTo(center) < radius * radius;
    }
}
