namespace Pulse.Core.BotMark;

using System;

public struct BotMarkSpring
{
    public double Value;
    public double Velocity;
    public double Target;

    public BotMarkSpring(double initial)
    {
        Value = initial;
        Velocity = 0;
        Target = initial;
    }

    public void Step(double frequency, double damping, double delta)
    {
        Velocity += (-2.0 * damping * frequency * Velocity - frequency * frequency * (Value - Target)) * delta;
        Value += Velocity * delta;
        if (!double.IsFinite(Value) || !double.IsFinite(Velocity))
        {
            Value = Target;
            Velocity = 0;
        }
    }
}

public static class BotMath
{
    public const double FixedStep = 1.0 / 120.0;

    public static double Clamp(double value, double low, double high)
    {
        return Math.Min(high, Math.Max(low, value));
    }

    public static double Mix(double from, double to, double amount)
    {
        return from + (to - from) * amount;
    }

    public static double Random(double low, double high)
    {
        if (low >= high) return low;
        return low + System.Random.Shared.NextDouble() * (high - low);
    }

    public static double CubicInOut(double value)
    {
        return value < 0.5 ? 4.0 * value * value * value : 1.0 - Math.Pow(-2.0 * value + 2.0, 3) / 2.0;
    }

    public static double CubicOut(double value)
    {
        return 1.0 - Math.Pow(1.0 - value, 3);
    }

    public static double BackOut(double value)
    {
        return 1.0 + 2.70158 * Math.Pow(value - 1.0, 3) + 1.70158 * Math.Pow(value - 1.0, 2);
    }

    public static double Smoothstep(double value)
    {
        return value * value * (3.0 - 2.0 * value);
    }

    public static double UnitRemainder(double value)
    {
        var rem = value % 1.0;
        return rem < 0 ? rem + 1.0 : rem;
    }
}
