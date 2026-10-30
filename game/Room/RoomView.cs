using Godot;

namespace Dialoid.Game.Room;

/// <summary>Graphics quality of the room (spec §4). Low is the minimum spec: no SSAO, torch shadows, fog or projectile lights; the hero keeps his shadow and outline.</summary>
public enum GraphicsQuality
{
    Low,
    High,
}

/// <summary>
/// The 3D training room (spec §4): a dark dungeon hall about 24×18 m built from Godot primitives with Poly Haven PBR materials,
/// a slab floor in chunks, walls with pilasters, five pillars with caps, barrels, flickering torches on stands and walls, blood
/// decals, the hero (outline, warm light circle with the only full shadow, fill light) and the dummy (own light). The torches
/// nearest the hero get partial shadows within the budget of <see cref="MaxShadowLights"/>; pillars between the camera and the
/// hero or the dummy fade to an alpha-hash dither. Draws only: positions come from the game layer, rules from the core.
/// </summary>
public partial class RoomView : Node3D
{
    public const float HalfX = 12f;
    public const float HalfZ = 9f;
    public const float WallHeight = 4.5f;

    /// <summary>Front walls (towards the camera) are cut to this height so they never hide the fight (spec §4: «срезаются по высоте»).</summary>
    public const float FrontWallHeight = 0.6f;

    /// <summary>Shadow-casting lights in the frame, hero light included (spec §4 budget).</summary>
    public const int MaxShadowLights = 8;

    /// <summary>Torches closer than this to the hero may cast shadows.</summary>
    public const float TorchShadowRadius = 10f;

    /// <summary>Torch shadows are partial so that crossed shadows do not hide the hero and the dummy (spec §4: 0.5–0.7).</summary>
    public const float TorchShadowOpacity = 0.6f;

    /// <summary>Faded occluder alpha (alpha hash dither).</summary>
    public const float FadedAlpha = 0.3f;

    /// <summary>User argument: «--quality low» or «--quality high» (default high).</summary>
    public const string QualityArg = "--quality";

    /// <summary>Torch light: warm amber, clearly yellower than the orange fire flash on the dummy (spec §4).</summary>
    public static readonly Color TorchColor = new(1f, 0.74f, 0.46f);

    /// <summary>Visual layer of the floor only: blood decals project onto it and never onto the figures.</summary>
    public const uint FloorLayer = 2;

    /// <summary>A pillar or a wall that fades when it stands between the camera and the hero or the dummy.</summary>
    public sealed class Occluder(string name, Aabb bounds, IReadOnlyList<StandardMaterial3D> materials)
    {
        public string Name { get; } = name;
        public Aabb Bounds { get; } = bounds;
        public IReadOnlyList<StandardMaterial3D> Materials { get; } = materials;
        public bool Faded { get; internal set; }
    }

    private sealed record Torch(OmniLight3D Light, float BaseEnergy, float Seed);

    private readonly List<Torch> _torches = new();
    private readonly List<Occluder> _occluders = new();
    private readonly FastNoiseLite _flicker = new() { Frequency = 2.5f };
    private readonly RandomNumberGenerator _rng = new() { Seed = 7 };
    private GraphicsQuality _quality = GraphicsQuality.High;
    private bool _volumetricFog;
    private double _time;

    public AssetLibrary Assets { get; private set; } = null!;
    public Godot.Environment Environment { get; private set; } = null!;
    public CameraRig CameraRig { get; private set; } = null!;

    /// <summary>The hero's body (collisions, position); the model is <see cref="HeroModel"/>.</summary>
    public CharacterBody3D Hero { get; private set; } = null!;
    public CharacterModel HeroModel { get; private set; } = null!;
    public StaticBody3D Dummy { get; private set; } = null!;
    public CharacterModel DummyModel { get; private set; } = null!;

    /// <summary>The hero's light circle (~8 m), the only light with a full shadow.</summary>
    public OmniLight3D HeroLight { get; private set; } = null!;

    /// <summary>Fill light on the hero's chest from the camera side; no shadow, outside the budget.</summary>
    public OmniLight3D FillLight { get; private set; } = null!;

    /// <summary>The dummy's own light so it reads outside the hero's circle; no shadow, outside the budget.</summary>
    public OmniLight3D DummyLight { get; private set; } = null!;

    public IReadOnlyList<OmniLight3D> TorchLights => _torches.Select(t => t.Light).ToList();
    public IReadOnlyList<Occluder> Occluders => _occluders;

    /// <summary>Where the hero appears: at the front edge of the hall, facing the dummy.</summary>
    public Vector3 HeroSpawn { get; } = new(5f, 0, 3.5f);

    /// <summary>Where the dummy stands: the middle of the hall.</summary>
    public Vector3 DummySpot { get; } = new(-1.5f, 0, -1f);

    /// <summary>Every light that casts a shadow now.</summary>
    public IEnumerable<OmniLight3D> ShadowLights =>
        FindChildren("*", nameof(OmniLight3D), true, false).OfType<OmniLight3D>().Where(l => l.ShadowEnabled);

    public GraphicsQuality Quality
    {
        get => _quality;
        set
        {
            _quality = value;
            ApplyQuality();
        }
    }

    /// <summary>Volumetric fog (off by default, spec §4: barely visible at 0.01 and costly); High quality only.</summary>
    public bool VolumetricFog
    {
        get => _volumetricFog;
        set
        {
            _volumetricFog = value;
            ApplyQuality();
        }
    }

    /// <summary>Projectiles may carry their own light (at most 8; spec §4). Off on Low.</summary>
    public bool ProjectileLights => _quality == GraphicsQuality.High;

    /// <summary>The quality from the user arguments («--quality low» / «--quality=low»), else High.</summary>
    public static GraphicsQuality QualityFromArgs(IReadOnlyList<string> args)
    {
        for (int i = 0; i < args.Count; i++)
        {
            string? value = null;
            if (args[i] == QualityArg && i + 1 < args.Count) value = args[i + 1];
            else if (args[i].StartsWith(QualityArg + "=", StringComparison.Ordinal)) value = args[i][(QualityArg.Length + 1)..];
            if (value != null) return value.Equals("low", StringComparison.OrdinalIgnoreCase) ? GraphicsQuality.Low : GraphicsQuality.High;
        }
        return GraphicsQuality.High;
    }

    /// <summary>Builds the whole room; call once, after the node is in the tree.</summary>
    public void Build(AssetLibrary assets, GraphicsQuality quality)
    {
        Assets = assets;
        BuildEnvironment();
        BuildFloor();
        BuildWalls();
        foreach (var p in new[] { new Vector3(-7, 0, -3), new Vector3(4, 0, -6), new Vector3(-7, 0, 5), new Vector3(7.5f, 0, 0.5f), new Vector3(-2, 0, -7) })
            BuildPillar(p);
        BuildBarrels(new Vector3(-9.6f, 0, -6.4f));
        foreach (var p in new[]
                 {
                     new Vector3(-9.5f, 0, -0.5f), new Vector3(9.8f, 0, -3.5f), new Vector3(-3f, 0, 6.8f), new Vector3(9.8f, 0, -7.4f),
                     new Vector3(-4.2f, 0, -3.4f), new Vector3(4.5f, 0, 1.2f), new Vector3(-10f, 0, 7.4f),
                 })
            BuildTorchStand(p);
        BuildBlood(new Vector3(-4f, 0, 2.5f), 1.6f);
        BuildBlood(new Vector3(2f, 0, 3.2f), 1.2f);
        BuildBlood(DummySpot + new Vector3(0.7f, 0, 0.9f), 2.2f);
        BuildBlood(new Vector3(5.5f, 0, -3.5f), 1.4f);
        BuildHero();
        BuildDummy();
        CameraRig = new CameraRig { Name = "CameraRig", Target = Hero };
        AddChild(CameraRig);
        CameraRig.Camera.Current = true;
        _quality = quality;
        ApplyQuality();
        PlaceHero(HeroSpawn);
    }

    /// <summary>Moves the hero (teleport) and updates the camera, the fill light, the shadow budget and the fading at once.</summary>
    public void PlaceHero(Vector3 position)
    {
        Hero.Position = new Vector3(position.X, 0, position.Z);
        var toDummy = new Vector3(DummySpot.X, 0, DummySpot.Z) - Hero.Position;
        if (toDummy.LengthSquared() > 0.01f) Hero.Basis = Basis.LookingAt(toDummy, Vector3.Up, true);
        RefreshView();
    }

    /// <summary>Camera follow, fill light, shadow budget and fading for the hero's current position.</summary>
    public void RefreshView()
    {
        CameraRig.Follow();
        var side = new Vector3(CameraRig.Direction.X, 0, CameraRig.Direction.Z).Normalized();
        FillLight.Position = Hero.Position + side * 1.4f + new Vector3(0, 1.3f, 0);
        UpdateShadowBudget();
        UpdateOcclusion();
    }

    /// <summary>
    /// The room is on screen: its SubViewport's container is visible (a hidden tab skips the per-frame work). A room not in a
    /// SubViewport counts as visible.
    /// </summary>
    public bool IsShown => GetViewport() is not SubViewport { } viewport || viewport.GetParent() is not CanvasItem parent || parent.IsVisibleInTree();

    public override void _Process(double delta)
    {
        if (Hero == null || !IsShown) return;
        _time += delta;
        foreach (var t in _torches)
            t.Light.LightEnergy = t.BaseEnergy * (0.82f + 0.25f * _flicker.GetNoise1D((float)(_time * 10.0) + t.Seed));
        RefreshView();
    }

    // ---------- budget and fading ----------

    /// <summary>A shadowed torch keeps its shadow out to this extra distance and wins ties by this much (no popping at the edge).</summary>
    public const float ShadowHysteresis = 0.5f;

    private float[] _scores = [];
    private int[] _order = [];

    /// <summary>
    /// The hero light always casts a full shadow. On High the torches nearest the hero (within <see cref="TorchShadowRadius"/>,
    /// a shadowed one within the radius + <see cref="ShadowHysteresis"/>) cast partial shadows, at most
    /// <see cref="MaxShadowLights"/> − 1 of them; every other torch casts none. A light is touched only when its state changes
    /// (each change re-pairs it in the renderer); no allocations per frame.
    /// </summary>
    public void UpdateShadowBudget()
    {
        if (!HeroLight.ShadowEnabled) HeroLight.ShadowEnabled = true;
        var allowed = _quality == GraphicsQuality.High ? MaxShadowLights - 1 : 0;
        var n = _torches.Count;
        if (_scores.Length != n) (_scores, _order) = (new float[n], new int[n]);
        var hero = Flat(Hero.Position);
        int candidates = 0;
        for (int i = 0; i < n; i++)
        {
            var light = _torches[i].Light;
            var d = Flat(light.Position).DistanceTo(hero);
            var limit = light.ShadowEnabled ? TorchShadowRadius + ShadowHysteresis : TorchShadowRadius;
            if (d > limit) continue;
            _scores[candidates] = light.ShadowEnabled ? d - ShadowHysteresis : d;
            _order[candidates++] = i;
        }
        // Insertion sort of the few candidates by score.
        for (int a = 1; a < candidates; a++)
        {
            var (s, o) = (_scores[a], _order[a]);
            int b = a - 1;
            for (; b >= 0 && _scores[b] > s; b--) (_scores[b + 1], _order[b + 1]) = (_scores[b], _order[b]);
            (_scores[b + 1], _order[b + 1]) = (s, o);
        }
        for (int i = 0; i < n; i++)
        {
            bool on = false;
            for (int k = 0; k < candidates && k < allowed; k++)
                if (_order[k] == i) { on = true; break; }
            var light = _torches[i].Light;
            if (light.ShadowEnabled != on) light.ShadowEnabled = on;
        }
    }

    private readonly Vector3[] _occlusionTargets = new Vector3[4];

    /// <summary>Occluders crossed by the line from the hero or the dummy (waist and head) towards the camera fade to alpha hash.</summary>
    public void UpdateOcclusion()
    {
        var t = _occlusionTargets;
        (t[0], t[1]) = (Hero.Position + new Vector3(0, HeroModel.TargetHeight * 0.5f, 0), Hero.Position + new Vector3(0, HeroModel.TargetHeight * 0.95f, 0));
        (t[2], t[3]) = (Dummy.Position + new Vector3(0, DummyModel.TargetHeight * 0.5f, 0), Dummy.Position + new Vector3(0, DummyModel.TargetHeight * 0.95f, 0));
        var ray = CameraRig.Direction * CameraRig.Distance;
        foreach (var o in _occluders)
        {
            var bounds = o.Bounds.Grow(0.15f);
            bool hide = false;
            for (int i = 0; i < t.Length && !hide; i++) hide = bounds.IntersectsSegment(t[i], t[i] + ray);
            if (hide == o.Faded) continue;
            o.Faded = hide;
            foreach (var m in o.Materials)
            {
                m.Transparency = hide ? BaseMaterial3D.TransparencyEnum.AlphaHash : BaseMaterial3D.TransparencyEnum.Disabled;
                var c = m.AlbedoColor;
                m.AlbedoColor = new Color(c.R, c.G, c.B, hide ? FadedAlpha : 1f);
            }
        }
    }

    private void ApplyQuality()
    {
        if (Environment == null) return;
        var high = _quality == GraphicsQuality.High;
        Environment.SsaoEnabled = high;
        Environment.VolumetricFogEnabled = high && _volumetricFog;
        if (Hero != null) UpdateShadowBudget();
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0, v.Z);

    // ---------- look ----------

    private void BuildEnvironment()
    {
        Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.005f, 0.005f, 0.008f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.35f, 0.4f, 0.55f),
            AmbientLightEnergy = 0.12f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = 1.1f,
            GlowEnabled = true,
            GlowIntensity = 0.6f,
            GlowBloom = 0.08f,
            GlowHdrThreshold = 1.2f,
            SsaoEnabled = true,
            SsaoRadius = 1.5f,
            SsaoIntensity = 2.5f,
            VolumetricFogEnabled = false,
            VolumetricFogDensity = 0.015f,
            VolumetricFogAlbedo = new Color(0.6f, 0.55f, 0.5f),
            AdjustmentEnabled = true,
            AdjustmentSaturation = 0.75f,
            AdjustmentContrast = 1.1f,
        };
        AddChild(new WorldEnvironment { Name = "Environment", Environment = Environment });
    }

    private static StandardMaterial3D Plain(Color c, float roughness = 0.8f, float metallic = 0f) =>
        new() { AlbedoColor = c, Roughness = roughness, Metallic = metallic };

    private MeshInstance3D Box(Vector3 size, Vector3 pos, Material material, float rotY = 0f, Node3D? parent = null)
    {
        var mi = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material };
        (parent ?? this).AddChild(mi);
        mi.Position = pos;
        mi.Rotation = new Vector3(0, rotY, 0);
        return mi;
    }

    private MeshInstance3D Cylinder(float top, float bottom, float height, Vector3 pos, Material material)
    {
        var mi = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = top, BottomRadius = bottom, Height = height },
            MaterialOverride = material,
        };
        AddChild(mi);
        mi.Position = pos;
        return mi;
    }

    private void Solid(Vector3 size, Vector3 center, string name)
    {
        var body = new StaticBody3D { Name = name };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        AddChild(body, true);
        body.Position = center;
    }

    // ---------- floor ----------

    /// <summary>
    /// Stone slabs 0.75–1 m with grout gaps, height and tint jitter, merged into one mesh per 4×3 m chunk (spec §4: the floor
    /// is in chunks, not one mesh and not hundreds of nodes). The dark grout bed below shows through the gaps.
    /// </summary>
    private void BuildFloor()
    {
        var bed = Box(new Vector3(HalfX * 2 + 2, 0.2f, HalfZ * 2 + 2), new Vector3(0, -0.13f, 0), Plain(new Color(0.03f, 0.028f, 0.026f), 1f));
        bed.Layers = 1 | FloorLayer;
        Solid(new Vector3(HalfX * 2 + 2, 1f, HalfZ * 2 + 2), new Vector3(0, -0.5f, 0), "FloorBody");

        var material = Assets.Surface("floor");
        material.VertexColorUseAsAlbedo = true;
        const float chunkX = 4f, chunkZ = 3f, rowDepth = 1f;
        float[] rowA = [0.8f, 0.8f, 0.8f, 0.8f, 0.8f];
        float[] rowB = [1f, 0.75f, 0.75f, 0.75f, 0.75f];
        for (float cx = -HalfX; cx < HalfX - 0.01f; cx += chunkX)
        {
            for (float cz = -HalfZ; cz < HalfZ - 0.01f; cz += chunkZ)
            {
                var st = new SurfaceTool();
                st.Begin(Mesh.PrimitiveType.Triangles);
                int row = 0;
                for (float z = 0; z < chunkZ - 0.01f; z += rowDepth, row++)
                {
                    var widths = Mathf.PosMod(row + (int)(cx / chunkX) + (int)(cz / chunkZ), 2) == 0 ? rowA : rowB;
                    var shift = _rng.RandiRange(0, widths.Length - 1);
                    float x = 0;
                    for (int i = 0; i < widths.Length; i++)
                    {
                        var w = widths[(i + shift) % widths.Length];
                        var tint = (float)_rng.RandfRange(0.82f, 1.08f);
                        var dirt = (float)_rng.RandfRange(0.9f, 1f);
                        var color = new Color(tint * dirt, tint * dirt * 0.98f, tint * dirt * 0.95f);
                        Slab(st, new Vector3(cx + x, 0, cz + z), w, rowDepth, (float)_rng.RandfRange(-0.012f, 0.012f), color);
                        x += w;
                    }
                }
                st.Index();
                var chunk = new MeshInstance3D { Name = "FloorChunk", Mesh = st.Commit(), MaterialOverride = material, Layers = 1 | FloorLayer };
                AddChild(chunk, true);
            }
        }
    }

    /// <summary>One slab: top and four sides, 0.1 m thick, inset by the grout gap.</summary>
    private static void Slab(SurfaceTool st, Vector3 corner, float width, float depth, float lift, Color color)
    {
        const float gap = 0.018f, thick = 0.1f;
        var x0 = corner.X + gap; var x1 = corner.X + width - gap;
        var z0 = corner.Z + gap; var z1 = corner.Z + depth - gap;
        var top = lift; var bottom = -thick;
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
        {
            st.SetColor(color);
            st.SetNormal(n);
            st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
            st.AddVertex(a); st.AddVertex(c); st.AddVertex(d);
        }
        Quad(new(x0, top, z0), new(x1, top, z0), new(x1, top, z1), new(x0, top, z1), Vector3.Up);
        Quad(new(x0, bottom, z1), new(x0, top, z1), new(x1, top, z1), new(x1, bottom, z1), Vector3.Back);
        Quad(new(x1, bottom, z0), new(x1, top, z0), new(x0, top, z0), new(x0, bottom, z0), Vector3.Forward);
        Quad(new(x1, bottom, z1), new(x1, top, z1), new(x1, top, z0), new(x1, bottom, z0), Vector3.Right);
        Quad(new(x0, bottom, z0), new(x0, top, z0), new(x0, top, z1), new(x0, bottom, z1), Vector3.Left);
    }

    // ---------- walls, pillars, props ----------

    private void BuildWalls()
    {
        var wall = Assets.Surface("wall");
        var pilaster = Assets.Surface("stone");
        pilaster.AlbedoColor *= new Color(0.72f, 0.72f, 0.72f);
        // Back walls (away from the camera) at full height; their pilasters and sconces.
        Box(new Vector3(1, WallHeight, HalfZ * 2 + 2), new Vector3(-HalfX - 0.5f, WallHeight / 2, 0), wall);
        Box(new Vector3(HalfX * 2 + 2, WallHeight, 1), new Vector3(0, WallHeight / 2, -HalfZ - 0.5f), wall);
        foreach (var z in new[] { -5f, 0f, 5f })
            Box(new Vector3(1.6f, WallHeight + 0.4f, 1.4f), new Vector3(-HalfX, (WallHeight + 0.4f) / 2, z), pilaster);
        foreach (var x in new[] { -6f, 0f, 6f })
            Box(new Vector3(1.4f, WallHeight + 0.4f, 1.6f), new Vector3(x, (WallHeight + 0.4f) / 2, -HalfZ), pilaster);
        // Front walls (towards the camera) cut low; their collision is full height.
        var front = Assets.Surface("wall");
        var frontX = Box(new Vector3(1, FrontWallHeight, HalfZ * 2 + 2), new Vector3(HalfX + 0.5f, FrontWallHeight / 2, 0), front);
        var frontZ = Box(new Vector3(HalfX * 2 + 2, FrontWallHeight, 1), new Vector3(0, FrontWallHeight / 2, HalfZ + 0.5f), front);
        _occluders.Add(new Occluder("wall-front-x", At(frontX.Position) * frontX.GetAabb(), [front]));
        _occluders.Add(new Occluder("wall-front-z", At(frontZ.Position) * frontZ.GetAabb(), [front]));

        Solid(new Vector3(1, 3, HalfZ * 2 + 2), new Vector3(-HalfX - 0.5f, 1.5f, 0), "WallBackX");
        Solid(new Vector3(HalfX * 2 + 2, 3, 1), new Vector3(0, 1.5f, -HalfZ - 0.5f), "WallBackZ");
        Solid(new Vector3(1, 3, HalfZ * 2 + 2), new Vector3(HalfX + 0.5f, 1.5f, 0), "WallFrontX");
        Solid(new Vector3(HalfX * 2 + 2, 3, 1), new Vector3(0, 1.5f, HalfZ + 0.5f), "WallFrontZ");
        foreach (var z in new[] { -5f, 0f, 5f }) Solid(new Vector3(1.6f, 3, 1.4f), new Vector3(-HalfX, 1.5f, z), "Pilaster");
        foreach (var x in new[] { -6f, 0f, 6f }) Solid(new Vector3(1.4f, 3, 1.6f), new Vector3(x, 1.5f, -HalfZ), "Pilaster");

        BuildSconce(new Vector3(-HalfX + 0.3f, 2.6f, -2.5f));
        BuildSconce(new Vector3(-HalfX + 0.3f, 2.6f, 2.5f));
        BuildSconce(new Vector3(-3f, 2.6f, -HalfZ + 0.3f));
        BuildSconce(new Vector3(3f, 2.6f, -HalfZ + 0.3f));
    }

    private static Transform3D At(Vector3 v) => new(Basis.Identity, v);

    private void BuildPillar(Vector3 p)
    {
        var shaft = Assets.Surface("stone");
        var dark = Assets.Surface("stone");
        dark.AlbedoColor *= new Color(0.7f, 0.7f, 0.7f);
        // Cap: a faint self-light so the top reads as stone, not a black hole (spec §4).
        var cap = Assets.Surface("stone");
        cap.AlbedoColor *= new Color(0.82f, 0.82f, 0.8f);
        // No light reaches the tops (every light hangs lower), so the cap glows its own texture faintly.
        cap.EmissionEnabled = true;
        cap.EmissionTexture = cap.AlbedoTexture;
        cap.EmissionOperator = BaseMaterial3D.EmissionOperatorEnum.Multiply; // colour × texture, not colour + texture
        cap.Emission = cap.AlbedoTexture != null ? new Color(0.07f, 0.06f, 0.05f) : new Color(0.05f, 0.04f, 0.03f);
        var parts = new[]
        {
            Box(new Vector3(1.9f, 0.35f, 1.9f), p + new Vector3(0, 0.175f, 0), dark),
            Box(new Vector3(1.6f, 0.3f, 1.6f), p + new Vector3(0, 0.5f, 0), shaft),
            Box(new Vector3(1.1f, 3f, 1.1f), p + new Vector3(0, 2.15f, 0), shaft),
            Box(new Vector3(1.5f, 0.3f, 1.5f), p + new Vector3(0, 3.8f, 0), shaft),
            Box(new Vector3(1.9f, 0.4f, 1.9f), p + new Vector3(0, 4.15f, 0), cap),
        };
        var bounds = parts.Select(m => At(m.Position) * m.GetAabb()).Aggregate((a, b) => a.Merge(b));
        _occluders.Add(new Occluder($"pillar {p.X:0.#},{p.Z:0.#}", bounds, [shaft, dark, cap]));
        Solid(new Vector3(1.6f, 3, 1.6f), p + new Vector3(0, 1.5f, 0), "Pillar");
    }

    private void BuildBarrels(Vector3 c)
    {
        var wood = Assets.Surface("wood");
        var band = Plain(new Color(0.12f, 0.11f, 0.1f), 0.5f, 0.7f);
        foreach (var o in new[] { Vector3.Zero, new Vector3(1f, 0, 0.3f), new Vector3(0.4f, 0, 1f), new Vector3(1.6f, 0, -0.5f), new Vector3(-0.2f, 0, 1.9f) })
        {
            var p = c + o;
            Cylinder(0.4f, 0.4f, 1f, p + new Vector3(0, 0.5f, 0), wood);
            Cylinder(0.46f, 0.46f, 0.4f, p + new Vector3(0, 0.5f, 0), wood);
            foreach (var y in new[] { 0.15f, 0.85f }) Cylinder(0.43f, 0.43f, 0.07f, p + new Vector3(0, y, 0), band);
            var body = new StaticBody3D { Name = "Barrel" };
            body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.46f, Height = 1f } });
            AddChild(body, true);
            body.Position = p + new Vector3(0, 0.5f, 0);
        }
    }

    // ---------- light and fire ----------

    /// <summary>Torch flame: soft blobs in plain alpha blend (no additive: it burns to white), colours never above 1 (spec §4).</summary>
    private static GpuParticles3D Flame(Vector3 extents, int amount, float size, float speed)
    {
        var scaleCurve = new Curve();
        scaleCurve.AddPoint(new Vector2(0, 0.6f));
        scaleCurve.AddPoint(new Vector2(0.3f, 1f));
        scaleCurve.AddPoint(new Vector2(1, 0.1f));
        var ramp = new Gradient();
        ramp.SetColor(0, new Color(1f, 0.4f, 0.08f, 0.5f));
        ramp.SetColor(1, new Color(0.25f, 0.03f, 0.01f, 0f));
        ramp.AddPoint(0.3f, new Color(0.85f, 0.22f, 0.03f, 0.45f));
        ramp.AddPoint(0.65f, new Color(0.45f, 0.08f, 0.02f, 0.28f));
        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = extents,
            Direction = Vector3.Up,
            Spread = 12f,
            InitialVelocityMin = speed * 0.6f,
            InitialVelocityMax = speed,
            Gravity = new Vector3(0, 1.2f, 0),
            DampingMin = 0.5f,
            DampingMax = 1f,
            ScaleMin = 0.5f,
            ScaleMax = 1.2f,
            ScaleCurve = new CurveTexture { Curve = scaleCurve },
            ColorRamp = new GradientTexture1D { Gradient = ramp },
        };
        var blobGradient = new Gradient();
        blobGradient.SetColor(0, new Color(1, 1, 1, 1));
        blobGradient.SetColor(1, new Color(1, 1, 1, 0));
        var draw = new StandardMaterial3D
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
                Gradient = blobGradient,
            },
        };
        return new GpuParticles3D
        {
            Amount = amount,
            Lifetime = 0.9,
            Preprocess = 2.0,
            ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = new Vector2(size, size), Material = draw },
            VisibilityAabb = new Aabb(new Vector3(-1, -0.5f, -1), new Vector3(2, 3, 2)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private OmniLight3D TorchLight(Vector3 pos, float energy, float range)
    {
        var light = new OmniLight3D
        {
            Name = "TorchLight",
            LightColor = TorchColor,
            LightEnergy = energy,
            OmniRange = range,
            OmniAttenuation = 1.4f,
            ShadowEnabled = false,
            ShadowOpacity = TorchShadowOpacity,
            LightVolumetricFogEnergy = 1.5f,
        };
        AddChild(light, true);
        light.Position = pos;
        _torches.Add(new Torch(light, energy, _rng.Randf() * 1000f));
        return light;
    }

    private void BuildTorchStand(Vector3 p)
    {
        var iron = Plain(new Color(0.1f, 0.09f, 0.08f), 0.55f, 0.8f);
        Cylinder(0.05f, 0.08f, 1.8f, p + new Vector3(0, 0.9f, 0), iron);
        for (int i = 0; i < 3; i++)
        {
            var leg = Box(new Vector3(0.06f, 0.06f, 0.6f), p + new Vector3(0, 0.15f, 0), iron, Mathf.Tau * i / 3f);
            leg.TranslateObjectLocal(new Vector3(0, 0, 0.25f));
            leg.Rotation = new Vector3(-0.5f, leg.Rotation.Y, 0);
        }
        // The bowl sits under its own flame: no shadow, or it paints a dark disc on the floor (prototype).
        var bowl = Cylinder(0.25f, 0.1f, 0.2f, p + new Vector3(0, 1.85f, 0), iron);
        bowl.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        var flame = Flame(new Vector3(0.08f, 0.02f, 0.08f), 18, 0.3f, 1.1f);
        AddChild(flame);
        flame.Position = p + new Vector3(0, 1.95f, 0);
        TorchLight(p + new Vector3(0, 2.3f, 0), 2.2f, 7f);
        var body = new StaticBody3D { Name = "TorchStand" };
        body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.25f, Height = 2f } });
        AddChild(body, true);
        body.Position = p + new Vector3(0, 1f, 0);
    }

    private void BuildSconce(Vector3 p)
    {
        var iron = Plain(new Color(0.1f, 0.09f, 0.08f), 0.55f, 0.8f);
        var cup = Cylinder(0.16f, 0.07f, 0.2f, p, iron);
        cup.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        var flame = Flame(new Vector3(0.05f, 0.02f, 0.05f), 20, 0.25f, 0.9f);
        AddChild(flame);
        flame.Position = p + new Vector3(0, 0.12f, 0);
        TorchLight(p + new Vector3(0, 0.4f, 0), 1.4f, 5f);
    }

    /// <summary>Procedural blood decal (prototype `_blood`): radial falloff times noise, dark red; projects onto the floor only.</summary>
    private void BuildBlood(Vector3 p, float size)
    {
        const int n = 128;
        var noise = new FastNoiseLite { Seed = (int)_rng.Randi(), Frequency = 0.07f };
        var image = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                var dx = (x - n / 2f) / (n / 2f);
                var dy = (y - n / 2f) / (n / 2f);
                var r = Mathf.Sqrt(dx * dx + dy * dy);
                var v = (1f - r) * 1.4f + noise.GetNoise2D(x, y) * 0.9f;
                var a = Mathf.Clamp((v - 0.35f) * 4f, 0f, 1f);
                image.SetPixel(x, y, new Color(0.32f + 0.1f * noise.GetNoise2D(y, x), 0.015f, 0.01f, a));
            }
        }
        var decal = new Decal
        {
            Name = "Blood",
            TextureAlbedo = ImageTexture.CreateFromImage(image),
            Size = new Vector3(size, 0.4f, size),
            CullMask = FloorLayer,
        };
        AddChild(decal, true);
        decal.Position = p;
        decal.Rotation = new Vector3(0, _rng.Randf() * Mathf.Tau, 0);
    }

    // ---------- figures ----------

    private void BuildHero()
    {
        Hero = new CharacterBody3D { Name = "Hero" };
        Hero.AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.35f, Height = 1.85f },
            Position = new Vector3(0, 0.925f, 0),
        });
        AddChild(Hero);
        HeroModel = Assets.Character(AssetLibrary.HeroId);
        Hero.AddChild(HeroModel);
        HeroModel.Play("hero_idle");

        // Light circle around the hero, like D2: the only full shadow (spec §4).
        HeroLight = new OmniLight3D
        {
            Name = "HeroLight",
            LightColor = new Color(1f, 0.88f, 0.7f),
            LightEnergy = 2f,
            OmniRange = 8.5f,
            OmniAttenuation = 1.2f,
            ShadowEnabled = true,
            ShadowOpacity = 1f,
        };
        Hero.AddChild(HeroLight);
        HeroLight.Position = new Vector3(0, 3f, 0);
        // Fill light at chest height on the camera side, no shadows (spec §4: shield and face lit).
        FillLight = new OmniLight3D
        {
            Name = "FillLight",
            LightColor = new Color(1f, 0.92f, 0.8f),
            LightEnergy = 2.2f,
            OmniRange = 4f,
            ShadowEnabled = false,
        };
        AddChild(FillLight);
    }

    private void BuildDummy()
    {
        Dummy = new StaticBody3D { Name = "Dummy" };
        Dummy.AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f },
            Position = new Vector3(0, 0.9f, 0),
        });
        AddChild(Dummy);
        Dummy.Position = DummySpot;
        DummyModel = Assets.Character("zombie");
        Dummy.AddChild(DummyModel);
        DummyModel.Play("zombie_idle");
        var toHero = Flat(HeroSpawn) - Flat(DummySpot);
        Dummy.Basis = Basis.LookingAt(toHero, Vector3.Up, true);
        // The dummy's own light so it reads outside the hero's circle; no shadow (spec §4).
        DummyLight = new OmniLight3D
        {
            Name = "DummyLight",
            LightColor = new Color(0.85f, 0.85f, 0.95f),
            LightEnergy = 1.6f,
            OmniRange = 5f,
            ShadowEnabled = false,
        };
        AddChild(DummyLight);
        DummyLight.Position = DummySpot + new Vector3(1.2f, 2.2f, 1.2f);
    }
}
