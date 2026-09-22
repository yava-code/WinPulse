namespace Pulse.Core.BotMark;

using System;
using System.Collections.Generic;

public enum BotMarkPersona
{
    Calm,
    Eager,
    Steady,
    Curious,
    Sleepy,
    Playful,
    Stoic,
    Proud
}

public static class BotMarkPersonaExtensions
{
    public static double Tempo(this BotMarkPersona persona) => persona switch
    {
        BotMarkPersona.Playful or BotMarkPersona.Eager => 0.85,
        BotMarkPersona.Sleepy => 1.5,
        BotMarkPersona.Stoic => 1.25,
        _ => 1.0
    };

    public static double MotionScale(this BotMarkPersona persona) => persona switch
    {
        BotMarkPersona.Playful => 1.15,
        BotMarkPersona.Eager => 1.1,
        BotMarkPersona.Sleepy => 0.8,
        BotMarkPersona.Stoic => 0.6,
        _ => 1.0
    };

    public static double GazeScale(this BotMarkPersona persona) => persona switch
    {
        BotMarkPersona.Curious or BotMarkPersona.Eager => 1.2,
        BotMarkPersona.Stoic => 0.5,
        BotMarkPersona.Sleepy => 0.7,
        _ => 1.0
    };

    public static double EyeScale(this BotMarkPersona persona) => persona switch
    {
        BotMarkPersona.Eager or BotMarkPersona.Curious => 1.06,
        BotMarkPersona.Stoic => 0.94,
        _ => 1.0
    };

    public static BotMarkPersona Automatic(int index)
    {
        var all = (BotMarkPersona[])Enum.GetValues(typeof(BotMarkPersona));
        var count = all.Length;
        return all[((index % count) + count) % count];
    }
}
