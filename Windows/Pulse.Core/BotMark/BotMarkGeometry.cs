namespace Pulse.Core.BotMark;

using System;
using System.Collections.Generic;

public static class BotMarkGeometry
{
    public static BotPoint Centroid(IReadOnlyList<BotPoint> ring)
    {
        double x = 0, y = 0;
        foreach (var p in ring)
        {
            x += p.X;
            y += p.Y;
        }
        return new BotPoint(x / ring.Count, y / ring.Count);
    }

    public static List<BotPoint> LerpRing(IReadOnlyList<BotPoint> from, IReadOnlyList<BotPoint> to, double amount)
    {
        if (from.Count != to.Count) return new List<BotPoint>(to);
        var output = new List<BotPoint>(from.Count);
        for (int i = 0; i < from.Count; i++)
        {
            output.Add(new BotPoint(
                from[i].X + (to[i].X - from[i].X) * amount,
                from[i].Y + (to[i].Y - from[i].Y) * amount
            ));
        }
        return output;
    }

    public static (double Left, double Right) SpanAt(IReadOnlyList<BotPoint> ring, double y, double headCentre)
    {
        double left = double.NegativeInfinity;
        double right = double.PositiveInfinity;

        for (int i = 0; i < ring.Count; i++)
        {
            var start = ring[i];
            var end = ring[(i + 1) % ring.Count];
            if ((start.Y <= y) == (end.Y <= y)) continue;

            var x = start.X + ((end.X - start.X) * (y - start.Y)) / (end.Y - start.Y);
            if (x <= headCentre)
            {
                left = Math.Max(left, x);
            }
            else
            {
                right = Math.Min(right, x);
            }
        }

        return (double.IsFinite(left) ? left : headCentre, double.IsFinite(right) ? right : headCentre);
    }

    public static (double Left, double Right) ShapeSpanAt(
        BotMarkShape shape,
        IReadOnlyList<(double Left, double Right)>? spanSamples,
        double y,
        double headCentre)
    {
        if (spanSamples == null || spanSamples.Count == 0)
        {
            return SpanAt(shape.Ring, y, headCentre);
        }

        double count = spanSamples.Count;
        double position = BotMath.Clamp(((y - shape.Top) / (shape.Bottom - shape.Top)) * count - 0.5, 0, count - 1);
        int start = (int)Math.Floor(position);
        int end = Math.Min(start + 1, spanSamples.Count - 1);
        double amount = position - start;

        return (
            spanSamples[start].Left + (spanSamples[end].Left - spanSamples[start].Left) * amount,
            spanSamples[start].Right + (spanSamples[end].Right - spanSamples[start].Right) * amount
        );
    }
}
