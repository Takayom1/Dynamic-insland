// Original HoshiBar code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Windows.Media;
using DIsland.Services;

namespace DIsland;

public static class HoshinoPalette
{
    public static Color Accent { get; private set; } = Color.FromRgb(234, 176, 207);
    public static Color Background { get; private set; } = Color.FromRgb(24, 20, 31);
    public static Color Text { get; private set; } = Color.FromRgb(244, 237, 246);
    public static bool IsLight { get; private set; }

    public static void Apply(AppSettings settings)
    {
        var spec = IslandSkinEngine.ApplyResources(settings);
        Accent = spec.Accent;
        Background = spec.Background;
        Text = spec.Text;
        IsLight = spec.IsLight;
    }

    public static Color ColorOf(string? value, string fallback) => IslandSkinPresets.Parse(value, fallback);
}
