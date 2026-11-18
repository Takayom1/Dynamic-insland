// Original HoshiBar code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using DIsland.Services;

namespace DIsland;

/// <summary>
/// Applies the active HoshiBar skin &amp; lore palette to Application resources and builds
/// skin-specific vector brushes, shadows, bevels, and retro LCD overlays.
/// </summary>
public static class IslandSkinEngine
{
    private static readonly Uri PackBaseUri = new("pack://application:,,,/");
    private static readonly FontFamily HorusFont = new("Segoe UI Variable Display, Segoe UI");
    private static readonly FontFamily AeroFont = new("Segoe UI");
    private static readonly FontFamily ShittimFont = new(PackBaseUri, "./Assets/Fonts/#Exo 2, Segoe UI");
    private static readonly FontFamily ChibiFont = new(PackBaseUri, "./Assets/Fonts/#Rounded Mplus 1c, Segoe UI");
    // Clean, ultra-readable condensed geometric UI font matching the crisp Cyrillic text in the Pixel Tamagotchi concept
    private static readonly FontFamily PixelFont = new("Bahnschrift, Segoe UI");

    public static SkinPaletteSpec Current { get; private set; } = IslandSkinPresets.Resolve(new AppSettings());

    public static FontFamily GetThemeFontFamily(SkinPaletteSpec spec) => spec.Skin switch
    {
        IslandSkin.PixelTamagotchi => PixelFont,
        IslandSkin.ChibiPop => ChibiFont,
        IslandSkin.ShittimChest => ShittimFont,
        IslandSkin.Win7Aero => AeroFont,
        _ => HorusFont,
    };

    public static SkinPaletteSpec ApplyResources(AppSettings settings)
    {
        var spec = IslandSkinPresets.Resolve(settings);
        Current = spec;

        if (Application.Current is null) return spec;

        SetSolid("HoshinoBackground", spec.Background);
        var backdropAlpha = spec.DisableGlassBlur ? (byte)255 : spec.IsAeroGlass ? (byte)216 : spec.IsLight ? (byte)242 : (byte)228;
        SetSolid("HoshinoBackdrop", Color.FromArgb(backdropAlpha, spec.Background.R, spec.Background.G, spec.Background.B));
        SetSolid("HoshinoSurface", spec.Surface);
        SetSolid("HoshinoCompanionSurface", spec.CompanionSurface);
        SetSolid("HoshinoPlayerSurface", spec.PlayerSurface);
        SetSolid("HoshinoTasksSurface", spec.TasksSurface);
        SetSolid("HoshinoControl", spec.Control);
        SetSolid("HoshinoBorder", spec.Border);
        SetSolid("HoshinoAccent", spec.Accent);
        SetSolid("HoshinoSecondaryAccent", spec.SecondaryAccent);
        SetSolid("HoshinoGold", spec.Gold);
        SetSolid("HoshinoMomoBar", spec.MomoBar);
        SetSolid("HoshinoText", spec.Text);
        SetSolid("HoshinoMuted", spec.Muted);
        SetSolid("HoshinoOnAccent", spec.OnAccent);

        var hover = spec.IsLight
            ? Color.FromArgb(0x1C, spec.Text.R, spec.Text.G, spec.Text.B)
            : Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF);
        var pressed = spec.IsLight
            ? Color.FromArgb(0x30, spec.Text.R, spec.Text.G, spec.Text.B)
            : Color.FromArgb(0x42, 0xFF, 0xFF, 0xFF);
        var divider = spec.IsCartoonSticker
            ? Color.FromArgb(0x55, spec.Border.R, spec.Border.G, spec.Border.B)
            : spec.IsPixelMode
                ? Color.FromArgb(0x88, spec.Border.R, spec.Border.G, spec.Border.B)
                : spec.IsLight
                    ? Color.FromArgb(0x24, spec.Text.R, spec.Text.G, spec.Text.B)
                    : Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF);
        var track = spec.IsCartoonSticker
            ? Color.FromRgb(0x4A, 0x39, 0x52)
            : spec.IsPixelMode
                ? Color.FromArgb(0x55, 0x1F, 0x38, 0x34)
                : spec.IsLight
                    ? Color.FromArgb(0x26, spec.Text.R, spec.Text.G, spec.Text.B)
                    : Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);
        var placeholder = spec.IsLight
            ? IslandSkinPresets.Mix(spec.Control, spec.Border, 0.35)
            : Color.FromRgb(0x2B, 0x26, 0x35);

        SetSolid("HoshinoControlHover", hover);
        SetSolid("HoshinoControlPressed", pressed);
        SetSolid("HoshinoDivider", divider);
        SetSolid("HoshinoTrack", track);
        SetSolid("HoshinoArtPlaceholder", placeholder);

        // Skin-adaptive button, chip & font template resources (App.xaml)
        var res = Application.Current.Resources;
        res["HoshinoFontFamily"] = GetThemeFontFamily(spec);
        res["HoshinoButtonCornerRadius"] = new CornerRadius(spec.IsPixelMode ? 0 : spec.IsAeroGlass ? 13 : 14);
        res["HoshinoButtonInnerCornerRadius"] = new CornerRadius(spec.IsPixelMode ? 0 : spec.IsAeroGlass ? 12 : 13);
        res["HoshinoChipBorderThickness"] = new Thickness(spec.IsCartoonSticker ? 2.2 : spec.IsPixelMode ? 1.8 : 1.0);
        SetSolid("HoshinoChipBorderBrush",
            spec.IsCartoonSticker ? Color.FromRgb(0x2A, 0x1E, 0x2E)
            : spec.IsPixelMode ? Color.FromRgb(0x24, 0x1A, 0x34)
            : spec.IsAeroGlass ? Color.FromArgb(0xC2, 0x07, 0x28, 0x4A)
            : spec.Border);

        res["HoshinoChipInnerRimThickness"] = new Thickness(spec.IsAeroGlass ? 1.0 : 0.0);
        SetSolid("HoshinoChipInnerRimBrush", spec.IsAeroGlass ? Color.FromArgb(0xBD, 0xEC, 0xF9, 0xFF) : Colors.Transparent);

        res["HoshinoPixelBevelTLThickness"] = spec.IsPixelMode ? new Thickness(2, 2, 0, 0) : new Thickness(0);
        res["HoshinoPixelBevelBRThickness"] = spec.IsPixelMode ? new Thickness(0, 0, 2, 2) : new Thickness(0);
        SetSolid("HoshinoPixelBevelTopLeft", spec.IsPixelMode ? Colors.White : Colors.Transparent);
        SetSolid("HoshinoPixelBevelBottomRight", spec.IsPixelMode ? Color.FromRgb(0x5B, 0x4B, 0x73) : Colors.Transparent);
        res["HoshinoCartoonShadowVis"] = spec.IsCartoonSticker ? Visibility.Visible : Visibility.Collapsed;

        res["HoshinoProgressBrush"] = BuildProgressBrush(spec);
        res["HoshinoProgressTrackBrush"] = BuildProgressTrackBrush(spec);
        res["HoshinoOsdTrackBrush"] = BuildOsdTrackBrush(spec);
        res["HoshinoRimBrush"] = CreateRimBrush(spec);
        return spec;
    }

    public static Brush CreateIslandBodyBrush(SkinPaletteSpec spec, Color? accent, double tint, double alphaScale = 1.0)
    {
        var baseOpacity = spec.BodyAlpha * Math.Clamp(alphaScale, 0.12, 1.0);
        byte ScaleA(double factor) => (byte)Math.Clamp(Math.Round(255.0 * baseOpacity * factor), 8, 255);

        if (spec.IsAeroGlass)
        {
            // Futuristic crystal sky-sapphire optical glass (lets DWM/GPU blur refract vividly underneath!)
            var aero = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.25, 1) };
            aero.GradientStops.Add(new GradientStop(Color.FromArgb(ScaleA(0.95), 0x2C, 0x8C, 0xDC), 0.00));
            aero.GradientStops.Add(new GradientStop(Color.FromArgb(ScaleA(1.05), 0x12, 0x52, 0x8E), 0.48));
            aero.GradientStops.Add(new GradientStop(Color.FromArgb(ScaleA(1.10), 0x0B, 0x3C, 0x6C), 0.80));
            aero.GradientStops.Add(new GradientStop(Color.FromArgb(ScaleA(1.00), 0x20, 0x7A, 0xC2), 1.00));
            aero.Freeze();
            return aero;
        }

        if (spec.IsShittimTablet)
        {
            // Matte frosted milky-cyan polycarbonate translucency (zero mirror glare, soft uniform diffusion)
            var shittim = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            shittim.GradientStops.Add(new GradientStop(Color.FromArgb(ScaleA(1.02), 0xF2, 0xF8, 0xFF), 0.0));
            shittim.GradientStops.Add(new GradientStop(Color.FromArgb(ScaleA(1.00), 0xE6, 0xF2, 0xFD), 0.55));
            shittim.GradientStops.Add(new GradientStop(Color.FromArgb(ScaleA(1.04), 0xDC, 0xED, 0xFB), 1.0));
            shittim.Freeze();
            return shittim;
        }

        if (spec.IsCartoonSticker || spec.IsPixelMode)
        {
            var solid = new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(Math.Round(255.0 * alphaScale), 16, 255), spec.Background.R, spec.Background.G, spec.Background.B));
            solid.Freeze();
            return solid;
        }

        // Horus Glass: velvety dark smoked obsidian glass over live GPU blur
        var baseCol = accent is { } ac && tint > 0 ? IslandSkinPresets.Mix(spec.Background, ac, tint) : spec.Background;
        var topCol = IslandSkinPresets.Mix(baseCol, spec.Surface, 0.38);
        var botCol = IslandSkinPresets.Mix(baseCol, Colors.Black, 0.20);
        var horus = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.3, 1) };
        horus.GradientStops.Add(new GradientStop(Color.FromArgb(ScaleA(0.98), topCol.R, topCol.G, topCol.B), 0.0));
        horus.GradientStops.Add(new GradientStop(Color.FromArgb(ScaleA(1.02), baseCol.R, baseCol.G, baseCol.B), 0.55));
        horus.GradientStops.Add(new GradientStop(Color.FromArgb(ScaleA(1.06), botCol.R, botCol.G, botCol.B), 1.0));
        horus.Freeze();
        return horus;
    }

    public static Brush CreateRimBrush(SkinPaletteSpec spec)
    {
        if (spec.RimLeft == spec.RimCenter && spec.RimCenter == spec.RimRight)
        {
            var solid = new SolidColorBrush(spec.RimCenter);
            solid.Freeze();
            return solid;
        }

        var grad = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.35),
            EndPoint = new Point(1, 0.65),
        };
        grad.GradientStops.Add(new GradientStop(spec.RimLeft, 0.0));
        grad.GradientStops.Add(new GradientStop(spec.RimCenter, 0.50));
        grad.GradientStops.Add(new GradientStop(spec.RimRight, 1.0));
        grad.Freeze();
        return grad;
    }

    public static Brush BuildProgressBrush(SkinPaletteSpec spec)
    {
        if (spec.IsCartoonSticker)
        {
            // Repeating diagonal candy-cane stripes (#FFE066 / #FF85AD / #5CE1E6)
            var group = new DrawingGroup();
            var bg = new SolidColorBrush(Color.FromRgb(0xFF, 0x85, 0xAD));
            var yellow = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x66));
            var cyan = new SolidColorBrush(Color.FromRgb(0x5C, 0xE1, 0xE6));
            group.Children.Add(new GeometryDrawing(bg, null, new RectangleGeometry(new Rect(0, 0, 24, 12))));
            group.Children.Add(new GeometryDrawing(yellow, null, Geometry.Parse("M -4,12 L 4,0 H 12 L 4,12 Z M 20,12 L 28,0 H 36 L 28,12 Z")));
            group.Children.Add(new GeometryDrawing(cyan, null, Geometry.Parse("M 4,12 L 12,0 H 20 L 12,12 Z")));
            var brush = new DrawingBrush(group)
            {
                Viewport = new Rect(0, 0, 24, 12),
                ViewportUnits = BrushMappingMode.Absolute,
                TileMode = TileMode.Tile,
                Stretch = Stretch.Fill,
            };
            brush.Freeze();
            return brush;
        }

        if (spec.IsPixelMode)
        {
            // Discrete stepped dark-mint LCD cubes (7px block + 2px gap)
            var group = new DrawingGroup();
            var ink = new SolidColorBrush(Color.FromRgb(0x18, 0x35, 0x31));
            group.Children.Add(new GeometryDrawing(ink, null, new RectangleGeometry(new Rect(0, 0, 7, 10))));
            var brush = new DrawingBrush(group)
            {
                Viewport = new Rect(0, 0, 9, 10),
                ViewportUnits = BrushMappingMode.Absolute,
                TileMode = TileMode.Tile,
                Stretch = Stretch.None,
                AlignmentY = AlignmentY.Center,
            };
            brush.Freeze();
            return brush;
        }

        if (spec.IsAeroGlass)
        {
            // Cylindrical 3D gel gradient (cyan -> soft sakura with glossy top sheen)
            var group = new DrawingGroup();
            var baseGrad = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            baseGrad.GradientStops.Add(new GradientStop(Color.FromRgb(0x28, 0xD8, 0xFF), 0));
            baseGrad.GradientStops.Add(new GradientStop(Color.FromRgb(0x7C, 0xD6, 0xFF), 0.55));
            baseGrad.GradientStops.Add(new GradientStop(Color.FromRgb(0xF6, 0xA6, 0xD0), 1));
            var glossGrad = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            glossGrad.GradientStops.Add(new GradientStop(Color.FromArgb(0xB8, 0xFF, 0xFF, 0xFF), 0));
            glossGrad.GradientStops.Add(new GradientStop(Color.FromArgb(0x45, 0xFF, 0xFF, 0xFF), 0.48));
            glossGrad.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.52));
            glossGrad.GradientStops.Add(new GradientStop(Color.FromArgb(0x38, 0x06, 0x24, 0x42), 1));

            group.Children.Add(new GeometryDrawing(baseGrad, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
            group.Children.Add(new GeometryDrawing(glossGrad, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
            var brush = new DrawingBrush(group) { Stretch = Stretch.Fill };
            brush.Freeze();
            return brush;
        }

        if (spec.IsShittimTablet)
        {
            var shittim = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            shittim.GradientStops.Add(new GradientStop(Color.FromRgb(0x38, 0xBD, 0xF8), 0.0));
            shittim.GradientStops.Add(new GradientStop(Color.FromRgb(0x6C, 0xB8, 0xF8), 1.0));
            shittim.Freeze();
            return shittim;
        }

        // Horus Glass heterochromatic lore gradient (#F2D17D -> #EAB0CF -> #83CFF3 or active lore colors)
        var horus = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        horus.GradientStops.Add(new GradientStop(spec.RimLeft, 0.0));
        horus.GradientStops.Add(new GradientStop(spec.RimCenter, 0.52));
        horus.GradientStops.Add(new GradientStop(spec.RimRight, 1.0));
        horus.Freeze();
        return horus;
    }

    public static Brush BuildProgressTrackBrush(SkinPaletteSpec spec)
    {
        if (spec.IsPixelMode)
        {
            // Empty LCD matrix cubes aligned with BuildProgressBrush (7px block + 2px gap)
            var group = new DrawingGroup();
            var dimInk = new SolidColorBrush(Color.FromArgb(0x36, 0x18, 0x35, 0x31));
            group.Children.Add(new GeometryDrawing(dimInk, null, new RectangleGeometry(new Rect(0, 0, 7, 10))));
            var brush = new DrawingBrush(group)
            {
                Viewport = new Rect(0, 0, 9, 10),
                ViewportUnits = BrushMappingMode.Absolute,
                TileMode = TileMode.Tile,
                Stretch = Stretch.None,
                AlignmentY = AlignmentY.Center,
            };
            brush.Freeze();
            return brush;
        }

        if (spec.IsAeroGlass)
        {
            var trough = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            trough.GradientStops.Add(new GradientStop(Color.FromArgb(0x78, 0x06, 0x20, 0x3A), 0));
            trough.GradientStops.Add(new GradientStop(Color.FromArgb(0x40, 0x20, 0x62, 0x98), 0.6));
            trough.GradientStops.Add(new GradientStop(Color.FromArgb(0x50, 0x9E, 0xDF, 0xFF), 1));
            trough.Freeze();
            return trough;
        }

        if (spec.IsCartoonSticker)
            return Frozen(Color.FromRgb(0x4F, 0x3C, 0x57));

        if (spec.IsShittimTablet)
            return Frozen(Color.FromRgb(0xF6, 0x96, 0xB8));

        return Frozen(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
    }

    public static Brush BuildOsdTrackBrush(SkinPaletteSpec spec)
    {
        if (spec.IsCartoonSticker)
            return Brushes.White;
        if (spec.IsShittimTablet)
            return Frozen(Color.FromRgb(0xF6, 0x96, 0xB8));
        if (spec.IsAeroGlass)
            return BuildProgressTrackBrush(spec);
        if (spec.IsPixelMode)
            return BuildProgressTrackBrush(spec);
        return Frozen(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
    }

    public static Brush[] CreateEqualizerBrushes(SkinPaletteSpec spec)
    {
        if (spec.IsPixelMode)
        {
            var lcd = Frozen(Color.FromRgb(0x14, 0x2D, 0x2A));
            return new[] { lcd, lcd, lcd, lcd };
        }
        if (spec.IsCartoonSticker)
        {
            return new[]
            {
                Frozen(Color.FromRgb(0xFF, 0xE0, 0x66)),
                Frozen(Color.FromRgb(0x5C, 0xE1, 0xE6)),
                Frozen(Color.FromRgb(0xFF, 0x85, 0xAD)),
                Frozen(Color.FromRgb(0x5C, 0xE1, 0xE6)),
            };
        }
        if (spec.IsShittimTablet)
        {
            return new[]
            {
                Frozen(Color.FromRgb(0x38, 0xBD, 0xF8)),
                Frozen(Color.FromRgb(0x38, 0xBD, 0xF8)),
                Frozen(Color.FromRgb(0xFF, 0x85, 0xAD)),
                Frozen(Color.FromRgb(0xFF, 0x85, 0xAD)),
            };
        }
        if (spec.IsAeroGlass)
        {
            return new[]
            {
                Frozen(Color.FromRgb(0x4A, 0xDE, 0xFF)),
                Frozen(Color.FromRgb(0x78, 0xE8, 0xFF)),
                Frozen(Color.FromRgb(0xF6, 0xA6, 0xD0)),
                Frozen(Color.FromRgb(0xF6, 0xA6, 0xD0)),
            };
        }
        return new[]
        {
            Frozen(spec.RimLeft),
            Frozen(spec.RimCenter),
            Frozen(IslandSkinPresets.Mix(spec.RimCenter, spec.RimRight, 0.45)),
            Frozen(spec.RimRight),
        };
    }

    public static Brush CreateStartOrbBrush()
    {
        var group = new DrawingGroup();
        var orb = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.72),
            GradientOrigin = new Point(0.5, 0.8),
            RadiusX = 0.65,
            RadiusY = 0.65,
        };
        orb.GradientStops.Add(new GradientStop(Color.FromRgb(0x64, 0xF0, 0xFF), 0.0));
        orb.GradientStops.Add(new GradientStop(Color.FromRgb(0x16, 0x88, 0xD8), 0.58));
        orb.GradientStops.Add(new GradientStop(Color.FromRgb(0x0B, 0x3E, 0x72), 1.0));
        group.Children.Add(new GeometryDrawing(orb, new Pen(Frozen(Color.FromArgb(0xCC, 0xBF, 0xF2, 0xFF)), 1.0),
            new EllipseGeometry(new Point(16, 16), 15, 15)));

        var crescent = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        crescent.GradientStops.Add(new GradientStop(Color.FromArgb(0xD8, 0xFF, 0xFF, 0xFF), 0));
        crescent.GradientStops.Add(new GradientStop(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF), 1));
        group.Children.Add(new GeometryDrawing(crescent, null, new EllipseGeometry(new Point(16, 9.5), 11.5, 7)));

        var db = new DrawingBrush(group) { Stretch = Stretch.Fill };
        db.Freeze();
        return db;
    }

    public static Brush CreateAeroConvexGlassBrush(bool activeAccent = false)
    {
        var grad = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        if (activeAccent)
        {
            grad.GradientStops.Add(new GradientStop(Color.FromRgb(0x7E, 0xE6, 0xFF), 0.0));
            grad.GradientStops.Add(new GradientStop(Color.FromRgb(0x2D, 0xB8, 0xF5), 0.49));
            grad.GradientStops.Add(new GradientStop(Color.FromRgb(0x12, 0x93, 0xDC), 0.51));
            grad.GradientStops.Add(new GradientStop(Color.FromRgb(0x38, 0xCC, 0xFF), 1.0));
        }
        else
        {
            grad.GradientStops.Add(new GradientStop(Color.FromArgb(0x78, 0xFF, 0xFF, 0xFF), 0.0));
            grad.GradientStops.Add(new GradientStop(Color.FromArgb(0x36, 0xFF, 0xFF, 0xFF), 0.49));
            grad.GradientStops.Add(new GradientStop(Color.FromArgb(0x14, 0x0A, 0x2E, 0x52), 0.51));
            grad.GradientStops.Add(new GradientStop(Color.FromArgb(0x38, 0x90, 0xDF, 0xFF), 1.0));
        }
        grad.Freeze();
        return grad;
    }

    public static DropShadowEffect CreateCartoonHardShadow(double depth = 4.2) => new()
    {
        BlurRadius = 0,
        ShadowDepth = depth,
        Direction = 315,
        Color = Color.FromRgb(0x2A, 0x1E, 0x2E),
        Opacity = 1.0,
    };

    public static Brush CreateMintLcdGridBrush()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Frozen(Color.FromRgb(0xC9, 0xED, 0xE3)), null, new RectangleGeometry(new Rect(0, 0, 4, 4))));
        group.Children.Add(new GeometryDrawing(Frozen(Color.FromArgb(0x52, 0x89, 0xBE, 0xB0)), null,
            Geometry.Parse("M 0,0 H 4 V 1 H 0 Z M 0,1 H 1 V 4 H 0 Z")));
        var brush = new DrawingBrush(group)
        {
            Viewport = new Rect(0, 0, 4, 4),
            ViewportUnits = BrushMappingMode.Absolute,
            TileMode = TileMode.Tile,
        };
        brush.Freeze();
        return brush;
    }

    public static Brush CreateWinampLcdBrush()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Frozen(Color.FromRgb(0x9D, 0xBD, 0xB5)), null, new RectangleGeometry(new Rect(0, 0, 3, 3))));
        group.Children.Add(new GeometryDrawing(Frozen(Color.FromArgb(0x38, 0x5E, 0x82, 0x7A)), null,
            new RectangleGeometry(new Rect(0, 0, 3, 1))));
        var brush = new DrawingBrush(group)
        {
            Viewport = new Rect(0, 0, 3, 3),
            ViewportUnits = BrushMappingMode.Absolute,
            TileMode = TileMode.Tile,
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>Builds the Tamagotchi LCD top status icon strip (fish, snack, heart, whale, star, zZ).</summary>
    public static UniformGrid CreateTamagotchiStatusStrip(bool drowsy, bool playing, bool focusing)
    {
        var row = new UniformGrid { Rows = 1, Columns = 6, Height = 14, Margin = new Thickness(6, 4, 6, 2), IsHitTestVisible = false };
        var activeInk = Frozen(Color.FromRgb(0x1E, 0x45, 0x40));
        var dimInk = Frozen(Color.FromArgb(0x68, 0x1E, 0x45, 0x40));

        string[] icons =
        {
            // 1. Pixel fish
            "M 1,5 H 3 V 3 H 8 V 5 H 10 V 3 H 12 V 9 H 10 V 7 H 8 V 9 H 3 V 7 H 1 Z M 4,5 H 5 V 6 H 4 Z",
            // 2. Pixel snack / leaf
            "M 3,7 H 5 V 5 H 7 V 3 H 10 V 6 H 8 V 8 H 6 V 10 H 2 V 8 H 3 Z",
            // 3. Pixel heart
            "M 2,3 H 5 V 4 H 7 V 3 H 10 V 4 H 11 V 7 H 9 V 9 H 7 V 11 H 5 V 9 H 3 V 7 H 1 V 4 H 2 Z",
            // 4. Pixel whale
            "M 2,5 H 8 V 4 H 10 V 3 H 12 V 5 H 10 V 8 H 9 V 9 H 3 V 8 H 1 V 6 H 2 Z",
            // 5. Pixel star
            "M 5,1 H 7 V 4 H 11 V 6 H 8 V 8 H 9 V 11 H 7 V 9 H 5 V 11 H 3 V 8 H 4 V 6 H 1 V 4 H 5 Z",
            // 6. Pixel zZ
            "M 1,6 H 5 V 7 L 2,10 H 5 V 11 H 1 V 10 L 4,7 H 1 Z M 6,2 H 11 V 3 L 7,7 H 11 V 8 H 6 V 7 L 10,3 H 6 Z",
        };
        bool[] lit = { !drowsy, playing, true, true, focusing, drowsy };

        for (var i = 0; i < icons.Length; i++)
        {
            var geo = Geometry.Parse(icons[i]);
            geo.Freeze();
            var p = new Path
            {
                Data = geo,
                Fill = lit[i] ? activeInk : dimInk,
                Width = 12,
                Height = 12,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            RenderOptions.SetEdgeMode(p, EdgeMode.Aliased);
            row.Children.Add(p);
        }
        return row;
    }

    /// <summary>Creates a 3D pixel-beveled container (raised button or sunken LCD well).</summary>
    public static void ApplyPixelBevel(Grid host, bool sunken)
    {
        host.SnapsToDevicePixels = true;
        host.UseLayoutRounding = true;
        for (var i = host.Children.Count - 1; i >= 0; i--)
        {
            if (host.Children[i] is FrameworkElement fe && Equals(fe.Tag, "PixelBevelAdorner"))
                host.Children.RemoveAt(i);
        }

        var topLeft = Frozen(sunken ? Color.FromRgb(0x5B, 0x4B, 0x73) : Colors.White);
        var bottomRight = Frozen(sunken ? Colors.White : Color.FromRgb(0x5B, 0x4B, 0x73));

        var tl = new Border
        {
            Tag = "PixelBevelAdorner",
            BorderBrush = topLeft,
            BorderThickness = new Thickness(2, 2, 0, 0),
            IsHitTestVisible = false,
        };
        var br = new Border
        {
            Tag = "PixelBevelAdorner",
            BorderBrush = bottomRight,
            BorderThickness = new Thickness(0, 0, 2, 2),
            IsHitTestVisible = false,
        };
        host.Children.Add(tl);
        host.Children.Add(br);
    }

    public static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static void SetSolid(string name, Color color)
    {
        if (Application.Current.Resources[name] is SolidColorBrush existing && !existing.IsFrozen)
            existing.Color = color;
        else
            Application.Current.Resources[name] = new SolidColorBrush(color);
    }
}
