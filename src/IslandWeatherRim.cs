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
/// Renders delicate vector weather atmosphere along the island's upper rim:
/// snowdrifts, mini-icicles and snowflakes during snow / freezing cold (&lt;= -3°C),
/// or translucent glossy raindrops during rain / drizzle / thunderstorm.
/// Zero redraws when ReduceMotion, !AnimateHoshino, hidden, or GameModeEnabled.
/// </summary>
public sealed class IslandWeatherRim : Canvas, IDisposable
{
    private enum RimWeatherKind { None, Snow, Rain }

    private readonly AppSettings settings;
    private readonly DispatcherTimer shimmerTimer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly List<(UIElement Element, TranslateTransform Drift, double Phase)> animatedItems = new();

    private bool suspended;
    private bool islandHidden;
    private string stateKey = "";

    private static readonly Brush SnowFill = Frozen(Color.FromRgb(0xF6, 0xFB, 0xFF));
    private static readonly Brush SnowShadow = Frozen(Color.FromRgb(0xAD, 0xDA, 0xF5));
    private static readonly Brush FlakeStroke = Frozen(Color.FromRgb(0xCA, 0xEC, 0xFF));
    private static readonly Brush DarkOutline = Frozen(Color.FromRgb(0x2A, 0x1E, 0x2E));
    private static readonly Brush DropFill = BuildDropGradient();
    private static readonly Brush DropRim = Frozen(Color.FromArgb(0xB8, 0xCA, 0xEE, 0xFF));
    private static readonly Brush SpecularDot = Frozen(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF));

    public IslandWeatherRim(AppSettings settings)
    {
        this.settings = settings;
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
        shimmerTimer.Tick += OnShimmerTick;
        IsVisibleChanged += (_, _) => SyncTimer();
    }

    public void SetSuspended(bool value)
    {
        if (suspended == value) return;
        suspended = value;
        SyncTimer();
    }

    public void Update(WeatherReport? report, double baseWidth, double pillWidth, double pillTop, bool isNotch, bool hidden)
    {
        islandHidden = hidden;
        var kind = Classify(report);
        if (kind == RimWeatherKind.None || pillWidth < 90 || hidden)
        {
            if (Visibility != Visibility.Collapsed)
            {
                Visibility = Visibility.Collapsed;
                Children.Clear();
                animatedItems.Clear();
                stateKey = "";
            }
            SyncTimer();
            return;
        }

        // Quantize width to 4px while morphing so we don't rebuild geometry excessively mid-spring
        var qw = Math.Round(pillWidth / 4.0) * 4.0;
        var qt = Math.Round(pillTop);
        var nextKey = FormattableString.Invariant($"{kind}|{qw:0}|{qt:0}|{isNotch}|{settings.IslandSkin}");
        if (nextKey != stateKey)
        {
            stateKey = nextKey;
            Rebuild(kind, baseWidth, qw, qt, isNotch);
        }

        Visibility = Visibility.Visible;
        SyncTimer();
    }

    private RimWeatherKind Classify(WeatherReport? report)
    {
        if (!settings.WeatherEnabled || !settings.WeatherRimEnabled || report is null)
            return RimWeatherKind.None;

        var tempC = report.Fahrenheit ? (report.Temperature - 32.0) * 5.0 / 9.0 : report.Temperature;
        if (report.Kind == WeatherKind.Snow || tempC <= -3.0)
            return RimWeatherKind.Snow;

        if (report.Kind is WeatherKind.Drizzle or WeatherKind.Rain or WeatherKind.Thunder)
            return RimWeatherKind.Rain;

        return RimWeatherKind.None;
    }

    private void Rebuild(RimWeatherKind kind, double baseWidth, double w, double top, bool isNotch)
    {
        Children.Clear();
        animatedItems.Clear();

        var left = (baseWidth - w) / 2.0;
        var right = left + w;
        var rimY = isNotch ? 0.0 : Math.Max(4.5, top);
        var cartoon = settings.IslandSkin == IslandSkin.ChibiPop;
        var pixel = settings.IslandSkin == IslandSkin.PixelTamagotchi;
        RenderOptions.SetEdgeMode(this, pixel ? EdgeMode.Aliased : EdgeMode.Unspecified);

        if (kind == RimWeatherKind.Snow)
        {
            var leftSpan = Math.Min(50.0, w * 0.22);
            var rightSpan = Math.Min(42.0, w * 0.18);
            var inset = Math.Min(20.0, w * 0.09);

            // Delicate shallow frosted snow caps hugging the top rim (never drooping over tabs!)
            var leftDrift = BuildSnowDrift(left + inset, rimY, leftSpan, flushTop: isNotch);
            Children.Add(new Path
            {
                Data = leftDrift,
                Fill = SnowFill,
                Stroke = cartoon ? DarkOutline : SnowShadow,
                StrokeThickness = cartoon ? 1.5 : 1.0,
                StrokeLineJoin = PenLineJoin.Round,
            });

            var rightDrift = BuildSnowDrift(right - rightSpan - inset, rimY, rightSpan, flushTop: isNotch);
            Children.Add(new Path
            {
                Data = rightDrift,
                Fill = SnowFill,
                Stroke = cartoon ? DarkOutline : SnowShadow,
                StrokeThickness = cartoon ? 1.5 : 1.0,
                StrokeLineJoin = PenLineJoin.Round,
            });

            var flakeBaseY = isNotch ? 5.5 : rimY + 3.5;
            AddSnowflake(left + Math.Max(8, inset * 0.55), flakeBaseY, 4.0, 0.0);
            AddSnowflake(right - Math.Max(8, inset * 0.55), flakeBaseY + 1.0, 3.6, 2.4);
        }
        else if (kind == RimWeatherKind.Rain)
        {
            var dropBaseY = isNotch ? 2.0 : rimY + 1.5;
            var inset = Math.Min(22.0, w * 0.10);
            AddRaindrop(left + inset, dropBaseY, 0.78, 0.0);
            AddRaindrop(left + inset + 18, dropBaseY + 2.5, 0.62, 1.6);
            AddRaindrop(right - inset - 18, dropBaseY + 1.0, 0.66, 2.9);
            AddRaindrop(right - inset, dropBaseY + 2.5, 0.75, 4.2);
        }
    }

    private static Geometry BuildSnowDrift(double x, double y, double span, bool flushTop)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            if (flushTop)
            {
                ctx.BeginFigure(new Point(x, 0.5), isFilled: true, isClosed: true);
                ctx.LineTo(new Point(x + span, 0.5), true, true);
                ctx.BezierTo(
                    new Point(x + span * 0.80, 3.4),
                    new Point(x + span * 0.56, 3.8),
                    new Point(x + span * 0.42, 2.5), true, true);
                ctx.BezierTo(
                    new Point(x + span * 0.24, 3.6),
                    new Point(x + span * 0.08, 2.8),
                    new Point(x, 0.5), true, true);
            }
            else
            {
                ctx.BeginFigure(new Point(x, y + 1.4), isFilled: true, isClosed: true);
                ctx.BezierTo(
                    new Point(x + span * 0.20, y - 1.4),
                    new Point(x + span * 0.44, y - 1.8),
                    new Point(x + span * 0.60, y - 0.5), true, true);
                ctx.BezierTo(
                    new Point(x + span * 0.76, y - 1.5),
                    new Point(x + span * 0.90, y - 0.9),
                    new Point(x + span, y + 1.4), true, true);
                ctx.BezierTo(
                    new Point(x + span * 0.72, y + 3.0),
                    new Point(x + span * 0.28, y + 3.0),
                    new Point(x, y + 1.4), true, true);
            }
        }
        geo.Freeze();
        return geo;
    }

    private void AddSnowflake(double cx, double cy, double r, double phase)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            for (var i = 0; i < 3; i++)
            {
                var ang = i * Math.PI / 3.0;
                var dx = Math.Cos(ang) * r;
                var dy = Math.Sin(ang) * r;
                ctx.BeginFigure(new Point(cx - dx, cy - dy), false, false);
                ctx.LineTo(new Point(cx + dx, cy + dy), true, false);
            }
        }
        geo.Freeze();

        var drift = new TranslateTransform();
        var flake = new Path
        {
            Data = geo,
            Stroke = FlakeStroke,
            StrokeThickness = 1.25,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Opacity = 0.88,
            RenderTransform = drift,
        };
        Children.Add(flake);
        animatedItems.Add((flake, drift, phase));
    }

    private void AddRaindrop(double cx, double cy, double scale, double phase)
    {
        var group = new Canvas { Width = 12, Height = 16 };
        var drift = new TranslateTransform(cx - 6, cy);
        var tg = new TransformGroup();
        tg.Children.Add(new ScaleTransform(scale, scale, 6, 4));
        tg.Children.Add(drift);
        group.RenderTransform = tg;

        var dropGeo = Geometry.Parse("M 6,0 C 6,0 10.2,5.6 10.2,8.6 A 4.2,4.2 0 1 1 1.8,8.6 C 1.8,5.6 6,0 6,0 Z");
        dropGeo.Freeze();
        group.Children.Add(new Path
        {
            Data = dropGeo,
            Fill = DropFill,
            Stroke = DropRim,
            StrokeThickness = 0.9,
        });

        var spec = new Ellipse { Width = 2.1, Height = 3.0, Fill = SpecularDot };
        SetLeft(spec, 3.8);
        SetTop(spec, 6.2);
        group.Children.Add(spec);

        Children.Add(group);
        animatedItems.Add((group, drift, phase));
    }

    private void SyncTimer()
    {
        var canAnimate = Visibility == Visibility.Visible
                         && IsVisible
                         && !suspended
                         && !islandHidden
                         && !settings.ReduceMotion
                         && settings.AnimateHoshino
                         && animatedItems.Count > 0;

        if (canAnimate)
        {
            if (!shimmerTimer.IsEnabled) shimmerTimer.Start();
        }
        else
        {
            shimmerTimer.Stop();
        }
    }

    private void OnShimmerTick(object? sender, EventArgs e)
    {
        var t = clock.Elapsed.TotalSeconds;
        foreach (var (el, drift, phase) in animatedItems)
        {
            var wave = Math.Sin(t * 1.4 + phase);
            if (el is Path)
            {
                drift.Y = wave * 1.2;
                el.Opacity = 0.72 + 0.24 * Math.Abs(wave);
            }
            else
            {
                // Subtle droplet bead pulse without moving base coordinate drift.X
                el.Opacity = 0.78 + 0.20 * wave;
            }
        }
    }

    private static Brush BuildDropGradient()
    {
        var grad = new LinearGradientBrush { StartPoint = new Point(0.2, 0), EndPoint = new Point(0.8, 1) };
        grad.GradientStops.Add(new GradientStop(Color.FromArgb(0xD8, 0xBD, 0xEA, 0xFF), 0));
        grad.GradientStops.Add(new GradientStop(Color.FromArgb(0x88, 0x67, 0xBE, 0xEE), 1));
        grad.Freeze();
        return grad;
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public void Dispose() => shimmerTimer.Stop();
}
