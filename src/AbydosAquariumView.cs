// Original HoshiBar code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DIsland.Interop;
using DIsland.Services;

namespace DIsland;

/// <summary>
/// Abydos Mini-Aquarium idle capsule: Hoshino sits on the left watching a glowing vector blue whale
/// swim sinusoidally through deep water with soft caustic light and rising bubbles.
/// </summary>
public sealed class AbydosAquariumView : Grid, IDisposable
{
    public const double CapsuleWidth = 248;
    public const double CapsuleHeight = 40;

    private const double SwimMinX = 50;
    private const double SwimMaxX = 206;

    private readonly AppSettings settings;
    private readonly HoshinoCompanion companion;
    private readonly Canvas tank = new() { Width = CapsuleWidth, Height = CapsuleHeight, ClipToBounds = true, IsHitTestVisible = false };
    private readonly TranslateTransform causticShiftA = new();
    private readonly TranslateTransform causticShiftB = new();
    private readonly TranslateTransform whaleMove = new(130, 10);
    private readonly ScaleTransform whaleFlip = new(-1, 1, 16, 10);
    private readonly RotateTransform whaleTilt = new(0, 16, 10);
    private readonly RotateTransform tailWag = new(0, 5.5, 11.4);
    private readonly RotateTransform finPaddle = new(0, 18.5, 13.8);
    private readonly Path spoutPath;
    private readonly (Ellipse Dot, TranslateTransform Move, double BaseX, double Speed, double Phase)[] bubbles;
    private readonly DispatcherTimer timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch clock = Stopwatch.StartNew();

    private double lastTime;
    private double whaleX = 130;
    private double whaleY = 10;
    private double facing = -1;
    private bool active;
    private bool suspended;

    public AbydosAquariumView(
        AppSettings settings,
        Func<bool>? playing = null,
        Func<bool>? listening = null,
        Func<bool>? working = null,
        Func<bool>? focusing = null,
        Func<WeatherReport?>? weather = null)
    {
        this.settings = settings;
        Width = CapsuleWidth;
        Height = CapsuleHeight;
        ClipToBounds = true;
        ToolTip = "Мини-океанариум Абидоса";

        // Deep-sea backdrop harmonized with HoshinoPalette
        var deepBack = new Border
        {
            CornerRadius = new CornerRadius(20),
            IsHitTestVisible = false,
            Background = new LinearGradientBrush(
                Color.FromArgb(0xDC, 0x0B, 0x17, 0x28),
                Color.FromArgb(0xEC, 0x11, 0x24, 0x3B),
                new Point(0, 0),
                new Point(1, 1)),
            BorderThickness = new Thickness(0.8),
        };
        deepBack.SetResourceReference(Border.BorderBrushProperty, "HoshinoBorder");
        Children.Add(deepBack);

        // Soft caustic water ribbons along the top surface
        var skyWave = Frozen(Color.FromArgb(0x28, 0x83, 0xCF, 0xF3));
        var pinkWave = Frozen(Color.FromArgb(0x1E, 0xEA, 0xB0, 0xCF));
        var waveA = new Path
        {
            Data = Geometry.Parse("M -40,0 H 290 V 10 C 245,15 205,4 160,9 C 115,14 70,4 25,9 C -5,12 -25,6 -40,9 Z"),
            Fill = skyWave,
            RenderTransform = causticShiftA,
        };
        var waveB = new Path
        {
            Data = Geometry.Parse("M -30,0 H 285 V 7 C 235,3 190,13 140,7 C 95,2 50,12 5,6 C -15,4 -25,7 -30,7 Z"),
            Fill = pinkWave,
            RenderTransform = causticShiftB,
        };
        tank.Children.Add(waveA);
        tank.Children.Add(waveB);

        // Rising bubbles
        var bubbleStroke = Frozen(Color.FromArgb(0x88, 0x83, 0xCF, 0xF3));
        var bubbleFill = Frozen(Color.FromArgb(0x26, 0xF4, 0xED, 0xF6));
        var specs = new (double X, double Size, double Speed, double Phase)[]
        {
            (64, 3.0, 0.28, 0.05),
            (98, 4.2, 0.22, 0.37),
            (136, 2.6, 0.31, 0.68),
            (172, 3.8, 0.25, 0.19),
            (206, 3.2, 0.29, 0.52),
            (230, 2.5, 0.34, 0.83),
        };
        bubbles = new (Ellipse, TranslateTransform, double, double, double)[specs.Length];
        for (var i = 0; i < specs.Length; i++)
        {
            var s = specs[i];
            var move = new TranslateTransform(s.X, 20);
            var dot = new Ellipse
            {
                Width = s.Size,
                Height = s.Size,
                Stroke = bubbleStroke,
                StrokeThickness = 0.8,
                Fill = bubbleFill,
                RenderTransform = move,
            };
            tank.Children.Add(dot);
            bubbles[i] = (dot, move, s.X, s.Speed, s.Phase);
        }

        // Glowing chibi blue whale (#83CFF3 body, #F4EDF6 belly, #EAB0CF blush)
        var whaleCanvas = BuildWhale(out spoutPath);
        var whaleTransforms = new TransformGroup();
        whaleTransforms.Children.Add(whaleFlip);
        whaleTransforms.Children.Add(whaleTilt);
        whaleTransforms.Children.Add(whaleMove);
        whaleCanvas.RenderTransform = whaleTransforms;
        tank.Children.Add(whaleCanvas);
        Children.Add(tank);

        // Hoshino seated on the left of the capsule, tracking the whale while cursor is outside
        companion = new HoshinoCompanion(settings)
        {
            Width = 34,
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 0, 0),
            IsHitTestVisible = false,
            Playing = playing,
            Listening = listening,
            Working = working,
            Focusing = focusing,
            Weather = weather,
            GazeTarget = ComputeWhaleGaze,
        };
        Children.Add(companion);

        timer.Tick += (_, _) => StepFrame();
        ApplyStaticPose();
    }

    private Canvas BuildWhale(out Path spout)
    {
        var sky = Frozen(Color.FromRgb(0x83, 0xCF, 0xF3));
        var deepFin = Frozen(Color.FromRgb(0x5E, 0xB5, 0xE2));
        var belly = Frozen(Color.FromRgb(0xF4, 0xED, 0xF6));
        var blush = Frozen(Color.FromRgb(0xEA, 0xB0, 0xCF));
        var eyeDark = Frozen(Color.FromRgb(0x18, 0x14, 0x1F));
        var auraBrush = Frozen(Color.FromArgb(0x36, 0x83, 0xCF, 0xF3));

        var c = new Canvas { Width = 32, Height = 20, IsHitTestVisible = false };

        // Soft bioluminescent glow behind the whale
        var aura = new Ellipse { Width = 30, Height = 16, Fill = auraBrush };
        Canvas.SetLeft(aura, 2);
        Canvas.SetTop(aura, 2.5);
        c.Children.Add(aura);

        // Wagging forked tail fluke
        var tail = new Path
        {
            Data = Geometry.Parse("M 6.2,11.3 C 3.6,8.4 1.2,7.1 0.5,8.8 C 0.1,9.9 1.9,11.0 3.5,11.4 C 1.9,11.8 0.1,12.9 0.5,14.0 C 1.2,15.7 3.6,14.4 6.2,11.5 Z"),
            Fill = sky,
            RenderTransform = tailWag,
        };
        c.Children.Add(tail);

        // Main rounded whale body (facing right in local coordinates)
        var body = new Path
        {
            Data = Geometry.Parse("M 5.4,11.4 C 7.2,10.2 9.4,5.0 17.8,4.3 C 24.4,3.8 29.2,7.0 29.4,11.4 C 29.5,14.3 26.8,16.0 21.2,16.0 H 12.2 C 8.6,16.0 6.8,13.0 5.4,11.4 Z"),
            Fill = sky,
        };
        c.Children.Add(body);

        // Pale cream underbelly (#F4EDF6) + gentle grooves
        var underbelly = new Path
        {
            Data = Geometry.Parse("M 12.2,15.9 C 15.2,13.1 22.4,12.7 28.7,13.7 C 27.4,15.4 24.6,16.0 20.8,16.0 Z"),
            Fill = belly,
        };
        var grooves = new Path
        {
            Data = Geometry.Parse("M 15.5,14.7 Q 20.5,14.2 25.5,14.7"),
            Stroke = sky,
            StrokeThickness = 0.65,
            Opacity = 0.65,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        c.Children.Add(underbelly);
        c.Children.Add(grooves);

        // Rosy blush (#EAB0CF) + eye + tiny catchlight + smile
        var cheek = new Ellipse { Width = 4.0, Height = 2.2, Fill = blush, Opacity = 0.9 };
        Canvas.SetLeft(cheek, 21.8);
        Canvas.SetTop(cheek, 11.1);
        c.Children.Add(cheek);

        var eye = new Ellipse { Width = 2.4, Height = 2.4, Fill = eyeDark };
        Canvas.SetLeft(eye, 24.0);
        Canvas.SetTop(eye, 8.8);
        c.Children.Add(eye);

        var glint = new Ellipse { Width = 0.9, Height = 0.9, Fill = Brushes.White };
        Canvas.SetLeft(glint, 24.8);
        Canvas.SetTop(glint, 9.1);
        c.Children.Add(glint);

        // Paddling pectoral fin
        var fin = new Path
        {
            Data = Geometry.Parse("M 18.8,13.4 C 17.5,15.8 15.2,17.3 14.3,16.4 C 13.7,15.7 15.4,13.9 17.6,13.2 Z"),
            Fill = deepFin,
            RenderTransform = finPaddle,
        };
        c.Children.Add(fin);

        // Tiny water spout above blowhole
        spout = new Path
        {
            Data = Geometry.Parse("M 21.5,4.1 C 21.2,1.8 19.4,1.0 18.4,1.8 M 21.8,4.1 C 22.3,1.8 24.0,1.1 24.9,1.9"),
            Stroke = belly,
            StrokeThickness = 1.0,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Opacity = 0,
        };
        c.Children.Add(spout);

        return c;
    }

    private Point? ComputeWhaleGaze()
    {
        if (IsCursorOverCapsule()) return null;
        var centerX = whaleX + 16;
        var centerY = whaleY + 10;
        var normX = 0.16 + 0.84 * Math.Clamp((centerX - SwimMinX) / (SwimMaxX + 16 - SwimMinX), 0, 1);
        var normY = Math.Clamp((centerY - CapsuleHeight * 0.52) / 9.5, -1, 1);
        return new Point(normX, normY);
    }

    private bool IsCursorOverCapsule()
    {
        if (!IsLoaded || ActualWidth <= 0 || !NativeMethods.GetCursorPos(out var pt)) return false;
        var local = PointFromScreen(new Point(pt.X, pt.Y));
        return local.X >= -6 && local.X <= ActualWidth + 6 && local.Y >= -6 && local.Y <= ActualHeight + 6;
    }

    public void SetActive(bool isActive)
    {
        active = isActive;
        SyncRunning();
    }

    public void SetSuspended(bool isSuspended)
    {
        suspended = isSuspended;
        SyncRunning();
    }

    public void ApplySettings() => SyncRunning();

    private void SyncRunning()
    {
        var companionActive = active && !suspended;
        companion.SetSuspended(!companionActive);

        var animate = companionActive && !settings.ReduceMotion && settings.AnimateHoshino;
        if (animate)
        {
            if (!timer.IsEnabled)
            {
                lastTime = clock.Elapsed.TotalSeconds;
                timer.Start();
            }
        }
        else
        {
            timer.Stop();
            if (companionActive) ApplyStaticPose();
        }
    }

    private void ApplyStaticPose()
    {
        whaleX = 132;
        whaleY = 10;
        facing = -1;
        whaleMove.X = whaleX;
        whaleMove.Y = whaleY;
        whaleFlip.ScaleX = -1;
        whaleTilt.Angle = 0;
        tailWag.Angle = 0;
        finPaddle.Angle = 0;
        spoutPath.Opacity = 0.55;
        causticShiftA.X = 0;
        causticShiftB.X = 0;

        for (var i = 0; i < bubbles.Length; i++)
        {
            var (dot, move, baseX, _, phase) = bubbles[i];
            move.X = baseX;
            move.Y = 8 + phase * 24;
            dot.Opacity = 0.45;
        }
        companion.UpdateState();
    }

    private void StepFrame()
    {
        var now = clock.Elapsed.TotalSeconds;
        var dt = Math.Clamp(now - lastTime, 0, 0.1);
        lastTime = now;

        // Smooth ping-pong swim with gentle ease at both ends of the capsule
        const double cycleSeconds = 12.0;
        var phase = now / cycleSeconds % 1.0;
        var angle = phase * Math.PI * 2;
        var t = 0.5 - 0.5 * Math.Cos(angle); // 0 -> 1 -> 0 smoothly
        var vx = Math.Sin(angle);            // > 0 swimming right, < 0 swimming left

        whaleX = SwimMinX + (SwimMaxX - SwimMinX) * t;
        var bob = Math.Sin(now * 2.2);
        whaleY = 10.0 + 4.4 * bob;

        var targetFace = vx >= 0 ? 1.0 : -1.0;
        facing += (targetFace - facing) * (1 - Math.Exp(-dt / 0.10));
        if (Math.Abs(facing) < 0.08) facing = targetFace * 0.08;

        whaleMove.X = whaleX;
        whaleMove.Y = whaleY;
        whaleFlip.ScaleX = facing;
        whaleTilt.Angle = Math.Cos(now * 2.2) * 6.0 * Math.Sign(facing);
        tailWag.Angle = Math.Sin(now * 7.5) * 14.0;
        finPaddle.Angle = Math.Sin(now * 5.2 + 0.8) * 10.0;
        spoutPath.Opacity = bob < -0.55 ? Math.Clamp((-bob - 0.55) / 0.45, 0, 0.8) : 0;

        causticShiftA.X = Math.Sin(now * 0.55) * 16;
        causticShiftB.X = -Math.Cos(now * 0.42) * 14;

        for (var i = 0; i < bubbles.Length; i++)
        {
            var (dot, move, baseX, speed, bPhase) = bubbles[i];
            var p = (now * speed + bPhase) % 1.0;
            move.X = baseX + Math.Sin(p * Math.PI * 2 + i) * 2.4;
            move.Y = 36 - p * 33;
            dot.Opacity = p < 0.2 ? p / 0.2 * 0.65 : (1 - (p - 0.2) / 0.8) * 0.65;
        }
    }

    private static Brush Frozen(Color color)
    {
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }

    public void Dispose()
    {
        timer.Stop();
        companion.SetSuspended(true);
    }
}
