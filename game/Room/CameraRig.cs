using Godot;

namespace Dialoid.Game.Room;

/// <summary>
/// Orthographic camera from above at an angle, like Diablo 2 (spec §4): it looks at the target's feet from the direction
/// (18, 17, 18) and follows it. <see cref="Zoom"/> is the frame height in metres. The wheel zooms within limits that keep any
/// point on the floor within <see cref="KeepInFrameRadius"/> of the target in the frame (the dummy in reach is never lost);
/// the middle button restores the default zoom.
/// </summary>
public partial class CameraRig : Node3D
{
    /// <summary>Direction from the target to the camera (spec §4).</summary>
    public static readonly Vector3 Direction = new Vector3(18, 17, 18).Normalized();

    public const float Distance = 30f;
    public const float DefaultZoom = 10.5f;
    public const float MaxZoomLimit = 14f;
    public const float ZoomStep = 0.75f;

    /// <summary>A floor point this far from the target stays in the frame at every zoom.</summary>
    public const float KeepInFrameRadius = 8f;

    /// <summary>Margin around that circle inside the frame, metres.</summary>
    public const float FrameMargin = 0.3f;

    private float _zoom = DefaultZoom;

    public CameraRig()
    {
        Camera = new Camera3D
        {
            Name = "Camera",
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = DefaultZoom,
            Near = 0.1f,
            Far = 100f,
            KeepAspect = Camera3D.KeepAspectEnum.Height,
        };
        AddChild(Camera);
        Camera.Position = Direction * Distance;
        Camera.Basis = Basis.LookingAt(-Direction, Vector3.Up);
    }

    public Camera3D Camera { get; }

    /// <summary>The node the camera follows (its feet are the frame centre).</summary>
    public Node3D? Target { get; set; }

    /// <summary>Width / height of the frame; 16:9 until the viewport has a size.</summary>
    public float Aspect
    {
        get
        {
            var size = IsInsideTree() ? GetViewport().GetVisibleRect().Size : Vector2.Zero;
            return size.X > 1 && size.Y > 1 ? size.X / size.Y : 16f / 9f;
        }
    }

    /// <summary>
    /// The smallest frame height that keeps a floor circle of <see cref="KeepInFrameRadius"/> around the target in the frame:
    /// away from the camera the circle is foreshortened by sin(pitch), sideways it spans the frame width.
    /// </summary>
    public float MinZoom
    {
        get
        {
            var sinPitch = Direction.Y;
            var byHeight = 2 * (KeepInFrameRadius * sinPitch + FrameMargin);
            var byWidth = 2 * (KeepInFrameRadius + FrameMargin) / Aspect;
            return Mathf.Max(byHeight, byWidth);
        }
    }

    public float MaxZoom => Mathf.Max(MaxZoomLimit, MinZoom);

    /// <summary>Frame height in metres, clamped to [<see cref="MinZoom"/>, <see cref="MaxZoom"/>].</summary>
    public float Zoom
    {
        get => _zoom;
        set
        {
            _zoom = Mathf.Clamp(value, MinZoom, MaxZoom);
            Camera.Size = _zoom;
        }
    }

    /// <summary>Wheel step: negative zooms in (smaller frame).</summary>
    public void ZoomBy(float steps) => Zoom = _zoom + steps * ZoomStep;

    public void ResetZoom() => Zoom = DefaultZoom;

    /// <summary>Moves the rig onto the target now (also done every frame).</summary>
    public void Follow()
    {
        if (Target != null) Position = Target.Position;
        if (_zoom < MinZoom || _zoom > MaxZoom) Zoom = _zoom; // the window aspect changed
    }

    public override void _Process(double delta)
    {
        Follow();
        StickZoom(delta);
    }

    /// <summary>Zoom steps per second with the right stick at full tilt.</summary>
    public const float StickZoomSteps = 6f;

    private float _stick;

    /// <summary>True while the interface owns the gamepad (a window has the focus): the stick does not zoom.</summary>
    public Func<bool>? InputBlocked { get; set; }

    /// <summary>The right stick's vertical value the camera holds now (0 inside the dead zone).</summary>
    public float StickValue => _stick;

    /// <summary>One frame of the right stick's zoom (the self-test calls it with a fixed step).</summary>
    public void StickZoom(double delta)
    {
        if (InputBlocked?.Invoke() == true) _stick = 0;
        if (Mathf.Abs(_stick) > Dialoid.Game.Input.InputActions.StickDeadzone) ZoomBy(_stick * StickZoomSteps * (float)delta);
    }

    /// <summary>Wheel / right stick zoom, the middle button / R3 restore the default (only events no window took; spec §4).</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (HandleInput(e)) GetViewport().SetInputAsHandled();
    }

    /// <summary>The camera's reaction to one input event (the actions zoom_in / zoom_out / zoom_reset); true when it used it.</summary>
    public bool HandleInput(InputEvent e)
    {
        if (e is InputEventJoypadMotion { Axis: JoyAxis.RightY } stick)
        {
            _stick = InputBlocked?.Invoke() == true ? 0 : stick.AxisValue;
            return true;
        }
        if (e is not InputEventMouseButton and not InputEventJoypadButton || !e.IsPressed()) return false;
        if (e.IsActionPressed(Dialoid.Game.Input.InputActions.ZoomIn)) ZoomBy(-1);
        else if (e.IsActionPressed(Dialoid.Game.Input.InputActions.ZoomOut)) ZoomBy(1);
        else if (e.IsActionPressed(Dialoid.Game.Input.InputActions.ZoomReset)) ResetZoom();
        else return false;
        return true;
    }

    /// <summary>
    /// The point (in the room's space) is inside the camera rectangle; windows over the scene do not count (spec §6.3).
    /// Pure projection, so it works headless and for points outside the room.
    /// </summary>
    public bool IsOnScreen(Vector3 worldPoint)
    {
        var view = (Transform * Camera.Transform).AffineInverse() * worldPoint;
        var halfHeight = Camera.Size / 2;
        var halfWidth = halfHeight * Aspect;
        var depth = -view.Z;
        return Mathf.Abs(view.X) <= halfWidth && Mathf.Abs(view.Y) <= halfHeight && depth >= Camera.Near && depth <= Camera.Far;
    }
}
