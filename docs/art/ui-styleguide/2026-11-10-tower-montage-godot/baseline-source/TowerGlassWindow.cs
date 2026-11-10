using Godot;

namespace Dialoid.Game.Ui.Tower;

/// <summary>All sockets sample the same window in panel coordinates, including the lead between them.</summary>
public partial class TowerGlassWindow : Control
{
    public enum FrameStyle { Outline, CornerClips, Recessed, Lead }
    private FrameStyle _frame = FrameStyle.Lead;
    [Export]
    public FrameStyle Frame
    {
        get => _frame;
        set { _frame = value; QueueRedraw(); }
    }
    private GridContainer _grid = null!;
    private Texture2D _window = null!;

    public void Setup(GridContainer grid)
    {
        _grid = grid;
        MouseFilter = MouseFilterEnum.Ignore;
        _window = GD.Load<Texture2D>("res://Ui/Tower/Art/activity-window.png");
        Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Ui/Tower/activity_window.gdshader") };
        _grid.SortChildren += QueueRedraw;
        Resized += QueueRedraw;
    }

    public override void _Draw()
    {
        if (_window == null || Size.X <= 0 || Size.Y <= 0) return;
        // Socket glass belongs to the window. Occupancy changes only the overlaid motif and frame.
        DrawTextureRect(_window, new Rect2(Vector2.Zero, Size), false, new Color(0, 1, 1));
        foreach (Control slot in _grid.GetChildren())
        {
            if (!slot.Visible) continue;
            var r = slot.GetGlobalRect();
            var origin = r.Position - GlobalPosition;
            float w = r.Size.X, h = r.Size.Y;
            var button = slot as Button;
            float cut = Mathf.Round(Mathf.Min(w, h) * .18f);
            Vector2[] points = [new(cut, 0), new(w - cut, 0), new(w, cut), new(w, h - cut),
                new(w - cut, h), new(cut, h), new(0, h - cut), new(0, cut)];
            for (int i = 0; i < points.Length; i++)
                points[i] += origin;
            var uv = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++) uv[i] = points[i] / Size;
            DrawPolygon(points, new[] { new Color(.5f, 1, 1) }, uv, _window);
            if (button == null)
            {
                for (int i = 0; i < points.Length; i++)
                    DrawLine(points[i], points[(i + 1) % points.Length],
                        new Color(.25f, i < 2 || i == 7 ? .8f : .35f, 0), 1.25f, true);
                continue;
            }
            bool selected = button.ThemeTypeVariation == "SelectedActivitySlot";
            bool highlighted = button.IsHovered();
            Vector2[] closed = [.. points, points[0]];
            float intensity = highlighted ? .85f : selected ? .95f : .55f;
            var activeRim = new Color(.25f, intensity, 1);
            switch (Frame)
            {
                case FrameStyle.Lead:
                    var metal = new Color(.25f, selected ? .75f : highlighted ? 1f : .55f, selected ? 1f : 0f);
                    DrawPolyline(closed, metal, selected ? 1.5f : 1f, true);
                    break;
                case FrameStyle.CornerClips:
                    DrawPolyline(closed, selected ? activeRim : new Color(.25f, .65f, 0), 1f, true);
                    for (int i = 1; i < points.Length; i += 2)
                    {
                        var a = points[i];
                        var b = points[(i + 1) % points.Length];
                        Vector2[] clip = [a.MoveToward(points[i - 1], 4), a, b,
                            b.MoveToward(points[(i + 2) % points.Length], 4)];
                        DrawPolyline(clip, activeRim, highlighted ? 2.75f : 2f, true);
                    }
                    break;
                case FrameStyle.Recessed:
                    var center = origin + r.Size / 2;
                    var inset = new Vector2[closed.Length];
                    for (int i = 0; i < closed.Length; i++) inset[i] = center.Lerp(closed[i], .89f);
                    for (int i = 0; i < points.Length; i++)
                    {
                        var edge = closed[i + 1] - closed[i];
                        bool inShade = edge.X > edge.Y;
                        Vector2[] facet = [closed[i], closed[i + 1], inset[i + 1], inset[i]];
                        DrawPolygon(facet, new[] { new Color(.25f, inShade ? .32f : .85f, 0) });
                    }
                    DrawPolyline(inset, activeRim, selected ? 1.8f : 1f, true);
                    break;
                default:
                    DrawPolyline(closed, activeRim, selected ? 2f : 1f, true);
                    break;
            }
            if (button.HasFocus())
            {
                // COLOR encodes material channels decoded by activity_window.gdshader.
                var cold = new Color(.25f, 1f, 1f);
                Vector2[] upper = [points[7] + new Vector2(0, 4), points[7], points[0], points[0] + new Vector2(4, 0)];
                Vector2[] lower = [points[3] - new Vector2(0, 4), points[3], points[4], points[4] - new Vector2(4, 0)];
                DrawPolyline(upper, cold, 2.5f, true);
                DrawPolyline(lower, cold, 2.5f, true);
            }
        }
    }
}
