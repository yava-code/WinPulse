namespace Pulse.Core.BotMark;

using System;
using System.Collections.Generic;
using System.Linq;
using Pulse.Core.Models;

public readonly record struct BotColor(byte R, byte G, byte B, byte A = 255)
{
    public static BotColor FromRgb(uint hex) =>
        new((byte)((hex >> 16) & 0xFF), (byte)((hex >> 8) & 0xFF), (byte)(hex & 0xFF));

    public static BotColor FromHsl(double degrees, double saturation, double lightness)
    {
        var h = (degrees % 360.0 + 360.0) % 360.0 / 360.0;
        var s = Math.Clamp(saturation, 0.0, 1.0);
        var l = Math.Clamp(lightness, 0.0, 1.0);

        if (s == 0.0)
        {
            var v = (byte)(l * 255.0);
            return new BotColor(v, v, v);
        }

        var q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
        var p = 2.0 * l - q;

        return new BotColor(
            (byte)(Math.Round(HueToRgb(p, q, h + 1.0 / 3.0) * 255.0)),
            (byte)(Math.Round(HueToRgb(p, q, h) * 255.0)),
            (byte)(Math.Round(HueToRgb(p, q, h - 1.0 / 3.0) * 255.0))
        );
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0.0) t += 1.0;
        if (t > 1.0) t -= 1.0;
        if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
        return p;
    }

    public double Luminance => 0.2126 * (R / 255.0) + 0.7152 * (G / 255.0) + 0.0722 * (B / 255.0);
}

public static class BotMarkTint
{
    private const int PaletteSize = 11;
    private const double LuminanceFloor = 0.42;

    private static readonly BotColor[] Wheel = Enumerable.Range(0, PaletteSize).Select(Dealt).ToArray();
    private static readonly double[] WheelHues = Wheel.Select(HueOf).ToArray();

    public static BotColor? Brand(Provider provider) => provider switch
    {
        Provider.ClaudeCode => BotColor.FromRgb(0xD97757),
        Provider.DeepSeek => BotColor.FromRgb(0x4D6BFE),
        Provider.Volcengine => BotColor.FromRgb(0x1664FF),
        Provider.MiniMax or Provider.MiniMaxCN => BotColor.FromRgb(0xE8483F),
        Provider.Antigravity => BotColor.FromRgb(0x4285F4),
        Provider.GlmCoding => BotColor.FromRgb(0x3A7BF7),
        Provider.KimiCode => BotColor.FromRgb(0x7AA5FF),
        Provider.XiaomiMiMo => BotColor.FromRgb(0xFF6900),
        _ => null
    };

    public static BotColor Body(Provider provider)
    {
        return Deal(new[] { provider })[0];
    }

    public static IReadOnlyList<BotColor> Deal(IReadOnlyList<Provider> providers, IReadOnlyList<BotColor?>? chosen = null)
    {
        var brands = providers.Select((provider, index) =>
            (chosen != null && index < chosen.Count ? chosen[index] : null) ?? Brand(provider)
        ).ToList();

        var dealtCount = brands.Count(b => b == null);
        if (dealtCount == 0)
        {
            return brands.Select(b => Lifted(b!.Value)).ToList();
        }

        (double score, int stride, int rotation)? best = null;
        for (var stride = 1; stride < PaletteSize; stride++)
        {
            if (!Coprime(stride, PaletteSize)) continue;
            for (var rotation = 0; rotation < PaletteSize; rotation++)
            {
                var score = WorstNeighbour(brands, stride, rotation);
                if (best == null || score > best.Value.score)
                {
                    best = (score, stride, rotation);
                }
            }
        }

        var winner = best ?? (0.0, 3, 0);
        var result = new List<BotColor>(providers.Count);
        var index = 0;
        foreach (var brand in brands)
        {
            if (brand != null)
            {
                result.Add(Lifted(brand.Value));
            }
            else
            {
                var slot = (winner.rotation + winner.stride * index) % PaletteSize;
                index++;
                result.Add(Wheel[slot]);
            }
        }

        return result;
    }

    public static BotColor Eyes(BotColor body)
    {
        return body.Luminance > 0.55 ? new BotColor(15, 15, 15) : new BotColor(247, 247, 247);
    }

    private static double WorstNeighbour(IReadOnlyList<BotColor?> brands, int stride, int rotation)
    {
        var hues = new List<(double hue, bool isBrand)>();
        var index = 0;
        foreach (var brand in brands)
        {
            if (brand != null)
            {
                hues.Add((HueOf(brand.Value), true));
            }
            else
            {
                hues.Add((WheelHues[(rotation + stride * index) % PaletteSize], false));
                index++;
            }
        }

        var worst = 360.0;
        for (var pos = 0; pos < hues.Count - 1; pos++)
        {
            var first = hues[pos];
            var second = hues[pos + 1];
            if (first.isBrand && second.isBrand) continue;
            worst = Math.Min(worst, Separation(first.hue, second.hue));
        }

        return worst;
    }

    private static bool Coprime(int a, int b)
    {
        while (b != 0)
        {
            var temp = b;
            b = a % b;
            a = temp;
        }
        return a == 1;
    }

    private static double Separation(double first, double second)
    {
        var diff = Math.Abs(first - second) % 360.0;
        return Math.Min(diff, 360.0 - diff);
    }

    private static double HueOf(BotColor color)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        if (delta < 0.00001) return 0.0;

        double h;
        if (Math.Abs(max - r) < 0.00001)
        {
            h = ((g - b) / delta) % 6.0;
        }
        else if (Math.Abs(max - g) < 0.00001)
        {
            h = ((b - r) / delta) + 2.0;
        }
        else
        {
            h = ((r - g) / delta) + 4.0;
        }

        h *= 60.0;
        if (h < 0.0) h += 360.0;
        return h;
    }

    private static BotColor Dealt(int index)
    {
        var hue = (27.0 + 360.0 * (index % PaletteSize) / PaletteSize);
        return Levelled(hue, 0.68, 0.62);
    }

    private static BotColor Levelled(double hue, double saturation, double target)
    {
        var low = 0.0;
        var high = 1.0;
        var color = BotColor.FromHsl(hue, saturation, 0.5);

        for (var i = 0; i < 20; i++)
        {
            var middle = (low + high) / 2.0;
            color = BotColor.FromHsl(hue, saturation, middle);
            if (color.Luminance < target)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return color;
    }

    private static BotColor Lifted(BotColor color)
    {
        var lum = color.Luminance;
        if (lum >= LuminanceFloor || double.IsNaN(lum)) return color;

        var amount = Math.Min(1.0, (LuminanceFloor - lum) / Math.Max(1.0 - lum, 0.0001));
        return new BotColor(
            (byte)Math.Round(BotMath.Mix(color.R, 255, amount)),
            (byte)Math.Round(BotMath.Mix(color.G, 255, amount)),
            (byte)Math.Round(BotMath.Mix(color.B, 255, amount))
        );
    }
}
