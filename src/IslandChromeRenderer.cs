// Original HoshiBar code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DIsland.Services;

namespace DIsland;

/// <summary>
/// Unified Bento geometry &amp; chrome renderer for floating Island and top-edge Notch modes across all 5 skins,
/// including open-top Notch rims, Perimeter Focus Timer contour, vector tab icons, Tamagotchi keychain bubble,
/// and 3-color Voice Focus waveform.
/// </summary>
public static class IslandChromeRenderer
{
    public readonly record struct IslandGeometrySet(Geometry Body, PathGeometry Rim, double Left, double Top, double Width, double Height);

    public static IslandGeometrySet BuildShapeAndRim(
        double baseWidth, double islandTopMargin, double notchShoulder,
        double w, double h, double r, double notchT, double roundness, SkinPaletteSpec spec)
    {
        var x = (baseWidth - w) / 2.0;
        var morphLower = Math.Max(0, 1 - notchT / 0.4);
        var morphSquare = Math.Clamp((notchT - 0.4) / 0.3, 0, 1);
        var morphFlare = Math.Clamp((notchT - 0.7) / 0.3, 0, 1);
        var top = islandTopMargin * morphLower;

        if (spec.IsPixelMode)
        {
            x = Math.Round(x);
            top = Math.Round(top);
            w = Math.Round(w);
            h = Math.Round(h);
            var step = h > 90 ? 4.0 : 3.0;
            if (Math.Min(w, h) < step * 5) step = Math.Max(1.0, Math.Floor(Math.Min(w, h) / 5.0));
            return notchT <= 0.01
                ? BuildPixelIsland(x, top, w, h, step)
                : BuildPixelNotch(x, top, w, h, step, Math.Round(notchShoulder * morphFlare));
        }

        if (notchT <= 0.001)
        {
            var body = new RectangleGeometry(new Rect(x, top, w, h), r, r);
            body.Freeze();
            var rim = BuildRoundedRectRim(x, top, w, h, r);
            return new IslandGeometrySet(body, rim, x, top, w, h);
        }

        var bottom = top + h;
        var topRadius = r * (1 - morphSquare);
        var s = Math.Min(notchShoulder * Math.Max(roundness, 0.25), h / 2.0) * morphFlare;

        var notchBody = BuildSmoothNotchBody(x, top, bottom, w, r, topRadius, s);
        var notchRim = BuildSmoothNotchOpenRim(x, top, bottom, w, r, topRadius, s);
        return new IslandGeometrySet(notchBody, notchRim, x, top, w, h);
    }

    private static PathGeometry BuildRoundedRectRim(double x, double y, double w, double h, double r)
    {
        r = Math.Clamp(r, 0, Math.Min(w, h) / 2.0);
        var pg = new PathGeometry();
        var fig = new PathFigure { StartPoint = new Point(x + w / 2.0, y), IsClosed = true, IsFilled = false };
        fig.Segments.Add(new LineSegment(new Point(x + w - r, y), true));
        if (r > 0) fig.Segments.Add(new ArcSegment(new Point(x + w, y + r), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(new Point(x + w, y + h - r), true));
        if (r > 0) fig.Segments.Add(new ArcSegment(new Point(x + w - r, y + h), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(new Point(x + r, y + h), true));
        if (r > 0) fig.Segments.Add(new ArcSegment(new Point(x, y + h - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(new Point(x, y + r), true));
        if (r > 0) fig.Segments.Add(new ArcSegment(new Point(x + r, y), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(new Point(x + w / 2.0, y), true));
        pg.Figures.Add(fig);
        pg.Freeze();
        return pg;
    }

    private static Geometry BuildSmoothNotchBody(double x, double top, double bottom, double w, double r, double topRadius, double s)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            if (s > 0)
            {
                ctx.BeginFigure(new Point(x - s, top), isFilled: true, isClosed: true);
                ctx.QuadraticBezierTo(new Point(x, top), new Point(x, top + s), true, true);
            }
            else
            {
                ctx.BeginFigure(new Point(x, top + topRadius), isFilled: true, isClosed: true);
            }
            ctx.LineTo(new Point(x, bottom - r), true, true);
            if (r > 0) ctx.ArcTo(new Point(x + r, bottom), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, true);
            ctx.LineTo(new Point(x + w - r, bottom), true, true);
            if (r > 0) ctx.ArcTo(new Point(x + w, bottom - r), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, true);
            if (s > 0)
            {
                ctx.LineTo(new Point(x + w, top + s), true, true);
                ctx.QuadraticBezierTo(new Point(x + w, top), new Point(x + w + s, top), true, true);
            }
            else
            {
                ctx.LineTo(new Point(x + w, top + topRadius), true, true);
                if (topRadius > 0)
                    ctx.ArcTo(new Point(x + w - topRadius, top), new Size(topRadius, topRadius), 0, false, SweepDirection.Counterclockwise, true, true);
                ctx.LineTo(new Point(x + topRadius, top), true, true);
                if (topRadius > 0)
                    ctx.ArcTo(new Point(x, top + topRadius), new Size(topRadius, topRadius), 0, false, SweepDirection.Counterclockwise, true, true);
            }
        }
        geo.Freeze();
        return geo;
    }

    /// <summary>Open-top Notch rim: flows from left shoulder down around bottom to right shoulder, ZERO line on y = 0.</summary>
    private static PathGeometry BuildSmoothNotchOpenRim(double x, double top, double bottom, double w, double r, double topRadius, double s)
    {
        var pg = new PathGeometry();
        if (s > 0)
        {
            var fig = new PathFigure { StartPoint = new Point(x - s, top), IsClosed = false, IsFilled = false };
            fig.Segments.Add(new QuadraticBezierSegment(new Point(x, top), new Point(x, top + s), true));
            fig.Segments.Add(new LineSegment(new Point(x, bottom - r), true));
            if (r > 0) fig.Segments.Add(new ArcSegment(new Point(x + r, bottom), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true));
            fig.Segments.Add(new LineSegment(new Point(x + w - r, bottom), true));
            if (r > 0) fig.Segments.Add(new ArcSegment(new Point(x + w, bottom - r), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true));
            fig.Segments.Add(new LineSegment(new Point(x + w, top + s), true));
            fig.Segments.Add(new QuadraticBezierSegment(new Point(x + w, top), new Point(x + w + s, top), true));
            pg.Figures.Add(fig);
        }
        else if (top < 0.5 && topRadius < 0.5)
        {
            var fig = new PathFigure { StartPoint = new Point(x, top), IsClosed = false, IsFilled = false };
            fig.Segments.Add(new LineSegment(new Point(x, bottom - r), true));
            if (r > 0) fig.Segments.Add(new ArcSegment(new Point(x + r, bottom), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true));
            fig.Segments.Add(new LineSegment(new Point(x + w - r, bottom), true));
            if (r > 0) fig.Segments.Add(new ArcSegment(new Point(x + w, bottom - r), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true));
            fig.Segments.Add(new LineSegment(new Point(x + w, top), true));
            pg.Figures.Add(fig);
        }
        else
        {
            return BuildRoundedRectRim(x, top, w, bottom - top, Math.Max(r, topRadius));
        }
        pg.Freeze();
        return pg;
    }

    /// <summary>True 90-degree multi-step pixel staircase corners (zero diagonal lines).</summary>
    private static IslandGeometrySet BuildPixelIsland(double x, double y, double w, double h, double s)
    {
        var s2 = s * 2.0;
        Point[] pts =
        {
            new(x + s2, y),
            new(x + w - s2, y),
            new(x + w - s2, y + s),
            new(x + w - s, y + s),
            new(x + w - s, y + s2),
            new(x + w, y + s2),
            new(x + w, y + h - s2),
            new(x + w - s, y + h - s2),
            new(x + w - s, y + h - s),
            new(x + w - s2, y + h - s),
            new(x + w - s2, y + h),
            new(x + s2, y + h),
            new(x + s2, y + h - s),
            new(x + s, y + h - s),
            new(x + s, y + h - s2),
            new(x, y + h - s2),
            new(x, y + s2),
            new(x + s, y + s2),
            new(x + s, y + s),
            new(x + s2, y + s),
        };

        var body = new StreamGeometry();
        using (var ctx = body.Open())
        {
            ctx.BeginFigure(pts[0], isFilled: true, isClosed: true);
            for (var i = 1; i < pts.Length; i++) ctx.LineTo(pts[i], true, false);
        }
        body.Freeze();

        var rim = new PathGeometry();
        var fig = new PathFigure { StartPoint = new Point(x + w / 2.0, y), IsClosed = true, IsFilled = false };
        for (var i = 1; i < pts.Length; i++) fig.Segments.Add(new LineSegment(pts[i], true));
        fig.Segments.Add(new LineSegment(pts[0], true));
        fig.Segments.Add(new LineSegment(new Point(x + w / 2.0, y), true));
        rim.Figures.Add(fig);
        rim.Freeze();

        return new IslandGeometrySet(body, rim, x, y, w, h);
    }

    private static IslandGeometrySet BuildPixelNotch(double x, double top, double w, double h, double s, double shoulderRaw)
    {
        var bottom = top + h;
        var s2 = s * 2.0;
        var sh = Math.Max(0, Math.Round(shoulderRaw / 2.0) * 2.0);
        var shHalf = Math.Round(sh / 2.0);

        Point[] openPts = sh >= 2.0
            ? new Point[]
            {
                new(x - sh, top),
                new(x - sh, top + shHalf),
                new(x - shHalf, top + shHalf),
                new(x - shHalf, top + sh),
                new(x, top + sh),
                new(x, bottom - s2),
                new(x + s, bottom - s2),
                new(x + s, bottom - s),
                new(x + s2, bottom - s),
                new(x + s2, bottom),
                new(x + w - s2, bottom),
                new(x + w - s2, bottom - s),
                new(x + w - s, bottom - s),
                new(x + w - s, bottom - s2),
                new(x + w, bottom - s2),
                new(x + w, top + sh),
                new(x + w + shHalf, top + sh),
                new(x + w + shHalf, top + shHalf),
                new(x + w + sh, top + shHalf),
                new(x + w + sh, top),
            }
            : new Point[]
            {
                new(x, top),
                new(x, bottom - s2),
                new(x + s, bottom - s2),
                new(x + s, bottom - s),
                new(x + s2, bottom - s),
                new(x + s2, bottom),
                new(x + w - s2, bottom),
                new(x + w - s2, bottom - s),
                new(x + w - s, bottom - s),
                new(x + w - s, bottom - s2),
                new(x + w, bottom - s2),
                new(x + w, top),
            };

        var body = new StreamGeometry();
        using (var ctx = body.Open())
        {
            ctx.BeginFigure(openPts[0], isFilled: true, isClosed: true);
            for (var i = 1; i < openPts.Length; i++) ctx.LineTo(openPts[i], true, false);
        }
        body.Freeze();

        var rim = new PathGeometry();
        var fig = new PathFigure { StartPoint = openPts[0], IsClosed = false, IsFilled = false };
        for (var i = 1; i < openPts.Length; i++) fig.Segments.Add(new LineSegment(openPts[i], true));
        rim.Figures.Add(fig);
        rim.Freeze();

        return new IslandGeometrySet(body, rim, x, top, w, h);
    }

    /// <summary>Updates the depleting Perimeter Focus Timer along the collapsed island / notch rim using exact continuous arc slicing (zero StrokeDashArray dots).</summary>
    public static void UpdatePerimeterFocus(
        Path dimTrack, Path activeArc, Ellipse tipDot,
        PathGeometry rim, SkinPaletteSpec spec, bool visible, double remainingRatio)
    {
        if (!visible || remainingRatio <= 0.001)
        {
            dimTrack.Visibility = Visibility.Collapsed;
            activeArc.Visibility = Visibility.Collapsed;
            tipDot.Visibility = Visibility.Collapsed;
            return;
        }

        var ratio = Math.Clamp(remainingRatio, 0.002, 1.0);
        var strokeWidth = spec.IsCartoonSticker ? 3.2 : spec.IsPixelMode ? 2.6 : 2.4;

        dimTrack.Data = rim;
        dimTrack.StrokeThickness = strokeWidth;
        dimTrack.Stroke = IslandSkinEngine.Frozen(Color.FromArgb(0x28, spec.RimCenter.R, spec.RimCenter.G, spec.RimCenter.B));
        dimTrack.Visibility = Visibility.Visible;

        var (sliced, tip) = SliceRimGeometry(rim, ratio);
        activeArc.StrokeDashArray = null;
        activeArc.Data = sliced;
        activeArc.StrokeThickness = strokeWidth;
        activeArc.StrokeStartLineCap = spec.IsPixelMode ? PenLineCap.Square : PenLineCap.Round;
        activeArc.StrokeEndLineCap = spec.IsPixelMode ? PenLineCap.Square : PenLineCap.Round;
        activeArc.Stroke = spec.IsCartoonSticker
            ? IslandSkinEngine.Frozen(Color.FromRgb(0xFF, 0x85, 0xAD))
            : spec.IsPixelMode
                ? IslandSkinEngine.Frozen(Color.FromRgb(0x38, 0xA8, 0x8E))
                : spec.IsShittimTablet || spec.IsAeroGlass
                    ? IslandSkinEngine.Frozen(Color.FromRgb(0x38, 0xBD, 0xF8))
                    : IslandSkinEngine.CreateRimBrush(spec);
        activeArc.Visibility = Visibility.Visible;

        tipDot.Width = tipDot.Height = spec.IsPixelMode ? 6 : 7.5;
        tipDot.Fill = IslandSkinEngine.Frozen(spec.IsShittimTablet || spec.IsAeroGlass
            ? Color.FromRgb(0x38, 0xBD, 0xF8)
            : spec.IsCartoonSticker
                ? Color.FromRgb(0xFF, 0xE0, 0x66)
                : spec.Gold);
        tipDot.Stroke = spec.IsCartoonSticker ? IslandSkinEngine.Frozen(Color.FromRgb(0x2A, 0x1E, 0x2E)) : Brushes.White;
        tipDot.StrokeThickness = spec.IsCartoonSticker ? 1.5 : 1.2;
        Canvas.SetLeft(tipDot, tip.X - tipDot.Width / 2.0);
        Canvas.SetTop(tipDot, tip.Y - tipDot.Height / 2.0);
        tipDot.Visibility = Visibility.Visible;
    }

    private static (Geometry Sliced, Point Tip) SliceRimGeometry(PathGeometry rim, double ratio)
    {
        // Clone with IsFilled = true so GetFlattenedPathGeometry never skips open/unfilled strokes!
        var temp = rim.CloneCurrentValue();
        foreach (var f in temp.Figures) f.IsFilled = true;
        var flat = temp.GetFlattenedPathGeometry(0.35, ToleranceType.Absolute);

        var pts = new List<Point>();
        foreach (var fig in flat.Figures)
        {
            pts.Add(fig.StartPoint);
            foreach (var seg in fig.Segments)
            {
                if (seg is LineSegment ls) pts.Add(ls.Point);
                else if (seg is PolyLineSegment pls) pts.AddRange(pls.Points);
            }
        }

        if (pts.Count < 2)
            return (Geometry.Empty, pts.Count == 1 ? pts[0] : new Point());

        var totalLen = 0.0;
        for (var i = 1; i < pts.Count; i++)
            totalLen += (pts[i] - pts[i - 1]).Length;

        var targetLen = totalLen * Math.Clamp(ratio, 0.001, 1.0);
        var walked = 0.0;
        var tip = pts[0];

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(pts[0], isFilled: false, isClosed: false);
            for (var i = 1; i < pts.Count; i++)
            {
                var segLen = (pts[i] - pts[i - 1]).Length;
                if (segLen <= 0.0001) continue;
                if (walked + segLen >= targetLen)
                {
                    var t = (targetLen - walked) / segLen;
                    tip = new Point(
                        pts[i - 1].X + (pts[i].X - pts[i - 1].X) * t,
                        pts[i - 1].Y + (pts[i].Y - pts[i - 1].Y) * t);
                    ctx.LineTo(tip, true, true);
                    break;
                }
                ctx.LineTo(pts[i], true, true);
                tip = pts[i];
                walked += segLen;
            }
        }
        geo.Freeze();
        return (geo, tip);
    }

    /// <summary>Creates the skin-adaptive vector Speaker / Brightness icon for compact OsdView matching all 5 design renders.</summary>
    public static FrameworkElement CreateOsdVectorIcon(bool isVolume, float level, bool muted, SkinPaletteSpec spec)
    {
        if (spec.IsPixelMode)
        {
            var px = new Canvas { Width = 16, Height = 16 };
            RenderOptions.SetEdgeMode(px, EdgeMode.Aliased);
            var ink = IslandSkinEngine.Frozen(Color.FromRgb(0x14, 0x2D, 0x2A));
            if (isVolume)
            {
                px.Children.Add(new Path { Data = Geometry.Parse("M 2,6 H 5 V 4 H 7 V 2 H 9 V 14 H 7 V 12 H 5 V 10 H 2 Z"), Fill = ink });
                if (muted || level <= 0.001f)
                    px.Children.Add(new Path { Data = Geometry.Parse("M 11,5 H 13 V 7 H 15 V 9 H 13 V 11 H 11 V 9 H 13 V 7 H 11 Z"), Fill = ink });
                else
                    px.Children.Add(new Path { Data = Geometry.Parse("M 11,6 H 12 V 10 H 11 Z M 13,4 H 15 V 12 H 13 V 10 H 14 V 6 H 13 Z"), Fill = ink });
            }
            else
            {
                px.Children.Add(new Path { Data = Geometry.Parse("M 5,5 H 11 V 11 H 5 Z M 7,1 H 9 V 3 H 7 Z M 7,13 H 9 V 15 H 7 Z M 1,7 H 3 V 9 H 1 Z M 13,7 H 15 V 9 H 13 Z"), Fill = ink });
            }
            return new Viewbox { Width = 20, Height = 20, Child = px };
        }

        var c = new Canvas { Width = 24, Height = 24 };
        var isSticker = spec.IsCartoonSticker;
        var stroke = isSticker
            ? IslandSkinEngine.Frozen(Color.FromRgb(0x2A, 0x1E, 0x2E))
            : spec.IsAeroGlass
                ? Brushes.White
                : IslandSkinEngine.Frozen(spec.Text);

        Brush? coneFill = null;
        if (isSticker)
        {
            coneFill = IslandSkinEngine.Frozen(Color.FromRgb(0xFF, 0xE0, 0x66));
        }
        else if (spec.IsAeroGlass)
        {
            var metal = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            metal.GradientStops.Add(new GradientStop(Colors.White, 0));
            metal.GradientStops.Add(new GradientStop(Color.FromRgb(0x98, 0xD2, 0xF8), 0.55));
            metal.GradientStops.Add(new GradientStop(Color.FromRgb(0xDC, 0xF4, 0xFF), 1));
            metal.Freeze();
            coneFill = metal;
        }
        else if (spec.IsShittimTablet)
        {
            coneFill = IslandSkinEngine.Frozen(spec.Text);
        }

        if (isVolume)
        {
            c.Children.Add(new Path
            {
                Data = Geometry.Parse("M 3,9.2 H 7.2 L 12.6,4.6 V 19.4 L 7.2,14.8 H 3 Z"),
                Fill = coneFill,
                Stroke = stroke,
                StrokeThickness = isSticker ? 2.0 : spec.IsAeroGlass ? 1.2 : 1.7,
                StrokeLineJoin = PenLineJoin.Round,
            });

            if (muted || level <= 0.001f)
            {
                c.Children.Add(new Path
                {
                    Data = Geometry.Parse("M 15.8,9.2 L 20.8,14.8 M 20.8,9.2 L 15.8,14.8"),
                    Stroke = stroke,
                    StrokeThickness = isSticker ? 2.1 : 1.8,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                });
            }
            else
            {
                var arcs = level < 0.45f
                    ? "M 15.8,9.2 A 4.2,4.2 0 0 1 15.8,14.8"
                    : "M 15.6,9.2 A 4.2,4.2 0 0 1 15.6,14.8 M 18.4,6.6 A 7.6,7.6 0 0 1 18.4,17.4";
                c.Children.Add(new Path
                {
                    Data = Geometry.Parse(arcs),
                    Stroke = stroke,
                    StrokeThickness = isSticker ? 2.1 : 1.85,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                });
            }
        }
        else
        {
            c.Children.Add(new Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = coneFill,
                Stroke = stroke,
                StrokeThickness = isSticker ? 2.0 : 1.7,
                Margin = new Thickness(7.5, 7.5, 0, 0),
            });
            c.Children.Add(new Path
            {
                Data = Geometry.Parse("M 12,2.5 V 5 M 12,19 V 21.5 M 2.5,12 H 5 M 19,12 H 21.5 M 5.3,5.3 L 7.1,7.1 M 16.9,16.9 L 18.7,18.7 M 5.3,18.7 L 7.1,16.9 M 16.9,7.1 L 18.7,5.3"),
                Stroke = stroke,
                StrokeThickness = isSticker ? 2.0 : 1.7,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            });
        }

        return new Viewbox { Width = 22, Height = 22, Child = c };
    }

    /// <summary>Renders skin-specific surface &amp; frame chrome (Aero light bands, Schale notches, Pixel 3D bevel + Win98 bar + screws).</summary>
    public static void PopulateSkinChrome(
        Canvas clippedOverlay, Canvas outerFrameOverlay,
        IslandGeometrySet geo, SkinPaletteSpec spec, bool isExpanded, bool isNotch)
    {
        clippedOverlay.Children.Clear();
        outerFrameOverlay.Children.Clear();
        double x = geo.Left, y = geo.Top, w = geo.Width, h = geo.Height;
        if (w < 40 || h < 20) return;

        RenderOptions.SetEdgeMode(clippedOverlay, spec.IsPixelMode ? EdgeMode.Aliased : EdgeMode.Unspecified);
        RenderOptions.SetEdgeMode(outerFrameOverlay, spec.IsPixelMode ? EdgeMode.Aliased : EdgeMode.Unspecified);

        if (spec.Skin == IslandSkin.HorusGlass)
        {
            // Subtle diagonal dark-glass sheen band on left half (no muddy glow)
            var sheen = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0.35) };
            sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF), 0.0));
            sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0x07, 0xFF, 0xFF, 0xFF), 0.48));
            sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.50));
            sheen.Freeze();
            var rect = new Rectangle { Width = w, Height = h, Fill = sheen };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            clippedOverlay.Children.Add(rect);
        }
        else if (spec.IsShittimTablet)
        {
            // Matte frosted milky-cyan translucency: ZERO glossy diagonal reflection streaks!
            // Instead, soft uniform frosted edge halo + Schale OS geometric dot-matrix halftone + skewed parallelogram tabs.
            var frostEdge = new Border
            {
                Width = Math.Max(0, w - 2),
                Height = Math.Max(0, h - 2),
                CornerRadius = new CornerRadius(isExpanded ? 21 : Math.Min(21, h / 2.0)),
                BorderBrush = IslandSkinEngine.Frozen(Color.FromArgb(0x98, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1.1),
            };
            Canvas.SetLeft(frostEdge, x + 1);
            Canvas.SetTop(frostEdge, y + 1);
            clippedOverlay.Children.Add(frostEdge);

            var cyan = IslandSkinEngine.Frozen(Color.FromRgb(0x38, 0xBD, 0xF8));
            var pink = IslandSkinEngine.Frozen(Color.FromRgb(0xFF, 0x85, 0xAD));
            if (isExpanded && w > 300)
            {
                // Subtle Schale OS dot-matrix halftone grid in bottom-right & top-left matte polycarbonate margins
                for (var dr = 0; dr < 4; dr++)
                for (var dc = 0; dc < 5; dc++)
                {
                    var dot = new Ellipse
                    {
                        Width = 2.6,
                        Height = 2.6,
                        Fill = cyan,
                        Opacity = Math.Max(0.06, 0.24 - (dr + dc) * 0.03),
                    };
                    Canvas.SetLeft(dot, x + w - 52 + dc * 8);
                    Canvas.SetTop(dot, y + h - 42 + dr * 8);
                    clippedOverlay.Children.Add(dot);
                }

                // Top-right & bottom-left skewed Schale parallelogram ticks
                if (!isNotch)
                {
                    var tx = x + w - 98;
                    outerFrameOverlay.Children.Add(MakePolygon(cyan,
                        new(tx, y - 4), new(tx + 9, y - 4), new(tx + 4, y + 1), new(tx - 5, y + 1)));
                    outerFrameOverlay.Children.Add(MakePolygon(cyan,
                        new(tx + 15, y - 4), new(tx + 38, y - 4), new(tx + 33, y + 1), new(tx + 10, y + 1)));
                    outerFrameOverlay.Children.Add(MakePolygon(pink,
                        new(tx + 43, y - 4), new(tx + 51, y - 4), new(tx + 46, y + 1), new(tx + 38, y + 1)));
                }
                var bx = x + 36;
                var by = y + h - 7;
                clippedOverlay.Children.Add(MakePolygon(cyan,
                    new(bx, by), new(bx + 7, by), new(bx + 3, by + 5), new(bx - 4, by + 5)));
                clippedOverlay.Children.Add(MakePolygon(cyan,
                    new(bx + 11, by), new(bx + 18, by), new(bx + 14, by + 5), new(bx + 7, by + 5)));
                clippedOverlay.Children.Add(MakePolygon(pink,
                    new(bx + 22, by), new(bx + 29, by), new(bx + 25, by + 5), new(bx + 18, by + 5)));
            }
            else if (!isExpanded && w > 120)
            {
                // Tiny crisp Schale skewed cyan/pink corner ticks on top-right of compact matte pill
                var cx = x + w - 26;
                var cy = y + 3;
                clippedOverlay.Children.Add(MakePolygon(cyan,
                    new(cx, cy), new(cx + 8, cy), new(cx + 5, cy + 4), new(cx - 3, cy + 4)));
                clippedOverlay.Children.Add(MakePolygon(pink,
                    new(cx + 11, cy), new(cx + 16, cy), new(cx + 13, cy + 4), new(cx + 8, cy + 4)));
            }
        }
        else if (spec.IsAeroGlass)
        {
            // Futuristic Frutiger Aero optical crystal glass: sweeping diagonal reflection panes + aqua bottom caustics + double bevel
            var aeroBands = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0.52) };
            aeroBands.GradientStops.Add(new GradientStop(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF), 0.00));
            aeroBands.GradientStops.Add(new GradientStop(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF), 0.22));
            aeroBands.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.235));
            aeroBands.GradientStops.Add(new GradientStop(Color.FromArgb(0x42, 0xFF, 0xFF, 0xFF), 0.45));
            aeroBands.GradientStops.Add(new GradientStop(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), 0.56));
            aeroBands.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.575));
            aeroBands.GradientStops.Add(new GradientStop(Color.FromArgb(0x36, 0x8C, 0xEE, 0xFF), 1.00));
            aeroBands.Freeze();

            var rect = new Rectangle { Width = w, Height = h, Fill = aeroBands };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            clippedOverlay.Children.Add(rect);

            // Top specular glass crown gloss
            var topCrown = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            topCrown.GradientStops.Add(new GradientStop(Color.FromArgb(0x48, 0xFF, 0xFF, 0xFF), 0.0));
            topCrown.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.0));
            topCrown.Freeze();
            var crownRect = new Rectangle { Width = w, Height = Math.Min(26, h * 0.46), Fill = topCrown };
            Canvas.SetLeft(crownRect, x);
            Canvas.SetTop(crownRect, y);
            clippedOverlay.Children.Add(crownRect);

            // Outer dark-sapphire edge stroke around the bright white crystal inner rim
            outerFrameOverlay.Children.Add(new Path
            {
                Data = geo.Rim,
                Stroke = IslandSkinEngine.Frozen(Color.FromArgb(0xCC, 0x05, 0x1E, 0x38)),
                StrokeThickness = 3.0,
            });
            outerFrameOverlay.Children.Add(new Path
            {
                Data = geo.Rim,
                Stroke = IslandSkinEngine.Frozen(Color.FromArgb(0xF2, 0xF2, 0xFC, 0xFF)),
                StrokeThickness = 1.25,
            });
        }
        else if (spec.IsPixelMode)
        {
            var step = h > 90 ? 4.0 : 3.0;
            var inset = step * 2.0;
            var hi = IslandSkinEngine.Frozen(Colors.White);
            var lo = IslandSkinEngine.Frozen(Color.FromRgb(0x6C, 0x5A, 0x88));

            // Crisp 2px orthogonal inner 3D bevel highlights (top/left white, bottom/right shadow)
            if (!isNotch)
            {
                clippedOverlay.Children.Add(MakePolyline(hi, 2.0,
                    new(x + 2, y + h - inset), new(x + 2, y + inset), new(x + inset, y + inset), new(x + inset, y + 2), new(x + w - inset, y + 2)));
            }
            clippedOverlay.Children.Add(MakePolyline(lo, 2.0,
                new(x + w - 2, y + (isNotch ? 2 : inset)), new(x + w - 2, y + h - inset),
                new(x + w - inset, y + h - inset), new(x + w - inset, y + h - 2), new(x + inset, y + h - 2)));

            if (isExpanded && h > 150)
            {
                // Retro pastel Win98 titlebar strip cleanly seated ABOVE PageNav (in floating Island mode)
                if (!isNotch)
                {
                    var barGrad = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
                    barGrad.GradientStops.Add(new GradientStop(Color.FromRgb(0xA6, 0xD6, 0xF2), 0));
                    barGrad.GradientStops.Add(new GradientStop(Color.FromRgb(0xF5, 0xB4, 0xD4), 1));
                    barGrad.Freeze();

                    var bar = new Border
                    {
                        Width = Math.Max(40, w - inset * 2 - 4),
                        Height = 16,
                        Background = barGrad,
                        BorderBrush = IslandSkinEngine.Frozen(Color.FromRgb(0x5B, 0x4B, 0x73)),
                        BorderThickness = new Thickness(1, 1, 1, 1.5),
                    };
                    Canvas.SetLeft(bar, x + inset + 2);
                    Canvas.SetTop(bar, y + 3);
                    clippedOverlay.Children.Add(bar);

                    // Crisp raised Win98 [ _ ][ □ ][ × ] pixel buttons with glyphs inside
                    var btnInk = IslandSkinEngine.Frozen(Color.FromRgb(0x2B, 0x20, 0x3D));
                    string[] glyphs =
                    {
                        "M 2,8 H 8 V 9 H 2 Z",
                        "M 2,2 H 8 V 8 H 2 Z M 3,4 H 7 V 7 H 3 Z",
                        "M 2,2 L 8,8 M 8,2 L 2,8",
                    };
                    for (var bi = 0; bi < 3; bi++)
                    {
                        var bx = x + w - inset - 39 + bi * 12;
                        var box = new Border
                        {
                            Width = 10,
                            Height = 10,
                            Background = IslandSkinEngine.Frozen(Color.FromRgb(0xE6, 0xDF, 0xF0)),
                            BorderBrush = btnInk,
                            BorderThickness = new Thickness(1),
                            Child = new Path
                            {
                                Data = Geometry.Parse(glyphs[bi]),
                                Fill = bi < 2 ? btnInk : null,
                                Stroke = bi == 2 ? btnInk : null,
                                StrokeThickness = 1.2,
                            },
                        };
                        Canvas.SetLeft(box, bx);
                        Canvas.SetTop(box, y + 6);
                        clippedOverlay.Children.Add(box);
                    }
                }

                // 4 decorative corner pixel screws (placed cleanly outside ExpandedContent margins)
                var topScrewY = isNotch ? y + 9 : y + 21;
                AddPixelScrew(clippedOverlay, x + 8, topScrewY);
                AddPixelScrew(clippedOverlay, x + w - 16, topScrewY);
                AddPixelScrew(clippedOverlay, x + 8, y + h - 16);
                AddPixelScrew(clippedOverlay, x + w - 16, y + h - 16);
            }
            else if (!isExpanded && h >= 28 && w >= 110)
            {
                // Sunken mint dot-matrix LCD inner screen inside collapsed Pixel Tamagotchi capsules
                var lcdWell = new Border
                {
                    Width = Math.Max(40, w - 16),
                    Height = Math.Max(18, h - 12),
                    Background = IslandSkinEngine.CreateMintLcdGridBrush(),
                    BorderBrush = IslandSkinEngine.Frozen(Color.FromRgb(0x52, 0x42, 0x6A)),
                    BorderThickness = new Thickness(2, 2, 0, 0),
                    Child = new Border
                    {
                        BorderBrush = Brushes.White,
                        BorderThickness = new Thickness(0, 0, 1.5, 1.5),
                    },
                };
                Canvas.SetLeft(lcdWell, x + 8);
                Canvas.SetTop(lcdWell, y + 6);
                clippedOverlay.Children.Add(lcdWell);
            }
        }
    }

    private static void AddPixelScrew(Canvas host, double sx, double sy)
    {
        var screw = new Canvas { Width = 8, Height = 8 };
        Canvas.SetLeft(screw, sx);
        Canvas.SetTop(screw, sy);
        screw.Children.Add(new Rectangle
        {
            Width = 8,
            Height = 8,
            Fill = IslandSkinEngine.Frozen(Color.FromRgb(0xB9, 0xAC, 0xCF)),
            Stroke = IslandSkinEngine.Frozen(Color.FromRgb(0x49, 0x3A, 0x62)),
            StrokeThickness = 1,
        });
        screw.Children.Add(new Line
        {
            X1 = 2, Y1 = 6, X2 = 6, Y2 = 2,
            Stroke = IslandSkinEngine.Frozen(Color.FromRgb(0x49, 0x3A, 0x62)),
            StrokeThickness = 1,
        });
        host.Children.Add(screw);
    }

    /// <summary>Builds crisp icon-only vector navigation glyphs for PageTabs across all 5 skins.</summary>
    public static FrameworkElement CreateTabIcon(string pageKey, bool selected, SkinPaletteSpec spec)
    {
        if (pageKey == "Ai")
            return CreateMiniHoshinoTabIcon(spec.IsPixelMode);

        Brush stroke = selected
            ? IslandSkinEngine.Frozen(spec.OnAccent)
            : IslandSkinEngine.Frozen(spec.IsCartoonSticker || spec.IsPixelMode ? spec.Text : spec.Muted);

        if (spec.IsPixelMode && pageKey == "Shelf")
        {
            // Retro floppy disk icon for Shelf in Pixel Tamagotchi
            var floppy = new Canvas { Width = 16, Height = 16 };
            RenderOptions.SetEdgeMode(floppy, EdgeMode.Aliased);
            var bodyFill = IslandSkinEngine.Frozen(Color.FromRgb(0x6A, 0x7F, 0xC8));
            var ink = IslandSkinEngine.Frozen(spec.Text);
            floppy.Children.Add(new Path { Data = Geometry.Parse("M 2,1 H 12 L 15,4 V 15 H 2 Z"), Fill = bodyFill, Stroke = ink, StrokeThickness = 1.2 });
            floppy.Children.Add(new Path { Data = Geometry.Parse("M 5,2 H 11 V 6 H 5 Z M 4,9 H 12 V 14 H 4 Z"), Fill = Brushes.White });
            return floppy;
        }

        var data = pageKey switch
        {
            "Home" => "M 2,8.5 L 8,3 L 14,8.5 V 14 H 10 V 10 H 6 V 14 H 2 Z",
            "Notifications" => "M 8,2.2 C 5.4,2.2 4,4.2 4,6.8 V 10.2 L 2.5,11.8 H 13.5 L 12,10.2 V 6.8 C 12,4.2 10.6,2.2 8,2.2 Z M 6.4,12.2 A 1.6,1.6 0 0 0 9.6,12.2",
            "Calendar" => "M 2.5,3.8 H 13.5 V 13.8 H 2.5 Z M 2.5,6.8 H 13.5 M 5.2,2 V 4.5 M 10.8,2 V 4.5 M 5.5,9.5 H 6.8 M 9.2,9.5 H 10.5",
            "Todo" => "M 2.5,2.5 H 13.5 V 13.5 H 2.5 Z M 5,6.2 L 6.4,7.6 L 9,5 M 10.2,6.5 H 11.8 M 5,10.2 L 6.4,11.6 L 9,9 M 10.2,10.5 H 11.8",
            "Notes" => "M 11.2,2.4 L 13.6,4.8 L 6.8,11.6 H 4.4 V 9.2 Z M 2.5,3.5 H 7.5 M 2.5,3.5 V 13.5 H 12.5 V 8.5",
            "Shelf" => "M 2.2,8.8 L 4.0,3.5 H 12.0 L 13.8,8.8 V 13.5 H 2.2 Z M 2.2,8.8 H 5.6 C 5.6,10.2 10.4,10.2 10.4,8.8 H 13.8",
            "Weather" => "M 5.2,12.5 H 12.2 A 2.4,2.4 0 0 0 12.2,7.8 A 3.4,3.4 0 0 0 5.8,8.4 A 2.2,2.2 0 0 0 5.2,12.5 Z M 11.5,2.8 V 1.6 M 14.2,5.5 H 15.2 M 13.4,3.6 L 14.2,2.8",
            _ => "M 2.5,2.5 H 7 V 7 H 2.5 Z M 9,2.5 H 13.5 V 7 H 9 Z M 2.5,9 H 7 V 13.5 H 2.5 Z M 9,9 H 13.5 V 13.5 H 9 Z",
        };

        var geo = Geometry.Parse(data);
        geo.Freeze();
        var isFilledHome = pageKey == "Home" && selected;
        return new Path
        {
            Data = geo,
            Width = 16,
            Height = 16,
            Stretch = Stretch.None,
            Fill = isFilledHome ? stroke : null,
            Stroke = stroke,
            StrokeThickness = isFilledHome ? 1.1 : (spec.IsCartoonSticker ? 1.65 : 1.4),
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
    }

    public static Viewbox CreateMiniHoshinoTabIcon(bool pixelMode)
    {
        var c = new Canvas { Width = 24, Height = 24, IsHitTestVisible = false };
        if (pixelMode) RenderOptions.SetEdgeMode(c, EdgeMode.Aliased);

        var hair = IslandSkinEngine.Frozen(Color.FromRgb(0xEA, 0xB0, 0xCF));
        var skin = IslandSkinEngine.Frozen(Color.FromRgb(0xFF, 0xF0, 0xE9));
        var gold = IslandSkinEngine.Frozen(Color.FromRgb(0xF2, 0xD1, 0x7D));
        var sky = IslandSkinEngine.Frozen(Color.FromRgb(0x83, 0xCF, 0xF3));

        c.Children.Add(new Path
        {
            Data = Geometry.Parse("M 11,4 C 10,1.2 13,0.4 15,2 C 14.5,3 13,2.5 12.6,4.2 C 17,4.2 20,7.5 20.5,12 L 22,13.5 L 19.8,14 L 20.6,20 L 18,18.5 L 17,21 C 15.5,19.5 15,17.5 14.8,15.5 H 9.2 C 9,17.5 8.5,19.5 7,21 L 6,18.5 L 3.4,20 L 4.2,14 L 2,13.5 L 3.5,12 C 4,7.5 7,4.2 11,4 Z"),
            Fill = hair,
        });
        c.Children.Add(new Path
        {
            Data = Geometry.Parse("M 6.4,11.5 C 6.4,7.8 17.6,7.8 17.6,11.5 V 14.5 C 17.6,17.6 15.2,19.5 12,19.6 C 8.8,19.5 6.4,17.6 6.4,14.5 Z"),
            Fill = skin,
        });
        c.Children.Add(new Path
        {
            Data = Geometry.Parse("M 5,11.8 C 5.5,6.8 8.2,4.5 12,4.8 C 15.8,4.5 18.5,6.8 19,11.8 L 16.5,13.5 L 15,10.5 L 13,13.8 L 12,10.2 L 10.2,13.8 L 8.8,10.5 L 7.2,13.5 Z"),
            Fill = hair,
        });

        var leftEye = new Ellipse { Width = 2.6, Height = 2.6, Fill = gold };
        Canvas.SetLeft(leftEye, 8.2);
        Canvas.SetTop(leftEye, 13.8);
        c.Children.Add(leftEye);

        var rightEye = new Ellipse { Width = 2.6, Height = 2.6, Fill = sky };
        Canvas.SetLeft(rightEye, 13.2);
        Canvas.SetTop(rightEye, 13.8);
        c.Children.Add(rightEye);

        return new Viewbox { Width = 19, Height = 19, Child = c };
    }

    public static Viewbox CreateFocusDiamondIcon(SkinPaletteSpec spec)
    {
        var c = new Canvas { Width = 18, Height = 18, IsHitTestVisible = false };
        var topFill = spec.IsShittimTablet || spec.IsAeroGlass
            ? IslandSkinEngine.Frozen(Color.FromRgb(0x78, 0xDF, 0xFF))
            : spec.IsPixelMode
                ? IslandSkinEngine.Frozen(Color.FromRgb(0xA4, 0xDC, 0xFF))
                : IslandSkinEngine.Frozen(Color.FromRgb(0xFA, 0xE2, 0x9C));
        var bottomFill = spec.IsShittimTablet || spec.IsAeroGlass
            ? IslandSkinEngine.Frozen(Color.FromRgb(0x29, 0xB6, 0xF6))
            : spec.IsPixelMode
                ? IslandSkinEngine.Frozen(Color.FromRgb(0x6D, 0x98, 0xE8))
                : IslandSkinEngine.Frozen(Color.FromRgb(0xE8, 0xBC, 0x5A));
        var stroke = spec.IsCartoonSticker || spec.IsPixelMode
            ? IslandSkinEngine.Frozen(spec.Text)
            : IslandSkinEngine.Frozen(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF));

        c.Children.Add(new Path { Data = Geometry.Parse("M 5,3 H 13 L 16.5,7.5 L 9,16 L 1.5,7.5 Z"), Fill = bottomFill, Stroke = stroke, StrokeThickness = 1.1, StrokeLineJoin = PenLineJoin.Round });
        c.Children.Add(new Path { Data = Geometry.Parse("M 5,3 H 13 L 16.5,7.5 H 1.5 Z"), Fill = topFill });
        c.Children.Add(new Path { Data = Geometry.Parse("M 5,3 L 6.8,7.5 L 9,16 L 11.2,7.5 L 13,3"), Stroke = stroke, StrokeThickness = 0.8, Opacity = 0.65 });
        return new Viewbox { Width = 16, Height = 16, Child = c };
    }

    /// <summary>
    /// Builds the dedicated Pixel Tamagotchi Focus keycap row placed directly underneath "Задачи · Obsidian"
    /// in the right column (matching Design Screenshot 5 1:1): tall vertical faceted crystal + '+' sparkle
    /// stacked above centered "Фокус", followed by 4 plump 3D raised mechanical keycaps (5м, 15м, 25м, 1ч).
    /// </summary>
    public static (FrameworkElement Root, TextBlock? ActiveTimeLabel) CreatePixelFocusDock(Services.TimerService timer)
    {
        var ink = IslandSkinEngine.Frozen(Color.FromRgb(0x25, 0x1B, 0x36));
        var textBrush = IslandSkinEngine.Frozen(Color.FromRgb(0x1A, 0x14, 0x26));
        var highlight = IslandSkinEngine.Frozen(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF));

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
        };

        // Left vertical emblem: tall hexagonal faceted pixel crystal + '+' sparkle above centered "Фокус"
        var emblem = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };

        var gemCanvas = new Canvas
        {
            Width = 26,
            Height = 22,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false,
        };

        // Left icy facet, center aqua-blue facet, right periwinkle-violet facet + dark pixel contour
        gemCanvas.Children.Add(new Path
        {
            Data = Geometry.Parse("M 8,1 L 14,5.5 L 14,16.5 L 8,21 L 2,16.5 L 2,5.5 Z"),
            Fill = IslandSkinEngine.Frozen(Color.FromRgb(0x98, 0xEA, 0xFB)),
        });
        gemCanvas.Children.Add(new Path
        {
            Data = Geometry.Parse("M 8,1 L 8,21 L 2,16.5 L 2,5.5 Z"),
            Fill = IslandSkinEngine.Frozen(Color.FromRgb(0xEE, 0xFF, 0xFF)),
        });
        gemCanvas.Children.Add(new Path
        {
            Data = Geometry.Parse("M 8,1 L 14,5.5 L 14,16.5 L 8,21 L 10.2,15.5 L 10.2,6.5 Z"),
            Fill = IslandSkinEngine.Frozen(Color.FromRgb(0x8E, 0x82, 0xF2)),
        });
        gemCanvas.Children.Add(new Path
        {
            Data = Geometry.Parse("M 8,21 L 14,16.5 L 10.2,15.5 Z"),
            Fill = IslandSkinEngine.Frozen(Color.FromRgb(0x6B, 0x5E, 0xD6)),
        });
        gemCanvas.Children.Add(new Path
        {
            Data = Geometry.Parse("M 8,1 L 14,5.5 L 14,16.5 L 8,21 L 2,16.5 L 2,5.5 Z"),
            Stroke = ink,
            StrokeThickness = 1.6,
            StrokeLineJoin = PenLineJoin.Round,
        });
        // Crisp '+' sparkle beside lower-right of the crystal
        gemCanvas.Children.Add(new Path
        {
            Data = Geometry.Parse("M 20.2,10.5 H 21.8 V 12.6 H 23.9 V 14.2 H 21.8 V 16.3 H 20.2 V 14.2 H 18.1 V 12.6 H 20.2 Z"),
            Fill = ink,
        });

        emblem.Children.Add(gemCanvas);
        emblem.Children.Add(new TextBlock
        {
            Text = "Фокус",
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = textBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 0),
        });
        row.Children.Add(emblem);

        FrameworkElement MakeKeycap(string label, double width, Color faceColor, Color ledgeColor, Thickness margin, string tooltip, Action onPress)
        {
            var outer = new Border
            {
                Width = width,
                Height = 36,
                Margin = margin,
                CornerRadius = new CornerRadius(5),
                Background = IslandSkinEngine.Frozen(ledgeColor),
                BorderBrush = ink,
                BorderThickness = new Thickness(1.8),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = tooltip,
            };

            var cap = new Border
            {
                Margin = new Thickness(0, 0, 0, 4.0),
                CornerRadius = new CornerRadius(3.2),
                Background = IslandSkinEngine.Frozen(faceColor),
                BorderBrush = highlight,
                BorderThickness = new Thickness(1.6, 1.6, 0, 0),
                Child = new TextBlock
                {
                    Text = label,
                    FontSize = 13.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = textBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 0.5),
                },
            };

            outer.Child = cap;
            outer.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                cap.Margin = new Thickness(0, 2.5, 0, 1.5);
            };
            outer.MouseLeave += (_, _) => cap.Margin = new Thickness(0, 0, 0, 4.0);
            outer.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                cap.Margin = new Thickness(0, 0, 0, 4.0);
                onPress();
            };
            return outer;
        }

        var creamFace = Color.FromRgb(0xF5, 0xE3, 0x9B);
        var creamLedge = Color.FromRgb(0xC6, 0xAF, 0x66);
        var pinkFace = Color.FromRgb(0xF5, 0xB8, 0xD2);
        var pinkLedge = Color.FromRgb(0xC8, 0x84, 0xA3);
        var lilacFace = Color.FromRgb(0xDD, 0xD3, 0xEE);
        var lilacLedge = Color.FromRgb(0xA8, 0x9B, 0xC2);

        if (!timer.IsActive)
        {
            row.Children.Add(MakeKeycap("5м", 60, creamFace, creamLedge, new Thickness(0, 0, 7, 0), "Фокус на 5 минут", () => timer.Start(TimeSpan.FromMinutes(5))));
            row.Children.Add(MakeKeycap("15м", 60, creamFace, creamLedge, new Thickness(0, 0, 7, 0), "Фокус на 15 минут", () => timer.Start(TimeSpan.FromMinutes(15))));
            row.Children.Add(MakeKeycap("25м", 60, pinkFace, pinkLedge, new Thickness(0, 0, 7, 0), "Помодоро · 25 минут", () => timer.Start(TimeSpan.FromMinutes(25))));
            row.Children.Add(MakeKeycap("1ч", 60, lilacFace, lilacLedge, new Thickness(0), "Глубокий фокус · 1 час", () => timer.Start(TimeSpan.FromMinutes(60))));
            return (row, null);
        }

        var activeTime = new TextBlock
        {
            Text = Services.TimerService.Format(timer.Remaining),
            FontSize = 17.5,
            FontWeight = FontWeights.Bold,
            Foreground = textBrush,
            Opacity = timer.IsPaused ? 0.58 : 1.0,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 10, 0),
            Typography = { NumeralAlignment = FontNumeralAlignment.Tabular },
        };
        row.Children.Add(activeTime);
        row.Children.Add(MakeKeycap("+1м", 52, creamFace, creamLedge, new Thickness(0, 0, 6, 0), "Добавить 1 минуту", () => timer.Add(TimeSpan.FromMinutes(1))));
        row.Children.Add(MakeKeycap(timer.IsPaused ? "▶" : "⏸", 44, pinkFace, pinkLedge, new Thickness(0, 0, 6, 0), "Пауза / продолжить", timer.TogglePause));
        row.Children.Add(MakeKeycap("✕", 44, lilacFace, lilacLedge, new Thickness(0), "Отменить таймер", timer.Cancel));
        return (row, activeTime);
    }

    public static Border CreateObsidianBadge(SkinPaletteSpec spec)
    {
        var gem = new Canvas { Width = 12, Height = 14, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
        gem.Children.Add(new Path
        {
            Data = Geometry.Parse("M 6,1 L 10.5,4.5 L 9.2,13 L 4.2,11.8 L 1.8,6 Z"),
            Fill = IslandSkinEngine.Frozen(Color.FromRgb(0x88, 0x62, 0xF8)),
            Stroke = IslandSkinEngine.Frozen(Color.FromRgb(0xB9, 0x9E, 0xFF)),
            StrokeThickness = 0.8,
        });
        gem.Children.Add(new Path
        {
            Data = Geometry.Parse("M 6,1 L 7.2,6.8 L 9.2,13 L 10.5,4.5 Z"),
            Fill = IslandSkinEngine.Frozen(Color.FromRgb(0x63, 0x3C, 0xD8)),
        });

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(gem);
        var txt = new TextBlock { Text = "Obsidian", FontSize = 11, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center };
        txt.SetResourceReference(TextBlock.ForegroundProperty, "HoshinoMuted");
        row.Children.Add(txt);

        var badge = new Border
        {
            CornerRadius = new CornerRadius(spec.IsPixelMode ? 2 : 10),
            Padding = new Thickness(8, 3, 9, 3),
            BorderThickness = new Thickness(spec.IsCartoonSticker ? 1.6 : 1),
            Child = row,
        };
        badge.SetResourceReference(Border.BackgroundProperty, "HoshinoControl");
        badge.SetResourceReference(Border.BorderBrushProperty, "HoshinoBorder");
        return badge;
    }

    public static Path CreateClockDiamondSeparator(SkinPaletteSpec spec)
    {
        var geo = Geometry.Parse("M 5,0.5 L 8.5,5 L 5,9.5 L 1.5,5 Z");
        geo.Freeze();
        var fill = spec.IsShittimTablet || spec.IsAeroGlass
            ? IslandSkinEngine.Frozen(Color.FromRgb(0x38, 0xBD, 0xF8))
            : spec.IsCartoonSticker
                ? IslandSkinEngine.Frozen(Color.FromRgb(0xFF, 0xE0, 0x66))
                : spec.IsPixelMode
                    ? IslandSkinEngine.Frozen(spec.Text)
                    : IslandSkinEngine.Frozen(spec.Accent);
        return new Path
        {
            Data = geo,
            Width = 10,
            Height = 10,
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Fill = fill,
            Stroke = spec.IsCartoonSticker ? IslandSkinEngine.Frozen(spec.Border) : null,
            StrokeThickness = spec.IsCartoonSticker ? 1.2 : 0,
        };
    }

    /// <summary>Builds circular progress arc geometry inside ActivityBubble for Focus Timer secondary view.</summary>
    public static Geometry BuildCircularProgressArc(double size, double inset, double ratio)
    {
        ratio = Math.Clamp(ratio, 0, 0.999);
        if (ratio <= 0.002 || size <= inset * 2) return Geometry.Empty;

        double cx = size / 2.0, cy = size / 2.0;
        var r = size / 2.0 - inset;
        var start = new Point(cx, cy - r);
        var endAngle = -Math.PI / 2.0 + ratio * Math.PI * 2.0;
        var end = new Point(cx + Math.Cos(endAngle) * r, cy + Math.Sin(endAngle) * r);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(start, isFilled: false, isClosed: false);
            ctx.ArcTo(end, new Size(r, r), 0, ratio > 0.5, SweepDirection.Clockwise, true, true);
        }
        geo.Freeze();
        return geo;
    }

    /// <summary>Builds the octagonal mini-Tamagotchi keychain charm overlay for ActivityBubble in PixelTamagotchi mode.</summary>
    public static Canvas CreateTamagotchiKeychainBubble(double size)
    {
        var c = new Canvas { Width = size, Height = size, IsHitTestVisible = false };
        RenderOptions.SetEdgeMode(c, EdgeMode.Aliased);
        var ink = IslandSkinEngine.Frozen(Color.FromRgb(0x2A, 0x1F, 0x3D));
        var shell = IslandSkinEngine.Frozen(Color.FromRgb(0xDE, 0xD4, 0xED));
        var lcd = IslandSkinEngine.Frozen(Color.FromRgb(0xC9, 0xED, 0xE3));
        var metal = IslandSkinEngine.Frozen(Color.FromRgb(0xA6, 0xA8, 0xBA));

        var ch = Math.Max(5.0, Math.Round(size * 0.22));
        // Keyring loop extending slightly above top
        c.Children.Add(new Path
        {
            Data = Geometry.Parse(FormattableString.Invariant(
                $"M {size / 2 - 3},0 V -5 H {size / 2 + 4} V -11 H {size / 2 - 1} V -5")),
            Stroke = metal,
            StrokeThickness = 1.8,
        });

        // Octagonal lavender shell
        c.Children.Add(MakePolygonWithStroke(shell, ink, 1.8,
            new(ch, 1), new(size - ch, 1), new(size - 1, ch), new(size - 1, size - ch),
            new(size - ch, size - 1), new(ch, size - 1), new(1, size - ch), new(1, ch)));

        // Sunken mini mint LCD window
        var m = Math.Round(size * 0.19);
        var lcdRect = new Border
        {
            Width = Math.Max(14, size - m * 2),
            Height = Math.Max(12, size - m * 2 - 4),
            Background = lcd,
            BorderBrush = IslandSkinEngine.Frozen(Color.FromRgb(0x5B, 0x4B, 0x73)),
            BorderThickness = new Thickness(1.2),
        };
        Canvas.SetLeft(lcdRect, m);
        Canvas.SetTop(lcdRect, m - 1);
        c.Children.Add(lcdRect);

        // 3 tiny Tamagotchi buttons at bottom
        for (var i = -1; i <= 1; i++)
        {
            var dot = new Rectangle
            {
                Width = 2.5,
                Height = 2.5,
                Fill = IslandSkinEngine.Frozen(Color.FromRgb(0xF0, 0xA8, 0xCA)),
                Stroke = ink,
                StrokeThickness = 0.7,
            };
            Canvas.SetLeft(dot, size / 2.0 + i * 5 - 1.2);
            Canvas.SetTop(dot, size - 6.2);
            c.Children.Add(dot);
        }
        return c;
    }

    /// <summary>Creates the 13-bar heterochromatic (#F2D17D / #EAB0CF / #83CFF3) waveform for VoiceFocusView.</summary>
    public static (StackPanel Host, Rectangle[] Bars) CreateVoiceWaveform()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Height = 22,
        };

        var gold = IslandSkinEngine.Frozen(Color.FromRgb(0xF2, 0xD1, 0x7D));
        var pink = IslandSkinEngine.Frozen(Color.FromRgb(0xEA, 0xB0, 0xCF));
        var sky = IslandSkinEngine.Frozen(Color.FromRgb(0x83, 0xCF, 0xF3));

        double[] baseHeights = { 6, 11, 7, 15, 9, 19, 22, 18, 10, 14, 7, 11, 6 };
        var bars = new Rectangle[baseHeights.Length];

        for (var i = 0; i < baseHeights.Length; i++)
        {
            var brush = i < 4 ? gold : i < 9 ? pink : sky;
            var rect = new Rectangle
            {
                Width = 3.0,
                Height = baseHeights[i],
                RadiusX = 1.5,
                RadiusY = 1.5,
                Margin = new Thickness(1.8, 0, 1.8, 0),
                Fill = brush,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 0.55),
            };
            bars[i] = rect;
            panel.Children.Add(rect);
        }

        return (panel, bars);
    }

    private static Polygon MakePolygon(Brush fill, params Point[] pts)
    {
        var poly = new Polygon { Fill = fill, Points = new PointCollection(pts), IsHitTestVisible = false };
        return poly;
    }

    private static Polygon MakePolygonWithStroke(Brush fill, Brush stroke, double thickness, params Point[] pts)
    {
        return new Polygon
        {
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = thickness,
            Points = new PointCollection(pts),
            IsHitTestVisible = false,
        };
    }

    private static Polyline MakePolyline(Brush stroke, double thickness, params Point[] pts)
    {
        return new Polyline
        {
            Stroke = stroke,
            StrokeThickness = thickness,
            Points = new PointCollection(pts),
            IsHitTestVisible = false,
        };
    }
}
