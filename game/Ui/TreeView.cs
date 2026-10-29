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
/// Control. Mouse wheel zooms around the cursor, dragging with the left (or middle) button pans, a left click on a node
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

    /// <summary>Captions show only on nodes drawn at least this large, so a zoomed-out tree stays readable.</summary>
    public const float CaptionMinPixels = 7f;

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

    /// <summary>Fits the whole graph into the control (no-op until the control has a size).</summary>
    public void FitToView()
    {
        if (Size.X <= 1 || Size.Y <= 1 || _nodes.Count == 0) return;
        var avail = Size - Vector2.One * FitMargin * 2;
        var span = new Vector2(Mathf.Max(_bounds.Size.X, 1f), Mathf.Max(_bounds.Size.Y, 1f));
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
            if (n.Caption != null && r >= CaptionMinPixels)
                DrawString(font, center + new Vector2(r + 4f, fontSize / 3f), n.Caption, HorizontalAlignment.Left, -1,
                    fontSize, CaptionColor);
        }
    }
}
