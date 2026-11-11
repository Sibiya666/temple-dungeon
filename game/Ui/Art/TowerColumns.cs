using Godot;

namespace Dialoid.Game.Ui.Art;

/// <summary>Two persistent panes: split on a wide workspace and stacked in a side window, without reparenting focus.</summary>
public partial class TowerColumns : Container
{
    public float Breakpoint { get; set; } = 760;
    public float LeftRatio { get; set; } = .30f;
    public float Gap { get; set; } = 16;
    public float LeftMinimumHeight { get; set; } = 240;

    public override Vector2 _GetMinimumSize()
    {
        var panes = GetChildren().OfType<Control>().Where(c => c.Visible).ToArray();
        if (panes.Length == 0) return Vector2.Zero;
        var minimum = panes.Select(c => c.GetCombinedMinimumSize()).ToArray();
        var wide = minimum.Length == 2 && Size.X >= Breakpoint && Size.X >= minimum[0].X + minimum[1].X + Gap;
        return new Vector2(minimum.Max(m => m.X), wide ? minimum.Max(m => m.Y) : minimum.Sum(m => m.Y) + (minimum.Length - 1) * Gap);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized) { UpdateMinimumSize(); QueueSort(); }
        if (what != NotificationSortChildren) return;
        var panes = GetChildren().OfType<Control>().Where(c => c.Visible).Take(2).ToArray();
        if (panes.Length == 0) return;
        if (panes.Length == 1) { FitChildInRect(panes[0], new Rect2(Vector2.Zero, Size)); return; }
        var left = panes[0].GetCombinedMinimumSize();
        var right = panes[1].GetCombinedMinimumSize();
        if (Size.X >= Breakpoint && Size.X >= left.X + right.X + Gap)
        {
            var width = Mathf.Clamp((Size.X - Gap) * LeftRatio, left.X, Size.X - Gap - right.X);
            FitChildInRect(panes[0], new Rect2(0, 0, width, Size.Y));
            FitChildInRect(panes[1], new Rect2(width + Gap, 0, Size.X - width - Gap, Size.Y));
        }
        else
        {
            var leftBudget = Mathf.Min((Size.Y - Gap) * .5f, Size.Y - Gap - right.Y);
            var height = Mathf.Max(left.Y, Mathf.Min(LeftMinimumHeight, Mathf.Max(0, leftBudget)));
            FitChildInRect(panes[0], new Rect2(0, 0, Size.X, height));
            FitChildInRect(panes[1], new Rect2(0, height + Gap, Size.X, Mathf.Max(0, Size.Y - height - Gap)));
        }
    }
}
