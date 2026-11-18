// Original HoshiBar code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DIsland.Services;

namespace DIsland;

/// <summary>
/// Decorates expanded and compact media covers according to the active skin:
/// spinning glossy CD disc in Win7 Aero, spinning cartoon vinyl record in Chibi Pop,
/// and retro pixel audio cassette with spinning tape reels in Pixel Tamagotchi.
/// Completely stops timers when paused, hidden, ReduceMotion, !AnimateHoshino, or GameModeEnabled.
/// </summary>
public sealed class IslandMediaArtDecorator : IDisposable
{
    private readonly AppSettings settings;
    private readonly Grid largeBackHost = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Grid largeFrontHost = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Grid smallBackHost = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Grid smallFrontHost = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };

    private readonly DispatcherTimer spinTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private double lastSec;
    private double angleDeg;
    private bool isPlaying;
    private bool islandHidden;
    private bool suspended;
    private IslandSkin builtSkin = (IslandSkin)(-1);
    private double builtCoverSize = -1;

    private RotateTransform? largeDiscRotate;
    private RotateTransform? smallDiscRotate;
    private RotateTransform? largeReelLeft;
    private RotateTransform? largeReelRight;
    private RotateTransform? smallReelLeft;
    private RotateTransform? smallReelRight;

    public FrameworkElement LargeBackLayer => largeBackHost;
    public FrameworkElement LargeFrontLayer => largeFrontHost;
    public FrameworkElement SmallBackLayer => smallBackHost;
    public FrameworkElement SmallFrontLayer => smallFrontHost;

    public IslandMediaArtDecorator(AppSettings settings)
    {
        this.settings = settings;
        spinTimer.Tick += OnTick;
        largeBackHost.IsVisibleChanged += (_, _) => SyncTimer();
        smallBackHost.IsVisibleChanged += (_, _) => SyncTimer();
    }

    private bool themedEnabled = true;

    public void UpdateState(bool playing, bool hidden, double coverSize, Border largeArtBorder, Border smallArtBorder)
    {
        isPlaying = playing;
        islandHidden = hidden;

        var skin = settings.ThemedMediaCovers ? settings.IslandSkin : IslandSkin.HorusGlass;
        coverSize = Math.Clamp(coverSize, 56, 112);

        if (skin != builtSkin || Math.Abs(coverSize - builtCoverSize) > 0.5 || themedEnabled != settings.ThemedMediaCovers)
        {
            builtSkin = skin;
            builtCoverSize = coverSize;
            themedEnabled = settings.ThemedMediaCovers;
            RebuildVisuals(skin, coverSize, largeArtBorder, smallArtBorder);
        }

        SyncTimer();
    }

    public void SetSuspended(bool value)
    {
        if (suspended == value) return;
        suspended = value;
        SyncTimer();
    }

    private void SyncTimer()
    {
        var wantsSpin = isPlaying
                        && !suspended
                        && !islandHidden
                        && !settings.ReduceMotion
                        && settings.AnimateHoshino
                        && builtSkin is IslandSkin.Win7Aero or IslandSkin.ChibiPop or IslandSkin.PixelTamagotchi
                        && (largeBackHost.IsVisible || largeFrontHost.IsVisible || smallBackHost.IsVisible || smallFrontHost.IsVisible);

        if (wantsSpin)
        {
            if (!spinTimer.IsEnabled)
            {
                lastSec = clock.Elapsed.TotalSeconds;
                spinTimer.Start();
            }
        }
        else
        {
            spinTimer.Stop();
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = clock.Elapsed.TotalSeconds;
        var dt = Math.Clamp(now - lastSec, 0, 0.1);
        lastSec = now;
        angleDeg = (angleDeg + dt * 58.0) % 360.0;

        if (largeDiscRotate is not null) largeDiscRotate.Angle = angleDeg;
        if (smallDiscRotate is not null) smallDiscRotate.Angle = angleDeg;

        // Pixel cassette reels step cleanly in 30-degree ticks for authentic retro LCD motion
        var stepped = Math.Floor(angleDeg / 30.0) * 30.0;
        if (largeReelLeft is not null) largeReelLeft.Angle = stepped;
        if (largeReelRight is not null) largeReelRight.Angle = stepped;
        if (smallReelLeft is not null) smallReelLeft.Angle = stepped;
        if (smallReelRight is not null) smallReelRight.Angle = stepped;
    }

    private void RebuildVisuals(IslandSkin skin, double coverSize, Border largeArtBorder, Border smallArtBorder)
    {
        largeBackHost.Children.Clear();
        largeFrontHost.Children.Clear();
        smallBackHost.Children.Clear();
        smallFrontHost.Children.Clear();
        largeDiscRotate = smallDiscRotate = null;
        largeReelLeft = largeReelRight = smallReelLeft = smallReelRight = null;

        // Reset cover border defaults before applying skin specifics
        largeArtBorder.Visibility = Visibility.Visible;
        smallArtBorder.Visibility = Visibility.Visible;
        largeArtBorder.Margin = new Thickness(0);
        smallArtBorder.Width = 24;
        smallArtBorder.Height = 24;

        switch (skin)
        {
            case IslandSkin.Win7Aero:
            {
                largeArtBorder.CornerRadius = new CornerRadius(10);
                largeArtBorder.BorderBrush = Frozen(Color.FromArgb(0xC2, 0xE6, 0xF7, 0xFF));
                largeArtBorder.BorderThickness = new Thickness(1.3);
                var discSize = Math.Round(coverSize * 0.88);
                var peek = Math.Round(coverSize * 0.34);
                largeBackHost.Width = coverSize + peek + 2;
                largeBackHost.Height = coverSize + 2;

                var (cd, rot) = CreateAeroCd(discSize);
                largeDiscRotate = rot;
                cd.HorizontalAlignment = HorizontalAlignment.Left;
                cd.VerticalAlignment = VerticalAlignment.Center;
                cd.Margin = new Thickness(coverSize + peek - discSize, 0, 0, 0);
                largeBackHost.Children.Add(cd);

                var sheenGrad = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                sheenGrad.GradientStops.Add(new GradientStop(Color.FromArgb(0x46, 0xFF, 0xFF, 0xFF), 0));
                sheenGrad.GradientStops.Add(new GradientStop(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 0.42));
                sheenGrad.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.48));
                sheenGrad.Freeze();
                largeFrontHost.Width = coverSize;
                largeFrontHost.Height = coverSize;
                largeFrontHost.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = sheenGrad });

                smallBackHost.Width = 0;
                smallBackHost.Margin = new Thickness(0);
                smallFrontHost.Width = 0;
                smallFrontHost.Margin = new Thickness(0);
                break;
            }

            case IslandSkin.ChibiPop:
            {
                var ink = Frozen(Color.FromRgb(0x2A, 0x1E, 0x2E));
                largeArtBorder.CornerRadius = new CornerRadius(10);
                largeArtBorder.BorderBrush = ink;
                largeArtBorder.BorderThickness = new Thickness(2.4);

                var discSize = Math.Round(coverSize * 0.88);
                var peek = Math.Round(coverSize * 0.35);
                largeBackHost.Width = coverSize + peek + 2;
                largeBackHost.Height = coverSize + 2;
                var (vinylLarge, rotL) = CreateCartoonVinyl(discSize, 2.4);
                largeDiscRotate = rotL;
                vinylLarge.HorizontalAlignment = HorizontalAlignment.Left;
                vinylLarge.VerticalAlignment = VerticalAlignment.Center;
                vinylLarge.Margin = new Thickness(coverSize + peek - discSize, 0, 0, 0);
                largeBackHost.Children.Add(vinylLarge);

                // Compact vinyl peeking cleanly right of MediaArtSmall (8px left + 24px cover + 10px peek = 42px)
                smallArtBorder.CornerRadius = new CornerRadius(6);
                smallArtBorder.BorderBrush = ink;
                smallArtBorder.BorderThickness = new Thickness(1.8);
                smallBackHost.Width = 35;
                smallBackHost.Height = 26;
                smallBackHost.Margin = new Thickness(8, 0, 0, 0);
                var (vinylSmall, rotS) = CreateCartoonVinyl(22, 1.6);
                smallDiscRotate = rotS;
                vinylSmall.HorizontalAlignment = HorizontalAlignment.Left;
                vinylSmall.VerticalAlignment = VerticalAlignment.Center;
                vinylSmall.Margin = new Thickness(12, 0, 0, 0);
                smallBackHost.Children.Add(vinylSmall);
                smallFrontHost.Width = 0;
                smallFrontHost.Margin = new Thickness(0);
                break;
            }

            case IslandSkin.PixelTamagotchi:
            {
                largeArtBorder.Visibility = Visibility.Collapsed;
                smallArtBorder.Visibility = Visibility.Collapsed;

                largeBackHost.Width = 0;
                largeFrontHost.Width = 56;
                largeFrontHost.Height = 54;
                var (largeCassette, rl1, rr1) = CreatePixelCassette(expandedWithMiniCd: true);
                largeReelLeft = rl1;
                largeReelRight = rr1;
                largeFrontHost.Children.Add(largeCassette);

                smallBackHost.Width = 0;
                smallBackHost.Margin = new Thickness(0);
                smallFrontHost.Width = 38;
                smallFrontHost.Height = 24;
                smallFrontHost.Margin = new Thickness(8, 0, 0, 0);
                var (smallCassette, sl1, sr1) = CreatePixelCassette(expandedWithMiniCd: false);
                smallReelLeft = sl1;
                smallReelRight = sr1;
                smallFrontHost.Children.Add(smallCassette);
                break;
            }

            default:
            {
                largeArtBorder.CornerRadius = new CornerRadius(14);
                largeArtBorder.BorderBrush = Frozen(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
                largeArtBorder.BorderThickness = new Thickness(1);
                smallArtBorder.CornerRadius = new CornerRadius(6);
                smallArtBorder.BorderThickness = new Thickness(0);
                largeBackHost.Width = 0;
                largeFrontHost.Width = 0;
                smallBackHost.Width = 0;
                smallFrontHost.Width = 0;
                break;
            }
        }
    }

    private static (Viewbox Visual, RotateTransform Rotate) CreateAeroCd(double size)
    {
        var canvas = new Canvas { Width = 100, Height = 100 };
        var rotate = new RotateTransform(0, 50, 50);
        canvas.RenderTransform = rotate;

        var baseGrad = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        baseGrad.GradientStops.Add(new GradientStop(Color.FromRgb(0xEC, 0xF5, 0xFA), 0));
        baseGrad.GradientStops.Add(new GradientStop(Color.FromRgb(0xC2, 0xD6, 0xE6), 0.5));
        baseGrad.GradientStops.Add(new GradientStop(Color.FromRgb(0xE8, 0xF2, 0xF8), 1));
        baseGrad.Freeze();

        // Outer disc with center cutout
        var discGeo = new CombinedGeometry(
            GeometryCombineMode.Exclude,
            new EllipseGeometry(new Point(50, 50), 48, 48),
            new EllipseGeometry(new Point(50, 50), 10, 10));
        discGeo.Freeze();
        canvas.Children.Add(new Path
        {
            Data = discGeo,
            Fill = baseGrad,
            Stroke = Frozen(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 1.4,
        });

        // Iridescent optical sectors (cyan / sakura / mint)
        var sectorCyan = new Path
        {
            Data = Geometry.Parse("M 50,50 L 82,14 A 48,48 0 0 1 97,46 Z M 50,50 L 18,86 A 48,48 0 0 1 3,54 Z"),
            Fill = Frozen(Color.FromArgb(0x88, 0x76, 0xE4, 0xFF)),
            Clip = discGeo,
        };
        var sectorPink = new Path
        {
            Data = Geometry.Parse("M 50,50 L 97,46 A 48,48 0 0 1 88,78 Z M 50,50 L 3,54 A 48,48 0 0 1 12,22 Z"),
            Fill = Frozen(Color.FromArgb(0x80, 0xF6, 0xA8, 0xD8)),
            Clip = discGeo,
        };
        var sectorLime = new Path
        {
            Data = Geometry.Parse("M 50,50 L 64,4 A 48,48 0 0 1 82,14 Z M 50,50 L 36,96 A 48,48 0 0 1 18,86 Z"),
            Fill = Frozen(Color.FromArgb(0x66, 0xAE, 0xF7, 0xD0)),
            Clip = discGeo,
        };
        canvas.Children.Add(sectorCyan);
        canvas.Children.Add(sectorPink);
        canvas.Children.Add(sectorLime);

        // Inner polycarbonate clamp ring
        var hubGeo = new CombinedGeometry(
            GeometryCombineMode.Exclude,
            new EllipseGeometry(new Point(50, 50), 18, 18),
            new EllipseGeometry(new Point(50, 50), 10, 10));
        hubGeo.Freeze();
        canvas.Children.Add(new Path
        {
            Data = hubGeo,
            Fill = Frozen(Color.FromArgb(0x95, 0xE2, 0xF2, 0xFC)),
            Stroke = Frozen(Color.FromArgb(0xAA, 0x8C, 0xB8, 0xD6)),
            StrokeThickness = 1.0,
        });

        return (new Viewbox { Width = size, Height = size, Child = canvas }, rotate);
    }

    private static (Viewbox Visual, RotateTransform Rotate) CreateCartoonVinyl(double size, double strokePx)
    {
        var canvas = new Canvas { Width = 100, Height = 100 };
        var rotate = new RotateTransform(0, 50, 50);
        canvas.RenderTransform = rotate;

        var ink = Frozen(Color.FromRgb(0x2A, 0x1E, 0x2E));
        var vinylBody = Frozen(Color.FromRgb(0x3B, 0x2D, 0x42));
        var groove = Frozen(Color.FromArgb(0x90, 0x6A, 0x55, 0x75));
        var pinkLabel = Frozen(Color.FromRgb(0xFF, 0x99, 0xBE));
        var yellowHub = Frozen(Color.FromRgb(0xFF, 0xE0, 0x66));

        var outer = new Ellipse { Width = 94, Height = 94, Fill = vinylBody, Stroke = ink, StrokeThickness = strokePx * (100.0 / Math.Max(22, size)) };
        Canvas.SetLeft(outer, 3);
        Canvas.SetTop(outer, 3);
        canvas.Children.Add(outer);

        // Vinyl groove arcs (makes rotation clearly visible!)
        canvas.Children.Add(new Path
        {
            Data = Geometry.Parse(
                "M 50,14 A 36,36 0 0 1 86,50 M 50,86 A 36,36 0 0 1 14,50 " +
                "M 24,28 A 34,34 0 0 1 46,16 M 76,72 A 34,34 0 0 1 54,84"),
            Stroke = groove,
            StrokeThickness = 2.2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        });

        // Pastel center label + spindle dot
        var label = new Ellipse { Width = 36, Height = 36, Fill = pinkLabel, Stroke = ink, StrokeThickness = 2.6 };
        Canvas.SetLeft(label, 32);
        Canvas.SetTop(label, 32);
        canvas.Children.Add(label);

        var spindle = new Ellipse { Width = 10, Height = 10, Fill = yellowHub, Stroke = ink, StrokeThickness = 2.2 };
        Canvas.SetLeft(spindle, 45);
        Canvas.SetTop(spindle, 45);
        canvas.Children.Add(spindle);

        return (new Viewbox { Width = size, Height = size, Child = canvas }, rotate);
    }

    private static (Viewbox Visual, RotateTransform ReelL, RotateTransform ReelR) CreatePixelCassette(bool expandedWithMiniCd)
    {
        double h = expandedWithMiniCd ? 56 : 36;
        double yOff = expandedWithMiniCd ? 20 : 0;
        var canvas = new Canvas { Width = 56, Height = h };
        RenderOptions.SetEdgeMode(canvas, EdgeMode.Aliased);

        var ink = Frozen(Color.FromRgb(0x24, 0x1A, 0x34));
        var shell = Frozen(Color.FromRgb(0xA7, 0x99, 0xC4));
        var shellLight = Frozen(Color.FromRgb(0xD8, 0xCE, 0xEB));
        var labelWhite = Frozen(Color.FromRgb(0xF8, 0xF5, 0xFC));
        var stripePink = Frozen(Color.FromRgb(0xF4, 0xA4, 0xC8));
        var stripeGold = Frozen(Color.FromRgb(0xF7, 0xE0, 0x88));
        var stripeSky = Frozen(Color.FromRgb(0x7E, 0xD0, 0xF0));
        var windowDark = Frozen(Color.FromRgb(0x32, 0x28, 0x44));

        if (expandedWithMiniCd)
        {
            // Cute pixel CD peeking above the cassette deck (matches pixel_tamagotchi_expanded concept!)
            canvas.Children.Add(Aliased("M 18,2 H 38 V 4 H 42 V 8 H 44 V 22 H 12 V 8 H 14 V 4 H 18 Z", ink));
            canvas.Children.Add(Aliased("M 18,4 H 38 V 8 H 42 V 22 H 14 V 8 H 18 Z", Frozen(Color.FromRgb(0xE6, 0xF2, 0xF8))));
            canvas.Children.Add(Aliased("M 20,6 H 26 V 12 H 20 Z M 30,12 H 38 V 18 H 30 Z", stripeSky));
            canvas.Children.Add(Aliased("M 30,6 H 36 V 11 H 30 Z", stripePink));
            canvas.Children.Add(Aliased("M 25,11 H 31 V 17 H 25 Z", ink));
            canvas.Children.Add(Aliased("M 27,13 H 29 V 15 H 27 Z", shellLight));
        }

        // Cassette outer pixel frame
        var tg = new TranslateTransform(0, yOff);
        var body = new Canvas { Width = 56, Height = 36, RenderTransform = tg };
        RenderOptions.SetEdgeMode(body, EdgeMode.Aliased);

        body.Children.Add(Aliased("M 2,2 H 54 V 34 H 2 Z", ink));
        body.Children.Add(Aliased("M 4,4 H 52 V 32 H 4 Z", shell));
        body.Children.Add(Aliased("M 4,4 H 52 V 6 H 4 Z M 4,6 H 6 V 30 H 4 Z", shellLight));

        // Pastel rainbow cassette label
        body.Children.Add(Aliased("M 8,7 H 48 V 23 H 8 Z", labelWhite));
        body.Children.Add(Aliased("M 8,17 H 48 V 19 H 8 Z", stripePink));
        body.Children.Add(Aliased("M 8,19 H 48 V 21 H 8 Z", stripeGold));
        body.Children.Add(Aliased("M 8,21 H 48 V 23 H 8 Z", stripeSky));

        // Tape window + two spinning pixel reels
        body.Children.Add(Aliased("M 12,10 H 44 V 20 H 12 Z", windowDark));
        body.Children.Add(Aliased("M 25,12 H 31 V 18 H 25 Z", Frozen(Color.FromRgb(0x6E, 0x5E, 0x82))));

        var (reel1, rot1) = CreatePixelReel(18, 15);
        var (reel2, rot2) = CreatePixelReel(38, 15);
        body.Children.Add(reel1);
        body.Children.Add(reel2);

        // Bottom trapezoid head guard & screw dots
        body.Children.Add(Aliased("M 12,26 H 44 V 32 H 12 Z", Frozen(Color.FromRgb(0x88, 0x78, 0xA8))));
        body.Children.Add(Aliased("M 16,28 H 19 V 31 H 16 Z M 37,28 H 40 V 31 H 37 Z M 6,28 H 8 V 30 H 6 Z M 48,28 H 50 V 30 H 48 Z", ink));

        canvas.Children.Add(body);
        return (new Viewbox { Stretch = Stretch.Uniform, Child = canvas }, rot1, rot2);
    }

    private static (Canvas Reel, RotateTransform Rotate) CreatePixelReel(int cx, int cy)
    {
        var c = new Canvas { Width = 56, Height = 36 };
        var rot = new RotateTransform(0, cx, cy);
        c.RenderTransform = rot;
        c.Children.Add(Aliased(FormattableString.Invariant(
            $"M {cx - 4},{cy - 4} H {cx + 4} V {cy + 4} H {cx - 4} Z"),
            Frozen(Color.FromRgb(0xEE, 0xEC, 0xF5))));
        c.Children.Add(Aliased(FormattableString.Invariant(
            $"M {cx - 2},{cy - 2} H {cx + 2} V {cy + 2} H {cx - 2} Z M {cx - 4},{cy - 1} H {cx - 2} V {cy + 1} H {cx - 4} Z M {cx + 2},{cy - 1} H {cx + 4} V {cy + 1} H {cx + 2} Z"),
            Frozen(Color.FromRgb(0x24, 0x1A, 0x34))));
        return (c, rot);
    }

    private static Path Aliased(string data, Brush fill)
    {
        var g = Geometry.Parse(data);
        g.Freeze();
        var p = new Path { Data = g, Fill = fill };
        RenderOptions.SetEdgeMode(p, EdgeMode.Aliased);
        return p;
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public void Dispose() => spinTimer.Stop();
}
