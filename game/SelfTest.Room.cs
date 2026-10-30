using Dialoid.Game.Room;
using Godot;

namespace Dialoid.Game;

/// <summary>
/// Self-test steps of the 3D room (plan 4 task 8): assets and stubs, scale, clips on every model, the camera's on-screen test
/// and zoom limits, the shadow budget, Low quality, fading and collisions. They pass with and without assets.
/// </summary>
public sealed partial class SelfTest
{
    /// <summary>Heights of spec §4 the characters must be scaled to (±5 %).</summary>
    private static readonly Dictionary<string, float> SpecHeights = new()
    {
        ["hero"] = 1.85f, ["zombie"] = 1.8f, ["orc"] = 2.25f, ["boss"] = 3.15f,
    };

    /// <summary>The models every clip must fit (the boss is the orc model).</summary>
    private static readonly string[] ClipModels = ["hero", "zombie", "orc"];

    /// <summary>Bones every fitted clip must still animate.</summary>
    private static readonly string[] CoreBones =
        ["Hips", "Spine", "Head", "LeftArm", "RightArm", "LeftForeArm", "RightForeArm", "LeftUpLeg", "RightUpLeg", "LeftLeg", "RightLeg"];

    private RoomView Room => _main.Room ?? throw new Failure("комната не построена");
    private AssetLibrary Assets => _main.Assets ?? throw new Failure("нет AssetLibrary");

    private static Vector3 TowardsCamera => new Vector3(CameraRig.Direction.X, 0, CameraRig.Direction.Z).Normalized();

    private IEnumerable<Step> RoomSteps()
    {
        yield return new("room.asset-names", () =>
        {
            Check(AssetLibrary.NormalizeName("sword and shield slash (2).fbx") == "sword_and_shield_slash_2.fbx", "правило имен не как в sync_assets.ps1");
            Check(AssetLibrary.VendorPath("mixamo/characters/Paladin WProp J Nordstrom.fbx") == "res://vendor/mixamo/characters/paladin_wprop_j_nordstrom.fbx",
                "путь в vendor не нормализован");
            Check(AssetLibrary.CanonicalBone("mixamorig2:Hips") == "Hips" && AssetLibrary.CanonicalBone("mixamorig_LeftArm") == "LeftArm"
                  && AssetLibrary.CanonicalBone("mixamorig1:Spine") == "Spine", "префикс костей mixamorig не снимается");
            return Task.CompletedTask;
        });
        yield return new("room.built", RoomBuilt);
        yield return new("room.partial-assets", RoomPartialAssets);
        yield return new("room.scale", RoomScale);
        yield return new("room.clips-find-bones", RoomClips);
        yield return new("room.camera-on-screen", RoomCameraOnScreen);
        yield return new("room.camera-zoom-limits", RoomZoomLimits);
        yield return new("room.shadow-budget", RoomShadowBudget);
        yield return new("room.quality-low", RoomQualityLow);
        yield return new("room.occlusion", RoomOcclusion);
        yield return new("room.collisions", RoomCollisions);
    }

    /// <summary>The room is built with assets or with stubs, consistently with what is in res://vendor/.</summary>
    private async Task RoomBuilt()
    {
        var room = Room;
        var assets = Assets;
        Check(_main.CurrentTab == Main.RoomTab, $"первая вкладка «{_main.CurrentTab}», а не «{Main.RoomTab}»");
        await Settle();
        if (assets.HasModels)
        {
            Check(!room.HeroModel.IsStub && !room.DummyModel.IsStub, "модели есть, а в комнате заглушки");
            Check(room.HeroModel.Skeleton != null && room.HeroModel.Player != null, "у героя нет скелета или AnimationPlayer");
            if (assets.Clip("hero_idle") != null)
                Check(room.HeroModel.CurrentClip == "hero_idle" && room.HeroModel.Player!.IsPlaying(), "герой не в стойке");
        }
        else
        {
            Check(room.HeroModel.IsStub && room.DummyModel.IsStub, "моделей нет, а заглушек нет");
            Check(room.HeroModel.Meshes.Single().Mesh is CapsuleMesh, "заглушка героя не капсула");
        }
        Check(room.HeroModel.HasOutline, "у героя нет контура");
        Check(room.DummyModel.Meshes.All(m => m.MaterialOverlay == null), "у манекена контур героя");

        var chunks = room.FindChildren("FloorChunk*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().ToList();
        Check(chunks.Count >= 20, $"пол не кусками: {chunks.Count}");
        var floor = chunks[0].MaterialOverride as StandardMaterial3D;
        var floorEntry = assets.Table.Surfaces["floor"];
        var floorTextured = new[] { floorEntry.Albedo, floorEntry.Normal, floorEntry.Roughness }.All(f => ResourceLoader.Exists(AssetLibrary.VendorPath(f)));
        Check(floor != null && (floor.AlbedoTexture != null) == floorTextured,
            floorTextured ? "текстуры пола есть, а пол однотонный" : "текстур пола нет, а у пола текстура");

        var env = room.Environment;
        Check(env.TonemapMode == Godot.Environment.ToneMapper.Filmic && env.GlowEnabled && env.GlowHdrThreshold >= 1f, "окружение не filmic со свечением по порогу");
        Check(env.SsaoEnabled == (room.Quality == GraphicsQuality.High) && !env.VolumetricFogEnabled, "SSAO или туман не по настройке");
        var decals = room.FindChildren("*", nameof(Decal), true, false).OfType<Decal>().ToList();
        Check(decals.Count >= 3 && decals.All(d => d.CullMask == RoomView.FloorLayer && d.TextureAlbedo != null), "нет декалей крови на полу");
        Check(room.Occluders.Count(o => o.Name.StartsWith("pillar", StringComparison.Ordinal)) is >= 4 and <= 5, "колонн не 4–5");
        Check(assets.Missing.All(l => l.StartsWith(AssetLibrary.MissingMarker, StringComparison.Ordinal)), "строка отсутствия без маркера");
        GD.Print($"SELFTEST NOTE комната: модели {(assets.HasModels ? "есть" : "заглушки")}, текстуры {(assets.HasTextures ? "есть" : "однотонные")}, " +
                 $"строк {AssetLibrary.MissingMarker} {assets.Missing.Count}, качество {room.Quality}");
    }

    /// <summary>Review Focus 5: one clip and one texture gone → stubs and exactly one ASSET_MISSING line each, no exception.</summary>
    private Task RoomPartialAssets()
    {
        var table = Assets.Table;
        const string clipId = "hero_idle";
        const string surfaceId = "floor";
        Check(table.Clips.ContainsKey(clipId) && table.Surfaces.ContainsKey(surfaceId), "в таблице нет hero_idle или floor");
        var clips = table.Clips.ToDictionary(p => p.Key, p => p.Value);
        clips[clipId] = clips[clipId] with { File = "mixamo/anims/sword/нет такого клипа (1).fbx" };
        var surfaces = table.Surfaces.ToDictionary(p => p.Key, p => p.Value);
        surfaces[surfaceId] = surfaces[surfaceId] with { Roughness = "polyhaven/monastery_stone_floor/нет такой текстуры.jpg" };
        var library = new AssetLibrary(table with { Clips = clips, Surfaces = surfaces });

        var hero = library.Character("hero");
        try
        {
            Check(library.Clip(clipId) == null && library.Clip(clipId) == null, "отсутствующий клип вернулся");
            Check(!hero.Play(clipId) && !hero.Play(clipId), "отсутствующий клип проигрывается");
            var material = library.Surface(surfaceId);
            library.Surface(surfaceId);
            Check(material.AlbedoTexture == null && material.RoughnessTexture == null, "поверхность без текстуры не однотонная");
            int Lines(string file) => library.Missing.Count(l => l.Contains(file, StringComparison.Ordinal));
            Check(Lines(clips[clipId].File) == 1, $"строк о клипе {Lines(clips[clipId].File)}, а не одна");
            Check(Lines(surfaces[surfaceId].Roughness) == 1, $"строк о текстуре {Lines(surfaces[surfaceId].Roughness)}, а не одна");
            if (Assets.HasModels && Assets.HasTextures)
            {
                Check(!hero.IsStub, "герой стал заглушкой из-за клипа");
                Check(library.Surface("wall").AlbedoTexture != null, "соседняя поверхность стала однотонной");
                var other = table.Clips.Keys.First(k => k != clipId);
                Check(library.Clip(other) != null && hero.Play("hero_walk"), "соседний клип не грузится");
                Check(library.Missing.Count == 2, $"строк {AssetLibrary.MissingMarker}: {library.Missing.Count}, ожидалось 2: {string.Join(" | ", library.Missing)}");
            }
        }
        finally
        {
            hero.Free();
        }
        return Task.CompletedTask;
    }

    /// <summary>The height of every character's AABB is the spec height ±5 % (models measured, never assumed in centimetres).</summary>
    private Task RoomScale()
    {
        var notes = new List<string>();
        foreach (var (id, spec) in SpecHeights)
        {
            Check(Assets.Table.Characters.TryGetValue(id, out var entry) && Mathf.IsEqualApprox(entry.Height, spec), $"рост {id} в таблице не {spec}");
            var c = Assets.Character(id);
            try
            {
                var h = AssetLibrary.MeasureHeight(c);
                Check(Mathf.Abs(h - spec) <= 0.05f * spec, $"{id}: высота {h:0.###} м вместо {spec} м ±5 %");
                notes.Add($"{id} {h:0.###} м{(c.IsStub ? " (заглушка)" : $" (масштаб {c.ModelScale:0.###})")}");
            }
            finally
            {
                c.Free();
            }
        }
        var inRoom = AssetLibrary.MeasureHeight(Room.HeroModel);
        Check(Mathf.Abs(inRoom - 1.85f) <= 0.05f * 1.85f, $"герой в комнате {inRoom:0.###} м");
        GD.Print("SELFTEST NOTE рост: " + string.Join(", ", notes));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Every clip of the table fitted to each of the three models: no track with an unknown node or bone, the core humanoid
    /// bones animated, hips pinned (X = Z = 0), loop per the table, and the cached clip never processed twice.
    /// </summary>
    private Task RoomClips()
    {
        var assets = Assets;
        if (!assets.HasModels)
        {
            GD.Print("SELFTEST NOTE клипы: моделей нет, проверка костей пропущена (заглушки не анимируются)");
            return Task.CompletedTask;
        }
        var models = ClipModels.Select(assets.Character).ToList();
        var droppedNotes = new SortedSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var (id, entry) in assets.Table.Clips)
            {
                var source = assets.Clip(id);
                if (source == null)
                {
                    GD.Print($"SELFTEST NOTE клип {id} отсутствует, пропущен");
                    continue;
                }
                Check(source.LoopMode == (entry.Loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None), $"{id}: зацикливание не по таблице");
                Check(source.ResourceName != "mixamo.com" && source.ResourceName != "mixamo_com", $"{id}: имя клипа не из имени файла");
                foreach (var model in models)
                {
                    var dropped = new List<string>();
                    var fitted = assets.ClipFor(id, model, dropped);
                    Check(fitted != null, $"{id} → {model.Id}: клип не подогнан");
                    var unresolved = AssetLibrary.UnresolvedTracks(fitted!, model);
                    Check(unresolved.Count == 0, $"{id} → {model.Id}: треки без кости: {string.Join(", ", unresolved.Take(5))}");
                    Check(dropped.Count <= Math.Max(1, source.GetTrackCount() / 20), $"{id} → {model.Id}: выброшено {dropped.Count} треков: {string.Join(", ", dropped)}");
                    foreach (var d in dropped) droppedNotes.Add($"{model.Id}:{AssetLibrary.CanonicalBone(d)}");
                    var animated = Enumerable.Range(0, fitted!.GetTrackCount())
                        .Select(i => AssetLibrary.CanonicalBone(fitted.TrackGetPath(i).GetConcatenatedSubNames().ToString())).ToHashSet();
                    var lost = CoreBones.Where(b => !animated.Contains(b)).ToList();
                    Check(lost.Count == 0, $"{id} → {model.Id}: не анимированы {string.Join(", ", lost)}");
                    var hips = HipsTrack(fitted);
                    Check(hips >= 0, $"{id} → {model.Id}: нет трека позиции бедер");
                    for (int k = 0; k < fitted.TrackGetKeyCount(hips); k++)
                    {
                        var v = fitted.TrackGetKeyValue(hips, k).AsVector3();
                        Check(v.X == 0 && v.Z == 0, $"{id} → {model.Id}: корень не прибит (ключ {k}: {v})");
                    }
                    // A second fit gives the same keys: the cached clip was not modified by the first one.
                    var again = assets.ClipFor(id, model)!;
                    Check(again.TrackGetKeyValue(HipsTrack(again), 0).AsVector3() == fitted.TrackGetKeyValue(hips, 0).AsVector3(), $"{id} → {model.Id}: клип из кеша обработан дважды");
                }
            }
        }
        finally
        {
            foreach (var m in models) m.Free();
        }
        GD.Print($"SELFTEST NOTE клипы: {assets.Table.Clips.Count} × {ClipModels.Length} моделей, выброшены треки костей, которых нет у модели: " +
                 (droppedNotes.Count == 0 ? "нет" : string.Join(", ", droppedNotes)));
        return Task.CompletedTask;
    }

    private static int HipsTrack(Animation a)
    {
        for (int i = 0; i < a.GetTrackCount(); i++)
            if (a.TrackGetType(i) == Animation.TrackType.Position3D && AssetLibrary.CanonicalBone(a.TrackGetPath(i).GetConcatenatedSubNames().ToString()) == "Hips")
                return i;
        return -1;
    }

    /// <summary>The dummy is on screen with the hero next to it; points 30 m away along the screen axes are not.</summary>
    private async Task RoomCameraOnScreen()
    {
        var room = Room;
        var rig = room.CameraRig;
        room.PlaceHero(room.DummySpot + TowardsCamera * 1.5f);
        rig.ResetZoom();
        await Settle();
        Check(Mathf.IsEqualApprox(rig.Camera.Size, CameraRig.DefaultZoom) || rig.Zoom == rig.MinZoom, $"зум по умолчанию {rig.Zoom}");
        Check(rig.Camera.Projection == Camera3D.ProjectionType.Orthogonal && rig.Camera.Current, "камера не ортографическая или не текущая");
        Check(rig.IsOnScreen(room.DummySpot), "манекен рядом с героем не в кадре");
        Check(rig.IsOnScreen(room.Hero.Position), "герой не в кадре");
        var right = (rig.Transform * rig.Camera.Transform).Basis.X;
        var hero = room.Hero.Position;
        Check(!rig.IsOnScreen(hero + right * 30), "точка в 30 м вправо по экрану в кадре");
        Check(!rig.IsOnScreen(hero - right * 30), "точка в 30 м влево по экрану в кадре");
        Check(!rig.IsOnScreen(hero - TowardsCamera * 30), "точка в 30 м вверх по экрану в кадре");
        Check(!rig.IsOnScreen(hero + TowardsCamera * 30), "точка в 30 м вниз по экрану в кадре");
    }

    /// <summary>At the minimum and maximum zoom a floor point 8 m from the hero, in any direction, is on screen; wheel and middle button work.</summary>
    private async Task RoomZoomLimits()
    {
        var room = Room;
        var rig = room.CameraRig;
        room.PlaceHero(room.DummySpot + TowardsCamera * CameraRig.KeepInFrameRadius);
        await Settle();
        Check(rig.MinZoom <= CameraRig.DefaultZoom && CameraRig.DefaultZoom <= rig.MaxZoom, $"зум по умолчанию вне пределов [{rig.MinZoom}, {rig.MaxZoom}]");
        rig.Zoom = 0.1f;
        Check(rig.Zoom == rig.MinZoom, "зум ниже минимума принят");
        rig.Zoom = 1000f;
        Check(rig.Zoom == rig.MaxZoom, "зум выше максимума принят");
        foreach (var zoom in new[] { rig.MinZoom, rig.MaxZoom })
        {
            rig.Zoom = zoom;
            Check(rig.IsOnScreen(room.DummySpot), $"манекен в 8 м от героя не в кадре при зуме {zoom:0.##}");
            for (int i = 0; i < 16; i++)
            {
                var a = Mathf.Tau * i / 16;
                var p = room.Hero.Position + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * CameraRig.KeepInFrameRadius;
                Check(rig.IsOnScreen(p), $"точка в 8 м (угол {i * 22.5f}°) не в кадре при зуме {zoom:0.##}");
            }
        }
        rig.ResetZoom();
        var before = rig.Zoom;
        Check(rig.HandleInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true }) && rig.Zoom > before, "колесо не отдаляет");
        Check(rig.HandleInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true }) && Mathf.IsEqualApprox(rig.Zoom, before), "колесо не приближает");
        rig.Zoom = rig.MaxZoom;
        Check(rig.HandleInput(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = true }) && Mathf.IsEqualApprox(rig.Zoom, before), "средняя кнопка не вернула зум");
        GD.Print($"SELFTEST NOTE зум: {rig.MinZoom:0.##}–{rig.MaxZoom:0.##} м, по умолчанию {CameraRig.DefaultZoom} м, кадр {rig.Aspect:0.##}:1");
        room.PlaceHero(room.HeroSpawn);
    }

    /// <summary>At most 8 shadow lights wherever the hero stands; the hero light full, torch shadows partial and near; the limit binds.</summary>
    private Task RoomShadowBudget()
    {
        var room = Room;
        var before = room.Quality;
        room.Quality = GraphicsQuality.High;
        var spots = new[]
        {
            room.HeroSpawn, room.DummySpot + new Vector3(1, 0, 1), new Vector3(-10.5f, 0, -7.5f), new Vector3(10.5f, 0, -7.5f),
            new Vector3(-10.5f, 0, 7.5f), new Vector3(10.5f, 0, 7.5f), new Vector3(-8f, 0, 2f), new Vector3(-5f, 0, 0.2f),
        };
        int maxTorches = 0;
        bool limitBound = false;
        foreach (var spot in spots)
        {
            room.PlaceHero(spot);
            var shadows = room.ShadowLights.ToList();
            Check(shadows.Count <= RoomView.MaxShadowLights, $"у {spot}: теневых источников {shadows.Count}");
            Check(shadows.Contains(room.HeroLight) && room.HeroLight.ShadowOpacity == 1f, "у света героя нет полной тени");
            Check(!room.FillLight.ShadowEnabled && !room.DummyLight.ShadowEnabled, "заполняющий свет или свет манекена с тенью");
            var torches = shadows.Where(l => l != room.HeroLight).ToList();
            foreach (var t in torches)
            {
                Check(room.TorchLights.Contains(t), $"тень у постороннего света {t.Name}");
                Check(t.ShadowOpacity is >= 0.5f and <= 0.7f, $"shadow_opacity факела {t.ShadowOpacity}");
                var d = new Vector2(t.Position.X - spot.X, t.Position.Z - spot.Z).Length();
                Check(d <= RoomView.TorchShadowRadius, $"тень у факела в {d:0.#} м от героя");
            }
            var near = room.TorchLights.Count(t => new Vector2(t.Position.X - spot.X, t.Position.Z - spot.Z).Length() <= RoomView.TorchShadowRadius);
            Check(torches.Count == Math.Min(near, RoomView.MaxShadowLights - 1), $"у {spot}: факелов с тенью {torches.Count} при {near} рядом");
            if (near > RoomView.MaxShadowLights - 1) limitBound = true;
            maxTorches = Math.Max(maxTorches, torches.Count);
        }
        Check(maxTorches > 0, "ни один факел не дает тень на высоком качестве");
        Check(limitBound, "ни в одной точке лимит теневых источников не сработал (проверка ничего не проверила)");
        GD.Print($"SELFTEST NOTE тени: факелов {room.TorchLights.Count}, теневых факелов до {maxTorches} + свет героя");
        room.Quality = before;
        room.PlaceHero(room.HeroSpawn);
        return Task.CompletedTask;
    }

    /// <summary>Low quality: one shadow light (the hero's), SSAO, fog and projectile lights off, the hero outline kept.</summary>
    private Task RoomQualityLow()
    {
        var room = Room;
        var before = room.Quality;
        try
        {
            room.Quality = GraphicsQuality.Low;
            room.PlaceHero(room.DummySpot + new Vector3(1, 0, 1));
            var shadows = room.ShadowLights.ToList();
            Check(shadows.Count == 1 && shadows[0] == room.HeroLight, $"на низком теневых источников {shadows.Count}");
            Check(!room.Environment.SsaoEnabled && !room.Environment.VolumetricFogEnabled && !room.ProjectileLights, "на низком SSAO, туман или свет снарядов");
            room.VolumetricFog = true;
            Check(!room.Environment.VolumetricFogEnabled, "туман включился на низком");
            Check(room.HeroModel.HasOutline, "на низком нет контура героя");

            room.Quality = GraphicsQuality.High;
            Check(room.Environment.VolumetricFogEnabled, "флаг тумана не работает на высоком");
            room.VolumetricFog = false;
            Check(room.Environment.SsaoEnabled && !room.Environment.VolumetricFogEnabled && room.ProjectileLights, "высокое качество не вернулось");
            Check(room.ShadowLights.Count() > 1, "на высоком нет теней факелов");
        }
        finally
        {
            room.VolumetricFog = false;
            room.Quality = before;
            room.PlaceHero(room.HeroSpawn);
        }
        Check(RoomView.QualityFromArgs(["--selftest", "--quality", "low"]) == GraphicsQuality.Low
              && RoomView.QualityFromArgs(["--quality=low"]) == GraphicsQuality.Low
              && RoomView.QualityFromArgs(["--selftest"]) == GraphicsQuality.High, "флаг --quality не разбирается");
        return Task.CompletedTask;
    }

    /// <summary>The hero behind a pillar (seen from the camera) fades it to alpha hash; stepping out restores it.</summary>
    private Task RoomOcclusion()
    {
        var room = Room;
        var (pillar, spot) = room.Occluders.Where(o => o.Name.StartsWith("pillar", StringComparison.Ordinal))
            .Select(o => (Pillar: o, Spot: new Vector3(o.Bounds.GetCenter().X, 0, o.Bounds.GetCenter().Z) - TowardsCamera * 2.2f))
            .FirstOrDefault(x => Mathf.Abs(x.Spot.X) < RoomView.HalfX - 0.6f && Mathf.Abs(x.Spot.Z) < RoomView.HalfZ - 0.6f
                                 && room.Occluders.All(o => !o.Bounds.Grow(0.4f).HasPoint(x.Spot + Vector3.Up)));
        Check(pillar != null, "нет колонны, за которую можно встать");
        room.PlaceHero(room.HeroSpawn);
        Check(!pillar!.Faded, $"{pillar.Name} прозрачна, когда героя за ней нет");
        room.PlaceHero(spot);
        Check(pillar.Faded, $"герой за {pillar.Name}, а она не прозрачна");
        Check(pillar.Materials.All(m => m.Transparency == BaseMaterial3D.TransparencyEnum.AlphaHash && Mathf.IsEqualApprox(m.AlbedoColor.A, RoomView.FadedAlpha)),
            "загораживающая колонна не в alpha hash");
        room.PlaceHero(room.HeroSpawn);
        Check(!pillar.Faded && pillar.Materials.All(m => m.Transparency == BaseMaterial3D.TransparencyEnum.Disabled && m.AlbedoColor.A == 1f),
            "колонна не вернулась в обычный материал");
        return Task.CompletedTask;
    }

    /// <summary>Jolt is the 3D engine; walls and pillars stop the hero's body.</summary>
    private async Task RoomCollisions()
    {
        var room = Room;
        Check(ProjectSettings.GetSetting("physics/3d/physics_engine").AsString() == "Jolt Physics", "физика 3D не Jolt Physics");
        var pillar = room.Occluders.First(o => o.Name.StartsWith("pillar", StringComparison.Ordinal));
        var center = new Vector3(pillar.Bounds.GetCenter().X, 0, pillar.Bounds.GetCenter().Z);
        try
        {
            room.PlaceHero(center + TowardsCamera * 2.5f);
            await PhysicsFrames();
            var hit = room.Hero.MoveAndCollide(-TowardsCamera * 4f, testOnly: true);
            Check(hit != null && hit.GetCollider() is StaticBody3D { Name: var n } && n.ToString().StartsWith("Pillar", StringComparison.Ordinal),
                $"герой проходит сквозь {pillar.Name}");
            room.PlaceHero(new Vector3(RoomView.HalfX - 1.5f, 0, 2f));
            await PhysicsFrames();
            hit = room.Hero.MoveAndCollide(new Vector3(4f, 0, 0), testOnly: true);
            Check(hit != null && hit.GetCollider() is StaticBody3D, "герой проходит сквозь стену");
        }
        finally
        {
            room.PlaceHero(room.HeroSpawn);
        }
    }

    private async Task PhysicsFrames()
    {
        for (int i = 0; i < 3; i++) await _main.ToSignal(_main.GetTree(), SceneTree.SignalName.PhysicsFrame);
    }
}
