using Godot;

namespace Dialoid.Game.Ui;

/// <summary>Display state of a graph node; the panel takes it from the session, the view only paints it.</summary>
public enum TreeNodeState { Taken, Available, Unavailable }

/// <summary>
/// One node of a <see cref="TreeView"/>: position and radius in world units (data coordinates), a short text drawn next to
/// it (null: none), a badge drawn inside (e.g. the chosen attribute's letter) and the hover text.
/// </summary>
public sealed record TreeViewNode(string Id, Vector2 Position, float Radius, string? Caption, string? Badge, string Tooltip)
{
    public Texture2D? Icon { get; init; }
}

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
public partial class TreeView : Control, IPadControl
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
    public IReadOnlyList<(string A, string B)> Edges => _edges;

    /// <summary>Opt-in Tower05 presentation used by Ascendancy; other trees keep their existing renderer.</summary>
    public bool ReferenceStyle { get; set; }
    private string? _referenceSelected;
    public string? SelectedNodeId => ReferenceStyle ? _referenceSelected ?? HoveredId : HoveredId;

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
        if (_referenceSelected != null && !_byId.ContainsKey(_referenceSelected)) _referenceSelected = null;

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
        if (captioned > 0 && !ReferenceStyle)
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

    public override string _GetTooltip(Vector2 atPosition) =>
        NodeAt(atPosition) is { } id ? _byId[id].Tooltip : "";

    // ---- gamepad (plan 4 task 13) ---------------------------------------------------------------------------------------

    /// <summary>A node counts as «that way» within this many degrees of the stick's direction.</summary>
    public const float PadConeDegrees = 50f;

    /// <summary>LT / RT zoom factor and the right stick's pan speed (pixels per second at full tilt).</summary>
    public const float PadZoomStep = 1.25f;
    public const float PadPanSpeed = 700f;

    /// <summary>The pixels kept between a selected node and the view's edge (the view pans to keep it inside).</summary>
    public const float PadKeepMargin = 40f;

    /// <summary>
    /// The nearest node from <paramref name="fromId"/> in the direction <paramref name="dir"/> (screen space, y down): inside a cone
    /// of ±<see cref="PadConeDegrees"/>, the smallest distance × (1 + angle / cone); null when none is that way.
    /// </summary>
    public string? NeighbourOf(string fromId, Vector2 dir)
    {
        if (!_byId.TryGetValue(fromId, out var from)) return null;
        string? best = null;
        var bestWeight = float.MaxValue;
        var cone = Mathf.DegToRad(PadConeDegrees);
        foreach (var n in _nodes)
        {
            if (n.Id == fromId) continue;
            var d = n.Position - from.Position;
            var dist = d.Length();
            if (dist < 1e-4f) continue;
            var angle = Mathf.Abs(d.AngleTo(dir));
            if (angle > cone) continue;
            var weight = dist * (1 + angle / cone);
            if (weight < bestWeight)
            {
                best = n.Id;
                bestWeight = weight;
            }
        }
        return best;
    }

    /// <summary>Selects a node from the gamepad (the hover ring, the panel's hover line) and pans so it stays in view.</summary>
    public void PadSelect(string id)
    {
        SetHovered(id);
        KeepInView(id);
    }

    /// <summary>Pans the least so the node is at least <see cref="PadKeepMargin"/> inside the view.</summary>
    public void KeepInView(string id)
    {
        if (NodeLocalPosition(id) is not { } p || Size.X <= 1 || Size.Y <= 1) return;
        var margin = Mathf.Min(PadKeepMargin, Mathf.Min(Size.X, Size.Y) / 3);
        var shift = new Vector2(
            p.X < margin ? margin - p.X : p.X > Size.X - margin ? Size.X - margin - p.X : 0,
            p.Y < margin ? margin - p.Y : p.Y > Size.Y - margin ? Size.Y - margin - p.Y : 0);
        if (shift != Vector2.Zero) PanBy(shift);
    }

    /// <summary>The node nearest the view's centre (the first selection when the tree gets the focus).</summary>
    public string? CentreNode()
    {
        var centre = Size / 2;
        return _nodes.MinBy(n => WorldToLocal(n.Position).DistanceSquaredTo(centre))?.Id;
    }

    public bool PadInput(PadCommand command)
    {
        if (HoveredId == null || !_byId.ContainsKey(HoveredId))
        {
            if (CentreNode() is not { } first) return false;
            PadSelect(first);
            return true;
        }
        var id = HoveredId;
        switch (command)
        {
            case PadCommand.Up or PadCommand.Down or PadCommand.Left or PadCommand.Right:
            {
                var dir = command switch
                {
                    PadCommand.Up => Vector2.Up,
                    PadCommand.Down => Vector2.Down,
                    PadCommand.Left => Vector2.Left,
                    _ => Vector2.Right,
                };
                if (NeighbourOf(id, dir) is not { } next) return false; // nothing that way: the focus leaves the tree
                PadSelect(next);
                return true;
            }
            case PadCommand.Accept:
                NodeClicked?.Invoke(id);
                return true;
            case PadCommand.Alt:
                NodeRightClicked?.Invoke(id, NodeLocalPosition(id) ?? Size / 2);
                return true;
            case PadCommand.TriggerLeft or PadCommand.TriggerRight:
                ZoomAt(NodeLocalPosition(id) ?? Size / 2, command == PadCommand.TriggerRight ? PadZoomStep : 1f / PadZoomStep);
                KeepInView(id);
                return true;
            case PadCommand.Center: // R3: «Вписать дерево», the selected node stays in view
                FitToView();
                KeepInView(id);
                return true;
            default:
                return false;
        }
    }

    public bool PadStick(Vector2 value, double delta)
    {
        PanBy(-value * PadPanSpeed * (float)delta);
        return true;
    }

    public string PadTooltip() => !DescriptionInPanel && HoveredId != null && _byId.TryGetValue(HoveredId, out var n) ? n.Tooltip : "";

    /// <summary>The panel shows the selected node's description itself (its hover line), so the focus tooltip would only repeat it.</summary>
    public bool DescriptionInPanel { get; set; }

    /// <summary>What X does on the selected node (the panel says: «Атрибут» on an attribute node), else none.</summary>
    public Func<string?, string?>? AltLabel { get; set; }

    public IEnumerable<(string Button, string Label)> PadPrompts()
    {
        var state = HoveredId != null ? StateOf(HoveredId) : TreeNodeState.Unavailable;
        yield return ("[A]", state == TreeNodeState.Taken ? "Снять" : "Взять");
        if (AltLabel?.Invoke(HoveredId) is { } alt) yield return ("[X]", alt);
        yield return ("[LT/RT]", "Масштаб");
        yield return ("[R3]", "Вписать");
    }

    public void PadModeChanged(bool gamepad)
    {
        if (!gamepad) SetHovered(null);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit && !Dialoid.Game.Input.InputDevice.Gamepad) SetHovered(null);
        else if (what == NotificationFocusEnter && Dialoid.Game.Input.InputDevice.Gamepad)
        {
            if ((HoveredId == null || !_byId.ContainsKey(HoveredId)) && CentreNode() is { } first) PadSelect(first);
            PadFocusZoom();
        }
        if (what == NotificationFocusEnter || what == NotificationFocusExit) QueueRedraw();
    }

    private void SetHovered(string? id)
    {
        if (ReferenceStyle && id != null) _referenceSelected = id;
        if (id == HoveredId) return;
        HoveredId = id;
        QueueRedraw();
        HoveredChanged?.Invoke(id);
    }

    private void OnResized()
    {
        if (!_fitted || ReferenceStyle) FitToView();
    }

    // ---- drawing -------------------------------------------------------------------------------------------------------

    public override void _Draw()
    {
        if (ReferenceStyle)
        {
            DrawReferenceTree();
            return;
        }
        DrawRect(new Rect2(Vector2.Zero, Size), Background);
        if (HasFocus()) DrawRect(new Rect2(Vector2.One, Size - Vector2.One * 2), WindowHost.FocusFrame, false, 2f);

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
            bool hasIcon = n.Icon != null && r >= 10f;
            if (hasIcon)
            {
                DrawCircle(center, r * 0.82f, Background);
                DrawTextureRect(n.Icon!, new Rect2(center - Vector2.One * r * 0.65f, Vector2.One * r * 1.3f), false,
                    StateOf(n.Id) == TreeNodeState.Unavailable ? new Color(0.55f, 0.55f, 0.55f) : Colors.White);
            }
            if (n.Id == HoveredId) DrawArc(center, r + 3f, 0, Mathf.Tau, 32, HoverColor, 2f, true);

            if (n.Badge != null && r >= 6f)
            {
                int badgeSize = Mathf.Clamp((int)(r * (hasIcon ? 0.65f : 1.3f)), 8, fontSize);
                var size = font.GetStringSize(n.Badge, HorizontalAlignment.Left, -1, badgeSize);
                var badgeCenter = hasIcon ? center + Vector2.One * r * 0.4f : center;
                if (hasIcon) DrawCircle(badgeCenter, badgeSize * 0.65f, color);
                DrawString(font, badgeCenter + new Vector2(-size.X / 2, size.Y / 4), n.Badge, HorizontalAlignment.Left, -1,
                    badgeSize, Colors.Black);
            }
        }

        // The gamepad's plates are laid out first: a plated node gets no regular caption, and regular captions avoid the plates.
        var plates = HasFocus() && Dialoid.Game.Input.InputDevice.Gamepad ? PadCaptions() : Array.Empty<(string Id, Rect2 Rect)>();
        foreach (var (id, rect, size) in LayoutCaptions(plates))
        {
            var caption = _byId[id].Caption!;
            var lineHeight = font.GetHeight(size);
            // A rect two lines high holds the caption split in two (LayoutCaptions' last resort).
            var lines = rect.Size.Y > lineHeight * 1.5f && SplitCaption(caption) is var (a, b) && b.Length > 0 ? new[] { a, b } : new[] { caption };
            for (int i = 0; i < lines.Length; i++)
                DrawString(font, rect.Position + new Vector2(0, font.GetAscent(size) + i * lineHeight), lines[i], HorizontalAlignment.Left, -1,
                    size, CaptionColor);
        }
        DrawPadCaptions(plates, font, fontSize);
    }

    /// <summary>Plate colour of the gamepad captions (the focused node and where the stick goes next).</summary>
    private static readonly Color PlateColor = new(0.05f, 0.045f, 0.04f, 0.9f);

    /// <summary>The name of a node for a caption: its caption, else its tooltip's first line.</summary>
    public string NameOf(string id) => _byId.TryGetValue(id, out var n) ? n.Caption ?? n.Tooltip.Split('\n')[0] : id;

    /// <summary>
    /// The gamepad's captions (curator, task 13 rounds 1–2): the focused node and its neighbours in the four directions always get
    /// their name on a dark plate. Plates are laid out before the regular captions, in the captions' order of sides (right, left,
    /// above, below, then the same sides slid along the node), at the first spot inside the view that covers no node circle and
    /// no other plate (then the nearest free place within ~200 px, with a thin leader), trying the full font, a smaller one and two
    /// lines. A neighbour with no free place gets no plate; the focused node (placed first) keeps the cheapest place.
    /// </summary>
    public IReadOnlyList<(string Id, Rect2 Rect)> PadCaptions()
    {
        var plates = new List<(string Id, Rect2 Rect)>();
        if (HoveredId == null || !_byId.ContainsKey(HoveredId)) return plates;
        var font = GetThemeDefaultFont();
        var size = GetThemeDefaultFontSize();
        var ids = new List<string> { HoveredId };
        foreach (var dir in new[] { Vector2.Up, Vector2.Down, Vector2.Left, Vector2.Right })
            // A neighbour outside the view gets no plate (a plate slid in from the edge would name a node nobody sees).
            if (NeighbourOf(HoveredId, dir) is { } next && !ids.Contains(next) && new Rect2(Vector2.Zero, Size).HasPoint(WorldToLocal(_byId[next].Position))) ids.Add(next);
        var circles = _nodes.Select(n => (Center: WorldToLocal(n.Position), R: Mathf.Max(n.Radius * Zoom, 3f))).ToList();
        var view = new Rect2(Vector2.Zero, Size);
        _plateText.Clear();
        foreach (var id in ids)
        {
            var c = WorldToLocal(_byId[id].Position);
            var r = Mathf.Max(_byId[id].Radius * Zoom, 3f) + 4;
            var name = NameOf(id);
            int Cost(Rect2 rect) =>
                circles.Count(o => CircleHitsRect(o.Center, o.R, rect.Grow(1))) * 10 + plates.Count(p => p.Rect.Intersects(rect)) * 100 + (view.Encloses(rect) ? 0 : 1000);
            // Variants until one finds a free place: the full font, a smaller one, the name in two lines.
            var small = Mathf.Max(8, (int)(size * SmallCaptionScale));
            var split = SplitCaption(name);
            var variants = new List<(int Size, string[] Lines)> { (size, [name]), (small, [name]) };
            if (split.Second.Length > 0) variants.Add((small, [split.First, split.Second]));
            Rect2? chosen = null;
            (int Size, string[] Lines) style = variants[0];
            var cheapest = (Rect: new Rect2(), Cost: int.MaxValue, Style: variants[0]);
            foreach (var v in variants)
            {
                var widest = v.Lines.Max(l => font.GetStringSize(l, HorizontalAlignment.Left, -1, v.Size).X);
                var e = new Vector2(widest + 10, font.GetHeight(v.Size) * v.Lines.Length + 4);
                Vector2[] spots =
                {
                    new(c.X + r, c.Y - e.Y / 2), new(c.X - r - e.X, c.Y - e.Y / 2), new(c.X - e.X / 2, c.Y - r - e.Y), new(c.X - e.X / 2, c.Y + r),
                    new(c.X + r, c.Y - e.Y * 1.5f), new(c.X + r, c.Y + e.Y * 0.5f), new(c.X - r - e.X, c.Y - e.Y * 1.5f), new(c.X - r - e.X, c.Y + e.Y * 0.5f),
                    new(c.X - e.X / 2, c.Y - r - 2 * e.Y), new(c.X - e.X / 2, c.Y + r + e.Y),
                };
                var best = spots.Select(p => new Rect2(p, e)).MinBy(Cost);
                if (Cost(best) > 0)
                {
                    // Crowded: the nearest free place around the node (a 12 px grid within ~200 px).
                    var near = new List<Rect2>();
                    for (var dy = -200f; dy <= 200f; dy += 12)
                        for (var dx = -200f - e.X; dx <= 200f; dx += 12)
                            near.Add(new Rect2(new Vector2(c.X + dx, c.Y + dy), e));
                    var found = near.Where(rect => !rect.Grow(1).HasPoint(c)).MinBy(rect => Cost(rect) * 10000f + rect.GetCenter().DistanceTo(c));
                    if (Cost(found) < Cost(best)) best = found;
                }
                if (Cost(best) == 0)
                {
                    chosen = best;
                    style = v;
                    break;
                }
                if (Cost(best) < cheapest.Cost) cheapest = (best, Cost(best), v);
            }
            if (chosen == null)
            {
                // No free place even small or in two lines: a neighbour goes without a plate (its regular caption may still fit);
                // the focused node keeps the cheapest place, slid inside the view.
                if (id != HoveredId) continue;
                var e = cheapest.Rect.Size;
                chosen = new Rect2(new Vector2(Mathf.Clamp(cheapest.Rect.Position.X, 0, Mathf.Max(0, Size.X - e.X)), Mathf.Clamp(cheapest.Rect.Position.Y, 0, Mathf.Max(0, Size.Y - e.Y))), e);
                style = cheapest.Style;
            }
            _plateText[id] = style;
            plates.Add((id, chosen.Value));
        }
        return plates;
    }

    /// <summary>The font size and lines of each plate of the last <see cref="PadCaptions"/>.</summary>
    private readonly Dictionary<string, (int Size, string[] Lines)> _plateText = new();

    private void DrawPadCaptions(IReadOnlyList<(string Id, Rect2 Rect)> plates, Font font, int fontSize)
    {
        foreach (var (id, rect) in plates)
        {
            var focused = id == HoveredId;
            // A plate moved away from a crowded node keeps a thin leader to it.
            var c = WorldToLocal(_byId[id].Position);
            var edge = new Vector2(Mathf.Clamp(c.X, rect.Position.X, rect.End.X), Mathf.Clamp(c.Y, rect.Position.Y, rect.End.Y));
            if (edge.DistanceTo(c) > Mathf.Max(_byId[id].Radius * Zoom, 3f) + 8) DrawLine(c, edge, focused ? WindowHost.FocusFrame : EdgeColor, 1f, true);
            DrawRect(rect, PlateColor);
            DrawRect(rect, focused ? WindowHost.FocusFrame : EdgeColor, false, 1f);
            var (size, lines) = _plateText.TryGetValue(id, out var t) ? t : (fontSize, new[] { NameOf(id) });
            for (int i = 0; i < lines.Length; i++)
                DrawString(font, rect.Position + new Vector2(5, 2 + font.GetAscent(size) + i * font.GetHeight(size)), lines[i], HorizontalAlignment.Left, -1, size,
                    focused ? WindowHost.FocusFrame : CaptionColor);
        }
    }

    /// <summary>The zoom the gamepad sets when the tree gets the focus: this many times «Вписать», so the neighbours' names fit.</summary>
    public const float PadZoomOverFit = 1.75f;

    /// <summary>The tree got the gamepad focus: «Вписать» zoom × <see cref="PadZoomOverFit"/>, centred on the selected node.</summary>
    public void PadFocusZoom()
    {
        // The seven-node Ascendancy is already a fitted overview. Do not hide outer nodes merely because
        // the gamepad gained focus; explicit LT/RT zoom and right-stick panning remain available.
        if (ReferenceStyle)
        {
            FitToView();
            return;
        }
        if (HoveredId == null || Size.X <= 1) return;
        FitToView();
        if (NodeLocalPosition(HoveredId) is { } p)
        {
            ZoomAt(p, PadZoomOverFit);
            if (NodeLocalPosition(HoveredId) is { } q) PanBy(Size / 2 - q);
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
    public IReadOnlyList<(string Id, Rect2 Rect, int FontSize)> LayoutCaptions() => LayoutCaptions(Array.Empty<(string Id, Rect2 Rect)>());

    /// <summary>The layout with the gamepad's plates as obstacles: a plated node gets no caption, no caption covers a plate.</summary>
    public IReadOnlyList<(string Id, Rect2 Rect, int FontSize)> LayoutCaptions(IReadOnlyList<(string Id, Rect2 Rect)> plates)
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
            if (placed.Any(p => p.Id == n.Id) || plates.Any(p => p.Id == n.Id)) continue;
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
                return !placed.Any(p => p.Rect.Intersects(padded)) && !plates.Any(p => p.Rect.Intersects(padded)) && !circles.Any(o => CircleHitsRect(o.Center, o.R, padded));
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
