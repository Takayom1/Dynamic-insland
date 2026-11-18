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
/// Pure WPF vector accessories for Hoshino (WhalePin, SummerShades, SleepMask, Auto),
/// Chibi Pop vinyl-sticker contour, Pixel Tamagotchi 30x30 pixel-chibi vector sprite,
/// and crisp geometric Eye of Horus backdrop ring.
/// </summary>
public static class HoshinoAccessories
{
    public static readonly (HoshinoAccessory Value, string Label)[] Choices =
    {
        (HoshinoAccessory.Auto, "Авто"),
        (HoshinoAccessory.WhalePin, "Заколка-кит"),
        (HoshinoAccessory.SummerShades, "Летние очки"),
        (HoshinoAccessory.SleepMask, "Маска сна"),
        (HoshinoAccessory.None, "Нет"),
    };

    private static readonly Brush SkyFill = Frozen(Color.FromRgb(0x83, 0xCF, 0xF3));
    private static readonly Brush SkyDeep = Frozen(Color.FromRgb(0x52, 0xA8, 0xD6));
    private static readonly Brush MaskFill = Frozen(Color.FromRgb(0xA6, 0xDF, 0xF7));
    private static readonly Brush CreamFill = Frozen(Color.FromRgb(0xF8, 0xF4, 0xFA));
    private static readonly Brush PinkFill = Frozen(Color.FromRgb(0xEA, 0xB0, 0xCF));
    private static readonly Brush GoldFill = Frozen(Color.FromRgb(0xF2, 0xD1, 0x7D));
    private static readonly Brush DarkInk = Frozen(Color.FromRgb(0x23, 0x1B, 0x2E));
    private static readonly Brush StickerInk = Frozen(Color.FromRgb(0x2A, 0x1E, 0x2E));

    public static HoshinoAccessory ResolveActive(AppSettings settings, WeatherReport? weather, DateTime now, bool isDrowsy)
    {
        if (settings.HoshinoAccessory != HoshinoAccessory.Auto)
            return settings.HoshinoAccessory;

        // Only wear SleepMask during actual late-night / early-dawn hours (not daytime mouse idle)
        if (now.Hour >= 23 || now.Hour < 6)
            return HoshinoAccessory.SleepMask;

        if (settings.WeatherEnabled && weather is not null && HoshinoAtmosphere.IsHotWeather(weather))
            return HoshinoAccessory.SummerShades;

        return HoshinoAccessory.WhalePin;
    }

    public static void RebuildAccessoryOverlay(Canvas host, HoshinoAccessory active, bool pixelMode)
    {
        host.Children.Clear();
        if (active == HoshinoAccessory.None)
        {
            host.Visibility = Visibility.Collapsed;
            return;
        }

        host.Visibility = Visibility.Visible;
        if (pixelMode)
        {
            BuildPixelAccessory(host, active);
            return;
        }

        switch (active)
        {
            case HoshinoAccessory.WhalePin:
                BuildWhalePin(host);
                break;
            case HoshinoAccessory.SummerShades:
                BuildSummerShades(host);
                break;
            case HoshinoAccessory.SleepMask:
                BuildSleepMask(host);
                break;
        }
    }

    private static void BuildWhalePin(Canvas host)
    {
        var pin = new Canvas
        {
            Width = 120,
            Height = 120,
            IsHitTestVisible = false,
            RenderTransform = new RotateTransform(-9, 79, 41),
        };

        // Tiny water spout above the whale's head
        pin.Children.Add(MakePath(
            "M 76.5,31.5 C 75.5,28.2 72.2,27.8 71.5,29.8 M 77.8,31.5 C 79.2,28.0 82.4,28.2 82.8,30.2",
            fill: null, stroke: SkyFill, thickness: 1.35));

        // Plump turquoise whale body + lifted tailuke
        pin.Children.Add(MakePath(
            "M 67.5,43.5 C 67.2,36.2 73.5,33.0 80.2,34.2 C 84.4,35.0 86.6,38.2 88.2,39.4 " +
            "C 90.0,37.8 92.5,35.5 94.2,36.0 C 93.8,38.6 92.2,40.4 90.8,41.2 " +
            "C 92.5,42.0 94.0,43.6 93.6,45.4 C 91.5,45.2 89.5,43.8 88.0,42.6 " +
            "C 85.4,45.8 80.2,47.0 73.2,46.6 C 69.4,46.4 67.6,45.2 67.5,43.5 Z",
            fill: SkyFill, stroke: SkyDeep, thickness: 1.05));

        // Soft cream belly stripe
        pin.Children.Add(MakePath(
            "M 68.8,43.8 C 72.5,42.6 78.5,42.8 83.6,44.8 C 79.8,46.4 73.5,46.4 69.5,45.2 Z",
            fill: CreamFill, stroke: null, thickness: 0));

        // Cute side flipper
        pin.Children.Add(MakePath(
            "M 78.2,43.2 C 79.5,46.0 82.2,46.2 82.8,44.0 Z",
            fill: SkyDeep, stroke: null, thickness: 0));

        // Eye & tiny pink blush dot
        var eye = new Ellipse { Width = 2.4, Height = 2.4, Fill = DarkInk };
        Canvas.SetLeft(eye, 72.2);
        Canvas.SetTop(eye, 39.2);
        pin.Children.Add(eye);

        var blush = new Ellipse { Width = 3.2, Height = 1.9, Fill = PinkFill, Opacity = 0.9 };
        Canvas.SetLeft(blush, 74.6);
        Canvas.SetTop(blush, 41.3);
        pin.Children.Add(blush);

        host.Children.Add(pin);
    }

    private static void BuildSummerShades(Canvas host)
    {
        var lensGrad = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        lensGrad.GradientStops.Add(new GradientStop(Color.FromRgb(0x2B, 0x20, 0x38), 0));
        lensGrad.GradientStops.Add(new GradientStop(Color.FromRgb(0x47, 0x33, 0x58), 1));
        lensGrad.Freeze();

        // Temple arms & gold bridge
        host.Children.Add(MakePath(
            "M 31,72.5 L 35.5,71.5 M 89,72.5 L 84.5,71.5 M 56.5,72.5 Q 60,70.5 63.5,72.5",
            fill: null, stroke: GoldFill, thickness: 1.8));

        // Left & right Aviator/Wayfarer lenses over Hoshino's eyes
        host.Children.Add(MakePath(
            "M 35,69.5 H 56.8 L 55.4,79.8 C 54.4,82.6 38.2,82.6 36.6,79.8 Z",
            fill: lensGrad, stroke: DarkInk, thickness: 1.6));
        host.Children.Add(MakePath(
            "M 63.2,69.5 H 85.0 L 83.4,79.8 C 81.8,82.6 65.6,82.6 64.6,79.8 Z",
            fill: lensGrad, stroke: DarkInk, thickness: 1.6));

        // Crisp diagonal sun reflections
        var shine = MakePath(
            "M 39.5,71.5 L 44.0,79.2 M 43.2,71.5 L 46.2,76.8 M 67.5,71.5 L 72.0,79.2 M 71.2,71.5 L 74.2,76.8",
            fill: null, stroke: CreamFill, thickness: 1.35);
        shine.Opacity = 0.50;
        host.Children.Add(shine);
    }

    private static void BuildSleepMask(Canvas host)
    {
        // Side elastic bands tucked into hair
        host.Children.Add(MakePath(
            "M 31,51.5 Q 34,49.5 37,49.5 M 83,49.5 Q 86,49.5 89,51.5",
            fill: null, stroke: SkyDeep, thickness: 2.4));

        // Plush quilted sleep mask resting on forehead
        host.Children.Add(MakePath(
            "M 36,47.5 C 36,41.0 46,39.2 60,39.5 C 74,39.2 84,41.0 84,47.5 " +
            "C 84,53.8 76.5,55.2 68.5,53.8 C 64.2,53.0 62.2,50.8 60,50.8 " +
            "C 57.8,50.8 55.8,53.0 51.5,53.8 C 43.5,55.2 36,53.8 36,47.5 Z",
            fill: MaskFill, stroke: CreamFill, thickness: 1.65));

        // Subtle outer contour so it stays crisp on light skins too
        var rim = MakePath(
            "M 36,47.5 C 36,41.0 46,39.2 60,39.5 C 74,39.2 84,41.0 84,47.5 " +
            "C 84,53.8 76.5,55.2 68.5,53.8 C 64.2,53.0 62.2,50.8 60,50.8 " +
            "C 57.8,50.8 55.8,53.0 51.5,53.8 C 43.5,55.2 36,53.8 36,47.5 Z",
            fill: null, stroke: SkyDeep, thickness: 0.85);
        rim.Opacity = 0.75;
        host.Children.Add(rim);
    }

    private static void BuildPixelAccessory(Canvas host, HoshinoAccessory active)
    {
        RenderOptions.SetEdgeMode(host, EdgeMode.Aliased);
        switch (active)
        {
            case HoshinoAccessory.WhalePin:
                host.Children.Add(MakeAliasedRectPath("M 68,36 H 88 V 44 H 68 Z M 88,32 H 96 V 40 H 88 Z", SkyFill));
                host.Children.Add(MakeAliasedRectPath("M 70,42 H 84 V 45 H 70 Z", CreamFill));
                host.Children.Add(MakeAliasedRectPath("M 72,38 H 75 V 41 H 72 Z", DarkInk));
                break;
            case HoshinoAccessory.SummerShades:
                host.Children.Add(MakeAliasedRectPath("M 36,68 H 56 V 80 H 36 Z M 64,68 H 84 V 80 H 64 Z M 56,70 H 64 V 73 H 56 Z", DarkInk));
                host.Children.Add(MakeAliasedRectPath("M 39,70 H 43 V 74 H 39 Z M 67,70 H 71 V 74 H 67 Z", SkyFill));
                break;
            case HoshinoAccessory.SleepMask:
                host.Children.Add(MakeAliasedRectPath("M 36,40 H 84 V 52 H 64 V 49 H 56 V 52 H 36 Z", MaskFill));
                host.Children.Add(MakeAliasedRectPath("M 36,40 H 84 V 42 H 36 Z M 36,50 H 56 V 52 H 36 Z M 64,50 H 84 V 52 H 64 Z", SkyDeep));
                break;
        }
    }

    /// <summary>Creates the Chibi Pop die-cut vinyl sticker white border + dark cartoon outline behind Hoshino.</summary>
    public static Canvas CreateStickerBackdrop()
    {
        var root = new Canvas { Width = 120, Height = 120, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        const string silhouette =
            "M55 24C49 11 61 1 72 7c6 3 7 8 4 12-1-6-6-8-11-6-5 2-6 7-4 11C82 21 99 36 101 56l10 8-10 1 6 7-9-1c1 15 2 24 8 35l-10-4 2 12-11-8-2 10c-8-8-11-18-12-29H47c-1 11-4 21-12 29l-2-10-11 8 2-12-10 4c6-11 7-20 8-35l-9 1 6-7-10-1 10-8C21 36 34 24 55 24Z";
        var geo = Geometry.Parse(silhouette);
        geo.Freeze();

        // Dark outer comic outline
        root.Children.Add(new Path
        {
            Data = geo,
            Fill = StickerInk,
            Stroke = StickerInk,
            StrokeThickness = 9.6,
            StrokeLineJoin = PenLineJoin.Round,
        });

        // Crisp white die-cut vinyl sticker margin
        root.Children.Add(new Path
        {
            Data = geo,
            Fill = Brushes.White,
            Stroke = Brushes.White,
            StrokeThickness = 5.0,
            StrokeLineJoin = PenLineJoin.Round,
        });

        // Subtle inner ink line hugging Hoshino's hair
        root.Children.Add(new Path
        {
            Data = geo,
            Fill = null,
            Stroke = StickerInk,
            StrokeThickness = 1.8,
            StrokeLineJoin = PenLineJoin.Round,
        });

        return root;
    }

    /// <summary>Builds the 30x30 virtual-pixel vector chibi sprite of Hoshino for the Pixel Tamagotchi skin.</summary>
    public static (Canvas Root, RotateTransform AhogeRotate, TranslateTransform EyeGaze, Path ClosedLids, Path HappyLids, Path Blush) CreatePixelChibiSprite()
    {
        var root = new Canvas { Width = 120, Height = 120, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        RenderOptions.SetEdgeMode(root, EdgeMode.Aliased);

        var outline = Frozen(Color.FromRgb(0x28, 0x1C, 0x38));
        var hair = Frozen(Color.FromRgb(0xF0, 0xA8, 0xCE));
        var hairShade = Frozen(Color.FromRgb(0xD8, 0x86, 0xB3));
        var skin = Frozen(Color.FromRgb(0xFF, 0xF1, 0xEA));
        var goldEye = Frozen(Color.FromRgb(0xF2, 0xD1, 0x7D));
        var skyEye = Frozen(Color.FromRgb(0x76, 0xCC, 0xF2));
        var blushBrush = Frozen(Color.FromRgb(0xF2, 0x8C, 0xB1));

        // Articulated pixel ahoge curl
        var ahogeGroup = new Canvas { Width = 120, Height = 120 };
        var ahogeRot = new RotateTransform(0, 58, 24);
        ahogeGroup.RenderTransform = ahogeRot;
        ahogeGroup.Children.Add(MakeAliasedRectPath(
            "M 52,12 H 68 V 16 H 72 V 24 H 64 V 16 H 56 V 28 H 52 Z", outline));
        ahogeGroup.Children.Add(MakeAliasedRectPath(
            "M 56,14 H 66 V 18 H 69 V 22 H 65 V 18 H 56 V 26 H 54 V 16 H 56 Z", hair));
        root.Children.Add(ahogeGroup);

        // Stepped pixel hair silhouette (dark pixel border)
        root.Children.Add(MakeAliasedRectPath(
            "M 36,24 H 84 V 28 H 92 V 36 H 96 V 52 H 104 V 60 H 96 V 76 H 100 V 100 H 88 V 92 H 84 V 104 H 72 V 92 H 48 V 104 H 36 V 92 H 32 V 100 H 20 V 76 H 24 V 60 H 16 V 52 H 24 V 36 H 28 V 28 H 36 Z",
            outline));

        // Pastel pink pixel hair fill
        root.Children.Add(MakeAliasedRectPath(
            "M 36,28 H 84 V 32 H 88 V 40 H 92 V 52 H 100 V 56 H 92 V 76 H 96 V 96 H 88 V 88 H 84 V 100 H 76 V 88 H 44 V 100 H 36 V 88 H 32 V 96 H 24 V 76 H 28 V 56 H 20 V 52 H 28 V 40 H 32 V 32 H 36 Z",
            hair));

        // Side lock pixel shading
        root.Children.Add(MakeAliasedRectPath(
            "M 28,68 H 34 V 92 H 28 Z M 86,68 H 92 V 92 H 86 Z",
            hairShade));

        // Pixel face skin area with stepped bangs
        root.Children.Add(MakeAliasedRectPath(
            "M 36,52 H 44 V 56 H 52 V 48 H 60 V 56 H 68 V 48 H 76 V 56 H 84 V 88 H 76 V 92 H 44 V 88 H 36 Z",
            skin));

        // Open heterochromatic pixel eyes
        var eyesCanvas = new Canvas { Width = 120, Height = 120 };
        var eyeGaze = new TranslateTransform();
        eyesCanvas.RenderTransform = eyeGaze;
        eyesCanvas.Children.Add(MakeAliasedRectPath("M 42,68 H 52 V 78 H 42 Z", goldEye));
        eyesCanvas.Children.Add(MakeAliasedRectPath("M 68,68 H 78 V 78 H 68 Z", skyEye));
        root.Children.Add(eyesCanvas);

        // Sleepy closed pixel lids (- -)
        var closedLids = MakeAliasedRectPath("M 40,73 H 54 V 77 H 40 Z M 66,73 H 80 V 77 H 66 Z", outline);
        closedLids.Visibility = Visibility.Collapsed;
        root.Children.Add(closedLids);

        // Happy squint pixel lids (^ ^)
        var happyLids = MakeAliasedRectPath(
            "M 40,73 H 44 V 69 H 50 V 73 H 54 V 76 H 50 V 72 H 44 V 76 H 40 Z " +
            "M 66,73 H 70 V 69 H 76 V 73 H 80 V 76 H 76 V 72 H 70 V 76 H 66 Z", outline);
        happyLids.Visibility = Visibility.Collapsed;
        root.Children.Add(happyLids);

        // Pixel cheek blush
        var blush = MakeAliasedRectPath("M 36,80 H 44 V 84 H 36 Z M 76,80 H 84 V 84 H 76 Z", blushBrush);
        blush.Opacity = 0;
        root.Children.Add(blush);

        return (root, ahogeRot, eyeGaze, closedLids, happyLids, blush);
    }

    /// <summary>Creates the crisp geometric Eye of Horus crosshair-halo behind Hoshino (no blurry fog).</summary>
    public static Viewbox CreateEyeOfHorusBackdrop(SkinPaletteSpec spec, bool includeRadarArcs = false)
    {
        var canvas = new Canvas { Width = 160, Height = 160, IsHitTestVisible = false };

        Brush primary = spec.IsShittimTablet
            ? Frozen(Color.FromRgb(0x38, 0xBD, 0xF8))
            : spec.IsAeroGlass
                ? Frozen(Color.FromArgb(0xCC, 0xD8, 0xF4, 0xFF))
                : spec.IsCartoonSticker
                    ? Frozen(Color.FromArgb(0x70, 0xEC, 0x9B, 0xBE))
                    : Frozen(Color.FromRgb(0xEA, 0xB0, 0xCF));

        Brush secondary = spec.IsShittimTablet
            ? Frozen(Color.FromRgb(0xFF, 0x85, 0xAD))
            : spec.IsAeroGlass
                ? Frozen(Color.FromArgb(0xA8, 0x8C, 0xDE, 0xFF))
                : spec.IsCartoonSticker
                    ? Frozen(Color.FromArgb(0x65, 0x83, 0xCF, 0xF3))
                    : Frozen(Color.FromRgb(0x83, 0xCF, 0xF3));

        // Outer ring + 4 cardinal compass spikes
        canvas.Children.Add(new Path
        {
            Data = Geometry.Parse(
                "M 22,80 A 58,58 0 1 1 138,80 A 58,58 0 1 1 22,80 " +
                "M 80,8 L 85,22 L 75,22 Z " +
                "M 80,152 L 85,138 L 75,138 Z " +
                "M 8,80 L 22,75 L 22,85 Z " +
                "M 152,80 L 138,75 L 138,85 Z"),
            Stroke = primary,
            StrokeThickness = 1.6,
            StrokeLineJoin = PenLineJoin.Round,
            Opacity = 0.85,
        });

        // Inner geometric ring + diagonal ticks + bottom Horus star chevrons
        canvas.Children.Add(new Path
        {
            Data = Geometry.Parse(
                "M 32,80 A 48,48 0 1 1 128,80 A 48,48 0 1 1 32,80 " +
                "M 41,41 L 48,48 M 119,41 L 112,48 M 41,119 L 48,112 M 119,119 L 112,112 " +
                "M 62,126 L 80,144 L 98,126 M 54,132 L 66,118 M 106,132 L 94,118"),
            Stroke = secondary,
            StrokeThickness = 1.15,
            StrokeLineJoin = PenLineJoin.Round,
            Opacity = 0.78,
        });

        if (includeRadarArcs)
        {
            canvas.Children.Add(new Path
            {
                Data = Geometry.Parse(
                    "M 14,52 A 72,72 0 0 0 14,108 M 146,52 A 72,72 0 0 1 146,108 " +
                    "M 6,58 A 81,81 0 0 0 6,102 M 154,58 A 81,81 0 0 1 154,102"),
                Stroke = primary,
                StrokeThickness = 1.0,
                Opacity = 0.35,
            });
        }

        return new Viewbox
        {
            Child = canvas,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false,
            Visibility = spec.IsPixelMode ? Visibility.Collapsed : Visibility.Visible,
        };
    }

    private static Path MakePath(string data, Brush? fill, Brush? stroke, double thickness)
    {
        var geo = Geometry.Parse(data);
        geo.Freeze();
        return new Path
        {
            Data = geo,
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
        };
    }

    private static Path MakeAliasedRectPath(string data, Brush fill)
    {
        var geo = Geometry.Parse(data);
        geo.Freeze();
        var p = new Path { Data = geo, Fill = fill, IsHitTestVisible = false };
        RenderOptions.SetEdgeMode(p, EdgeMode.Aliased);
        return p;
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
