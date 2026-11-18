// Original HoshiBar code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Windows.Media;
using DIsland.Services;

namespace DIsland;

public sealed record SkinPaletteSpec(
    IslandSkin Skin,
    string PresetId,
    Color Background,
    Color Surface,
    Color CompanionSurface,
    Color PlayerSurface,
    Color TasksSurface,
    Color Control,
    Color Border,
    Color Accent,
    Color SecondaryAccent,
    Color Gold,
    Color MomoBar,
    Color RimLeft,
    Color RimCenter,
    Color RimRight,
    Color Text,
    Color Muted,
    Color OnAccent,
    double RimThickness,
    double CardBorderThickness,
    double CardCornerRadius,
    double ExpandedHullRadius,
    double FixedBlurRatio,
    double BodyAlpha,
    bool IsLight,
    bool DisableGlassBlur,
    bool IsAeroGlass,
    bool IsShittimTablet,
    bool IsCartoonSticker,
    bool IsPixelMode);

/// <summary>
/// Definitions for the 5 visual skins of HoshiBar (Horus Glass + 5 lore palettes,
/// Shittim Chest, Win7 Aero, Chibi Pop, Pixel Tamagotchi), including fixed per-theme glass/matte physics
/// and hull radii.
/// </summary>
public static class IslandSkinPresets
{
    public static readonly (IslandSkin Value, string Label)[] SkinChoices =
    {
        (IslandSkin.HorusGlass, "Horus Glass"),
        (IslandSkin.ShittimChest, "Shittim Chest"),
        (IslandSkin.Win7Aero, "Win7 Aero"),
        (IslandSkin.ChibiPop, "Chibi Pop"),
        (IslandSkin.PixelTamagotchi, "Pixel Tamagotchi"),
    };

    public static readonly (string Value, string Label)[] HorusLorePalettes =
    {
        ("SakuraNap", "Sakura Nap"),
        ("AbydosOasis", "Abydos Oasis"),
        ("YumesCompass", "Yume's Compass"),
        ("IronHorus", "Iron Horus"),
        ("Aquarium", "Aquarium"),
        ("Custom", "Своя"),
    };

    public static SkinPaletteSpec Resolve(AppSettings settings) => settings.IslandSkin switch
    {
        IslandSkin.ShittimChest => BuildShittimChest(),
        IslandSkin.Win7Aero => BuildWin7Aero(),
        IslandSkin.ChibiPop => BuildChibiPop(),
        IslandSkin.PixelTamagotchi => BuildPixelTamagotchi(),
        _ => BuildHorusGlass(settings),
    };

    private static SkinPaletteSpec BuildHorusGlass(AppSettings s)
    {
        var preset = NormalizePreset(s.DesignPreset);
        if (preset == "Custom")
        {
            var bg = Parse(s.DesignBackground, "#18141F");
            var accent = Parse(s.DesignAccent, "#EAB0CF");
            var text = Parse(s.DesignText, "#F4EDF6");
            var light = Luminance(bg) > 0.55;
            var surface = WithAlpha(Shift(bg, light ? -0.05 : 0.08), 0xCC);
            var companion = WithAlpha(Shift(bg, light ? -0.03 : 0.11), 0xD6);
            var control = WithAlpha(Shift(bg, light ? -0.10 : 0.15), 0xDD);
            var border = WithAlpha(Shift(bg, light ? -0.18 : 0.24), 0xAA);
            var muted = Mix(text, bg, 0.36);
            var onAccent = Luminance(accent) > 0.52 ? Color.FromRgb(0x18, 0x14, 0x1F) : Colors.White;
            var sky = Parse("#83CFF3", "#83CFF3");
            var gold = Parse("#F2D17D", "#F2D17D");
            return new SkinPaletteSpec(
                IslandSkin.HorusGlass, "Custom",
                bg, surface, companion, surface, surface, control, border,
                accent, sky, gold, accent,
                gold, accent, sky,
                text, muted, onAccent,
                RimThickness: 1.4, CardBorderThickness: 1.0, CardCornerRadius: 18,
                ExpandedHullRadius: 28, FixedBlurRatio: 0.52, BodyAlpha: 0.78,
                IsLight: light, DisableGlassBlur: false,
                IsAeroGlass: false, IsShittimTablet: false, IsCartoonSticker: false, IsPixelMode: false);
        }

        return preset switch
        {
            "AbydosOasis" => MakeHorus("AbydosOasis",
                bg: "#101924", surface: "#182638", companion: "#1C2C40", control: "#243850", border: "#3B597A",
                accent: "#83CFF3", secondary: "#EAB0CF", gold: "#F2D17D",
                rimL: "#F2D17D", rimC: "#83CFF3", rimR: "#EAB0CF",
                text: "#EDF5FC", muted: "#98B2CA"),

            "YumesCompass" => MakeHorus("YumesCompass",
                bg: "#1C1713", surface: "#28211C", companion: "#2E2620", control: "#3A3028", border: "#544538",
                accent: "#F2D17D", secondary: "#83CFF3", gold: "#EAB0CF",
                rimL: "#F2D17D", rimC: "#EAB0CF", rimR: "#83CFF3",
                text: "#F7F1E8", muted: "#B8A998"),

            "IronHorus" => MakeHorus("IronHorus",
                bg: "#111318", surface: "#1B1E27", companion: "#1F232E", control: "#282D3B", border: "#3D465A",
                accent: "#FF85B3", secondary: "#5CE1E6", gold: "#F2D17D",
                rimL: "#F2D17D", rimC: "#FF85B3", rimR: "#5CE1E6",
                text: "#EEF1F7", muted: "#9EA7BA"),

            "Aquarium" => MakeHorus("Aquarium",
                bg: "#0B1623", surface: "#132438", companion: "#162A42", control: "#1E3652", border: "#2E5075",
                accent: "#64DFDF", secondary: "#F0A6CA", gold: "#83CFF3",
                rimL: "#83CFF3", rimC: "#64DFDF", rimR: "#F0A6CA",
                text: "#EBF7FA", muted: "#94B6C8"),

            _ => MakeHorus("SakuraNap",
                bg: "#18141F", surface: "#252030", companion: "#2A2336", control: "#352C42", border: "#4E405E",
                accent: "#EAB0CF", secondary: "#83CFF3", gold: "#F2D17D",
                rimL: "#F2D17D", rimC: "#EAB0CF", rimR: "#83CFF3",
                text: "#F4EDF6", muted: "#ACA2B4"),
        };
    }

    private static SkinPaletteSpec MakeHorus(
        string id,
        string bg, string surface, string companion, string control, string border,
        string accent, string secondary, string gold,
        string rimL, string rimC, string rimR,
        string text, string muted)
    {
        var ac = Parse(accent, "#EAB0CF");
        var onAccent = Luminance(ac) > 0.50 ? Color.FromRgb(0x18, 0x14, 0x1F) : Colors.White;
        return new SkinPaletteSpec(
            IslandSkin.HorusGlass, id,
            Parse(bg, "#18141F"),
            WithAlpha(Parse(surface, "#252030"), 0xCC),
            WithAlpha(Parse(companion, "#2A2336"), 0xD6),
            WithAlpha(Parse(surface, "#252030"), 0xCC),
            WithAlpha(Parse(surface, "#252030"), 0xCC),
            WithAlpha(Parse(control, "#352C42"), 0xDE),
            WithAlpha(Parse(border, "#4E405E"), 0xAA),
            ac,
            Parse(secondary, "#83CFF3"),
            Parse(gold, "#F2D17D"),
            ac,
            Parse(rimL, "#F2D17D"),
            Parse(rimC, "#EAB0CF"),
            Parse(rimR, "#83CFF3"),
            Parse(text, "#F4EDF6"),
            Parse(muted, "#ACA2B4"),
            onAccent,
            RimThickness: 1.45, CardBorderThickness: 1.0, CardCornerRadius: 18,
            ExpandedHullRadius: 28, FixedBlurRatio: 0.52, BodyAlpha: 0.78,
            IsLight: false, DisableGlassBlur: false,
            IsAeroGlass: false, IsShittimTablet: false, IsCartoonSticker: false, IsPixelMode: false);
    }

    /// <summary>
    /// Shittim Chest: Matte frosted milky-cyan translucency (heavy 0.88 blur diffusion, zero glossy streaks,
    /// frosted translucent white cards, crisp #38BDF8 technical Schale OS frame).
    /// </summary>
    private static SkinPaletteSpec BuildShittimChest() => new(
        IslandSkin.ShittimChest, "ShittimChest",
        Background: Color.FromRgb(0xEB, 0xF4, 0xFD),
        Surface: Color.FromArgb(0xD8, 0xFF, 0xFF, 0xFF),
        CompanionSurface: Color.FromArgb(0xE2, 0xFF, 0xFF, 0xFF),
        PlayerSurface: Color.FromArgb(0xD8, 0xFF, 0xFF, 0xFF),
        TasksSurface: Color.FromArgb(0xD8, 0xFF, 0xFF, 0xFF),
        Control: Color.FromArgb(0xE0, 0xE6, 0xF2, 0xFC),
        Border: Color.FromArgb(0xCC, 0xBF, 0xDC, 0xF6),
        Accent: Parse("#29B6F6", "#29B6F6"),
        SecondaryAccent: Parse("#FF85AD", "#FF85AD"),
        Gold: Parse("#38BDF8", "#38BDF8"),
        MomoBar: Parse("#FF85AD", "#FF85AD"),
        RimLeft: Parse("#38BDF8", "#38BDF8"),
        RimCenter: Parse("#29B6F6", "#29B6F6"),
        RimRight: Parse("#38BDF8", "#38BDF8"),
        Text: Parse("#1F3147", "#1F3147"),
        Muted: Parse("#5C7490", "#5C7490"),
        OnAccent: Colors.White,
        RimThickness: 1.75, CardBorderThickness: 1.1, CardCornerRadius: 15,
        ExpandedHullRadius: 22, FixedBlurRatio: 0.88, BodyAlpha: 0.70,
        IsLight: true, DisableGlassBlur: false,
        IsAeroGlass: false, IsShittimTablet: true, IsCartoonSticker: false, IsPixelMode: false);

    /// <summary>
    /// Win7 Aero: Futuristic glossy optical crystal glass (crisp 0.36 blur, translucent sky-sapphire tint,
    /// sweeping white reflection panes, double specular glass bevel).
    /// </summary>
    private static SkinPaletteSpec BuildWin7Aero() => new(
        IslandSkin.Win7Aero, "Win7Aero",
        Background: Color.FromRgb(0x14, 0x58, 0x94),
        Surface: Color.FromArgb(0x42, 0x09, 0x2E, 0x54),
        CompanionSurface: Color.FromArgb(0x4A, 0x0C, 0x36, 0x62),
        PlayerSurface: Color.FromArgb(0x42, 0x09, 0x2E, 0x54),
        TasksSurface: Color.FromArgb(0x42, 0x09, 0x2E, 0x54),
        Control: Color.FromArgb(0x58, 0x1A, 0x6A, 0xAC),
        Border: Color.FromArgb(0xB8, 0xA8, 0xE8, 0xFF),
        Accent: Parse("#22C4FF", "#22C4FF"),
        SecondaryAccent: Parse("#F6A8D2", "#F6A8D2"),
        Gold: Parse("#7CE8FF", "#7CE8FF"),
        MomoBar: Parse("#C4F0FF", "#C4F0FF"),
        RimLeft: Parse("#ECFAFF", "#ECFAFF"),
        RimCenter: Parse("#FFFFFF", "#FFFFFF"),
        RimRight: Parse("#AEECFF", "#AEECFF"),
        Text: Colors.White,
        Muted: Parse("#DCF2FF", "#DCF2FF"),
        OnAccent: Colors.White,
        RimThickness: 1.4, CardBorderThickness: 1.0, CardCornerRadius: 15,
        ExpandedHullRadius: 20, FixedBlurRatio: 0.36, BodyAlpha: 0.48,
        IsLight: false, DisableGlassBlur: false,
        IsAeroGlass: true, IsShittimTablet: false, IsCartoonSticker: false, IsPixelMode: false);

    private static SkinPaletteSpec BuildChibiPop() => new(
        IslandSkin.ChibiPop, "ChibiPop",
        Background: Parse("#FFF8FC", "#FFF8FC"),
        Surface: Parse("#FFFDF9", "#FFFDF9"),
        CompanionSurface: Parse("#FFE6F2", "#FFE6F2"),
        PlayerSurface: Parse("#E3F6FF", "#E3F6FF"),
        TasksSurface: Parse("#FFFDF7", "#FFFDF7"),
        Control: Parse("#FFFFFF", "#FFFFFF"),
        Border: Parse("#2A1E2E", "#2A1E2E"),
        Accent: Parse("#FF85AD", "#FF85AD"),
        SecondaryAccent: Parse("#5CE1E6", "#5CE1E6"),
        Gold: Parse("#FFE066", "#FFE066"),
        MomoBar: Parse("#FF85AD", "#FF85AD"),
        RimLeft: Parse("#2A1E2E", "#2A1E2E"),
        RimCenter: Parse("#2A1E2E", "#2A1E2E"),
        RimRight: Parse("#2A1E2E", "#2A1E2E"),
        Text: Parse("#2A1E2E", "#2A1E2E"),
        Muted: Parse("#685370", "#685370"),
        OnAccent: Parse("#2A1E2E", "#2A1E2E"),
        RimThickness: 2.8, CardBorderThickness: 2.4, CardCornerRadius: 16,
        ExpandedHullRadius: 24, FixedBlurRatio: 0.0, BodyAlpha: 1.0,
        IsLight: true, DisableGlassBlur: true,
        IsAeroGlass: false, IsShittimTablet: false, IsCartoonSticker: true, IsPixelMode: false);

    private static SkinPaletteSpec BuildPixelTamagotchi() => new(
        IslandSkin.PixelTamagotchi, "PixelTamagotchi",
        Background: Parse("#E6DFF0", "#E6DFF0"),
        Surface: Parse("#D5CAE6", "#D5CAE6"),
        CompanionSurface: Parse("#C9EDE3", "#C9EDE3"),
        PlayerSurface: Parse("#D5CAE6", "#D5CAE6"),
        TasksSurface: Parse("#FFFFFF", "#FFFFFF"),
        Control: Parse("#DCD1EC", "#DCD1EC"),
        Border: Parse("#3E3056", "#3E3056"),
        Accent: Parse("#F2B1D0", "#F2B1D0"),
        SecondaryAccent: Parse("#79CBE8", "#79CBE8"),
        Gold: Parse("#F5E49C", "#F5E49C"),
        MomoBar: Parse("#1F3834", "#1F3834"),
        RimLeft: Parse("#251B36", "#251B36"),
        RimCenter: Parse("#251B36", "#251B36"),
        RimRight: Parse("#251B36", "#251B36"),
        Text: Parse("#1B1528", "#1B1528"),
        Muted: Parse("#504168", "#504168"),
        OnAccent: Parse("#1B1528", "#1B1528"),
        RimThickness: 2.0, CardBorderThickness: 2.0, CardCornerRadius: 0,
        ExpandedHullRadius: 0, FixedBlurRatio: 0.0, BodyAlpha: 1.0,
        IsLight: true, DisableGlassBlur: true,
        IsAeroGlass: false, IsShittimTablet: false, IsCartoonSticker: false, IsPixelMode: true);

    public static string NormalizePreset(string? value) => value switch
    {
        null or "" or "Hoshino" or "SakuraNap" => "SakuraNap",
        "Abydos" or "AbydosOasis" => "AbydosOasis",
        "YumesCompass" => "YumesCompass",
        "Graphite" or "IronHorus" => "IronHorus",
        "Aquarium" => "Aquarium",
        "Custom" => "Custom",
        _ => "SakuraNap",
    };

    public static Color Parse(string? hex, string fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex ?? fallback); }
        catch (Exception e) when (e is FormatException or NotSupportedException)
        {
            return (Color)ColorConverter.ConvertFromString(fallback);
        }
    }

    public static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    public static double Luminance(Color c) =>
        (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    public static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (byte)Math.Round(a.A + (b.A - a.A) * t),
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }

    private static Color Shift(Color c, double delta)
    {
        var target = delta >= 0 ? Colors.White : Colors.Black;
        return Mix(c, target, Math.Abs(delta));
    }
}
