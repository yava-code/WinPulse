namespace Pulse.Core.BotMark;

using System;
using System.Collections.Generic;

public enum BotMarkMood
{
    Idle,
    Working,
    Fetching,
    Spent,
    Unavailable
}

public sealed record BotMarkFrame(
    IReadOnlyList<BotPoint> HeadRing,
    IReadOnlyList<BotPoint> LeftEye,
    IReadOnlyList<BotPoint> RightEye,
    bool EyesVisible,
    double Rotation,
    double ScaleY,
    double HeadX,
    double HeadY,
    double ViewBoxRadius = 129.5
);

public sealed class BotMarkEngine
{
    private readonly BotMarkLibrary _library;
    private double HeadCentre => _library.HeadCentre;

    public string State { get; private set; } = "idle";
    public BotMarkMood Mood { get; private set; } = BotMarkMood.Idle;

    private double _clockTime; // in milliseconds
    private double _blinkNext;
    private double _expressionNext;
    private int _expressionIndex;
    private IReadOnlyList<IReadOnlyList<BotPoint>> _exprFrom;
    private IReadOnlyList<IReadOnlyList<BotPoint>> _exprTo;

    // Springs
    public BotMarkSpring HeadX = new(0);
    public BotMarkSpring HeadY = new(0);
    public BotMarkSpring ScaleY = new(1);
    public BotMarkSpring Rotation = new(0);
    public BotMarkSpring AimX = new(0);
    public BotMarkSpring AimY = new(0);
    public BotMarkSpring Facing = new(1);
    public BotMarkSpring EyeOpen = new(1);
    public BotMarkSpring EyeScale = new(1);
    public BotMarkSpring ExpressionSpring = new(1);

    // Gaze / pointer target (-1..1 unit coordinates)
    public double PointerX { get; set; }
    public double PointerY { get; set; }
    public bool HasPointer { get; set; }
    public double LeanBias { get; set; } = 0; // -1 for left dock, +1 for right dock

    public BotMarkEngine(BotMarkLibrary? library = null)
    {
        _library = library ?? BotMarkLibrary.Shared;
        _exprFrom = _library.Expressions[0];
        _exprTo = _library.Expressions[0];
        _expressionIndex = 0;
        SetMood(BotMarkMood.Idle);
    }

    public void SetMood(BotMarkMood mood)
    {
        Mood = mood;
        var stateId = mood switch
        {
            BotMarkMood.Working => "working",
            BotMarkMood.Fetching => "searching",
            BotMarkMood.Spent => "sad",
            BotMarkMood.Unavailable => "confused",
            _ => "idle"
        };
        SetState(stateId);
    }

    public void SetState(string stateId)
    {
        State = stateId;
        var info = _library.GetState(stateId);

        // Schedule first blink & expression change
        var tempo = Mood == BotMarkMood.Working ? 0.5 : (Mood == BotMarkMood.Fetching ? 0.7 : 1.0);
        if (info.BlinkCadence.HasValue)
        {
            _blinkNext = _clockTime + BotMath.Random(info.BlinkCadence.Value.Min, info.BlinkCadence.Value.Max) * tempo;
        }

        _expressionNext = _clockTime + BotMath.Random(info.ExpressionCadence.Min, info.ExpressionCadence.Max) * tempo;

        if (info.ExpressionPool.Count > 0)
        {
            var nextIdx = info.ExpressionPool[System.Random.Shared.Next(info.ExpressionPool.Count)];
            TriggerExpression(nextIdx);
        }
    }

    public void TriggerExpression(int index)
    {
        if (index < 0 || index >= _library.Expressions.Count) return;
        _exprFrom = _exprTo;
        _exprTo = _library.Expressions[index];
        _expressionIndex = index;
        ExpressionSpring.Value = 0;
        ExpressionSpring.Target = 1;
    }

    public void Step(double deltaSeconds)
    {
        if (deltaSeconds <= 0) return;
        if (deltaSeconds > 0.1) deltaSeconds = 0.1; // clamp to prevent explosive physics

        _clockTime += deltaSeconds * 1000.0;

        var tempo = Mood == BotMarkMood.Working ? 0.5 : (Mood == BotMarkMood.Fetching ? 0.7 : 1.0);
        var rotEmphasis = Mood == BotMarkMood.Working ? 2.4 : (Mood == BotMarkMood.Fetching ? 1.6 : 1.0);
        var squashEmphasis = Mood == BotMarkMood.Working ? 3.0 : (Mood == BotMarkMood.Fetching ? 2.0 : 1.0);

        // 1. Natural blinking cadence (ensure all moods blink, even if state cadence is null)
        var stateInfo = _library.GetState(State);
        var blinkRange = stateInfo.BlinkCadence ?? (3500.0, 7500.0);
        if (_clockTime >= _blinkNext)
        {
            EyeOpen.Target = 0.0; // close eyelid
            _blinkNext = _clockTime + BotMath.Random(blinkRange.Min, blinkRange.Max) * tempo;
        }
        else if (EyeOpen.Value < 0.1)
        {
            EyeOpen.Target = 1.0; // reopen eyelid
        }

        // 2. Expression cadence
        if (_clockTime >= _expressionNext)
        {
            _expressionNext = _clockTime + BotMath.Random(stateInfo.ExpressionCadence.Min, stateInfo.ExpressionCadence.Max) * tempo;
            if (stateInfo.ExpressionPool.Count > 0)
            {
                var nextIdx = stateInfo.ExpressionPool[System.Random.Shared.Next(stateInfo.ExpressionPool.Count)];
                TriggerExpression(nextIdx);
            }
        }

        // 3. Motion & Breathing Formulas
        var timeSec = _clockTime * 0.001;
        switch (Mood)
        {
            case BotMarkMood.Working:
            {
                // Active rhythmic bouncing
                var bounce = Math.Sin(timeSec * 8.0) * 2.5;
                var rock = Math.Sin(timeSec * 4.0) * 0.08 * rotEmphasis;
                var squash = Math.Cos(timeSec * 8.0) * 0.04 * squashEmphasis;

                HeadY.Target = bounce;
                Rotation.Target = rock;
                ScaleY.Target = 1.0 + squash;
                EyeScale.Target = 1.15; // wide alert eyes
                break;
            }
            case BotMarkMood.Fetching:
            {
                // Searching side to side
                var pan = Math.Sin(timeSec * 3.0) * 6.0;
                var tilt = Math.Cos(timeSec * 3.0) * 0.05 * rotEmphasis;
                HeadX.Target = pan;
                Rotation.Target = tilt;
                HeadY.Target = Math.Sin(timeSec * 4.0) * 1.0;
                ScaleY.Target = 1.0;
                break;
            }
            case BotMarkMood.Spent:
            {
                // Slow exhausted breathing and drooping tired sway
                var slowBob = Math.Sin(timeSec * 0.9) * 0.8;
                var slowSquash = Math.Cos(timeSec * 0.9) * 0.025 * squashEmphasis;
                var slowSway = Math.Sin(timeSec * 0.45) * 0.025 * rotEmphasis;

                HeadY.Target = 3.6 + slowBob;
                ScaleY.Target = 0.93 + slowSquash;
                Rotation.Target = 0.03 + slowSway;
                EyeScale.Target = 0.88;
                break;
            }
            case BotMarkMood.Unavailable:
            {
                // Puzzled head tilt with subtle floating drift
                var floatY = Math.Sin(timeSec * 1.3) * 0.9;
                var tiltWobble = Math.Cos(timeSec * 1.3) * 0.03 * rotEmphasis;
                var breath = Math.Cos(timeSec * 1.8) * 0.015 * squashEmphasis;

                Rotation.Target = 0.14 + tiltWobble;
                HeadX.Target = 1.5 + Math.Sin(timeSec * 0.7) * 0.6;
                HeadY.Target = -1.0 + floatY;
                ScaleY.Target = 0.98 + breath;
                EyeScale.Target = 0.96;
                break;
            }
            default: // Idle
            {
                // Gentle organic breathing
                var bob = Math.Sin(timeSec * 1.5) * 1.2;
                var breath = Math.Cos(timeSec * 2.0) * 0.015 * squashEmphasis;
                var slightRock = Math.Sin(timeSec * 0.8) * 0.02;

                HeadY.Target = bob;
                ScaleY.Target = 1.0 + breath;
                Rotation.Target = slightRock;
                EyeScale.Target = 1.0;
                break;
            }
        }

        // 4. Gaze & pointer tracking
        if (HasPointer)
        {
            AimX.Target = BotMath.Clamp(PointerX * 18.0, -22.0, 22.0);
            AimY.Target = BotMath.Clamp(PointerY * 14.0, -16.0, 16.0);
            Facing.Target = PointerX >= 0 ? 1.0 : -1.0;
        }
        else
        {
            // Lean bias (facing into screen away from dock edge)
            AimX.Target = LeanBias * 7.0;
            AimY.Target = 0.0;
            Facing.Target = LeanBias >= 0 ? 1.0 : -1.0;
        }

        // Substep physics integration at 120Hz
        var remaining = deltaSeconds;
        while (remaining > 0)
        {
            var dt = Math.Min(remaining, BotMath.FixedStep);
            remaining -= dt;

            HeadX.Step(12.0, 0.75, dt);
            HeadY.Step(12.0, 0.75, dt);
            ScaleY.Step(10.0, 0.7, dt);
            Rotation.Step(8.0, 0.8, dt);
            AimX.Step(9.0, 0.75, dt);
            AimY.Step(9.0, 0.75, dt);
            Facing.Step(6.0, 0.85, dt);
            EyeOpen.Step(24.0, 0.8, dt);
            EyeScale.Step(12.0, 0.8, dt);
            ExpressionSpring.Step(7.0, 0.8, dt);
        }
    }

    public BotMarkFrame RenderFrame(BotMarkShape shape)
    {
        // 1. Interpolate eye expressions
        var exprAmount = BotMath.Clamp(ExpressionSpring.Value, 0.0, 1.0);
        var leftRaw = BotMarkGeometry.LerpRing(_exprFrom[0], _exprTo[0], exprAmount);
        var rightRaw = BotMarkGeometry.LerpRing(_exprFrom[1], _exprTo[1], exprAmount);

        // 2. Transform head ring (breathing, bob, rotation)
        var headRing = new List<BotPoint>(shape.Ring.Count);
        var rot = Rotation.Value;
        var cos = Math.Cos(rot);
        var sin = Math.Sin(rot);
        var hx = HeadX.Value;
        var hy = HeadY.Value;
        var sy = ScaleY.Value;
        var hc = HeadCentre;

        foreach (var pt in shape.Ring)
        {
            var relX = pt.X - hc;
            var relY = (pt.Y - hc) * sy;
            var rotX = relX * cos - relY * sin + hc + hx;
            var rotY = relX * sin + relY * cos + hc + hy;
            headRing.Add(new BotPoint(rotX, rotY));
        }

        // 3. Transform eye rings with gaze offset, eyeOpen squash, and eyeScale
        var aimX = AimX.Value;
        var aimY = AimY.Value;
        var eyeOpen = BotMath.Clamp(EyeOpen.Value, 0.0, 1.0);
        var eyeScale = EyeScale.Value;
        var face = shape.Face;

        var leftEye = TransformEye(leftRaw, hc, face, aimX, aimY, eyeOpen, eyeScale, cos, sin, hx, hy);
        var rightEye = TransformEye(rightRaw, hc, face, aimX, aimY, eyeOpen, eyeScale, cos, sin, hx, hy);

        return new BotMarkFrame(
            headRing,
            leftEye,
            rightEye,
            eyeOpen > 0.05,
            rot,
            sy,
            hx,
            hy
        );
    }

    private static List<BotPoint> TransformEye(
        IReadOnlyList<BotPoint> rawRing,
        double hc,
        BotMarkFace face,
        double aimX,
        double aimY,
        double eyeOpen,
        double eyeScale,
        double cos,
        double sin,
        double hx,
        double hy)
    {
        var output = new List<BotPoint>(rawRing.Count);
        var eyeCentroid = BotMarkGeometry.Centroid(rawRing);

        foreach (var pt in rawRing)
        {
            // Scale around eye centroid
            var localX = (pt.X - eyeCentroid.X) * eyeScale;
            var localY = (pt.Y - eyeCentroid.Y) * eyeScale * eyeOpen;

            // Position in face unit space
            var eyeX = (eyeCentroid.X + localX - hc) * face.Sx + face.X + aimX;
            var eyeY = (eyeCentroid.Y + localY - hc) * face.Sy + face.Y + aimY;

            // Rotate with head
            var rotX = eyeX * cos - eyeY * sin + hc + hx;
            var rotY = eyeX * sin + eyeY * cos + hc + hy;

            output.Add(new BotPoint(rotX, rotY));
        }

        return output;
    }
}
