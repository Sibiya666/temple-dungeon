using Godot;

namespace Dialoid.Game.Room;

/// <summary>
/// The dummy in the room (spec §4 «Выбор манекена», «Манекен в бою»): its figures by the preset — «Тренировочный» a zombie,
/// «Бронированный» an orc, «Стихийный» a zombie with slowly turning pale-gold rune rings at its feet, «Рой» five zombies in a
/// cluster (one figure per target of the pack), «Босс» and «Атакующий босс» the orc ×1.4. Enemies are dimmed, without outline and
/// with the baked emission off (<see cref="AssetLibrary"/>). Each figure stands in <c>zombie idle</c> and has a static collision.
/// A hit plays the reaction (<c>sword and shield impact</c>) at most once per <see cref="ReactCooldownSec"/>; DoT ticks only
/// flash. The ring on the floor marks the figure of the last hit. The dummy's own shadowless light is on only while the hero is
/// farther than <see cref="LightDistance"/> (outside the hero's light circle). Positions are in the room's space.
/// </summary>
public partial class DummyView : Node3D
{
    public const double ReactCooldownSec = 0.35;
    public const float LightDistance = 8f;
    public const string IdleClip = "zombie_idle";
    public const string ReactClip = "hit_react";

    /// <summary>Rune rings: pale gold, colder and paler than the hero's golden outline (no purple — that is dark damage's).</summary>
    public static readonly Color RuneColor = new(0.86f, 0.86f, 0.74f);

    /// <summary>Character of the asset table and collision radius per preset look.</summary>
    private static (string Character, float Radius, int Count, bool Runes) Look(string presetId) => presetId switch
    {
        "armored" => ("orc", 0.5f, 1, false),
        "elemental" => ("zombie", 0.4f, 1, true),
        "swarm" => ("zombie", 0.4f, 5, false),
        "boss" or "attacking_boss" => ("boss", 0.7f, 1, false),
        _ => ("zombie", 0.4f, 1, false),
    };

    /// <summary>Offsets of the five «Рой» figures around the spot, metres.</summary>
    private static readonly Vector3[] SwarmOffsets =
    [
        new(0, 0, 0), new(1.15f, 0, 0.45f), new(-1.05f, 0, 0.6f), new(0.45f, 0, -1.1f), new(-0.7f, 0, -0.95f),
    ];

    public sealed class Figure(CharacterModel model, StaticBody3D body, Node3D root, float radius)
    {
        public CharacterModel Model { get; } = model;
        public StaticBody3D Body { get; } = body;
        public Node3D Root { get; } = root;
        public float Radius { get; } = radius;
        public double LastReact { get; internal set; } = double.NegativeInfinity;
    }

    private readonly List<Figure> _figures = new();
    private AssetLibrary _assets = null!;
    private Vector3 _faceTowards;
    private bool _frozen;
    private double _time;
    private StandardMaterial3D _ringMaterial = null!;

    public string PresetId { get; private set; } = "";
    public IReadOnlyList<Figure> Figures => _figures;
    public int FigureCount => _figures.Count;
    public string CharacterId { get; private set; } = "";

    /// <summary>The first figure's model (the only one but on «Рой»).</summary>
    public CharacterModel MainModel => _figures[0].Model;

    /// <summary>The dummy's own light (no shadow, outside the shadow budget).</summary>
    public OmniLight3D Light { get; private set; } = null!;

    /// <summary>The ring under the figure of the last hit (hidden until the first hit).</summary>
    public MeshInstance3D Ring { get; private set; } = null!;

    /// <summary>The target the ring marks, or null before the first hit.</summary>
    public int? RingTarget { get; private set; }

    /// <summary>The «Стихийный» rune rings, or null for other presets.</summary>
    public Node3D? Runes { get; private set; }

    /// <summary>Reactions started (for checks).</summary>
    public int ReactionCount { get; private set; }

    /// <summary>Flashes shown on the dummy, ticks included (for checks).</summary>
    public int FlashCount { get; private set; }

    /// <summary>Animations of every figure stop (hit-stop: speed_scale 0) while true.</summary>
    public bool Frozen
    {
        get => _frozen;
        set
        {
            _frozen = value;
            foreach (var f in _figures)
                if (f.Model.Player != null) f.Model.Player.SpeedScale = value ? 0 : 1;
        }
    }

    /// <summary>Builds the dummy at its current position; call once after it is in the room.</summary>
    public void Build(AssetLibrary assets, string presetId, Vector3 faceTowards)
    {
        _assets = assets;
        _faceTowards = faceTowards;
        Light = new OmniLight3D
        {
            Name = "DummyLight",
            LightColor = new Color(0.85f, 0.85f, 0.95f),
            LightEnergy = 1.6f,
            OmniRange = 5f,
            ShadowEnabled = false,
            Visible = false,
        };
        AddChild(Light);
        Light.Position = new Vector3(1.2f, 2.2f, 1.2f);
        _ringMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.95f, 0.35f, 0.25f, 0.75f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        Ring = new MeshInstance3D
        {
            Name = "TargetRing",
            Mesh = new TorusMesh { InnerRadius = 0.55f, OuterRadius = 0.62f, Rings = 48, RingSegments = 8 },
            MaterialOverride = _ringMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(Ring);
        SetPreset(presetId);
    }

    /// <summary>Rebuilds the figures for another preset (the fight restarts in the core separately).</summary>
    public void SetPreset(string presetId)
    {
        foreach (var f in _figures) f.Root.QueueFree();
        _figures.Clear();
        Runes?.QueueFree();
        Runes = null;
        RingTarget = null;
        Ring.Visible = false;

        PresetId = presetId;
        var (character, radius, count, runes) = Look(presetId);
        CharacterId = character;
        for (int i = 0; i < count; i++)
        {
            var offset = count > 1 ? SwarmOffsets[i % SwarmOffsets.Length] : Vector3.Zero;
            var model = _assets.Character(character);
            var root = new Node3D { Name = $"Figure{i}" };
            AddChild(root);
            root.Position = offset;
            root.AddChild(model);
            var body = new StaticBody3D { Name = "DummyBody" };
            body.AddChild(new CollisionShape3D
            {
                Shape = new CapsuleShape3D { Radius = radius, Height = Mathf.Max(model.TargetHeight, 2 * radius + 0.01f) },
                Position = new Vector3(0, model.TargetHeight / 2, 0),
            });
            root.AddChild(body);
            var toHero = new Vector3(_faceTowards.X, 0, _faceTowards.Z) - (Position + offset) with { Y = 0 };
            if (toHero.LengthSquared() > 0.01f) root.Basis = Basis.LookingAt(toHero, Vector3.Up, true);
            // Swarm figures start their idle at different phases so the pack does not sway in sync.
            model.Play(IdleClip, offset: i * 0.37);
            if (model.Player != null)
            {
                model.Player.SpeedScale = _frozen ? 0 : 1;
                model.Player.AnimationFinished += name =>
                {
                    if (name.ToString().EndsWith(ReactClip, StringComparison.Ordinal)) model.Play(IdleClip, blend: 0.2);
                };
            }
            _figures.Add(new Figure(model, body, root, radius));
        }
        if (runes) BuildRunes();
        var ring = (TorusMesh)Ring.Mesh;
        ring.InnerRadius = radius + 0.2f;
        ring.OuterRadius = radius + 0.28f;
    }

    /// <summary>Two pale-gold rings with rune marks at the feet, turning slowly in opposite directions (not a damage type's look).</summary>
    private void BuildRunes()
    {
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = RuneColor with { A = 0.85f },
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        Runes = new Node3D { Name = "Runes" };
        AddChild(Runes);
        Runes.Position = new Vector3(0, 0.04f, 0);
        foreach (var (inner, outer, marks, name) in new[] { (0.62f, 0.66f, 8, "Inner"), (0.92f, 0.96f, 12, "Outer") })
        {
            var ring = new Node3D { Name = name };
            Runes.AddChild(ring);
            ring.AddChild(new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = inner, OuterRadius = outer, Rings = 48, RingSegments = 6 },
                MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
            // Rune marks: short bars and ticks standing across the ring, alternating.
            var bar = new BoxMesh { Size = new Vector3(0.05f, 0.02f, 0.16f) };
            var tick = new BoxMesh { Size = new Vector3(0.12f, 0.02f, 0.04f) };
            for (int i = 0; i < marks; i++)
            {
                var a = Mathf.Tau * i / marks;
                var r = (inner + outer) / 2 + 0.09f;
                var mark = new MeshInstance3D
                {
                    Mesh = i % 2 == 0 ? bar : tick,
                    MaterialOverride = material,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                ring.AddChild(mark);
                mark.Position = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                mark.Rotation = new Vector3(0, -a, 0);
            }
        }
    }

    public override void _Process(double delta)
    {
        if (!_frozen) Advance(delta);
    }

    /// <summary>Turns the runes and pulses the ring.</summary>
    public void Advance(double dt)
    {
        _time += dt;
        if (Runes != null)
        {
            Runes.GetChild<Node3D>(0).Rotation = new Vector3(0, (float)(_time * 0.35), 0);
            Runes.GetChild<Node3D>(1).Rotation = new Vector3(0, (float)(-_time * 0.22), 0);
        }
        if (Ring.Visible)
            _ringMaterial.AlbedoColor = _ringMaterial.AlbedoColor with { A = 0.6f + 0.2f * Mathf.Sin((float)_time * 5f) };
    }

    /// <summary>The figure of a core target (0 is the first pack member); targets past the figures wrap around.</summary>
    public int FigureOf(int target) => _figures.Count == 0 ? 0 : ((target % _figures.Count) + _figures.Count) % _figures.Count;

    /// <summary>Collision radius of the target's figure (the boss has its own).</summary>
    public float CollisionRadius(int target) => _figures[FigureOf(target)].Radius;

    /// <summary>The target figure's feet in the room's space.</summary>
    public Vector3 TargetPosition(int target) => Position + _figures[FigureOf(target)].Root.Position;

    /// <summary>Where hits land on the target figure (chest height).</summary>
    public Vector3 HitPoint(int target) => TargetPosition(target) + new Vector3(0, _figures[FigureOf(target)].Model.TargetHeight * 0.62f, 0);

    /// <summary>Distance from <paramref name="point"/> to the collision edge of the nearest figure, metres (0 inside), and that figure.</summary>
    public (float Distance, int Target) Nearest(Vector3 point)
    {
        var best = (float.MaxValue, 0);
        for (int i = 0; i < _figures.Count; i++)
        {
            var p = TargetPosition(i);
            var d = Mathf.Max(0, new Vector2(point.X - p.X, point.Z - p.Z).Length() - _figures[i].Radius);
            if (d < best.Item1) best = (d, i);
        }
        return best;
    }

    /// <summary>The figure under the ray, if any (a capsule test at several heights; the ray in the room's space).</summary>
    public int? Pick(Vector3 origin, Vector3 direction)
    {
        if (Mathf.Abs(direction.Y) < 1e-4f) return null;
        for (int i = 0; i < _figures.Count; i++)
        {
            var p = TargetPosition(i);
            var h = _figures[i].Model.TargetHeight;
            for (int k = 1; k <= 5; k++)
            {
                var y = h * k / 6f;
                var t = (y - origin.Y) / direction.Y;
                if (t < 0) continue;
                var q = origin + direction * t;
                if (new Vector2(q.X - p.X, q.Z - p.Z).Length() <= _figures[i].Radius + 0.2f) return i;
            }
        }
        return null;
    }

    /// <summary>The reaction on the target's figure, unless it reacted less than <see cref="ReactCooldownSec"/> ago; true when it played.</summary>
    public bool React(int target, double now)
    {
        var f = _figures[FigureOf(target)];
        if (now - f.LastReact < ReactCooldownSec) return false;
        f.LastReact = now;
        ReactionCount++;
        f.Model.Play(ReactClip, blend: 0.05, restart: true);
        return true;
    }

    /// <summary>The flash of a hit on the target (the type's shape and colour, crit ×1.6, tick weak).</summary>
    public void Flash(EffectLayer effects, int target, EffectKind kind, bool crit, bool tick)
    {
        FlashCount++;
        effects.Impact(kind, HitPoint(target), crit, tick);
    }

    /// <summary>Moves the ring under the target's figure.</summary>
    public void MarkTarget(int target)
    {
        RingTarget = FigureOf(target);
        Ring.Visible = true;
        Ring.Position = _figures[RingTarget.Value].Root.Position + new Vector3(0, 0.03f, 0);
    }

    /// <summary>The dummy's light is on only while the hero stands farther than <see cref="LightDistance"/> from the dummy.</summary>
    public void UpdateLight(Vector3 hero)
    {
        var on = new Vector2(hero.X - Position.X, hero.Z - Position.Z).Length() > LightDistance;
        if (Light.Visible != on) Light.Visible = on;
    }
}
