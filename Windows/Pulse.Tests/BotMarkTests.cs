namespace Pulse.Tests;

using System;
using System.Linq;
using Pulse.Core.BotMark;
using Pulse.Core.Models;
using Xunit;

public class BotMarkTests
{
    [Fact]
    public void Library_LoadsEmbeddedBotDataJson_Correctly()
    {
        var lib = BotMarkLibrary.Shared;

        Assert.NotNull(lib);
        Assert.Equal(114.27, lib.HeadCentre, precision: 1);
        Assert.True(lib.Shapes.Count >= 18, $"Expected >= 18 shapes, got {lib.Shapes.Count}");
        Assert.True(lib.Shapes.ContainsKey("blob"), "Should contain 'blob' shape");
        Assert.True(lib.Shapes.ContainsKey("squircle"), "Should contain 'squircle' shape");
        Assert.True(lib.Shapes.ContainsKey("tablet"), "Should contain 'tablet' shape");

        Assert.True(lib.Expressions.Count >= 25, $"Expected >= 25 expressions, got {lib.Expressions.Count}");
        Assert.True(lib.States.Count >= 39, $"Expected >= 39 states, got {lib.States.Count}");

        // Each shape ring should be a 96-point polygon
        var blob = lib.GetShape("blob");
        Assert.Equal(96, blob.Ring.Count);

        // Each expression should have 2 eyes with 48 points each
        var expr0 = lib.Expressions[0];
        Assert.Equal(2, expr0.Count);
        Assert.Equal(48, expr0[0].Count);
        Assert.Equal(48, expr0[1].Count);
    }

    [Fact]
    public void Engine_StepsPhysicsAndRendersValidVectorFrame()
    {
        var lib = BotMarkLibrary.Shared;
        var engine = new BotMarkEngine(lib);
        var shape = lib.GetShape("blob");

        // Initial step
        engine.Step(0.016);
        var frame = engine.RenderFrame(shape);

        Assert.NotNull(frame);
        Assert.Equal(96, frame.HeadRing.Count);
        Assert.Equal(48, frame.LeftEye.Count);
        Assert.Equal(48, frame.RightEye.Count);
        Assert.True(frame.EyesVisible);

        // Test pointer tracking alters eye aim
        engine.PointerX = 1.0;
        engine.PointerY = 0.5;
        engine.HasPointer = true;

        for (int i = 0; i < 30; i++)
        {
            engine.Step(0.016);
        }

        var gazeFrame = engine.RenderFrame(shape);
        Assert.NotNull(gazeFrame);
        // Left eye should have moved rightwards with positive PointerX
        Assert.True(engine.AimX.Value > 5.0, $"Expected AimX > 5.0, got {engine.AimX.Value}");
    }

    [Fact]
    public void Tint_DealsHarmoniousColorsAndEyeContrast()
    {
        var providers = new[]
        {
            Provider.ClaudeCode,
            Provider.Codex,
            Provider.Cursor,
            Provider.Antigravity,
            Provider.Copilot
        };

        var tints = BotMarkTint.Deal(providers);
        Assert.Equal(5, tints.Count);

        // Claude Code has fixed brand color
        var claudeBrand = BotMarkTint.Brand(Provider.ClaudeCode);
        Assert.NotNull(claudeBrand);
        Assert.Equal(0xD9, claudeBrand.Value.R);
        Assert.Equal(0x77, claudeBrand.Value.G);
        Assert.Equal(0x57, claudeBrand.Value.B);

        // Antigravity has fixed brand color
        var agBrand = BotMarkTint.Brand(Provider.Antigravity);
        Assert.NotNull(agBrand);
        Assert.Equal(0x42, agBrand.Value.R);
        Assert.Equal(0x85, agBrand.Value.G);
        Assert.Equal(0xF4, agBrand.Value.B);

        // Eye contrast test
        var darkBody = new BotColor(20, 20, 30);
        var lightEyes = BotMarkTint.Eyes(darkBody);
        Assert.True(lightEyes.R > 200, "Dark body should receive light eyes for contrast");

        var lightBody = new BotColor(240, 240, 250);
        var darkEyes = BotMarkTint.Eyes(lightBody);
        Assert.True(darkEyes.R < 50, "Light body should receive dark eyes for contrast");
    }
}
