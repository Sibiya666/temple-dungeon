using Dialoid.Core.Explain;
using Godot;

namespace Dialoid.Game.Room;

/// <summary>
/// Damage numbers over the target (spec §4 «Экран боя», §5): the core's final damage, grouped like the combat log; a crit is
/// larger and ends with «!»; a DoT tick is small and fainter (<see cref="TickAlpha"/>) so hits read first; the damage type's
/// icon stands left of the number so the meaning does not rest on colour alone. A number lives <see cref="LifeSec"/> and rises;
/// numbers take turns over <see cref="Slots"/> places along the screen horizontal, and a new number that would cover a young
/// one on screen moves up a line. At most <see cref="MaxShown"/> at once (the oldest go first). Stepped with the visual time
/// (frozen by hit-stop). <see cref="Enabled"/> off shows nothing (the meter's switch, task 10).
/// </summary>
public sealed partial class FloatingNumbers : Node3D
{
    public const double LifeSec = 0.6;
    public const int Slots = 4;
    public const int MaxShown = 40;
    public const float PixelSize = 0.0065f;
    public const int FontSize = 64;
    public const int CritFontSize = 96;
    public const int TickFontSize = 40;
    public const float Rise = 1f;
    public const float TickAlpha = 0.7f;

    /// <summary>A new number moves up a line while it covers a number younger than this.</summary>
    public const double OverlapAgeSec = 0.45;

    private sealed record Shown(Node3D Root, Label3D Label, Sprite3D Icon, Vector3 Start, float Width, float Height, float BaseAlpha);

    private readonly List<(Shown Item, double Age)> _shown = new();
    private static readonly Dictionary<EffectKind, ImageTexture> Icons = new();
    private int _slot;

    public bool Enabled { get; set; } = true;

    /// <summary>The room's camera: the slots lie along its horizontal.</summary>
    public Camera3D? Camera { get; set; }

    public int Count => _shown.Count;

    /// <summary>Numbers shown since the start (for checks).</summary>
    public int ShownTotal { get; private set; }

    /// <summary>Texts of the numbers on screen now, oldest first.</summary>
    public IEnumerable<string> Texts => _shown.Select(s => s.Item.Label.Text);

    /// <summary>The last number shown (text and font size), for checks.</summary>
    public (string Text, int FontSize, EffectKind Kind)? Last { get; private set; }

    /// <summary>Pairs of young numbers on screen whose boxes overlap now (for checks; the layout keeps it 0 where it can).</summary>
    public int Overlaps()
    {
        var (right, up) = ScreenAxes();
        int n = 0;
        for (int i = 0; i < _shown.Count; i++)
        for (int j = i + 1; j < _shown.Count; j++)
            if (Covers(_shown[i].Item, _shown[i].Item.Root.Position, _shown[j].Item, _shown[j].Item.Root.Position, right, up)) n++;
        return n;
    }

    /// <summary>The text of a number: grouped like the combat log, a crit with « !».</summary>
    public static string Format(double amount, bool crit) => RuNumber.Grouped(amount) + (crit ? " !" : "");

    public void Show(Vector3 at, double amount, EffectKind kind, bool crit, bool tick)
    {
        if (!Enabled) return;
        if (_shown.Count >= MaxShown)
        {
            _shown[0].Item.Root.QueueFree();
            _shown.RemoveAt(0);
        }
        var size = crit ? CritFontSize : tick ? TickFontSize : FontSize;
        var color = DamageVisuals.Number(kind);
        var baseAlpha = tick ? TickAlpha : 1f;
        var text = Format(amount, crit);
        var label = new Label3D
        {
            Text = text,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Shaded = false,
            FontSize = size,
            PixelSize = PixelSize,
            OutlineSize = crit ? 18 : 14,
            OutlineModulate = new Color(0, 0, 0, baseAlpha),
            Modulate = color with { A = baseAlpha },
            RenderPriority = 10,
            OutlineRenderPriority = 9,
        };
        // The icon left of the text, along the camera's horizontal (the camera never rotates, only follows).
        var (right, up) = ScreenAxes();
        var textWidth = text.Length * size * PixelSize * 0.45f;
        var iconSize = size * PixelSize * 0.8f;
        var icon = new Sprite3D
        {
            Texture = Icon(kind),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Shaded = false,
            PixelSize = iconSize / 32f,
            // The fire icon carries its own colours (yellow core, orange edge); the others are white and take the number's colour.
            Modulate = (kind == EffectKind.Fire ? Colors.White : color) with { A = baseAlpha },
            RenderPriority = 10,
            Position = -right * (textWidth / 2 + iconSize * 0.7f),
        };
        var root = new Node3D { Name = "Number" };
        root.AddChild(label);
        root.AddChild(icon);
        AddChild(root);
        _slot = (_slot + 1) % Slots;
        var start = at + right * ((_slot - 1.5f) * 0.75f) + new Vector3(0, (_slot % 2) * 0.35f, 0);
        var line = size * PixelSize * 1.05f;
        var item = new Shown(root, label, icon, start, textWidth + iconSize * 1.4f, line, baseAlpha);
        // Move up a line (at most six) while the new number covers a young one.
        for (int k = 0; k < 6 && _shown.Any(s => s.Age < OverlapAgeSec && Covers(s.Item, s.Item.Root.Position, item, start, right, up)); k++)
            start += up * line;
        item = item with { Start = start };
        root.Position = start;
        _shown.Add((item, 0));
        Last = (text, size, kind);
        ShownTotal++;
    }

    public void Advance(double dt)
    {
        if (dt <= 0) return;
        for (int i = 0; i < _shown.Count; i++)
        {
            var (item, age) = _shown[i];
            age += dt;
            if (age >= LifeSec)
            {
                item.Root.QueueFree();
                _shown.RemoveAt(i--);
                continue;
            }
            _shown[i] = (item, age);
            var t = (float)(age / LifeSec);
            item.Root.Position = item.Start + new Vector3(0, Rise * t, 0);
            var alpha = item.BaseAlpha * (t < 0.5f ? 1f : 1f - (t - 0.5f) * 2f);
            item.Label.Modulate = item.Label.Modulate with { A = alpha };
            item.Label.OutlineModulate = item.Label.OutlineModulate with { A = alpha };
            item.Icon.Modulate = item.Icon.Modulate with { A = alpha };
        }
    }

    public void Clear()
    {
        foreach (var (item, _) in _shown) item.Root.QueueFree();
        _shown.Clear();
    }

    private (Vector3 Right, Vector3 Up) ScreenAxes() => Camera != null
        ? (Camera.GlobalBasis.X, Camera.GlobalBasis.Y)
        : (new Vector3(1, 0, -1).Normalized(), Basis.LookingAt(-CameraRig.Direction, Vector3.Up).Y);

    /// <summary>The two numbers' screen boxes (centred on their positions) overlap.</summary>
    private static bool Covers(Shown a, Vector3 pa, Shown b, Vector3 pb, Vector3 right, Vector3 up)
    {
        var d = pb - pa;
        return Mathf.Abs(d.Dot(right)) < (a.Width + b.Width) / 2 && Mathf.Abs(d.Dot(up)) < (a.Height + b.Height) / 2;
    }

    // ---------- icons ----------

    /// <summary>
    /// A 32×32 icon with a dark rim: physical — crossed swords, fire — a flame (wide base, a tall middle tongue and two lower tips
    /// bent outward, yellow inner tongue in an orange flame), cold — a snowflake, lightning — a zigzag bolt, poison — bubbles,
    /// dark — a ring. All but fire are white and take the number's colour. Drawn once per type (no font glyphs needed).
    /// </summary>
    public static ImageTexture Icon(EffectKind kind)
    {
        if (Icons.TryGetValue(kind, out var cached)) return cached;
        const int n = 32, ss = 3;
        var image = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        var outer = kind == EffectKind.Fire ? new Color(1f, 0.45f, 0.1f) : Colors.White;
        var inner = kind == EffectKind.Fire ? new Color(1f, 0.85f, 0.35f) : Colors.White;
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float inside = 0, core = 0, rim = 0;
                for (int sy = 0; sy < ss; sy++)
                for (int sx = 0; sx < ss; sx++)
                {
                    var px = ((x + (sx + 0.5f) / ss) / n) * 2 - 1;
                    var py = 1 - ((y + (sy + 0.5f) / ss) / n) * 2;
                    var d = Distance(kind, px, py);
                    if (d <= 0)
                    {
                        inside++;
                        if (kind == EffectKind.Fire && InFlame(new Vector2(px, py), inner: true)) core++;
                    }
                    else if (d <= 0.16f) rim++;
                }
                var total = ss * ss;
                var a = Mathf.Clamp((inside + rim * 0.85f) / total, 0, 1);
                if (a <= 0)
                {
                    image.SetPixel(x, y, new Color(0, 0, 0, 0));
                    continue;
                }
                // Colour: rim black, inside outer/inner colour by coverage.
                var c = inside > 0 ? outer.Lerp(inner, core / inside) : Colors.Black;
                var lit = inside / (inside + rim * 0.85f);
                image.SetPixel(x, y, new Color(c.R * lit, c.G * lit, c.B * lit, a));
            }
        }
        return Icons[kind] = ImageTexture.CreateFromImage(image);
    }

    // The flame outline (y up): a wide rounded base, two side tips bent outward, a tall middle tongue slightly off centre.
    private static readonly Vector2[] Flame =
    [
        new(0f, -0.95f), new(0.45f, -0.86f), new(0.7f, -0.6f), new(0.74f, -0.25f), new(0.7f, 0.05f), new(0.86f, 0.5f),
        new(0.5f, 0.2f), new(0.38f, 0.3f), new(0.26f, 0.58f), new(0.06f, 0.98f), new(-0.16f, 0.62f), new(-0.32f, 0.32f),
        new(-0.46f, 0.2f), new(-0.84f, 0.48f), new(-0.7f, 0.05f), new(-0.74f, -0.25f), new(-0.7f, -0.6f), new(-0.45f, -0.86f),
    ];

    // The inner tongue: the flame scaled to ~half around a point low in the base.
    private static readonly Vector2[] FlameInner = Flame.Select(v => new Vector2(0, -0.5f) + (v - new Vector2(0, -0.5f)) * new Vector2(0.48f, 0.55f)).ToArray();

    private static bool InFlame(Vector2 p, bool inner) => Geometry2D.IsPointInPolygon(p, inner ? FlameInner : Flame);

    /// <summary>Signed-ish distance of (x, y) in [-1, 1]² to the icon shape (≤ 0 inside).</summary>
    private static float Distance(EffectKind kind, float x, float y)
    {
        var p = new Vector2(x, y);
        switch (kind)
        {
            case EffectKind.Physical:
                return Mathf.Min(Sword(p, new Vector2(1, 1).Normalized()), Sword(p, new Vector2(-1, 1).Normalized()));
            case EffectKind.Fire:
                return InFlame(p, inner: false) ? -1f : PolygonDistance(p, Flame);
            case EffectKind.Cold:
            {
                var d = float.MaxValue;
                for (int i = 0; i < 3; i++)
                {
                    var a = Mathf.Pi / 2 + i * Mathf.Pi / 3;
                    var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.85f;
                    d = Mathf.Min(d, Segment(p, -dir, dir));
                }
                return d - 0.11f;
            }
            case EffectKind.Lightning:
            {
                Vector2[] bolt = [new(0.3f, 0.95f), new(-0.45f, 0.02f), new(0.02f, 0.02f), new(-0.3f, -0.95f), new(0.45f, -0.05f), new(-0.02f, -0.05f)];
                return Geometry2D.IsPointInPolygon(p, bolt) ? -1f : PolygonDistance(p, bolt);
            }
            case EffectKind.Poison:
                return Mathf.Min(p.DistanceTo(new Vector2(-0.35f, -0.35f)) - 0.4f,
                    Mathf.Min(p.DistanceTo(new Vector2(0.42f, -0.2f)) - 0.3f, p.DistanceTo(new Vector2(0.02f, 0.5f)) - 0.28f));
            default:
                return Mathf.Abs(p.Length() - 0.62f) - 0.18f;
        }
    }

    /// <summary>A sword pointing along <paramref name="d"/> (blade up, hilt at the opposite corner): blade, crossguard, grip, pommel.</summary>
    private static float Sword(Vector2 p, Vector2 d)
    {
        var n = new Vector2(-d.Y, d.X);
        Vector2 At(float u, float v) => d * u + n * v;
        var blade = Segment(p, At(-0.42f, 0), At(0.98f, 0)) - 0.11f;
        var guard = Segment(p, At(-0.5f, -0.36f), At(-0.5f, 0.36f)) - 0.09f;
        var grip = Segment(p, At(-0.95f, 0), At(-0.5f, 0)) - 0.05f;
        var pommel = p.DistanceTo(At(-1.05f, 0)) - 0.09f;
        return Mathf.Min(Mathf.Min(blade, guard), Mathf.Min(grip, pommel));
    }

    private static float Segment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var t = Mathf.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0, 1);
        return p.DistanceTo(a + ab * t);
    }

    private static float PolygonDistance(Vector2 p, Vector2[] poly)
    {
        var d = float.MaxValue;
        for (int i = 0; i < poly.Length; i++) d = Mathf.Min(d, Segment(p, poly[i], poly[(i + 1) % poly.Length]));
        return d;
    }
}
