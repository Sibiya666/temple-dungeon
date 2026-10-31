using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;

namespace Dialoid.Game.Room;

/// <summary>A character model of the asset table: the file and the height it is scaled to (spec §4).</summary>
public sealed record CharacterEntry(string File, float Height);

/// <summary>An animation clip of the asset table; idle, walk and run loop.</summary>
public sealed record ClipEntry(string File, bool Loop);

/// <summary>A Poly Haven PBR set of the asset table: three files, a tint, the world triplanar scale and the flat stub colour.</summary>
public sealed record SurfaceEntry(string Albedo, string Normal, string Roughness, Color Tint, float Uv, Color Flat, float NormalScale = 1f);

/// <summary>The table id → file of game/data/assets.json (files relative to the assets folder, names as on disk).</summary>
public sealed record AssetTable(
    IReadOnlyDictionary<string, CharacterEntry> Characters,
    IReadOnlyDictionary<string, ClipEntry> Clips,
    IReadOnlyDictionary<string, SurfaceEntry> Surfaces)
{
    public static readonly AssetTable Empty = new(
        new Dictionary<string, CharacterEntry>(), new Dictionary<string, ClipEntry>(), new Dictionary<string, SurfaceEntry>());

    /// <summary>Parses the JSON text of game/data/assets.json; throws on a broken table.</summary>
    public static AssetTable Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var characters = new Dictionary<string, CharacterEntry>();
        foreach (var p in root.GetProperty("characters").EnumerateObject())
            characters[p.Name] = new CharacterEntry(p.Value.GetProperty("file").GetString()!, p.Value.GetProperty("height").GetSingle());
        var clips = new Dictionary<string, ClipEntry>();
        foreach (var p in root.GetProperty("clips").EnumerateObject())
            clips[p.Name] = new ClipEntry(p.Value.GetProperty("file").GetString()!, p.Value.TryGetProperty("loop", out var l) && l.GetBoolean());
        var surfaces = new Dictionary<string, SurfaceEntry>();
        foreach (var p in root.GetProperty("surfaces").EnumerateObject())
        {
            var v = p.Value;
            surfaces[p.Name] = new SurfaceEntry(
                v.GetProperty("albedo").GetString()!, v.GetProperty("normal").GetString()!, v.GetProperty("roughness").GetString()!,
                ColorOf(v.GetProperty("tint")), v.GetProperty("uv").GetSingle(), ColorOf(v.GetProperty("flat")),
                v.TryGetProperty("normalScale", out var ns) ? ns.GetSingle() : 1f);
        }
        return new AssetTable(characters, clips, surfaces);
    }

    private static Color ColorOf(JsonElement e)
    {
        var c = e.EnumerateArray().Select(x => x.GetSingle()).ToArray();
        return new Color(c[0], c[1], c[2]);
    }
}

/// <summary>
/// Room assets from res://vendor/ (copied there by tools/sync_assets.ps1, not in git; spec §9). Every missing asset becomes a
/// stub (a capsule of the same height, a flat material, no clip) and prints one line «ASSET_MISSING: …» — never an error.
/// Every <c>Load</c> is guarded by <see cref="ResourceLoader.Exists(string, string)"/>, so Godot prints no ERROR either.
/// FBX import settings live here, not in .import files: the clip name comes from the file name, loops are set in code, the
/// hips are pinned (no root motion), bone tracks are rewritten for the model's skeleton — always on a duplicate of the cached
/// <see cref="Animation"/>, never on the shared resource.
/// </summary>
public sealed class AssetLibrary
{
    public const string TablePath = "res://data/assets.json";
    public const string VendorRoot = "res://vendor/";
    public const string MissingMarker = "ASSET_MISSING:";

    /// <summary>The hero gets the outline; every other character is an enemy (dimmed skin, no outline; spec §4).</summary>
    public const string HeroId = "hero";

    /// <summary>World-space outline width of the hero, metres (prototype: 0.022 / model scale).</summary>
    public const float OutlineWidth = 0.012f;

    /// <summary>Enemy skin multiplier (prototype `_style` had 0.62; the art review asked for ~0.5).</summary>
    public static readonly Color EnemyDim = new(0.5f, 0.49f, 0.47f, 1f);

    /// <summary>The hero's outline: warm white / pale cream (HUD spec §9; gold is the rare leader's mark, enemies spec 7.2).</summary>
    public static readonly Color OutlineColor = new(0.55f, 0.58f, 0.55f);

    private const string OutlineShaderCode = """
        shader_type spatial;
        render_mode unshaded, cull_front, depth_draw_opaque;
        uniform vec4 color : source_color = vec4(1.0, 0.95, 0.84, 1.0);
        uniform float grow = 0.022;
        void vertex() { VERTEX += NORMAL * grow; }
        void fragment() { ALBEDO = color.rgb; }
        """;

    private static readonly Regex BonePrefix = new(@"^mixamorig\d*[:_]", RegexOptions.CultureInvariant);

    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private readonly List<string> _missing = new();
    private readonly Dictionary<string, Texture2D?> _textures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Aabb> _restBounds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (Animation Anim, float HipsRestY)?> _clips = new(StringComparer.Ordinal);
    private Shader? _outlineShader;

    public AssetLibrary(AssetTable table) => Table = table;

    public AssetTable Table { get; }

    /// <summary>The «ASSET_MISSING:» lines printed by this library, one per missing file.</summary>
    public IReadOnlyList<string> Missing => _missing;

    /// <summary>Every character model of the table is imported.</summary>
    public bool HasModels => Table.Characters.Count > 0 && Table.Characters.Values.All(c => ResourceLoader.Exists(VendorPath(c.File)));

    /// <summary>Every texture of the table is imported.</summary>
    public bool HasTextures => Table.Surfaces.Count > 0 &&
        Table.Surfaces.Values.All(s => new[] { s.Albedo, s.Normal, s.Roughness }.All(f => ResourceLoader.Exists(VendorPath(f))));

    /// <summary>Reads res://data/assets.json; a missing or broken table prints one ASSET_MISSING line and gives an empty table.</summary>
    public static AssetTable LoadTable()
    {
        if (!Godot.FileAccess.FileExists(TablePath))
        {
            GD.Print($"{MissingMarker} таблица {TablePath}, комната из заглушек");
            return AssetTable.Empty;
        }
        try
        {
            return AssetTable.Parse(Godot.FileAccess.GetFileAsString(TablePath));
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            GD.Print($"{MissingMarker} таблица {TablePath} не читается ({e.Message}), комната из заглушек");
            return AssetTable.Empty;
        }
    }

    /// <summary>
    /// The file name rule of tools/sync_assets.ps1: lower case, spaces and brackets → «_», repeated «_» collapsed, no «_»
    /// before the extension or at the ends («sword and shield slash (2).fbx» → «sword_and_shield_slash_2.fbx»).
    /// </summary>
    public static string NormalizeName(string name)
    {
        var n = Regex.Replace(name.ToLowerInvariant(), @"[\s\(\)\[\]]", "_");
        n = Regex.Replace(n, "_+", "_");
        n = Regex.Replace(n, @"_+(\.[^.]+)$", "$1");
        return n.Trim('_');
    }

    /// <summary>res:// path of a table file after the sync (every path segment normalized).</summary>
    public static string VendorPath(string file) =>
        VendorRoot + string.Join("/", file.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries).Select(NormalizeName));

    /// <summary>Bone name without the Mixamo prefix (mixamorig:, mixamorig1:, mixamorig_ …): «mixamorig2:Hips» → «Hips».</summary>
    public static string CanonicalBone(string bone) => BonePrefix.Replace(bone, "");

    // ---------- characters ----------

    /// <summary>
    /// The character of the table id, its feet on y = 0, scaled from the measured height of its skinned rest pose to the table
    /// height; or a capsule stub of the same height. The hero has the unshaded outline, enemies a dimmed skin.
    /// </summary>
    public CharacterModel Character(string id)
    {
        if (!Table.Characters.TryGetValue(id, out var entry)) throw new ArgumentException($"Нет персонажа {id} в таблице ассетов", nameof(id));
        var hero = id == HeroId;
        var root = new CharacterModel { Name = id };
        var path = VendorPath(entry.File);
        Node3D? model = null;
        if (ResourceLoader.Exists(path) && ResourceLoader.Load<PackedScene>(path) is { } scene)
            model = scene.Instantiate() as Node3D;
        if (model == null)
        {
            ReportMissing("модель", id, entry.File);
            root.InitStub(this, id, entry.Height, StubCapsule(entry.Height, hero));
        }
        else
        {
            if (!_restBounds.TryGetValue(path, out var bounds))
                _restBounds[path] = bounds = SkinnedBounds(model, model);
            var scale = bounds.Size.Y > 0.01f ? entry.Height / bounds.Size.Y : 1f;
            model.Scale = Vector3.One * scale;
            model.Position = new Vector3(0, -bounds.Position.Y * scale, 0);
            root.InitModel(this, id, entry.Height, model, scale);
        }
        Style(root, hero);
        return root;
    }

    /// <summary>Height of the character as built: the skinned rest-pose bounds of its meshes in its own space (stub: the capsule).</summary>
    public static float MeasureHeight(CharacterModel character) => SkinnedBounds(character, character).Size.Y;

    private static MeshInstance3D StubCapsule(float height, bool hero)
    {
        var radius = 0.18f * height;
        return new MeshInstance3D
        {
            Name = "Stub",
            Mesh = new CapsuleMesh { Radius = radius, Height = height },
            Position = new Vector3(0, height / 2, 0),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = hero ? new Color(0.42f, 0.4f, 0.37f) : new Color(0.55f, 0.5f, 0.42f) * EnemyDim,
                Roughness = hero ? 0.45f : 0.85f,
                Metallic = hero ? 0.7f : 0f,
            },
        };
    }

    /// <summary>Prototype `_style`: the hero gets the outline overlay (width independent of the model scale), enemies a dimmed copy of their materials with the baked emission off.</summary>
    private void Style(CharacterModel character, bool hero)
    {
        ShaderMaterial? outline = null;
        if (hero)
        {
            _outlineShader ??= new Shader { Code = OutlineShaderCode };
            outline = new ShaderMaterial { Shader = _outlineShader };
            outline.SetShaderParameter("color", OutlineColor);
            outline.SetShaderParameter("grow", OutlineWidth / character.ModelScale);
        }
        foreach (var mesh in character.Meshes)
        {
            if (outline != null)
            {
                mesh.MaterialOverlay = outline;
                continue;
            }
            if (character.IsStub || mesh.Mesh == null) continue;
            for (int s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
            {
                if (mesh.GetActiveMaterial(s) is not StandardMaterial3D source) continue;
                var dim = (StandardMaterial3D)source.Duplicate();
                dim.AlbedoColor = source.AlbedoColor * EnemyDim;
                dim.EmissionEnabled = false;
                mesh.SetSurfaceOverrideMaterial(s, dim);
            }
        }
    }

    /// <summary>
    /// Bounds of every mesh under <paramref name="node"/> in <paramref name="space"/>: skinned meshes by their weighted bind
    /// transforms at the skeleton rest (the T-pose), others by their vertices. Imported Mixamo meshes keep their vertices in
    /// bind space, so their own AABB says nothing about the height.
    /// </summary>
    private static Aabb SkinnedBounds(Node3D node, Node3D space)
    {
        var lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var hi = -lo;
        foreach (var mi in node.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().Prepend(node as MeshInstance3D))
        {
            if (mi?.Mesh == null) continue;
            var toSpace = Relative(mi, space);
            var skeleton = mi.Skin != null ? mi.GetNodeOrNull<Skeleton3D>(mi.Skeleton) : null;
            Transform3D[]? binds = null;
            if (skeleton != null)
            {
                var skeletonToSpace = Relative(skeleton, space);
                binds = new Transform3D[mi.Skin!.GetBindCount()];
                for (int b = 0; b < binds.Length; b++)
                {
                    var bone = mi.Skin.GetBindBone(b);
                    if (bone < 0) bone = skeleton.FindBone(mi.Skin.GetBindName(b));
                    binds[b] = bone < 0 ? toSpace : skeletonToSpace * skeleton.GetBoneGlobalRest(bone) * mi.Skin.GetBindPose(b);
                }
            }
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                var arrays = mi.Mesh.SurfaceGetArrays(s);
                var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var bonesV = arrays[(int)Mesh.ArrayType.Bones];
                var weightsV = arrays[(int)Mesh.ArrayType.Weights];
                int[]? bones = binds != null && bonesV.VariantType != Variant.Type.Nil ? bonesV.AsInt32Array() : null;
                float[]? weights = bones != null && weightsV.VariantType != Variant.Type.Nil ? weightsV.AsFloat32Array() : null;
                var per = bones != null && verts.Length > 0 ? bones.Length / verts.Length : 0;
                for (int i = 0; i < verts.Length; i++)
                {
                    Vector3 p;
                    if (weights != null && per > 0)
                    {
                        p = Vector3.Zero;
                        float total = 0;
                        for (int k = 0; k < per; k++)
                        {
                            var w = weights[i * per + k];
                            var b = bones![i * per + k];
                            if (w <= 0 || b < 0 || b >= binds!.Length) continue;
                            p += binds[b] * verts[i] * w;
                            total += w;
                        }
                        p = total > 0 ? p / total : toSpace * verts[i];
                    }
                    else
                    {
                        p = toSpace * verts[i];
                    }
                    lo = lo.Min(p);
                    hi = hi.Max(p);
                }
            }
        }
        return lo.X > hi.X ? new Aabb() : new Aabb(lo, hi - lo);
    }

    /// <summary>Transform of <paramref name="node"/> in the space of its ancestor <paramref name="space"/> (works outside the scene tree).</summary>
    public static Transform3D Relative(Node3D node, Node3D space)
    {
        var xf = Transform3D.Identity;
        for (Node? n = node; n != null && n != space; n = n.GetParent())
            if (n is Node3D n3) xf = n3.Transform * xf;
        return xf;
    }

    // ---------- clips ----------

    /// <summary>
    /// A fresh copy of the clip: named after its file (Mixamo calls every clip «mixamo.com»), looped per the table, hips position
    /// pinned to X = Z = 0 (Y kept: the bob of a run). Bone paths are still the source file's; <see cref="ClipFor"/> fits them
    /// to a model. Null when the file is missing (one ASSET_MISSING line).
    /// </summary>
    public Animation? Clip(string id)
    {
        var source = SourceClip(id);
        return source == null ? null : (Animation)source.Value.Anim.Duplicate(true);
    }

    /// <summary>
    /// The clip fitted to <paramref name="character"/>: bone tracks rewritten to the model's skeleton (any mixamorig prefix),
    /// tracks of bones the model lacks dropped and named in <paramref name="dropped"/>, hips height scaled to the model's hips.
    /// Null for a stub or a missing clip.
    /// </summary>
    public Animation? ClipFor(string id, CharacterModel character, List<string>? dropped = null)
    {
        var source = SourceClip(id);
        if (source == null || character.Skeleton == null || character.Player == null) return null;
        var anim = (Animation)source.Value.Anim.Duplicate(true);
        var skeleton = character.Skeleton;
        var root = character.Player.GetNode(character.Player.RootNode);
        var skeletonPath = root.GetPathTo(skeleton).ToString();
        var bones = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int b = 0; b < skeleton.GetBoneCount(); b++) bones.TryAdd(CanonicalBone(skeleton.GetBoneName(b)), skeleton.GetBoneName(b));
        var ratio = source.Value.HipsRestY > 0.001f && character.RestHipsY > 0.001f ? character.RestHipsY / source.Value.HipsRestY : 1f;

        for (int i = anim.GetTrackCount() - 1; i >= 0; i--)
        {
            var path = anim.TrackGetPath(i);
            var bone = path.GetConcatenatedSubNames().ToString();
            if (bone.Length == 0) continue;
            if (!bones.TryGetValue(CanonicalBone(bone), out var actual))
            {
                dropped?.Add(bone);
                anim.RemoveTrack(i);
                continue;
            }
            anim.TrackSetPath(i, new NodePath($"{skeletonPath}:{actual}"));
            if (anim.TrackGetType(i) == Animation.TrackType.Position3D && CanonicalBone(actual) == "Hips" && ratio != 1f)
                for (int k = 0; k < anim.TrackGetKeyCount(i); k++)
                    anim.TrackSetKeyValue(i, k, anim.TrackGetKeyValue(i, k).AsVector3() * new Vector3(1, ratio, 1));
        }
        return anim;
    }

    /// <summary>Tracks of <paramref name="anim"/> whose node or bone is not found from the character's AnimationPlayer root.</summary>
    public static List<string> UnresolvedTracks(Animation anim, CharacterModel character)
    {
        var result = new List<string>();
        if (character.Player == null) return result;
        var root = character.Player.GetNode(character.Player.RootNode);
        for (int i = 0; i < anim.GetTrackCount(); i++)
        {
            var path = anim.TrackGetPath(i);
            var node = root.GetNodeOrNull(new NodePath(path.GetConcatenatedNames().ToString()));
            var sub = path.GetConcatenatedSubNames().ToString();
            var ok = node != null && (sub.Length == 0 || (node is Skeleton3D sk ? sk.FindBone(sub) >= 0 : node.Get(sub).VariantType != Variant.Type.Nil));
            if (!ok) result.Add(path.ToString());
        }
        return result;
    }

    private (Animation Anim, float HipsRestY)? SourceClip(string id)
    {
        if (!Table.Clips.TryGetValue(id, out var entry)) throw new ArgumentException($"Нет клипа {id} в таблице ассетов", nameof(id));
        if (_clips.TryGetValue(id, out var cached)) return cached;
        var path = VendorPath(entry.File);
        (Animation, float)? result = null;
        if (ResourceLoader.Exists(path) && ResourceLoader.Load<PackedScene>(path) is { } scene)
        {
            var inst = scene.Instantiate();
            var player = inst.FindChild("AnimationPlayer", true, false) as AnimationPlayer;
            var skeleton = inst.FindChild("Skeleton3D", true, false) as Skeleton3D;
            var names = (player?.GetAnimationList() ?? []).Where(n => n != "RESET").ToArray();
            if (player != null && names.Length > 0)
            {
                var anim = (Animation)player.GetAnimation(names[0]).Duplicate(true);
                anim.ResourceName = Path.GetFileNameWithoutExtension(path);
                anim.LoopMode = entry.Loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
                PinHips(anim);
                result = (anim, HipsRestY(skeleton));
            }
            inst.Free();
        }
        if (result == null) ReportMissing("клип", id, entry.File);
        _clips[id] = result;
        return result;
    }

    private static void PinHips(Animation anim)
    {
        for (int i = 0; i < anim.GetTrackCount(); i++)
        {
            if (anim.TrackGetType(i) != Animation.TrackType.Position3D) continue;
            if (CanonicalBone(anim.TrackGetPath(i).GetConcatenatedSubNames().ToString()) != "Hips") continue;
            for (int k = 0; k < anim.TrackGetKeyCount(i); k++)
                anim.TrackSetKeyValue(i, k, new Vector3(0, anim.TrackGetKeyValue(i, k).AsVector3().Y, 0));
        }
    }

    /// <summary>Rest height of the hips bone in skeleton space, or 0 without a skeleton.</summary>
    public static float HipsRestY(Skeleton3D? skeleton)
    {
        if (skeleton == null) return 0;
        for (int b = 0; b < skeleton.GetBoneCount(); b++)
            if (CanonicalBone(skeleton.GetBoneName(b)) == "Hips") return skeleton.GetBoneGlobalRest(b).Origin.Y;
        return 0;
    }

    // ---------- surfaces ----------

    /// <summary>
    /// A new material for the table surface: Poly Haven PBR in world triplanar metres, roughness from the green channel; or a
    /// flat colour when any of its three textures is missing. A new instance each call (pillars fade their own).
    /// </summary>
    public StandardMaterial3D Surface(string id)
    {
        if (!Table.Surfaces.TryGetValue(id, out var entry)) throw new ArgumentException($"Нет поверхности {id} в таблице ассетов", nameof(id));
        var albedo = Texture("текстура", id, entry.Albedo);
        var normal = Texture("текстура", id, entry.Normal);
        var rough = Texture("текстура", id, entry.Roughness);
        if (albedo == null || normal == null || rough == null)
            return new StandardMaterial3D { AlbedoColor = entry.Flat, Roughness = 0.9f };
        return new StandardMaterial3D
        {
            AlbedoTexture = albedo,
            AlbedoColor = entry.Tint,
            NormalEnabled = true,
            NormalTexture = normal,
            NormalScale = entry.NormalScale,
            RoughnessTexture = rough,
            RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Green,
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = Vector3.One * entry.Uv,
        };
    }

    private Texture2D? Texture(string kind, string id, string file)
    {
        if (_textures.TryGetValue(file, out var cached)) return cached;
        var path = VendorPath(file);
        var texture = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        if (texture == null) ReportMissing(kind, id, file);
        _textures[file] = texture;
        return texture;
    }

    private void ReportMissing(string kind, string id, string file)
    {
        if (!_reported.Add(file)) return;
        var line = $"{MissingMarker} {kind} {id}: {file} — заглушка";
        _missing.Add(line);
        GD.Print(line);
    }
}

/// <summary>
/// A character built by <see cref="AssetLibrary.Character"/>: the imported model (scaled, feet on y = 0) or a capsule stub.
/// Clips are fitted to the model on first use and kept in its own animation library «room».
/// </summary>
public partial class CharacterModel : Node3D
{
    public const string Library = "room";

    private AssetLibrary? _assets;
    private AnimationLibrary? _library;

    public string Id { get; private set; } = "";
    public bool IsStub { get; private set; }
    public float TargetHeight { get; private set; }

    /// <summary>Uniform scale applied to the imported model (1 for a stub).</summary>
    public float ModelScale { get; private set; } = 1f;

    public Node3D? Model { get; private set; }
    public Skeleton3D? Skeleton { get; private set; }
    public AnimationPlayer? Player { get; private set; }

    /// <summary>Rest height of the model's hips in skeleton space (0 for a stub).</summary>
    public float RestHipsY { get; private set; }

    /// <summary>The clip playing now, or null.</summary>
    public string? CurrentClip { get; private set; }

    public IEnumerable<MeshInstance3D> Meshes =>
        FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>();

    /// <summary>Every mesh carries the hero outline overlay.</summary>
    public bool HasOutline => Meshes.Any() && Meshes.All(m => m.MaterialOverlay is ShaderMaterial);

    internal void InitStub(AssetLibrary assets, string id, float height, MeshInstance3D capsule)
    {
        (_assets, Id, TargetHeight, IsStub) = (assets, id, height, true);
        AddChild(capsule);
        Model = capsule;
    }

    internal void InitModel(AssetLibrary assets, string id, float height, Node3D model, float scale)
    {
        (_assets, Id, TargetHeight, ModelScale, Model) = (assets, id, height, scale, model);
        AddChild(model);
        Skeleton = model.FindChild("Skeleton3D", true, false) as Skeleton3D;
        Player = model.FindChild("AnimationPlayer", true, false) as AnimationPlayer;
        RestHipsY = AssetLibrary.HipsRestY(Skeleton);
        if (Player != null)
        {
            _library = new AnimationLibrary();
            Player.AddAnimationLibrary(Library, _library);
        }
    }

    /// <summary>
    /// Plays a clip of the table from <paramref name="offset"/> seconds at <paramref name="speed"/> (a use stretched to the core's
    /// time), blending from the previous clip over <paramref name="blend"/> seconds (−1: the player's default); the same clip
    /// again only restarts with <paramref name="restart"/>. False (no exception) for a stub or a missing clip.
    /// </summary>
    public bool Play(string clipId, double offset = 0, float speed = 1f, double blend = -1, bool restart = false)
    {
        if (!EnsureClip(clipId)) return false;
        var name = $"{Library}/{clipId}";
        var same = Player!.CurrentAnimation == name && Player.IsPlaying();
        Player.Play(name, blend, speed);
        if (offset > 0) Player.Seek(offset, true);
        else if (same && restart) Player.Seek(0, true);
        CurrentClip = clipId;
        return true;
    }

    /// <summary>Length of a clip of the table in seconds (fitted on first use), or null for a stub or a missing clip.</summary>
    public double? ClipLength(string clipId) => EnsureClip(clipId) ? _library!.GetAnimation(clipId).Length : null;

    private bool EnsureClip(string clipId)
    {
        if (_assets == null || Player == null || _library == null) return false;
        if (_library.HasAnimation(clipId)) return true;
        var anim = _assets.ClipFor(clipId, this);
        if (anim == null) return false;
        _library.AddAnimation(clipId, anim);
        return true;
    }
}
