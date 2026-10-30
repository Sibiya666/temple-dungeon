using Dialoid.Core.Explain;
using Godot;

namespace Dialoid.Game.Room;

/// <summary>
/// Damage numbers over the target (spec §4 «Экран боя», §5): the core's final damage, grouped like the combat log; a crit is
/// larger and ends with «!»; a DoT tick is small; the damage type's icon stands left of the number so the meaning does not rest
/// on colour alone. A number lives <see cref="LifeSec"/> and rises; numbers take turns over <see cref="Slots"/> places along the
/// screen horizontal so they do not overlap. At most <see cref="MaxShown"/> at once (the oldest go first). Stepped with the
/// visual time (frozen by hit-stop). <see cref="Enabled"/> off shows nothing (the meter's switch, task 10).
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

    private sealed record Shown(Node3D Root, Label3D Label, Sprite3D Icon, Vector3 Start);

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
            OutlineModulate = new Color(0, 0, 0, 0.9f),
            Modulate = color,
            RenderPriority = 10,
            OutlineRenderPriority = 9,
        };
        // The icon left of the text, along the camera's horizontal (the camera never rotates, only follows).
        var right = Camera != null ? Camera.GlobalBasis.X : new Vector3(1, 0, -1).Normalized();
        var textWidth = text.Length * size * PixelSize * 0.55f;
        var iconSize = size * PixelSize * 0.8f;
        var icon = new Sprite3D
        {
            Texture = Icon(kind),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Shaded = false,
            PixelSize = iconSize / 32f,
            Modulate = color,
            RenderPriority = 10,
            Position = -right * (textWidth / 2 + iconSize * 0.7f),
        };
        var root = new Node3D { Name = "Number" };
        root.AddChild(label);
        root.AddChild(icon);
        AddChild(root);
        _slot = (_slot + 1) % Slots;
        var start = at + right * ((_slot - 1.5f) * 0.75f) + new Vector3(0, (_slot % 2) * 0.35f, 0);
        root.Position = start;
        _shown.Add((new Shown(root, label, icon, start), 0));
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
            var alpha = t < 0.5f ? 1f : 1f - (t - 0.5f) * 2f;
            item.Label.Modulate = item.Label.Modulate with { A = alpha };
            item.Label.OutlineModulate = item.Label.OutlineModulate with { A = 0.9f * alpha };
            item.Icon.Modulate = item.Icon.Modulate with { A = alpha };
        }
    }

    public void Clear()
    {
        foreach (var (item, _) in _shown) item.Root.QueueFree();
        _shown.Clear();
    }

    // ---------- icons ----------

    /// <summary>
    /// A 32×32 white icon with a dark rim, tinted by the number's colour: physical — crossed blades, fire — a flame drop, cold — a
    /// snowflake, lightning — a zigzag bolt, poison — bubbles, dark — a ring. Drawn once per type (no font glyphs needed).
    /// </summary>
    public static ImageTexture Icon(EffectKind kind)
    {
        if (Icons.TryGetValue(kind, out var cached)) return cached;
        const int n = 32, ss = 3;
        var image = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float inside = 0, rim = 0;
                for (int sy = 0; sy < ss; sy++)
                for (int sx = 0; sx < ss; sx++)
                {
                    var px = ((x + (sx + 0.5f) / ss) / n) * 2 - 1;
                    var py = 1 - ((y + (sy + 0.5f) / ss) / n) * 2;
                    var d = Distance(kind, px, py);
                    if (d <= 0) inside++;
                    else if (d <= 0.16f) rim++;
                }
                inside /= ss * ss;
                rim /= ss * ss;
                var a = Mathf.Clamp(inside + rim * 0.85f, 0, 1);
                var v = a > 0 ? inside / a : 0;
                image.SetPixel(x, y, new Color(v, v, v, a));
            }
        }
        return Icons[kind] = ImageTexture.CreateFromImage(image);
    }

    /// <summary>Signed-ish distance of (x, y) in [-1, 1]² to the icon shape (≤ 0 inside).</summary>
    private static float Distance(EffectKind kind, float x, float y)
    {
        var p = new Vector2(x, y);
        switch (kind)
        {
            case EffectKind.Physical:
                return Mathf.Min(Segment(p, new(-0.7f, -0.7f), new(0.7f, 0.7f)), Segment(p, new(-0.7f, 0.7f), new(0.7f, -0.7f))) - 0.17f;
            case EffectKind.Fire:
            {
                var circle = p.DistanceTo(new Vector2(0, -0.3f)) - 0.52f;
                var half = y is > -0.3f and < 0.9f ? 0.52f * (0.9f - y) / 1.2f : -1f;
                var cone = half < 0 ? 1f : Mathf.Abs(x) - half;
                return Mathf.Min(circle, cone);
            }
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
