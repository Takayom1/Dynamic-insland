// Original Hoshino Island code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Windows;
using System.Windows.Media;
using DIsland.Services;
namespace DIsland;
public static class HoshinoPalette
{
    public static Color Accent { get; private set; } = Color.FromRgb(234, 176, 207);
    public static Color Background { get; private set; } = Color.FromRgb(24, 20, 31);
    public static void Apply(AppSettings settings)
    {
        var graphite = settings.DesignPreset == "Graphite";
        var blue = settings.DesignPreset == "Abydos";
        Accent = ColorOf(settings.DesignPreset == "Custom" ? settings.DesignAccent : blue ? "#83CFF3" : graphite ? "#DADCE5" : "#EAB0CF", "#EAB0CF");
        Background = ColorOf(settings.DesignPreset == "Custom" ? settings.DesignBackground : graphite ? "#101113" : blue ? "#141D29" : "#18141F", "#18141F");
        Set("HoshinoBackground", Background);
        Set("HoshinoBackdrop", Color.FromArgb(224,Background.R,Background.G,Background.B));
        Set("HoshinoSurface", ColorOf(graphite ? "#18191C" : blue ? "#1D2A3B" : "#25202D", "#25202D"));
        Set("HoshinoControl", ColorOf(graphite ? "#25272B" : blue ? "#293A50" : "#332B3D", "#332B3D"));
        Set("HoshinoBorder", ColorOf(graphite ? "#34373D" : blue ? "#3A516B" : "#4A3C55", "#4A3C55"));
        Set("HoshinoAccent", Accent); Set("HoshinoText", ColorOf(settings.DesignText, "#F4EDF6"));
        Set("HoshinoMuted", ColorOf("#A59CAC", "#A59CAC"));
    }
    public static Color ColorOf(string? value, string fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(value ?? fallback); }
        catch (Exception e) when (e is FormatException or NotSupportedException) { return (Color)ColorConverter.ConvertFromString(fallback); }
    }
    private static void Set(string name, Color color)
    {
        if (Application.Current.Resources[name] is SolidColorBrush existing && !existing.IsFrozen) existing.Color = color;
        else Application.Current.Resources[name] = new SolidColorBrush(color);
    }
}
