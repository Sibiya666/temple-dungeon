using Dialoid.Game.Ui.Art;
using Godot;

namespace Dialoid.Game.Ui;

public partial class TreeView
{
    private static Vector2[] Socket(Vector2 center, float radius)
    {
        var cut = radius * .32f;
        return new[]
        {
            center + new Vector2(-radius + cut, -radius), center + new Vector2(radius - cut, -radius),
            center + new Vector2(radius, -radius + cut), center + new Vector2(radius, radius - cut),
            center + new Vector2(radius - cut, radius), center + new Vector2(-radius + cut, radius),
            center + new Vector2(-radius, radius - cut), center + new Vector2(-radius, -radius + cut),
        };
    }

    private void DrawReferenceTree()
    {
        var font = GetThemeDefaultFont();
        foreach (var (a, b) in _edges)
        {
            var from = WorldToLocal(_byId[a].Position);
            var to = WorldToLocal(_byId[b].Position);
            var direction = (to - from).Normalized();
            var taken = StateOf(a) == TreeNodeState.Taken && StateOf(b) == TreeNodeState.Taken;
            DrawLine(from + direction * _byId[a].Radius * Zoom, to - direction * _byId[b].Radius * Zoom,
                taken ? RoomHudTheme.LitBronze : RoomHudTheme.Bronze, taken ? 1.8f : 1f, true);
        }
        foreach (var node in _nodes)
        {
            var center = WorldToLocal(node.Position);
            var radius = Mathf.Max(node.Radius * Zoom, 5f);
            var state = StateOf(node.Id);
            var selected = node.Id == SelectedNodeId;
            var points = Socket(center, radius);
            DrawColoredPolygon(points, new Color(.047f, .067f, .078f, .94f));
            var border = selected ? RoomHudTheme.Focus : state == TreeNodeState.Taken ? RoomHudTheme.LitBronze : RoomHudTheme.Bronze;
            DrawPolyline(points.Concat(new[] { points[0] }).ToArray(), border, selected ? 2f : 1.2f, true);
            if (node.Icon != null && radius >= 8)
                DrawTextureRect(node.Icon, new Rect2(center - Vector2.One * radius * .83f, Vector2.One * radius * 1.66f), false,
                    state == TreeNodeState.Unavailable ? new Color(.45f, .45f, .45f) : Colors.White);
            if (selected && HasFocus())
            {
                var r = radius + 4;
                var arm = Mathf.Min(10, radius * .35f);
                foreach (var x in new[] { -1, 1 })
                    foreach (var y in new[] { -1, 1 })
                    {
                        var p = center + new Vector2(x * r, y * r);
                        DrawLine(p, p - new Vector2(x * arm, 0), RoomHudTheme.Parchment, 2f, true);
                        DrawLine(p, p - new Vector2(0, y * arm), RoomHudTheme.Parchment, 2f, true);
                    }
            }
        }
        // Label surfaces interrupt edges and are placed clear of every socket and previous label.
        var captions = LayoutReferenceCaptions();
        foreach (var (id, rect, _) in captions)
        {
            var node = _byId[id];
            var center = WorldToLocal(node.Position);
            var nearest = new Vector2(Mathf.Clamp(center.X, rect.Position.X, rect.End.X), Mathf.Clamp(center.Y, rect.Position.Y, rect.End.Y));
            if (center.DistanceTo(nearest) > node.Radius * Zoom + 18)
                DrawLine(center + (nearest - center).Normalized() * (node.Radius * Zoom + 3), nearest,
                    RoomHudTheme.Bronze, 1f, true);
        }
        foreach (var (id, rect, size) in captions)
        {
            var node = _byId[id];
            DrawRect(rect, new Color(.047f, .067f, .078f, .94f));
            var captionWidth = font.GetStringSize(node.Caption!, HorizontalAlignment.Left, -1, size).X;
            DrawString(font, new Vector2(rect.GetCenter().X - captionWidth / 2, rect.Position.Y + font.GetAscent(size) + 2), node.Caption!,
                HorizontalAlignment.Left, -1, size, RoomHudTheme.Parchment);
            var status = ReferenceStatus(node);
            var secondarySize = Mathf.Max(11, size - 2);
            var statusWidth = font.GetStringSize(status, HorizontalAlignment.Left, -1, secondarySize).X;
            DrawString(font, new Vector2(rect.GetCenter().X - statusWidth / 2, rect.Position.Y + font.GetHeight(size) + font.GetAscent(secondarySize) + 2), status,
                HorizontalAlignment.Left, -1, secondarySize, RoomHudTheme.MutedText);
        }
    }

    private string ReferenceStatus(TreeViewNode node)
    {
        var stateText = StateOf(node.Id) switch
        {
            TreeNodeState.Taken => "Взят",
            TreeNodeState.Available => "Доступен",
            _ => "Недоступен",
        };
        return node.Badge == null ? "Старт · взят" : $"{stateText} · {node.Badge}";
    }

    /// <summary>Deterministic two-line labels avoiding all cut sockets and other labels, including at 720p.</summary>
    public IReadOnlyList<(string Id, Rect2 Rect, int FontSize)> LayoutReferenceCaptions()
    {
        var placed = new List<(string Id, Rect2 Rect, int FontSize)>();
        var font = GetThemeDefaultFont();
        var defaultSize = Mathf.Clamp(GetThemeDefaultFontSize(), 12, 18);
        var view = new Rect2(Vector2.One * 2, Size - Vector2.One * 4);
        var sockets = _nodes.Select(n => new Rect2(WorldToLocal(n.Position) - Vector2.One * n.Radius * Zoom,
            Vector2.One * n.Radius * Zoom * 2).Grow(3)).ToArray();
        // Side mode shows the complete compact graph, with the selected node named in graph and detail.
        // Expanded mode has room for all seven names; side labels must not crowd out the small portraits.
        var compact = Size.X < 600;
        var order = _nodes.Where(n => n.Caption != null && (!compact || n.Id == SelectedNodeId))
            .OrderBy(n => n.Position == Vector2.Zero ? 0 : n.Id == SelectedNodeId ? 1 : 2).ThenBy(n => n.Id, StringComparer.Ordinal);
        foreach (var node in order)
        {
            var center = WorldToLocal(node.Position);
            var radius = node.Radius * Zoom;
            if (radius < 9 || !view.HasPoint(center)) continue;
            foreach (var size in new[] { defaultSize, Mathf.Max(12, defaultSize - 2) }.Distinct())
            {
                var width = Mathf.Max(font.GetStringSize(node.Caption!, HorizontalAlignment.Left, -1, size).X,
                    font.GetStringSize(ReferenceStatus(node), HorizontalAlignment.Left, -1, Mathf.Max(11, size - 2)).X) + 8;
                var height = font.GetHeight(size) + font.GetHeight(Mathf.Max(11, size - 2)) + 4;
                var x = Mathf.Clamp(center.X - width / 2, view.Position.X, Mathf.Max(view.Position.X, view.End.X - width));
                var below = center.Y + radius + 6;
                var above = center.Y - radius - 6 - height;
                var middle = center.Y - height / 2;
                var candidates = new List<Vector2>
                {
                    new(x, below), new(x, above), new(center.X + radius + 7, middle), new(center.X - radius - width - 7, middle),
                };
                // Slide labels along the empty area beside a ring, rather than drawing over its centre.
                foreach (var y in new[] { below, above, middle })
                    foreach (var offset in new[] { -width * .5f, width * .5f, -width, width })
                        candidates.Add(new Vector2(Mathf.Clamp(x + offset, view.Position.X, Mathf.Max(view.Position.X, view.End.X - width)), y));
                bool Fits(Rect2 r) => view.Encloses(r) && !sockets.Any(s => s.Intersects(r)) && !placed.Any(p => p.Rect.Grow(2).Intersects(r));
                var found = candidates.Select(p => new Rect2(p, new Vector2(width, height)))
                    .Where(Fits)
                    .Cast<Rect2?>().FirstOrDefault();
                // The lowest node may have no below/above slot in a short viewport. Search the remaining quiet
                // space nearby instead of dropping its label or painting over another node. A leader links it.
                if (found == null)
                {
                    var quiet = new List<Rect2>();
                    for (var y = view.Position.Y; y + height <= view.End.Y; y += 12)
                        for (var xx = view.Position.X; xx + width <= view.End.X; xx += 12)
                        {
                            var candidate = new Rect2(xx, y, width, height);
                            if (Fits(candidate)) quiet.Add(candidate);
                        }
                    found = quiet.OrderBy(r => r.GetCenter().DistanceSquaredTo(center)).Cast<Rect2?>().FirstOrDefault();
                }
                if (found == null) continue;
                placed.Add((node.Id, found.Value, size));
                break;
            }
        }
        return placed;
    }
}
