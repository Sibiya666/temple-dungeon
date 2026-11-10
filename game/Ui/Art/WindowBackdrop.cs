using Godot;

namespace Dialoid.Game.Ui.Art;

/// <summary>Window-local artwork, drawn before the UI without participating in container layout.</summary>
public partial class WindowBackdrop : Node2D
{
    private const string LayerName = "WindowBackdrop";
    private GameWindow _window = null!;
    private Texture2D _texture = null!;
    private float _opacity;

    internal static void Install(GameWindow window)
    {
        if (window.GetNodeOrNull<Node>(LayerName) != null) return;
        // Explicit allowlist: an unknown window keeps its existing opaque surface.
        var quiet = window.Spec.Id is WindowIds.Log or WindowIds.Meter or WindowIds.Charts;
        if (window.Spec.Id is not (WindowIds.Character or WindowIds.Skills or WindowIds.Passives
            or WindowIds.Ascendancy or WindowIds.Builds or WindowIds.Breakdown
            or WindowIds.Simulation or WindowIds.Meter or WindowIds.Log or WindowIds.Charts)) return;
        var texture = GD.Load<Texture2D>($"res://Ui/Art/WindowPanels/{window.Spec.Id}-v1.png");
        if (texture == null) return;
        var backdrop = new WindowBackdrop
        {
            Name = LayerName,
            _window = window,
            _texture = texture,
            _opacity = quiet ? 0.28f : 0.38f,
        };
        // PanelContainer only arranges Control children. A Node2D has no minimum size,
        // mouse filter or focus, and inherits this window's transform and draw order.
        window.AddChild(backdrop);
        window.MoveChild(backdrop, 0);
    }

    public override void _EnterTree() => _window.Resized += QueueRedraw;

    public override void _ExitTree() => _window.Resized -= QueueRedraw;

    public override void _Draw()
    {
        // Stay inside the live frame and below the title. Never change its style instance.
        var rect = new Rect2(2, GameWindow.TitleHeight + 2,
            _window.Size.X - 4, _window.Size.Y - GameWindow.TitleHeight - 4);
        if (rect.Size.X <= 0 || rect.Size.Y <= 0) return;
        var image = _texture.GetSize();
        if (image.X <= 0 || image.Y <= 0) return;
        var scale = Mathf.Max(rect.Size.X / image.X, rect.Size.Y / image.Y);
        var sourceSize = rect.Size / scale;
        // Cover without distortion; retain the upper architectural motif even in short windows.
        var source = new Rect2(new Vector2((image.X - sourceSize.X) * 0.5f, 0), sourceSize);
        DrawTextureRectRegion(_texture, rect, source, new Color(1, 1, 1, _opacity));

        var fadeHeight = Mathf.Min(rect.Size.Y, Mathf.Max(120, rect.Size.Y * 0.6f));
        // Skill nodes sit over the full artwork, including the lower graph area.
        bool skillTree = _window.Spec.Id == WindowIds.Skills;
        var top = WindowHost.Background with { A = skillTree ? 0.64f : 0.18f };
        var body = WindowHost.Background with { A = skillTree ? 0.94f : 0.96f };
        var left = rect.Position;
        var right = left + new Vector2(rect.Size.X, 0);
        DrawPolygon(new[] { left, right, right + new Vector2(0, fadeHeight), left + new Vector2(0, fadeHeight) },
            new[] { top, top, body, body });
        if (fadeHeight < rect.Size.Y)
            DrawRect(new Rect2(left + new Vector2(0, fadeHeight), new Vector2(rect.Size.X, rect.Size.Y - fadeHeight)), body);
    }
}
