// Original HoshiBar code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DIsland.Interop;
using DIsland.Services;

namespace DIsland;

// Vector geometry from the user's approved Hoshino design. Eyes and ahoge move independently of the hair.
public sealed class HoshinoCompanion : Grid
{
    private readonly AppSettings settings;
    private readonly Canvas scene = new() { Width = 120, Height = 120 };
    private readonly Canvas face = new() { Width = 120, Height = 120, IsHitTestVisible = false };
    private readonly Canvas smoothBody = new() { Width = 120, Height = 120, IsHitTestVisible = false };
    private readonly Canvas stickerBackdrop;
    private readonly Canvas pixelBody;
    private readonly RotateTransform pixelAhogeRotate;
    private readonly TranslateTransform pixelEyeGaze;
    private readonly Path pixelClosedLids;
    private readonly Path pixelHappyLids;
    private readonly Path pixelBlush;

    private readonly TranslateTransform gaze = new();
    private readonly ScaleTransform eyelids = new(1, 1, 60, 77);
    private readonly ScaleTransform squish = new(1, 1, 60, 88);
    private readonly TranslateTransform bounce = new();
    private readonly RotateTransform sway = new(0, 60, 72);
    private readonly RotateTransform ahogeRotate = new(0, 58, 25);
    private readonly ScaleTransform ahogeScale = new(1, 1, 58, 25);

    private readonly Canvas haloRoot;
    private readonly TranslateTransform haloOffset;
    private readonly ScaleTransform haloScale;
    private readonly Canvas sleepRoot;
    private readonly (Path Shape, TranslateTransform Move, ScaleTransform Scale)[] sleepItems;
    private readonly Canvas headphones;
    private readonly Canvas accessoryOverlay = new() { Width = 120, Height = 120, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Canvas weatherBadge = new() { Width = 120, Height = 120, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Canvas sunglassesOverlay = new() { Width = 120, Height = 120, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Canvas eyesCanvas = new() { IsHitTestVisible = false };
    private readonly Path happyArchPath = new()
    {
        Data = Geometry.Parse("M 41,78 Q 47,71 53,78 M 67,78 Q 73,71 79,78"),
        Stroke = (Brush)new BrushConverter().ConvertFromString("#4B364C")!,
        StrokeThickness = 2.2,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        Visibility = Visibility.Collapsed,
        IsHitTestVisible = false,
    };
    private readonly Rectangle crownZone = new()
    {
        Width = 68,
        Height = 42,
        Fill = Brushes.Transparent,
        Cursor = Cursors.Hand,
        Visibility = Visibility.Collapsed,
        ToolTip = "Погладить Хошино по макушке",
    };

    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch time = Stopwatch.StartNew();
    private double last;
    private bool interactive;
    private bool suspended;
    private string weatherKey = "";
    private string accessoryKey = "";
    private double headpatUntil = -10;
    private double happyTaskUntil = -10;
    private bool fileDragHover;
    private double lastHeadpatCue = -10;
    private double patOffset;
    private double patVelocity;
    private double patTilt;
    private double ahogeAngle;
    private double ahogeVel;
    private double lastPatX = double.NaN;
    private double lastPatMoveTime = -10;
    private int lastPatDir;
    private int patStrokes;

    public bool Interactive
    {
        get => interactive;
        set
        {
            interactive = value;
            Background = value ? Brushes.Transparent : null;
        }
    }

    public bool EnableHeadpat
    {
        get => crownZone.Visibility == Visibility.Visible;
        set => crownZone.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Hides the small floating top halo when a full Eye of Horus backdrop or clean lyric mode is active.</summary>
    public bool SuppressFloatingHalo { get; set; }

    /// <summary>Compact capsule mode: clean iconic silhouette with padded viewbox, no forehead/halo/ear-badge clutter.</summary>
    public bool IsCompactPill
    {
        get => _isCompactPill;
        set
        {
            _isCompactPill = value;
            scene.Margin = new Thickness(value ? 5.5 : 2.0);
        }
    }
    private bool _isCompactPill;

    public event Action<string>? Headpatted;
    public string Reaction { get; private set; } = "normal";
    private int reactionStep;
    private readonly Canvas cheeks = new() { Opacity = 0, IsHitTestVisible = false };
    private readonly Path leftLid = new() { Fill = (Brush)new BrushConverter().ConvertFromString("#FFF0E9")!, IsHitTestVisible = false };
    private readonly Path rightLid = new() { Fill = (Brush)new BrushConverter().ConvertFromString("#FFF0E9")!, IsHitTestVisible = false };

    public void React()
    {
        Reaction = new[] { "blush", "angry", "normal" }[reactionStep++ % 3];
        UpdateState();
    }

    /// <summary>2-second satisfied squint &amp; blush when the user completes the last active task.</summary>
    public void TriggerHappyTaskReaction()
    {
        var now = time.Elapsed.TotalSeconds;
        happyTaskUntil = now + 2.2;
        if (!settings.ReduceMotion && settings.AnimateHoshino)
        {
            ahogeVel -= 140;
            patVelocity = -22;
        }
        UpdateState();
    }

    /// <summary>Attentive upward gaze &amp; perked ahoge while files are dragged over the island toward the Shelf.</summary>
    public void SetFileDragHover(bool hovering)
    {
        if (fileDragHover == hovering) return;
        fileDragHover = hovering;
        if (hovering && !settings.ReduceMotion && settings.AnimateHoshino)
            ahogeVel -= 95;
        UpdateState();
    }

    public Func<bool>? Playing { get; set; }
    public Func<double>? AudioLevel { get; set; }
    public Func<bool>? Listening { get; set; }
    public Func<bool>? Working { get; set; }
    public Func<bool>? Focusing { get; set; }
    public Func<WeatherReport?>? Weather { get; set; }
    public Func<Point?>? GazeTarget { get; set; }
    public string State { get; private set; } = "neutral";
    public Point GazeOffset => new(gaze.X, gaze.Y);

    public HoshinoCompanion(AppSettings settings)
    {
        this.settings = settings;
        ToolTip = "Таканаши Хошино";
        (haloRoot, haloOffset, haloScale) = HoshinoAtmosphere.CreateHalo();
        (sleepRoot, sleepItems) = HoshinoAtmosphere.CreateSleepParticles();
        headphones = HoshinoAtmosphere.CreateHeadphones();
        stickerBackdrop = HoshinoAccessories.CreateStickerBackdrop();
        (pixelBody, pixelAhogeRotate, pixelEyeGaze, pixelClosedLids, pixelHappyLids, pixelBlush) = HoshinoAccessories.CreatePixelChibiSprite();

        // Articulated top ahoge curl (bounces independently in rhythm with AudioMeter & music)
        var ahogeTransforms = new TransformGroup();
        ahogeTransforms.Children.Add(ahogeScale);
        ahogeTransforms.Children.Add(ahogeRotate);
        var ahogeGeo = Geometry.Parse("M54.5 25.5C49 11 61 1 72 7c6 3 7 8 4 12-1-6-6-8-11-6-5 2-6 7-3.5 12.5Z");
        ahogeGeo.Freeze();
        var ahogePath = new Path
        {
            Data = ahogeGeo,
            Fill = (Brush)new BrushConverter().ConvertFromString("#EAB0CF")!,
            RenderTransform = ahogeTransforms,
            IsHitTestVisible = false,
        };

        // Back hair silhouette (seamlessly joined behind crown bangs)
        AddSmoothPath("M61 24C82 21 99 36 101 56l10 8-10 1 6 7-9-1c1 15 2 24 8 35l-10-4 2 12-11-8-2 10c-8-8-11-18-12-29H47c-1 11-4 21-12 29l-2-10-11 8 2-12-10 4c6-11 7-20 8-35l-9 1 6-7-10-1 10-8C21 36 34 24 55 24Z", "#EAB0CF", 1);
        smoothBody.Children.Add(ahogePath);
        AddSmoothPath("M30 57C30 36 90 36 90 57V71C90 87 78 97 60 98 42 97 30 87 30 71Z", "#FFF0E9", 1);
        AddSmoothPath("M23 59C25 34 38 21 60 24 82 21 98 37 98 59L104 65C94 64 89 59 85 54c4 9 3 15-2 19-2-8-3-16-7-23 0 12-6 19-15 24 4-13 1-23-2-32-1 14-5 23-12 28 1-13-1-21-4-27-1 12-5 21-13 26l3-13c-4 4-9 7-15 8Z", "#EAB0CF", 1);
        AddSmoothPath("M29 72c-1 14-2 26-5 31 7-6 10-17 11-30ZM91 72c1 14 2 26 5 31-7-6-10-17-11-30Z", "#D795BC", .8);

        foreach (var (x, color) in new[] { (47d, "#F2D17D"), (73d, "#83CFF3") })
        {
            var eye = new Ellipse { Width = 11.6, Height = 11, Fill = (Brush)new BrushConverter().ConvertFromString(color)! };
            Canvas.SetLeft(eye, x - 5.8);
            Canvas.SetTop(eye, 71.5);
            eyesCanvas.Children.Add(eye);
        }
        var transforms = new TransformGroup();
        transforms.Children.Add(eyelids);
        transforms.Children.Add(gaze);
        eyesCanvas.RenderTransform = transforms;
        smoothBody.Children.Add(eyesCanvas);

        foreach (var cx in new[] { 35d, 85d })
        {
            var blush = new Ellipse { Width = 14, Height = 7, Fill = (Brush)new BrushConverter().ConvertFromString("#EC81A3")! };
            Canvas.SetLeft(blush, cx - 7);
            Canvas.SetTop(blush, 82);
            cheeks.Children.Add(blush);
        }
        smoothBody.Children.Add(cheeks);
        smoothBody.Children.Add(happyArchPath);
        smoothBody.Children.Add(leftLid);
        smoothBody.Children.Add(rightLid);

        var bodyTransforms = new TransformGroup();
        bodyTransforms.Children.Add(squish);
        bodyTransforms.Children.Add(sway);
        bodyTransforms.Children.Add(bounce);
        face.RenderTransform = bodyTransforms;

        face.Children.Add(stickerBackdrop);
        face.Children.Add(smoothBody);
        face.Children.Add(pixelBody);
        face.Children.Add(accessoryOverlay);
        face.Children.Add(headphones);

        Canvas.SetLeft(crownZone, 26);
        Canvas.SetTop(crownZone, 4);
        crownZone.MouseMove += OnCrownMouseMove;
        crownZone.MouseLeave += OnCrownMouseLeave;
        crownZone.MouseLeftButtonDown += (_, e) => e.Handled = true;
        crownZone.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            TriggerHeadpat(time.Elapsed.TotalSeconds);
        };

        scene.Children.Add(haloRoot);
        scene.Children.Add(face);
        scene.Children.Add(sleepRoot);
        scene.Children.Add(weatherBadge);
        scene.Children.Add(crownZone);

        System.Windows.Automation.AutomationProperties.SetName(this, "Хошино — нажми, чтобы сменить настроение");
        Focusable = true;
        MouseLeftButtonDown += (_, e) => { if (Interactive) { Focus(); e.Handled = true; } };
        MouseLeftButtonUp += (_, e) => { if (!Interactive) return; React(); e.Handled = true; };
        PreviewKeyDown += (_, e) => { if (Interactive && (e.Key == Key.Enter || e.Key == Key.Space)) { React(); e.Handled = true; } };
        MouseEnter += (_, _) => { if (Interactive) Cursor = Cursors.Hand; };
        scene.Margin = new Thickness(2.0);
        Children.Add(new Viewbox { Child = scene, Stretch = Stretch.Uniform });

        timer.Tick += (_, _) => UpdateState();
        IsVisibleChanged += (_, _) => SyncTimer();
        Loaded += (_, _) => { last = time.Elapsed.TotalSeconds; SyncTimer(); UpdateState(); };
        Unloaded += (_, _) => timer.Stop();
    }

    public void SetSuspended(bool value)
    {
        if (suspended == value) return;
        suspended = value;
        SyncTimer();
    }

    private void SyncTimer()
    {
        if (IsVisible && IsLoaded && !suspended)
        {
            last = time.Elapsed.TotalSeconds;
            timer.Start();
        }
        else
        {
            timer.Stop();
        }
    }

    private void OnCrownMouseMove(object sender, MouseEventArgs e)
    {
        var now = time.Elapsed.TotalSeconds;
        var pos = e.GetPosition(crownZone);
        if (double.IsNaN(lastPatX) || now - lastPatMoveTime > 1.2)
        {
            lastPatX = pos.X;
            lastPatMoveTime = now;
            lastPatDir = 0;
            patStrokes = 0;
            return;
        }
        var dx = pos.X - lastPatX;
        if (Math.Abs(dx) < 6) return;
        var dir = Math.Sign(dx);
        lastPatX = pos.X;
        lastPatMoveTime = now;
        patTilt = dir * 2.8;
        if (lastPatDir != 0 && dir != lastPatDir)
        {
            patStrokes++;
            if (patStrokes >= 2)
            {
                patStrokes = 0;
                TriggerHeadpat(now);
            }
        }
        lastPatDir = dir;
    }

    private void OnCrownMouseLeave(object sender, MouseEventArgs e)
    {
        lastPatX = double.NaN;
        lastPatDir = 0;
        patStrokes = 0;
        patTilt = 0;
    }

    public void TriggerHeadpat(double? nowOverride = null)
    {
        var now = nowOverride ?? time.Elapsed.TotalSeconds;
        headpatUntil = now + 2.8;
        if (!settings.ReduceMotion && settings.AnimateHoshino)
        {
            patVelocity = -36;
            patTilt = (Random.Shared.Next(2) == 0 ? -1 : 1) * 3.5;
            ahogeVel += patTilt * 45;
        }
        if (now - lastHeadpatCue >= 1.5)
        {
            lastHeadpatCue = now;
            AssistantCues.PlayHeadpat();
            Headpatted?.Invoke(HoshinoAtmosphere.NextHeadpatPhrase());
        }
        UpdateState();
    }

    private void AddSmoothPath(string data, string fill, double opacity)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        smoothBody.Children.Add(new Path
        {
            Data = geometry,
            Fill = (Brush)new BrushConverter().ConvertFromString(fill)!,
            Opacity = opacity,
            IsHitTestVisible = false,
        });
    }

    public void UpdateState()
    {
        var now = time.Elapsed.TotalSeconds;
        var dt = Math.Clamp(now - last, 0, .1);
        last = now;

        var skin = settings.IslandSkin;
        var isPixel = skin == IslandSkin.PixelTamagotchi;
        var isSticker = skin == IslandSkin.ChibiPop;
        smoothBody.Visibility = isPixel ? Visibility.Collapsed : Visibility.Visible;
        pixelBody.Visibility = isPixel ? Visibility.Visible : Visibility.Collapsed;
        stickerBackdrop.Visibility = isSticker ? Visibility.Visible : Visibility.Collapsed;

        var still = settings.ReduceMotion || !settings.AnimateHoshino;
        var isListening = Listening?.Invoke() == true;
        var isPlaying = Playing?.Invoke() == true;
        var isWorking = Working?.Invoke() == true;
        var isFocusing = Focusing?.Invoke() == true;
        var isPetted = now < headpatUntil;
        var isHappyTask = now < happyTaskUntil;
        var isCatchingFile = fileDragHover;
        var clockNow = DateTime.Now;
        var isDrowsy = !Interactive && !isListening && !isPlaying && !isWorking && !isPetted && !isHappyTask && !isCatchingFile
                       && HoshinoAtmosphere.IsDrowsy(clockNow);

        headphones.Visibility = isFocusing && !isPixel && !IsCompactPill ? Visibility.Visible : Visibility.Collapsed;

        // Weather micro-badge & switchable mini-accessory (suppressed inside tiny compact pills so her silhouette stays crisp!)
        var report = Weather?.Invoke();
        var showWeatherBadge = settings.WeatherEnabled && Weather is not null && !IsCompactPill;
        var nextWeatherKey = HoshinoAtmosphere.WeatherKey(report, showWeatherBadge);
        if (nextWeatherKey != weatherKey)
        {
            weatherKey = nextWeatherKey;
            HoshinoAtmosphere.RebuildWeatherBadge(weatherBadge, sunglassesOverlay, report, showWeatherBadge);
        }
        if (IsCompactPill) weatherBadge.Visibility = Visibility.Collapsed;

        var activeAccessory = IsCompactPill
            ? HoshinoAccessory.None
            : HoshinoAccessories.ResolveActive(settings, report, clockNow, isDrowsy);
        var nextAccessoryKey = $"{activeAccessory}|{isPixel}";
        if (nextAccessoryKey != accessoryKey)
        {
            accessoryKey = nextAccessoryKey;
            HoshinoAccessories.RebuildAccessoryOverlay(accessoryOverlay, activeAccessory, isPixel);
        }

        double x = 0, y = 0, lid = 1, angle = 0;
        State = "neutral";

        if (isCatchingFile)
        {
            State = "catch";
            y = -2.4;
            lid = 1.18;
            if (!still) angle = Math.Sin(now * 5.0) * 1.6;
        }
        else if (isListening)
        {
            State = "listening";
            lid = 1.25;
        }
        else if (isHappyTask)
        {
            State = "happy";
            lid = 0.16;
            if (!still) angle = Math.Sin(now * 6.4) * 2.5;
        }
        else if (isDrowsy)
        {
            State = "sleepy";
            lid = still ? 0.22 : 0.20 + 0.05 * Math.Sin(now * 0.9);
            if (!still) angle = Math.Sin(now * 0.65) * 1.4;
        }
        else if (!still)
        {
            if (isPlaying)
            {
                angle = Math.Sin(now * 3) * 4;
                State = angle < 0 ? "dance-left" : "dance-right";
            }
            else if (isWorking)
            {
                x = Math.Sin(now * 1.4) * 2;
                State = x < 0 ? "look-left" : "look-right";
            }

            if (GazeTarget?.Invoke() is { } target)
            {
                x = Math.Clamp(target.X, -1, 1) * 3.2;
                y = Math.Clamp(target.Y, -1, 1) * 1.8;
            }
            else if (settings.HoshinoFollowsCursor && IsLoaded && IsVisible && ActualWidth > 0 && NativeMethods.GetCursorPos(out var cursor))
            {
                var center = PointToScreen(new Point(ActualWidth / 2, ActualHeight * .64));
                x = Math.Clamp((cursor.X - center.X) / 100, -1, 1) * 3;
                y = Math.Clamp((cursor.Y - center.Y) / 180, -1, 1) * 1.8;
            }

            // Short soft blink, independent of the cursor and frame rate.
            var phase = now % 5.4;
            if (phase > 5.08)
            {
                lid = .12 + .88 * Math.Abs((phase - 5.24) / .16);
                if (State == "neutral") State = "sleepy";
            }
        }
        else if (GazeTarget?.Invoke() is { } staticTarget)
        {
            x = Math.Clamp(staticTarget.X, -1, 1) * 2.4;
            y = Math.Clamp(staticTarget.Y, -1, 1) * 1.2;
        }

        // Articulated ahoge spring driven by AudioMeter & music rhythm
        var audio = Math.Clamp(AudioLevel?.Invoke() ?? 0, 0, 1);
        if (still)
        {
            ahogeAngle = 0;
            ahogeVel = 0;
            ahogeRotate.Angle = 0;
            ahogeScale.ScaleY = 1;
            pixelAhogeRotate.Angle = 0;
        }
        else
        {
            double targetAhoge = 0;
            if (isCatchingFile) targetAhoge = -9;
            else if (isPlaying || audio > 0.03)
            {
                var beat = Math.Sin(now * 8.2) * (4.5 + audio * 12.0) - audio * 9.5;
                targetAhoge = beat;
            }
            else if (isDrowsy)
            {
                targetAhoge = 6 + Math.Sin(now * 0.9) * 2.0;
            }
            else
            {
                targetAhoge = Math.Sin(now * 1.9) * 1.6;
            }

            const double ak = 190, ad = 11.5;
            ahogeVel += (ak * (targetAhoge - ahogeAngle) - ad * ahogeVel) * dt;
            ahogeAngle = Math.Clamp(ahogeAngle + ahogeVel * dt, -24, 22);
            ahogeRotate.Angle = ahogeAngle;
            ahogeScale.ScaleY = 1 + Math.Clamp(audio * 0.14, 0, 0.14);
            pixelAhogeRotate.Angle = Math.Round(ahogeAngle / 4.0) * 4.0;
        }

        // Halo state & animation (hidden when pixel mode, compact pill, or suppressed by full Eye of Horus backdrop)
        var showHalo = settings.ShowHoshinoHalo && !SuppressFloatingHalo && !IsCompactPill && !isPixel;
        haloRoot.Visibility = showHalo ? Visibility.Visible : Visibility.Collapsed;
        if (showHalo)
        {
            if (still)
            {
                haloOffset.Y = 0;
                haloScale.ScaleX = haloScale.ScaleY = 1;
                haloRoot.Opacity = isDrowsy ? 0.38 : 0.92;
            }
            else if (isWorking)
            {
                var pulse = 1 + 0.08 * Math.Sin(now * 5.2);
                haloScale.ScaleX = haloScale.ScaleY = pulse;
                haloOffset.Y = Math.Sin(now * 3.4) * 1.9;
                haloRoot.Opacity = 0.84 + 0.16 * Math.Sin(now * 5.2);
            }
            else if (isDrowsy)
            {
                haloScale.ScaleX = haloScale.ScaleY = 0.97;
                haloOffset.Y = 1.2 + Math.Sin(now * 0.9) * 0.9;
                haloRoot.Opacity = 0.36 + 0.06 * Math.Sin(now * 0.9);
            }
            else
            {
                haloScale.ScaleX = haloScale.ScaleY = 1 + 0.025 * Math.Sin(now * 1.8);
                haloOffset.Y = Math.Sin(now * 1.8) * 1.5;
                haloRoot.Opacity = 0.9;
            }
        }

        // Sleep zZz vector particles
        sleepRoot.Visibility = isDrowsy ? Visibility.Visible : Visibility.Collapsed;
        if (isDrowsy)
        {
            for (var i = 0; i < sleepItems.Length; i++)
            {
                var (shape, move, scale) = sleepItems[i];
                if (still)
                {
                    move.X = 90 + i * 7;
                    move.Y = 34 - i * 8;
                    scale.ScaleX = scale.ScaleY = 0.72 + i * 0.15;
                    shape.Opacity = i < 2 ? 0.48 : 0;
                }
                else
                {
                    var p = (now * 0.36 + i / 3.0) % 1.0;
                    move.X = 88 + p * 14 + Math.Sin(p * Math.PI * 2 + i) * 2.2;
                    move.Y = 40 - p * 26;
                    scale.ScaleX = scale.ScaleY = 0.65 + p * 0.55;
                    shape.Opacity = p < 0.2 ? p / 0.2 * 0.75 : (1 - (p - 0.2) / 0.8) * 0.75;
                }
            }
        }

        // Headpat spring physics & blush
        if (still)
        {
            patOffset = 0;
            patVelocity = 0;
            squish.ScaleX = squish.ScaleY = 1;
        }
        else
        {
            const double k = 220, d = 14;
            patVelocity += (-k * patOffset - d * patVelocity) * dt;
            patOffset += patVelocity * dt;
            if (Math.Abs(patOffset) < 0.02 && Math.Abs(patVelocity) < 0.05) { patOffset = 0; patVelocity = 0; }
            var compress = Math.Clamp(patOffset * 0.018, -0.08, 0.08);
            squish.ScaleY = 1 + compress;
            squish.ScaleX = 1 - compress * 0.65;
            patTilt *= Math.Exp(-dt * 5.5);
        }

        if (isPetted)
        {
            State = "blush";
            lid = Math.Min(lid, 0.55);
            angle += patTilt + (still ? 0 : Math.Sin(now * 7.5) * 1.8);
        }

        double targetBlush = (isPetted || isHappyTask) ? 0.88
            : activeAccessory == HoshinoAccessory.SummerShades ? 0.52
            : isCatchingFile ? 0.42
            : 0;

        if (Interactive)
        {
            if (Reaction == "blush") { State = "blush"; lid = .6; targetBlush = .85; }
            else if (Reaction == "angry") { State = "angry"; lid = 1; x = 0; y = 0; }
            else if (!still && !isHappyTask) { lid = .55; }
            if (!still && isPlaying) angle *= 1.8;
            leftLid.Data = Reaction == "angry" ? Geometry.Parse("M40 70 L54 78 L54 70 Z") : Geometry.Empty;
            rightLid.Data = Reaction == "angry" ? Geometry.Parse("M66 70 L66 78 L80 70 Z") : Geometry.Empty;
        }
        else
        {
            leftLid.Data = Geometry.Empty;
            rightLid.Data = Geometry.Empty;
        }

        // Happy curved squint eyes (^ ^) when finishing the last open task
        var showHappySquint = isHappyTask && activeAccessory != HoshinoAccessory.SummerShades;
        eyesCanvas.Visibility = showHappySquint ? Visibility.Hidden : Visibility.Visible;
        happyArchPath.Visibility = showHappySquint ? Visibility.Visible : Visibility.Collapsed;

        cheeks.Opacity += (targetBlush - cheeks.Opacity) * (still ? 1 : 1 - Math.Exp(-dt / .12));

        var blend = still ? 1 : 1 - Math.Exp(-dt / 0.11);
        gaze.X += (x - gaze.X) * blend;
        gaze.Y += (y - gaze.Y) * blend;
        eyelids.ScaleY += (lid - eyelids.ScaleY) * blend;
        sway.Angle += (angle - sway.Angle) * blend;
        var hop = (Interactive && !still && isPlaying ? -Math.Abs(Math.Sin(now * 3)) * 3 : 0) + patOffset;
        bounce.Y += (hop - bounce.Y) * blend;

        // Sync pixel-chibi sprite state
        if (isPixel)
        {
            pixelEyeGaze.X = Math.Round(gaze.X / 2.0) * 2.0;
            pixelEyeGaze.Y = Math.Round(gaze.Y / 2.0) * 2.0;
            pixelHappyLids.Visibility = showHappySquint ? Visibility.Visible : Visibility.Collapsed;
            pixelClosedLids.Visibility = !showHappySquint && (isDrowsy || lid < 0.35) ? Visibility.Visible : Visibility.Collapsed;
            pixelBlush.Opacity = cheeks.Opacity;
        }
    }
}
