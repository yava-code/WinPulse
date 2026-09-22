namespace Pulse.Windows.Controls;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using Pulse.Core.BotMark;

/// <summary>
/// High-performance vector mascot control rendering the animated 1:1 BotMark face
/// with spring-damper kinematics, natural blinking, eye expression morphing,
/// idle breathing bob/squash, pointer gaze tracking, and 5 mood states.
/// </summary>
public class BotMarkControl : FrameworkElement
{
    private readonly BotMarkLibrary _library;
    private readonly BotMarkEngine _engine;
    private readonly Stopwatch _stopwatch = new();
    private double _lastTime;
    private bool _isRendering;

    public static readonly DependencyProperty BodyBrushProperty =
        DependencyProperty.Register(nameof(BodyBrush), typeof(Brush), typeof(BotMarkControl),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x53)), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EyeBrushProperty =
        DependencyProperty.Register(nameof(EyeBrush), typeof(Brush), typeof(BotMarkControl),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MoodProperty =
        DependencyProperty.Register(nameof(Mood), typeof(BotMarkMood), typeof(BotMarkControl),
            new FrameworkPropertyMetadata(BotMarkMood.Idle, OnMoodChanged));

    public static readonly DependencyProperty ShapeNameProperty =
        DependencyProperty.Register(nameof(ShapeName), typeof(string), typeof(BotMarkControl),
            new FrameworkPropertyMetadata("blob", OnShapeChanged));

    public static readonly DependencyProperty PersonaProperty =
        DependencyProperty.Register(nameof(Persona), typeof(BotMarkPersona), typeof(BotMarkControl),
            new FrameworkPropertyMetadata(BotMarkPersona.Calm, OnPersonaChanged));

    public static readonly DependencyProperty LeanBiasProperty =
        DependencyProperty.Register(nameof(LeanBias), typeof(double), typeof(BotMarkControl),
            new FrameworkPropertyMetadata(0.0, OnLeanBiasChanged));

    public Brush BodyBrush
    {
        get => (Brush)GetValue(BodyBrushProperty);
        set => SetValue(BodyBrushProperty, value);
    }

    public Brush EyeBrush
    {
        get => (Brush)GetValue(EyeBrushProperty);
        set => SetValue(EyeBrushProperty, value);
    }

    public BotMarkMood Mood
    {
        get => (BotMarkMood)GetValue(MoodProperty);
        set => SetValue(MoodProperty, value);
    }

    public string ShapeName
    {
        get => (string)GetValue(ShapeNameProperty);
        set => SetValue(ShapeNameProperty, value);
    }

    public BotMarkPersona Persona
    {
        get => (BotMarkPersona)GetValue(PersonaProperty);
        set => SetValue(PersonaProperty, value);
    }

    public double LeanBias
    {
        get => (double)GetValue(LeanBiasProperty);
        set => SetValue(LeanBiasProperty, value);
    }

    public BotMarkControl()
    {
        _library = BotMarkLibrary.Shared;
        _engine = new BotMarkEngine(_library);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private static void OnMoodChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BotMarkControl control && e.NewValue is BotMarkMood mood)
        {
            control._engine.SetMood(mood);
        }
    }

    private static void OnShapeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BotMarkControl control)
        {
            control.InvalidateVisual();
        }
    }

    private static void OnPersonaChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BotMarkControl control && e.NewValue is BotMarkPersona persona)
        {
            control._engine.EyeScale.Target = persona.EyeScale();
        }
    }

    private static void OnLeanBiasChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BotMarkControl control && e.NewValue is double bias)
        {
            control._engine.LeanBias = bias;
        }
    }

    public void SetPointer(double px, double py)
    {
        _engine.PointerX = Math.Clamp(px, -1.0, 1.0);
        _engine.PointerY = Math.Clamp(py, -1.0, 1.0);
        _engine.HasPointer = true;
    }

    public void ClearPointer()
    {
        _engine.HasPointer = false;
    }

    public void SetColors(Color bodyColor, Color? eyeColor = null)
    {
        var bodyBrush = new SolidColorBrush(bodyColor);
        bodyBrush.Freeze();
        BodyBrush = bodyBrush;

        if (eyeColor.HasValue)
        {
            var eyeBrush = new SolidColorBrush(eyeColor.Value);
            eyeBrush.Freeze();
            EyeBrush = eyeBrush;
        }
        else
        {
            var lum = 0.2126 * (bodyColor.R / 255.0) + 0.7152 * (bodyColor.G / 255.0) + 0.0722 * (bodyColor.B / 255.0);
            var ec = lum > 0.55 ? Color.FromRgb(15, 15, 15) : Color.FromRgb(247, 247, 247);
            var eyeBrush = new SolidColorBrush(ec);
            eyeBrush.Freeze();
            EyeBrush = eyeBrush;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        StartAnimation();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopAnimation();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            StartAnimation();
        }
        else
        {
            StopAnimation();
        }
    }

    private void StartAnimation()
    {
        if (_isRendering) return;
        _isRendering = true;
        _stopwatch.Restart();
        _lastTime = 0;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopAnimation()
    {
        if (!_isRendering) return;
        _isRendering = false;
        _stopwatch.Stop();
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!IsVisible) return;

        var now = _stopwatch.Elapsed.TotalSeconds;
        var dt = now - _lastTime;
        _lastTime = now;

        if (dt <= 0.0001) return;
        if (dt > 0.1) dt = 0.1;

        _engine.Step(dt);
        InvalidateVisual();
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var pos = e.GetPosition(this);
        var halfW = ActualWidth / 2.0;
        var halfH = ActualHeight / 2.0;
        if (halfW > 0 && halfH > 0)
        {
            _engine.PointerX = Math.Clamp((pos.X - halfW) / halfW, -1.0, 1.0);
            _engine.PointerY = Math.Clamp((pos.Y - halfH) / halfH, -1.0, 1.0);
            _engine.HasPointer = true;
        }
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _engine.HasPointer = false;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 2 || height < 2) return;

        var shape = _library.GetShape(ShapeName);
        var frame = _engine.RenderFrame(shape);

        var extent = Math.Min(width, height);
        var scale = extent / (frame.ViewBoxRadius * 2.0);
        var origin = _library.HeadCentre - frame.ViewBoxRadius;
        var offsetX = (width - extent) / 2.0;
        var offsetY = (height - extent) / 2.0;

        var headGeom = BuildGeometry(frame.HeadRing, origin, scale, offsetX, offsetY);
        dc.DrawGeometry(BodyBrush, null, headGeom);

        if (frame.EyesVisible && frame.LeftEye.Count > 0 && frame.RightEye.Count > 0)
        {
            dc.PushClip(headGeom);
            var leftEyeGeom = BuildGeometry(frame.LeftEye, origin, scale, offsetX, offsetY);
            var rightEyeGeom = BuildGeometry(frame.RightEye, origin, scale, offsetX, offsetY);
            dc.DrawGeometry(EyeBrush, null, leftEyeGeom);
            dc.DrawGeometry(EyeBrush, null, rightEyeGeom);
            dc.Pop();
        }
    }

    private static StreamGeometry BuildGeometry(
        IReadOnlyList<BotPoint> points,
        double origin,
        double scale,
        double offsetX,
        double offsetY)
    {
        var geometry = new StreamGeometry();
        if (points == null || points.Count == 0) return geometry;

        using (var ctx = geometry.Open())
        {
            var p0 = points[0];
            var start = new Point(offsetX + (p0.X - origin) * scale, offsetY + (p0.Y - origin) * scale);
            ctx.BeginFigure(start, isFilled: true, isClosed: true);

            var count = points.Count;
            for (var i = 1; i < count; i++)
            {
                var pt = points[i];
                ctx.LineTo(new Point(offsetX + (pt.X - origin) * scale, offsetY + (pt.Y - origin) * scale), isStroked: false, isSmoothJoin: true);
            }
        }

        geometry.Freeze();
        return geometry;
    }
}
