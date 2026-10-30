using Dialoid.Core.Combat;
using Dialoid.Core.Stats;
using Godot;

namespace Dialoid.Game.Room;

/// <summary>How a damage type looks in the room (spec §5 «Боевые эффекты — проверено на прототипе»). Dark is the Apostate's future type.</summary>
public enum EffectKind
{
    Physical,
    Lightning,
    Cold,
    Fire,
    Poison,
    Dark,
}

/// <summary>Colours and shapes per damage type: saturated on the muted room, and different in shape, not only in colour.</summary>
public static class DamageVisuals
{
    /// <summary>The bright part (projectile, burst, number).</summary>
    public static Color Core(EffectKind k) => k switch
    {
        // Fire: clearly redder and more saturated than the torch flame (≈#E8B869) and the torch light (art review, task 8).
        EffectKind.Fire => new Color(1f, 0.36f, 0.06f),
        EffectKind.Cold => new Color(0.35f, 0.8f, 1f),
        EffectKind.Lightning => new Color(0.92f, 0.95f, 1f),
        EffectKind.Physical => new Color(0.85f, 0.85f, 0.85f),
        EffectKind.Poison => new Color(0.42f, 0.85f, 0.22f),
        EffectKind.Dark => new Color(0.08f, 0.02f, 0.12f),
        _ => Colors.White,
    };

    /// <summary>The light of a projectile or a flash.</summary>
    public static Color Light(EffectKind k) => k switch
    {
        EffectKind.Fire => new Color(1f, 0.32f, 0.06f),
        EffectKind.Cold => new Color(0.35f, 0.75f, 1f),
        EffectKind.Lightning => new Color(0.55f, 0.7f, 1f),
        EffectKind.Physical => new Color(1f, 0.62f, 0.25f), // warm sparks «from metal»
        EffectKind.Poison => new Color(0.35f, 0.75f, 0.2f),
        EffectKind.Dark => new Color(0.72f, 0.45f, 1f),
        _ => Colors.White,
    };

    /// <summary>The colour of a floating number (dark numbers use the violet rim, black would vanish).</summary>
    public static Color Number(EffectKind k) => k == EffectKind.Dark ? Light(k) : Core(k);

    public static EffectKind Of(DamageType? type) => type switch
    {
        DamageType.Fire => EffectKind.Fire,
        DamageType.Cold => EffectKind.Cold,
        DamageType.Lightning => EffectKind.Lightning,
        DamageType.Poison => EffectKind.Poison,
        _ => EffectKind.Physical,
    };

    /// <summary>The look of a skill by its compiled tags (the projectile flies before the core's damage type is known).</summary>
    public static EffectKind Of(TagSet tags)
    {
        if (tags.Contains("dark")) return EffectKind.Dark;
        if (tags.Contains("fire")) return EffectKind.Fire;
        if (tags.Contains("cold")) return EffectKind.Cold;
        if (tags.Contains("lightning")) return EffectKind.Lightning;
        if (tags.Contains("poison")) return EffectKind.Poison;
        return EffectKind.Physical;
    }
}

/// <summary>
/// A short-lived visual of the room. <see cref="EffectLayer"/> steps it with the visual time (which stops during hit-stop), so a
/// frozen frame freezes every effect; <see cref="Step"/> returns false when the effect is over and may be freed.
/// </summary>
public abstract partial class VisualEffect : Node3D
{
    public double Age { get; protected set; }

    public abstract bool Step(double dt);
}

/// <summary>An effect driven by a function of its progress 0–1 over a fixed duration (sparks, bursts, flashes, numbers).</summary>
public sealed partial class TweenEffect : VisualEffect
{
    private readonly double _duration;
    private readonly Action<float> _update;

    public TweenEffect(double duration, Action<float> update)
    {
        _duration = Math.Max(duration, 0.001);
        _update = update;
    }

    public override bool Step(double dt)
    {
        Age += dt;
        var t = (float)Math.Min(1, Age / _duration);
        _update(t);
        return Age < _duration;
    }
}

/// <summary>
/// A ball of the damage type flying from the hero to the dummy «for the eyes» (spec §5): the core has already counted the hit,
/// the room shows its number and flash on arrival. Fire: a red-orange ball with a yellow centre and a flame tail (plain alpha,
/// no additive); cold: an ice shard ~1.1 m along the flight; physical: a spinning grey axe ~0.65 m; dark: an unlit black core with
/// a violet back shell. With <c>lit</c> it carries a small shadowless light (at most <see cref="EffectLayer.MaxLitProjectiles"/>).
/// </summary>
public sealed partial class ProjectileView : VisualEffect
{
    private Vector3 _from, _to;
    private double _flight;

    public EffectKind Kind { get; private set; }
    public bool Lit { get; private set; }
    public double FlightSec => _flight;
    public bool Arrived { get; private set; }

    /// <summary>Called once on arrival (the hits' numbers and flashes).</summary>
    public Action? Landed { get; set; }

    internal static ProjectileView Create(EffectLayer layer, EffectKind kind, Vector3 from, Vector3 to, double flightSec, bool lit)
    {
        var p = new ProjectileView { Name = "Projectile", Kind = kind, Lit = lit, _from = from, _to = to, _flight = Math.Max(0.05, flightSec) };
        switch (kind)
        {
            case EffectKind.Dark:
                p.AddChild(EffectLayer.Sphere(0.33f, new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = DamageVisuals.Core(kind) }));
                var shell = layer.Halo(DamageVisuals.Light(kind), 0.5f, cullFront: true); // only the back shell: a rim behind the core
                p.AddChild(EffectLayer.Sphere(0.4f, shell));
                break;
            case EffectKind.Physical:
                p.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(0.65f, 0.1f, 0.32f) },
                    MaterialOverride = new StandardMaterial3D { AlbedoColor = DamageVisuals.Core(kind) * 0.75f, Roughness = 0.35f, Metallic = 0.8f },
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });
                break;
            case EffectKind.Cold:
                p.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(0.22f, 0.22f, 1.1f) },
                    MaterialOverride = layer.Glow(DamageVisuals.Core(kind), 1.6f),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });
                break;
            default: // fire and anything else: a ball with a yellow centre, fire gets the flame tail
                p.AddChild(EffectLayer.Sphere(0.1f, layer.Glow(kind == EffectKind.Fire ? new Color(1f, 0.78f, 0.3f) : DamageVisuals.Core(kind), 1.8f)));
                p.AddChild(EffectLayer.Sphere(0.24f, layer.Halo(DamageVisuals.Core(kind), 0.75f)));
                if (kind == EffectKind.Fire) p.AddChild(FireTail());
                break;
        }
        if (lit)
            p.AddChild(new OmniLight3D { Name = "Light", LightColor = DamageVisuals.Light(kind), LightEnergy = 1.2f, OmniRange = 2.5f, ShadowEnabled = false });
        return p;
    }

    internal void Launch()
    {
        Position = _from;
        var dir = _to - _from;
        if (dir.LengthSquared() > 1e-4f && Mathf.Abs(dir.Normalized().Y) < 0.99f) Basis = Basis.LookingAt(dir, Vector3.Up);
    }

    public override bool Step(double dt)
    {
        if (Arrived) return false;
        Age += dt;
        var t = (float)Math.Min(1, Age / _flight);
        Position = _from.Lerp(_to, t);
        if (Kind == EffectKind.Physical) RotateObjectLocal(Vector3.Up, (float)(dt * Mathf.Tau * 3 / _flight));
        if (t < 1) return true;
        Arrived = true;
        Landed?.Invoke();
        return false;
    }

    /// <summary>The fire tail: soft blobs left behind in world space, red-orange, alpha blended (spec §5: no additive).</summary>
    private static GpuParticles3D FireTail()
    {
        var ramp = new Gradient();
        ramp.SetColor(0, new Color(1f, 0.7f, 0.25f, 0.8f));
        ramp.SetColor(1, new Color(0.25f, 0.03f, 0.01f, 0f));
        ramp.AddPoint(0.3f, new Color(1f, 0.32f, 0.05f, 0.7f));
        ramp.AddPoint(0.65f, new Color(0.5f, 0.08f, 0.02f, 0.4f));
        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(0.05f, 0.05f, 0.05f),
            Direction = Vector3.Up,
            Spread = 12f,
            InitialVelocityMin = 0.18f,
            InitialVelocityMax = 0.3f,
            Gravity = new Vector3(0, 1.2f, 0),
            ScaleMin = 0.5f,
            ScaleMax = 1.2f,
            ColorRamp = new GradientTexture1D { Gradient = ramp },
        };
        return new GpuParticles3D
        {
            Name = "Tail",
            Amount = 24,
            Lifetime = 0.35,
            LocalCoords = false,
            ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = new Vector2(0.35f, 0.35f), Material = EffectLayer.BlobMaterial },
            VisibilityAabb = new Aabb(new Vector3(-20, -2, -20), new Vector3(40, 8, 40)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }
}

/// <summary>
/// The room's combat effects (spec §5): projectiles, lightning ribbons, poison clouds, hit flashes, sparks. Everything is stepped
/// by <see cref="Advance"/> with the visual time; <see cref="Paused"/> also stops the particles (hit-stop). At most
/// <see cref="MaxLitProjectiles"/> projectiles carry a light, at most <see cref="MaxFlashLights"/> flashes; the rest only glow.
/// </summary>
public sealed partial class EffectLayer : Node3D
{
    public const int MaxLitProjectiles = 8;
    public const int MaxFlashLights = 6;
    public const float CritScale = 1.6f;
    public const double FlashSec = 0.18;

    private readonly List<VisualEffect> _active = new();
    private readonly Dictionary<(Color, float, float, bool), StandardMaterial3D> _materials = new();
    private readonly RandomNumberGenerator _rng = new() { Seed = 11 };
    private bool _paused;
    private int _flashLights;

    /// <summary>The room's camera (lightning ribbons face it).</summary>
    public Camera3D? Camera { get; set; }

    /// <summary>Projectiles may carry their own light (High quality).</summary>
    public Func<bool> LightsAllowed { get; set; } = () => true;

    public int Count => _active.Count;
    public int LitProjectiles => _active.OfType<ProjectileView>().Count(p => p.Lit);
    public int Projectiles => _active.OfType<ProjectileView>().Count();
    public IReadOnlyList<VisualEffect> Active => _active;

    /// <summary>Impacts shown so far (for checks).</summary>
    public int Impacts { get; private set; }

    public bool Paused
    {
        get => _paused;
        set
        {
            if (_paused == value) return;
            _paused = value;
            foreach (var p in FindChildren("*", nameof(GpuParticles3D), true, false).OfType<GpuParticles3D>()) p.SpeedScale = value ? 0 : 1;
        }
    }

    public void Advance(double dt)
    {
        if (_paused || dt <= 0) return;
        for (int i = 0; i < _active.Count; i++)
        {
            var e = _active[i];
            if (e.Step(dt)) continue;
            _active.RemoveAt(i--);
            e.QueueFree();
        }
    }

    public T Add<T>(T effect) where T : VisualEffect
    {
        AddChild(effect);
        _active.Add(effect);
        if (_paused)
            foreach (var p in effect.FindChildren("*", nameof(GpuParticles3D), true, false).OfType<GpuParticles3D>()) p.SpeedScale = 0;
        return effect;
    }

    /// <summary>Removes every effect now (a preset change, a restart).</summary>
    public void Clear()
    {
        foreach (var e in _active) e.QueueFree();
        _active.Clear(); // flash lights give their slot back when they leave the tree
    }

    // ---------- effects ----------

    public ProjectileView Projectile(EffectKind kind, Vector3 from, Vector3 to, double flightSec)
    {
        var lit = LightsAllowed() && LitProjectiles < MaxLitProjectiles;
        var p = ProjectileView.Create(this, kind, from, to, flightSec, lit);
        Add(p);
        p.Launch();
        return p;
    }

    /// <summary>Lightning: a jagged camera-facing ribbon (blue-white halo ~0.14 m, white core ~0.05 m), redrawn 3 times in 0.2 s.</summary>
    public void Arc(Vector3 from, Vector3 to)
    {
        var mesh = new ImmediateMesh();
        var halo = Halo(DamageVisuals.Light(EffectKind.Lightning), 0.55f, cullDisabled: true);
        var core = Glow(DamageVisuals.Core(EffectKind.Lightning), 2.5f, cullDisabled: true);
        int drawn = 0;
        TweenEffect? fx = null;
        fx = new TweenEffect(0.22, t =>
        {
            var due = Math.Min(3, (int)(t * 0.22 / 0.07) + 1);
            if (drawn >= due) return;
            drawn = due;
            DrawArc(mesh, from, to, halo, core);
        });
        fx.Name = "Arc";
        fx.AddChild(new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        Add(fx);
        fx.Step(0);
    }

    private void DrawArc(ImmediateMesh im, Vector3 from, Vector3 to, Material halo, Material core)
    {
        im.ClearSurfaces();
        var view = Camera != null ? (Camera.GlobalPosition - (from + to) * 0.5f).Normalized() : CameraRig.Direction;
        const int steps = 8;
        var pts = new Vector3[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            var q = from.Lerp(to, (float)i / steps);
            if (i > 0 && i < steps)
                q += new Vector3((float)_rng.RandfRange(-0.35f, 0.35f), (float)_rng.RandfRange(-0.3f, 0.3f), (float)_rng.RandfRange(-0.35f, 0.35f));
            pts[i] = q;
        }
        for (int pass = 0; pass < 2; pass++)
        {
            var w = pass == 0 ? 0.14f : 0.05f;
            im.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip, pass == 0 ? halo : core);
            for (int i = 0; i < pts.Length; i++)
            {
                var a = pts[Math.Max(i - 1, 0)];
                var b = pts[Math.Min(i + 1, pts.Length - 1)];
                var side = (b - a).Cross(view).Normalized() * w;
                im.SurfaceAddVertex(pts[i] + side);
                im.SurfaceAddVertex(pts[i] - side);
            }
            im.SurfaceEnd();
        }
    }

    /// <summary>Poison: a pale (alpha ~0.18) toxic cloud at the floor, no higher than the dummy's knees.</summary>
    public void Cloud(Vector3 feet)
    {
        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(1.2f, 0.12f, 1.2f),
            Direction = Vector3.Up,
            InitialVelocityMin = 0.02f,
            InitialVelocityMax = 0.08f,
            Gravity = new Vector3(0, 0.01f, 0),
            ScaleMin = 0.8f,
            ScaleMax = 1.5f,
            Color = new Color(0.3f, 0.62f, 0.14f, 0.18f),
        };
        var particles = new GpuParticles3D
        {
            Amount = 22,
            Lifetime = 1.4,
            OneShot = true,
            Explosiveness = 0.6f,
            ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = new Vector2(0.9f, 0.9f), Material = BlobMaterial },
            VisibilityAabb = new Aabb(new Vector3(-3, -1, -3), new Vector3(6, 3, 6)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Emitting = true,
        };
        var fx = new TweenEffect(2.0, _ => { }) { Name = "PoisonCloud" };
        fx.AddChild(particles);
        Add(fx);
        fx.Position = feet + new Vector3(0, 0.3f, 0);
    }

    /// <summary>
    /// A hit at <paramref name="at"/>: a short light flash plus the type's shape — physical warm sparks, lightning blue branching
    /// sparks, dark a black ball in a violet ring, the rest a soft burst; a crit ×<see cref="CritScale"/>; a DoT tick only a weak
    /// flash.
    /// </summary>
    public void Impact(EffectKind kind, Vector3 at, bool crit, bool tick)
    {
        Impacts++;
        var big = crit ? CritScale : 1f;
        if (_flashLights < MaxFlashLights)
        {
            var light = new OmniLight3D { LightColor = DamageVisuals.Light(kind), OmniRange = 3f * (crit ? 1.25f : 1f), ShadowEnabled = false };
            var energy = (tick ? 1.2f : 3f) * big;
            light.LightEnergy = energy;
            _flashLights++;
            var released = false;
            var fx = new TweenEffect(FlashSec, t =>
            {
                light.LightEnergy = energy * (1 - t);
                if (t >= 1 && !released) { released = true; _flashLights--; }
            }) { Name = "Flash" };
            fx.TreeExiting += () => { if (!released) { released = true; _flashLights--; } };
            fx.AddChild(light);
            Add(fx);
            fx.Position = at;
        }
        if (tick) return;
        switch (kind)
        {
            case EffectKind.Physical:
                Sparks(at, DamageVisuals.Light(kind), 10, big);
                break;
            case EffectKind.Lightning:
                Sparks(at, new Color(0.45f, 0.65f, 1f), 7, big);
                break;
            case EffectKind.Dark:
                DarkBurst(at, big);
                break;
            default:
            {
                var burst = Sphere(0.28f * big, Halo(DamageVisuals.Core(kind), 0.7f));
                var fx = new TweenEffect(0.12, t => burst.Scale = Vector3.One * (1 + t)) { Name = "Burst" };
                fx.AddChild(burst);
                Add(fx);
                fx.Position = at;
                break;
            }
        }
    }

    private void Sparks(Vector3 at, Color c, int n, float big)
    {
        var material = Glow(c, 2f);
        var mesh = new BoxMesh { Size = new Vector3(0.025f, 0.025f, 0.22f * big) };
        var sparks = new (MeshInstance3D Mesh, Vector3 Dir, float Reach)[n];
        for (int i = 0; i < n; i++)
        {
            var dir = new Vector3((float)_rng.RandfRange(-1, 1), (float)_rng.RandfRange(-0.2f, 1), (float)_rng.RandfRange(-1, 1)).Normalized();
            var spark = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            spark.Basis = Basis.LookingAt(dir, Mathf.Abs(dir.Y) < 0.95f ? Vector3.Up : Vector3.Right);
            sparks[i] = (spark, dir, (float)_rng.RandfRange(0.5f, 0.9f) * big);
        }
        var fx = new TweenEffect(0.16, t =>
        {
            foreach (var (spark, dir, reach) in sparks)
            {
                spark.Position = dir * reach * t;
                spark.Scale = new Vector3(1, 1, 1 - 0.8f * t);
            }
        }) { Name = "Sparks" };
        foreach (var s in sparks) fx.AddChild(s.Mesh);
        Add(fx);
        fx.Position = at;
    }

    private void DarkBurst(Vector3 at, float big)
    {
        var ball = Sphere(0.25f * big, new StandardMaterial3D { AlbedoColor = DamageVisuals.Core(EffectKind.Dark), Roughness = 0.3f });
        var ring = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.32f * big, OuterRadius = 0.42f * big },
            MaterialOverride = Glow(DamageVisuals.Light(EffectKind.Dark), 1.6f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        var fx = new TweenEffect(0.18, t =>
        {
            ring.Scale = Vector3.One * (1 + 0.8f * t);
            ball.Scale = Vector3.One * (1 + 0.5f * t);
        }) { Name = "DarkBurst" };
        fx.AddChild(ball);
        fx.AddChild(ring);
        Add(fx);
        fx.Position = at;
        // The ring faces the camera.
        var toCamera = Camera != null ? (Camera.GlobalPosition - at).Normalized() : CameraRig.Direction;
        ring.Basis = Basis.LookingAt(toCamera, Vector3.Up) * new Basis(Vector3.Right, Mathf.Pi / 2);
    }

    // ---------- materials and meshes ----------

    /// <summary>The soft round particle used by the fire tail and the poison cloud (vertex colour, alpha blend, unshaded).</summary>
    public static readonly StandardMaterial3D BlobMaterial = MakeBlob();

    private static StandardMaterial3D MakeBlob()
    {
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(1, 1, 1, 1));
        gradient.SetColor(1, new Color(1, 1, 1, 0));
        return new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = new GradientTexture2D
            {
                Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.5f, 0.5f),
                FillTo = new Vector2(0.5f, 0f),
                Gradient = gradient,
            },
        };
    }

    /// <summary>Unshaded glowing material (prototype `_glow_mat`), cached per colour.</summary>
    public StandardMaterial3D Glow(Color c, float energy = 2f, bool cullDisabled = false)
    {
        var key = (c, energy, 1f, cullDisabled);
        if (_materials.TryGetValue(key, out var m)) return m;
        m = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = c,
            EmissionEnabled = true,
            Emission = c,
            EmissionEnergyMultiplier = energy,
            CullMode = cullDisabled ? BaseMaterial3D.CullModeEnum.Disabled : BaseMaterial3D.CullModeEnum.Back,
        };
        return _materials[key] = m;
    }

    /// <summary>Translucent glowing halo (prototype `_halo_mat`), cached per colour.</summary>
    public StandardMaterial3D Halo(Color c, float alpha, bool cullFront = false, bool cullDisabled = false)
    {
        var key = (c, alpha, cullFront ? -1f : cullDisabled ? -2f : -3f, true);
        if (_materials.TryGetValue(key, out var m)) return m;
        m = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(c.R, c.G, c.B, alpha),
            EmissionEnabled = true,
            Emission = c,
            EmissionEnergyMultiplier = 1.5f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            RenderPriority = -1,
            CullMode = cullFront ? BaseMaterial3D.CullModeEnum.Front : cullDisabled ? BaseMaterial3D.CullModeEnum.Disabled : BaseMaterial3D.CullModeEnum.Back,
        };
        return _materials[key] = m;
    }

    public static MeshInstance3D Sphere(float r, Material material) => new()
    {
        Mesh = new SphereMesh { Radius = r, Height = 2 * r, RadialSegments = 16, Rings = 8 },
        MaterialOverride = material,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };
}
