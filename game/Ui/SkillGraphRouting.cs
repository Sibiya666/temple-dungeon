using System;
using System.Collections.Generic;
using Godot;

namespace Dialoid.Game.Ui;

/// <summary>Routes dependency lines around the caller's already padded obstacles.</summary>
public static class SkillGraphRouting
{
    private const float CornerEpsilon = 1f;

    public static IReadOnlyList<Vector2> Route(Vector2 from, Vector2 to, IReadOnlyList<Rect2> obstacles)
    {
        ArgumentNullException.ThrowIfNull(obstacles);
        Validate(from);
        Validate(to);
        foreach (var obstacle in obstacles)
        {
            Validate(obstacle.Position);
            Validate(obstacle.End);
        }

        bool Clear(Vector2 a, Vector2 b)
        {
            foreach (var obstacle in obstacles)
                if (Intersects(a, b, obstacle))
                    return false;
            return true;
        }

        if (Clear(from, to))
            return new[] { from, to };

        var corners = new List<Vector2>();
        foreach (var obstacle in obstacles)
        {
            var end = obstacle.End;
            float left = Math.Min(obstacle.Position.X, end.X) - CornerEpsilon;
            float right = Math.Max(obstacle.Position.X, end.X) + CornerEpsilon;
            float top = Math.Min(obstacle.Position.Y, end.Y) - CornerEpsilon;
            float bottom = Math.Max(obstacle.Position.Y, end.Y) + CornerEpsilon;
            corners.Add(new Vector2(left, top));
            corners.Add(new Vector2(left, bottom));
            corners.Add(new Vector2(right, top));
            corners.Add(new Vector2(right, bottom));
        }
        // Coordinate ordering makes equal-cost choices independent of obstacle order.
        corners.Sort((a, b) =>
        {
            int x = a.X.CompareTo(b.X);
            return x != 0 ? x : a.Y.CompareTo(b.Y);
        });

        var vertices = new List<Vector2> { from, to };
        foreach (var corner in corners)
            if (corner != from && corner != to && Clear(corner, corner)
                && (vertices.Count == 2 || vertices[vertices.Count - 1] != corner))
                vertices.Add(corner);

        int count = vertices.Count;
        var weights = new double[count, count];
        for (int i = 0; i < count; i++)
        for (int j = i + 1; j < count; j++)
        {
            double dx = (double)vertices[i].X - vertices[j].X;
            double dy = (double)vertices[i].Y - vertices[j].Y;
            weights[i, j] = weights[j, i] = Clear(vertices[i], vertices[j])
                ? Math.Sqrt(dx * dx + dy * dy) : double.PositiveInfinity;
        }

        var distances = new double[count];
        var previous = new int[count];
        var visited = new bool[count];
        Array.Fill(distances, double.PositiveInfinity);
        Array.Fill(previous, -1);
        distances[0] = 0;
        for (int step = 0; step < count; step++)
        {
            int current = -1;
            for (int i = 0; i < count; i++)
                if (!visited[i] && !double.IsPositiveInfinity(distances[i])
                    && (current < 0 || distances[i] < distances[current]))
                    current = i;
            if (current < 0)
                break;
            if (current == 1)
            {
                var path = new List<Vector2>();
                for (int vertex = 1; vertex >= 0; vertex = previous[vertex])
                    path.Add(vertices[vertex]);
                path.Reverse();
                return path;
            }
            visited[current] = true;
            for (int next = 0; next < count; next++)
            {
                if (visited[next] || next == current)
                    continue;
                double candidate = distances[current] + weights[current, next];
                if (candidate < distances[next])
                {
                    distances[next] = candidate;
                    previous[next] = current;
                }
            }
        }
        throw new InvalidOperationException("No obstacle-free route exists in the visibility graph.");
    }

    /// <summary>Inclusive segment/rectangle intersection, including edges and point segments.</summary>
    public static bool Intersects(Vector2 a, Vector2 b, Rect2 rectangle)
    {
        Validate(a);
        Validate(b);
        Validate(rectangle.Position);
        Validate(rectangle.End);
        double enter = 0;
        double exit = 1;
        var end = rectangle.End;
        return Clip(a.X, (double)b.X - a.X,
                   Math.Min(rectangle.Position.X, end.X), Math.Max(rectangle.Position.X, end.X),
                   ref enter, ref exit)
            && Clip(a.Y, (double)b.Y - a.Y,
                    Math.Min(rectangle.Position.Y, end.Y), Math.Max(rectangle.Position.Y, end.Y),
                    ref enter, ref exit);
    }

    private static bool Clip(double origin, double direction, double min, double max,
        ref double enter, ref double exit)
    {
        if (direction == 0)
            return origin >= min && origin <= max;
        double first = (min - origin) / direction;
        double last = (max - origin) / direction;
        if (first > last)
            (first, last) = (last, first);
        enter = Math.Max(enter, first);
        exit = Math.Min(exit, last);
        return enter <= exit;
    }

    private static void Validate(Vector2 point)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
            throw new ArgumentException("Routing coordinates must be finite.");
    }
}
