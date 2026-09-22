namespace Pulse.Core.BotMark;

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

public readonly record struct BotPoint(double X, double Y);

public sealed record BotMarkFace(double X, double Y, double Sx, double Sy, double Eye)
{
    public static BotMarkFace Blend(BotMarkFace from, BotMarkFace to, double amount) => new(
        BotMath.Mix(from.X, to.X, amount),
        BotMath.Mix(from.Y, to.Y, amount),
        BotMath.Mix(from.Sx, to.Sx, amount),
        BotMath.Mix(from.Sy, to.Sy, amount),
        BotMath.Mix(from.Eye, to.Eye, amount)
    );
}

public sealed record BotMarkShape(
    string Id,
    IReadOnlyList<BotPoint> Ring,
    BotMarkFace Face,
    double Radius,
    double Top,
    double Bottom,
    IReadOnlyList<(double Left, double Right)>? SpanSamples
);

public sealed record BotMarkStateInfo(
    string Id,
    string? Morph,
    (double Min, double Max)? BlinkCadence,
    (double Min, double Max) ExpressionCadence,
    IReadOnlyList<int> ExpressionPool
);

public sealed class BotMarkLibrary
{
    public double HeadCentre { get; }
    public double EyeHalf { get; }
    public IReadOnlyList<BotPoint> CircleRing { get; }
    public IReadOnlyDictionary<string, BotMarkShape> Shapes { get; }
    public IReadOnlyList<IReadOnlyList<IReadOnlyList<BotPoint>>> Expressions { get; }
    public IReadOnlyList<BotMarkStateInfo> States { get; }

    private static BotMarkLibrary? _shared;
    public static BotMarkLibrary Shared => _shared ??= LoadFromResource();

    public BotMarkLibrary(
        double headCentre,
        double eyeHalf,
        IReadOnlyList<BotPoint> circleRing,
        IReadOnlyDictionary<string, BotMarkShape> shapes,
        IReadOnlyList<IReadOnlyList<IReadOnlyList<BotPoint>>> expressions,
        IReadOnlyList<BotMarkStateInfo> states)
    {
        HeadCentre = headCentre;
        EyeHalf = eyeHalf;
        CircleRing = circleRing;
        Shapes = shapes;
        Expressions = expressions;
        States = states;
    }

    public BotMarkShape GetShape(string? id)
    {
        if (id != null && Shapes.TryGetValue(id, out var s)) return s;
        if (Shapes.TryGetValue("blob", out var blob)) return blob;
        return Shapes.Values.First();
    }

    public BotMarkStateInfo GetState(string? id)
    {
        return States.FirstOrDefault(s => s.Id == id) ?? States[0];
    }

    public static BotMarkLibrary LoadFromResource()
    {
        var asm = typeof(BotMarkLibrary).Assembly;
        using var stream = asm.GetManifestResourceStream("Pulse.Core.Resources.bot-data.json")
            ?? asm.GetManifestResourceStream("bot-data.json");

        if (stream == null)
        {
            // Fallback to reading file from disk
            var candidatePaths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Resources", "bot-data.json"),
                Path.Combine(AppContext.BaseDirectory, "bot-data.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "Windows", "Pulse.Core", "Resources", "bot-data.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "Sources", "Pulse", "Resources", "bot-data.json")
            };

            foreach (var p in candidatePaths)
            {
                if (File.Exists(p))
                {
                    using var fs = File.OpenRead(p);
                    return LoadFromStream(fs);
                }
            }

            throw new InvalidOperationException("bot-data.json resource not found");
        }

        return LoadFromStream(stream);
    }

    public static BotMarkLibrary LoadFromStream(Stream stream)
    {
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var headCentre = root.GetProperty("headC").GetDouble();
        var eyeHalf = root.GetProperty("eyeHalf").GetDouble();

        var circleRing = new List<BotPoint>();
        foreach (var pt in root.GetProperty("circleRing").EnumerateArray())
        {
            circleRing.Add(new BotPoint(pt[0].GetDouble(), pt[1].GetDouble()));
        }

        var shapes = new Dictionary<string, BotMarkShape>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in root.GetProperty("shapes").EnumerateObject())
        {
            var shapeEl = prop.Value;
            var ring = new List<BotPoint>();
            foreach (var pt in shapeEl.GetProperty("ring").EnumerateArray())
            {
                ring.Add(new BotPoint(pt[0].GetDouble(), pt[1].GetDouble()));
            }

            var fEl = shapeEl.GetProperty("face");
            var face = new BotMarkFace(
                fEl.GetProperty("x").GetDouble(),
                fEl.GetProperty("y").GetDouble(),
                fEl.GetProperty("sx").GetDouble(),
                fEl.GetProperty("sy").GetDouble(),
                fEl.GetProperty("eye").GetDouble()
            );

            var radius = shapeEl.GetProperty("radius").GetDouble();
            var top = shapeEl.GetProperty("top").GetDouble();
            var bottom = shapeEl.GetProperty("bottom").GetDouble();

            List<(double Left, double Right)>? spanSamples = null;
            if (shapeEl.TryGetProperty("spanSamples", out var ssEl) && ssEl.ValueKind == JsonValueKind.Array)
            {
                spanSamples = new List<(double, double)>();
                foreach (var s in ssEl.EnumerateArray())
                {
                    spanSamples.Add((s[0].GetDouble(), s[1].GetDouble()));
                }
            }

            shapes[prop.Name] = new BotMarkShape(prop.Name, ring, face, radius, top, bottom, spanSamples);
        }

        var expressions = new List<IReadOnlyList<IReadOnlyList<BotPoint>>>();
        foreach (var expr in root.GetProperty("expressions").EnumerateArray())
        {
            var eyeList = new List<IReadOnlyList<BotPoint>>();
            foreach (var eye in expr.EnumerateArray())
            {
                var eyeRing = new List<BotPoint>();
                foreach (var pt in eye.EnumerateArray())
                {
                    eyeRing.Add(new BotPoint(pt[0].GetDouble(), pt[1].GetDouble()));
                }
                eyeList.Add(eyeRing);
            }
            expressions.Add(eyeList);
        }

        var states = new List<BotMarkStateInfo>();
        foreach (var st in root.GetProperty("states").EnumerateArray())
        {
            var id = st.GetProperty("id").GetString()!;
            string? morph = st.TryGetProperty("morph", out var mProp) && mProp.ValueKind == JsonValueKind.String ? mProp.GetString() : null;

            (double, double)? blinkCadence = null;
            if (st.TryGetProperty("blinkCadence", out var bcProp) && bcProp.ValueKind == JsonValueKind.Array)
            {
                blinkCadence = (bcProp[0].GetDouble(), bcProp[1].GetDouble());
            }

            (double, double) exprCadence = (2000, 5000);
            if (st.TryGetProperty("expressionCadence", out var ecProp) && ecProp.ValueKind == JsonValueKind.Array)
            {
                exprCadence = (ecProp[0].GetDouble(), ecProp[1].GetDouble());
            }

            var pool = new List<int>();
            if (st.TryGetProperty("expressionPool", out var epProp) && epProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var idx in epProp.EnumerateArray())
                {
                    pool.Add(idx.GetInt32());
                }
            }

            states.Add(new BotMarkStateInfo(id, morph, blinkCadence, exprCadence, pool));
        }

        return new BotMarkLibrary(headCentre, eyeHalf, circleRing, shapes, expressions, states);
    }
}
