using Godot;

namespace Dialoid.Game.Room;

/// <summary>World-space GPU fire; owned and retired by EffectLayer's visual clock, never by Core.</summary>
public sealed partial class FireParticles : VisualEffect
{
    public const int MaxRigs = 24;
    public const double DrainSec = 1.05; // longest GPU lifetime (.8 s) plus a scheduling margin
    private ProjectileView? _source;
    private double _drainAge;
    public bool Stopped { get; private set; }
    public int ParticleBudget => GetChildren().OfType<GpuParticles3D>().Sum(p => p.Amount);

    internal static FireParticles Trail(ProjectileView source, bool low)
    {
        var fx = new FireParticles { Name = "FireTail", _source = source };
        fx.AddChild(Emitter("Flame", low ? 10 : 24, .28, .23f,
            new Color(1f, .42f, .08f, .65f), new Color(.32f, .04f, .01f, 0), .15f, .35f, .65f));
        fx.AddChild(Emitter("Embers", low ? 4 : 10, .5, .045f,
            new Color(.95f, .24f, .025f, .9f), new Color(.28f, .025f, .01f, 0), .25f, .65f, -.45f));
        return fx;
    }

    internal static FireParticles Impact(bool low, float size)
    {
        var fx = new FireParticles { Name = "FireImpact", Stopped = true };
        // Crit increases local extent slightly, never particle count or screen coverage dramatically.
        var extent = Mathf.Min(size, 1.25f);
        fx.AddChild(Emitter("HitSparks", low ? 6 : 14, .23, .055f * extent,
            new Color(1f, .38f, .055f, .85f), new Color(.4f, .04f, .01f, 0), 1.2f, 2.4f, -2f, true));
        fx.AddChild(Emitter("Smoke", low ? 3 : 7, .8, .32f * extent,
            new Color(.16f, .14f, .13f, .19f), new Color(.11f, .12f, .13f, 0), .12f, .32f, .3f, true));
        return fx;
    }

    public void Stop()
    {
        if (Stopped) return;
        Stopped = true;
        _source = null;
        foreach (var p in GetChildren().OfType<GpuParticles3D>()) p.Emitting = false;
    }

    public override bool Step(double dt)
    {
        Age += dt;
        if (!Stopped && (_source == null || !GodotObject.IsInstanceValid(_source) ||
            _source.IsQueuedForDeletion() || _source.Arrived || _source.GetParent() != GetParent())) Stop();
        if (!Stopped) return true;
        _drainAge += dt;
        return _drainAge < DrainSec;
    }

    private static GpuParticles3D Emitter(string name, int count, double lifetime, float size,
        Color hot, Color cold, float speedMin, float speedMax, float gravity, bool burst = false)
    {
        var ramp = new Gradient();
        ramp.SetColor(0, hot);
        ramp.SetColor(1, cold);
        ramp.AddPoint(.45f, hot.Lerp(cold, .55f));
        return new GpuParticles3D
        {
            Name = name,
            Amount = count,
            Lifetime = lifetime,
            LocalCoords = false,
            OneShot = burst,
            Explosiveness = burst ? 1f : 0f,
            Emitting = true,
            ProcessMaterial = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
                EmissionBoxExtents = new Vector3(.045f, .045f, .045f),
                Direction = Vector3.Up,
                Spread = burst ? 85f : 32f,
                InitialVelocityMin = speedMin,
                InitialVelocityMax = speedMax,
                Gravity = new Vector3(0, gravity, 0),
                ScaleMin = .5f,
                ScaleMax = 1.1f,
                ColorRamp = new GradientTexture1D { Gradient = ramp },
            },
            DrawPass1 = new QuadMesh { Size = new Vector2(size, size), Material = EffectLayer.BlobMaterial },
            VisibilityAabb = new Aabb(new Vector3(-20, -3, -20), new Vector3(40, 10, 40)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }
}
