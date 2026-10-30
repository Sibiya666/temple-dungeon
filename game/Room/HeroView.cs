using Godot;

namespace Dialoid.Game.Room;

/// <summary>
/// The hero in the room (spec §4 «Наведение», §5): the Mixamo Paladin on a capsule body. It moves by WASD relative to the camera
/// or towards a floor point (a click walks, a held button runs after the cursor), turns smoothly to its movement or to the dummy
/// when a skill is used, and plays <c>sword and shield idle/walk/run</c>. <see cref="PlayAttack"/> stretches an attack clip to the
/// core's use time; a channel replays it on every tick. A refused press shows its short text over the head. The outline (unshaded
/// overlay, see <see cref="AssetLibrary"/>), the light circle and the fill light are the room's. Input comes only through the
/// methods (<see cref="CombatDriver"/> turns events into calls), never from the global Input.
/// </summary>
public partial class HeroView : CharacterBody3D
{
    public const float Radius = 0.35f;
    public const float Height = 1.85f;
    public const float WalkSpeed = 2.2f;
    public const float RunSpeed = 5f;
    public const float TurnRate = 12f;
    public const float ArriveDistance = 0.08f;
    public const double RefusalSec = 1.2;
    public const double LocomotionBlend = 0.15;

    public const string IdleClip = "hero_idle";
    public const string WalkClip = "hero_walk";
    public const string RunClip = "hero_run";

    private Vector2 _wasd;
    private Vector3? _target;
    private bool _run;
    private float? _faceYaw;
    private string? _attackClip;
    private double _attackLeft;
    private bool _frozen;
    private Label3D _refusal = null!;
    private double _refusalLeft;

    public CharacterModel Model { get; private set; } = null!;

    /// <summary>The camera WASD is relative to.</summary>
    public CameraRig? Rig { get; set; }

    /// <summary>The attack clip playing now, or null while moving or standing.</summary>
    public string? AttackClip => _attackClip;

    /// <summary>Attacks started (for checks; counted on stubs too).</summary>
    public int AttackCount { get; private set; }

    /// <summary>The last attack: clip, use time and the playback speed that fits the clip into it (1 on a stub).</summary>
    public (string Clip, double UseSec, float Speed)? LastAttack { get; private set; }

    /// <summary>The refusal text over the head now, or null.</summary>
    public string? RefusalText => _refusalLeft > 0 ? _refusal.Text : null;

    /// <summary>The last refusal text shown.</summary>
    public string? LastRefusal { get; private set; }

    /// <summary>Where the hero is walking to, or null.</summary>
    public Vector3? MoveTarget => _target;

    public bool Frozen
    {
        get => _frozen;
        set
        {
            _frozen = value;
            if (Model.Player != null) Model.Player.SpeedScale = value ? 0 : 1;
        }
    }

    public static HeroView Create(AssetLibrary assets)
    {
        var hero = new HeroView { Name = "Hero" };
        hero.AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = Radius, Height = Height },
            Position = new Vector3(0, Height / 2, 0),
        });
        hero.Model = assets.Character(AssetLibrary.HeroId);
        hero.AddChild(hero.Model);
        hero.Model.Play(IdleClip);
        hero._refusal = new Label3D
        {
            Name = "Refusal",
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Shaded = false,
            FontSize = 44,
            PixelSize = 0.0065f,
            OutlineSize = 12,
            OutlineModulate = new Color(0, 0, 0, 0.9f),
            Modulate = new Color(1f, 0.5f, 0.42f),
            Position = new Vector3(0, Height + 0.55f, 0),
            Visible = false,
            RenderPriority = 10,
            OutlineRenderPriority = 9,
        };
        hero.AddChild(hero._refusal);
        return hero;
    }

    // ---------- commands ----------

    /// <summary>WASD as a vector: X right, Y up the screen (zero stops the key movement).</summary>
    public void SetMoveInput(Vector2 wasd)
    {
        _wasd = wasd.LengthSquared() > 1 ? wasd.Normalized() : wasd;
        if (_wasd != Vector2.Zero) _target = null;
    }

    /// <summary>Walks (or runs) to a floor point.</summary>
    public void MoveTo(Vector3 point, bool run)
    {
        _target = new Vector3(point.X, 0, point.Z);
        _run = run;
    }

    /// <summary>Stops the click movement (keys still move).</summary>
    public void Stop()
    {
        _target = null;
        Velocity = Vector3.Zero;
    }

    /// <summary>Turns smoothly towards a point (a skill aimed at the dummy).</summary>
    public void FaceTowards(Vector3 point)
    {
        var d = new Vector2(point.X - Position.X, point.Z - Position.Z);
        if (d.LengthSquared() > 1e-4f) _faceYaw = Mathf.Atan2(d.X, d.Y);
    }

    /// <summary>Turns towards a point at once (a teleport).</summary>
    public void SnapFacing(Vector3 point)
    {
        FaceTowards(point);
        if (_faceYaw is { } yaw) Rotation = new Vector3(0, yaw, 0);
    }

    /// <summary>Plays an attack clip stretched (or squeezed) to the core's use time; the same clip again restarts it.</summary>
    public void PlayAttack(string clipId, double useSec)
    {
        useSec = Math.Max(useSec, 0.05);
        var length = Model.ClipLength(clipId);
        var speed = length is > 0 ? (float)(length.Value / useSec) : 1f;
        Model.Play(clipId, speed: speed, blend: 0.08, restart: true);
        if (Model.Player != null) Model.Player.SpeedScale = _frozen ? 0 : 1;
        _attackClip = clipId;
        _attackLeft = useSec;
        _target = null; // a use stops the click movement (keys still move)
        AttackCount++;
        LastAttack = (clipId, useSec, speed);
    }

    /// <summary>Hides the refusal text (a restarted fight).</summary>
    public void ClearRefusal()
    {
        _refusalLeft = 0;
        _refusal.Visible = false;
    }

    /// <summary>Shows a refused press's short text over the head.</summary>
    public void ShowRefusal(string text)
    {
        _refusal.Text = text;
        _refusal.Visible = true;
        _refusal.Modulate = _refusal.Modulate with { A = 1 };
        _refusalLeft = RefusalSec;
        LastRefusal = text;
    }

    // ---------- frame ----------

    /// <summary>Visual time step (stops during hit-stop): attack timer, locomotion clip, refusal fade.</summary>
    public void Advance(double dt)
    {
        if (dt <= 0) return;
        if (_attackClip != null)
        {
            _attackLeft -= dt;
            if (_attackLeft <= 0) _attackClip = null;
        }
        if (_attackClip == null)
        {
            var speed = new Vector2(Velocity.X, Velocity.Z).Length();
            var clip = speed > (WalkSpeed + RunSpeed) / 2 ? RunClip : speed > 0.2f ? WalkClip : IdleClip;
            if (Model.CurrentClip != clip || Model.Player is { } p && !p.IsPlaying()) Model.Play(clip, blend: LocomotionBlend);
        }
        if (_refusalLeft > 0)
        {
            _refusalLeft -= dt;
            var t = (float)Math.Clamp(_refusalLeft / RefusalSec, 0, 1);
            _refusal.Modulate = _refusal.Modulate with { A = Mathf.Min(1, t * 2.5f) };
            _refusal.Position = new Vector3(0, Height + 0.55f + 0.25f * (1 - t), 0);
            if (_refusalLeft <= 0) _refusal.Visible = false;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        var dt = (float)delta;
        var move = Vector3.Zero;
        var speed = 0f;
        if (_wasd != Vector2.Zero)
        {
            var (right, up) = ScreenAxes();
            move = (right * _wasd.X + up * _wasd.Y).Normalized();
            speed = RunSpeed;
        }
        else if (_target is { } target)
        {
            var to = new Vector3(target.X - Position.X, 0, target.Z - Position.Z);
            var dist = to.Length();
            speed = _run ? RunSpeed : WalkSpeed;
            if (dist <= Mathf.Max(ArriveDistance, speed * dt))
            {
                _target = null;
                speed = 0;
            }
            else move = to / dist;
        }

        if (move != Vector3.Zero)
        {
            Velocity = move * speed;
            if (_attackClip == null) _faceYaw = Mathf.Atan2(move.X, move.Z);
            MoveAndSlide();
            Position = Position with { Y = 0 };
        }
        else if (Velocity != Vector3.Zero) Velocity = Vector3.Zero;

        if (_faceYaw is { } yaw)
        {
            var current = Rotation.Y;
            var next = Mathf.LerpAngle(current, yaw, Mathf.Min(1, TurnRate * dt));
            Rotation = new Vector3(0, next, 0);
            if (Mathf.Abs(Mathf.AngleDifference(next, yaw)) < 0.01f) _faceYaw = null;
        }
    }

    /// <summary>The camera's right and up-the-screen directions on the floor.</summary>
    private (Vector3 Right, Vector3 Up) ScreenAxes()
    {
        var basis = Rig != null ? (Rig.Transform * Rig.Camera.Transform).Basis : Basis.LookingAt(-CameraRig.Direction, Vector3.Up);
        var right = new Vector3(basis.X.X, 0, basis.X.Z).Normalized();
        var up = new Vector3(-basis.Z.X, 0, -basis.Z.Z).Normalized();
        return (right, up);
    }
}
