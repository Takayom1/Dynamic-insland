// Original HoshiBar code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DIsland.Interop;
using DIsland.Services;

namespace DIsland;

public enum TrayVisualState
{
    Idle,
    FocusRing,
    Music,
    Sleep,
    Voice,
    GhostOrHidden,
}

/// <summary>
/// Event-driven vector tray icon renderer for Hoshino across all 5 skins and 6 live states.
/// Uses canonical Hoshino silhouette (hair, ahoge, heterochromatic eyes) and cleans up HICON handles safely.
/// </summary>
public static class TrayHoshinoIconRenderer
{
    private static readonly Geometry HairGeo = FrozenGeo(
        "M55 24C49 11 61 1 72 7c6 3 7 8 4 12-1-6-6-8-11-6-5 2-6 7-4 11C82 21 99 36 101 56l10 8-10 1 6 7-9-1c1 15 2 24 8 35l-10-4 2 12-11-8-2 10c-8-8-11-18-12-29H47c-1 11-4 21-12 29l-2-10-11 8 2-12-10 4c6-11 7-20 8-35l-9 1 6-7-10-1 10-8C21 36 34 24 55 24Z");

    private static readonly Geometry FaceGeo = FrozenGeo(
        "M30 57C30 36 90 36 90 57V71C90 87 78 97 60 98 42 97 30 87 30 71Z");

    private static readonly Geometry BangsGeo = FrozenGeo(
        "M23 59C25 34 38 21 60 24 82 21 98 37 98 59L104 65C94 64 89 59 85 54c4 9 3 15-2 19-2-8-3-16-7-23 0 12-6 19-15 24 4-13 1-23-2-32-1 14-5 23-12 28 1-13-1-21-4-27-1 12-5 21-13 26l3-13c-4 4-9 7-15 8Z");

    public static System.Drawing.Icon RenderIcon(
        SkinPaletteSpec spec,
        TrayVisualState state,
        double timerRatio = 0.0,
        int pixelSize = 32)
    {
        pixelSize = Math.Clamp(pixelSize, 16, 64);
        var visual = new DrawingVisual();
        if (spec.IsPixelMode)
            RenderOptions.SetEdgeMode(visual, EdgeMode.Aliased);

        using (var dc = visual.RenderOpen())
        {
            var scale = pixelSize / 120.0;
            dc.PushTransform(new ScaleTransform(scale, scale));

            var ghost = state == TrayVisualState.GhostOrHidden;
            if (ghost)
                dc.PushOpacity(0.56);

            // Subtle dark/light badge plate for crisp contrast on both dark & light Windows taskbars
            var plateFill = IslandSkinEngine.Frozen(spec.IsLight
                ? Color.FromArgb(0xEC, 0xFF, 0xFB, 0xFE)
                : Color.FromArgb(0xEA, 0x1A, 0x15, 0x23));
            var plateRim = new Pen(IslandSkinEngine.Frozen(
                ghost ? Color.FromRgb(0x83, 0xCF, 0xF3)
                : state == TrayVisualState.Voice ? Color.FromRgb(0xF2, 0xD1, 0x7D)
                : spec.IsCartoonSticker ? Color.FromRgb(0x2A, 0x1E, 0x2E)
                : Color.FromArgb(0x98, spec.Accent.R, spec.Accent.G, spec.Accent.B)),
                spec.IsCartoonSticker ? 6.5 : 4.2);

            if (ghost)
                plateRim.DashStyle = new DashStyle(new double[] { 2.2, 1.8 }, 0);
            plateRim.Freeze();

            if (spec.IsPixelMode)
                dc.DrawRectangle(plateFill, plateRim, new Rect(6, 6, 108, 108));
            else
                dc.DrawEllipse(plateFill, state == TrayVisualState.FocusRing ? null : plateRim, new Point(60, 60), 54, 54);

            // Depleting heterochromatic Focus Ring around the badge
            if (state == TrayVisualState.FocusRing)
            {
                var trackPen = new Pen(IslandSkinEngine.Frozen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), 7.5);
                trackPen.Freeze();
                dc.DrawEllipse(null, trackPen, new Point(60, 60), 53, 53);

                var arcGeo = IslandChromeRenderer.BuildCircularProgressArc(120, 7.0, Math.Clamp(timerRatio, 0.04, 0.999));
                var arcPen = new Pen(IslandSkinEngine.CreateRimBrush(spec), 8.0)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                };
                arcPen.Freeze();
                dc.DrawGeometry(null, arcPen, arcGeo);
            }

            // Canonical Hoshino head (scaled nicely inside the 120x120 canvas)
            dc.PushTransform(new ScaleTransform(0.92, 0.92, 60, 62));

            if (spec.IsCartoonSticker)
            {
                var outlinePen = new Pen(IslandSkinEngine.Frozen(Color.FromRgb(0x2A, 0x1E, 0x2E)), 9.0) { LineJoin = PenLineJoin.Round };
                outlinePen.Freeze();
                dc.DrawGeometry(Brushes.White, outlinePen, HairGeo);
            }

            var hairBrush = IslandSkinEngine.Frozen(ghost ? Color.FromRgb(0xB8, 0xAE, 0xC4) : Color.FromRgb(0xEA, 0xB0, 0xCF));
            var faceBrush = IslandSkinEngine.Frozen(Color.FromRgb(0xFF, 0xF0, 0xE9));
            dc.DrawGeometry(hairBrush, null, HairGeo);
            dc.DrawGeometry(faceBrush, null, FaceGeo);
            dc.DrawGeometry(hairBrush, null, BangsGeo);

            if (state == TrayVisualState.Sleep)
            {
                var sleepPen = new Pen(IslandSkinEngine.Frozen(Color.FromRgb(0x49, 0x36, 0x52)), 4.6)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                };
                sleepPen.Freeze();
                dc.DrawLine(sleepPen, new Point(40, 76), new Point(53, 76));
                dc.DrawLine(sleepPen, new Point(67, 76), new Point(80, 76));
            }
            else
            {
                var goldEye = IslandSkinEngine.Frozen(Color.FromRgb(0xF2, 0xD1, 0x7D));
                var skyEye = IslandSkinEngine.Frozen(Color.FromRgb(0x83, 0xCF, 0xF3));
                dc.DrawEllipse(goldEye, null, new Point(47, 77), 6.8, 6.5);
                dc.DrawEllipse(skyEye, null, new Point(73, 77), 6.8, 6.5);
            }

            // Music headphones cue
            if (state == TrayVisualState.Music)
            {
                var cyan = IslandSkinEngine.Frozen(Color.FromRgb(0x83, 0xCF, 0xF3));
                var bandPen = new Pen(cyan, 6.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                bandPen.Freeze();
                dc.DrawGeometry(null, bandPen, FrozenGeo("M 22,64 A 38,38 0 0 1 98,64"));
                dc.DrawRoundedRectangle(cyan, null, new Rect(14, 58, 12, 22), 5, 5);
                dc.DrawRoundedRectangle(cyan, null, new Rect(94, 58, 12, 22), 5, 5);
            }

            dc.Pop(); // head scale

            // Sleep "Z" corner badge or Voice amber shield dot
            if (state == TrayVisualState.Sleep)
            {
                var zPen = new Pen(IslandSkinEngine.Frozen(Color.FromRgb(0x83, 0xCF, 0xF3)), 5.5)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round,
                };
                zPen.Freeze();
                dc.DrawGeometry(null, zPen, FrozenGeo("M 82,16 H 106 L 82,38 H 106"));
            }
            else if (state == TrayVisualState.Voice)
            {
                var gold = IslandSkinEngine.Frozen(Color.FromRgb(0xF2, 0xD1, 0x7D));
                var dark = new Pen(IslandSkinEngine.Frozen(Color.FromRgb(0x18, 0x14, 0x1F)), 3.0);
                dark.Freeze();
                dc.DrawEllipse(gold, dark, new Point(96, 94), 15, 15);
            }

            if (ghost)
                dc.Pop();

            dc.Pop(); // root scale
        }

        var rtb = new RenderTargetBitmap(pixelSize, pixelSize, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new System.IO.MemoryStream();
        encoder.Save(ms);
        ms.Position = 0;

        using var bmp = new System.Drawing.Bitmap(ms);
        var hIcon = bmp.GetHicon();
        try
        {
            using var temp = System.Drawing.Icon.FromHandle(hIcon);
            return (System.Drawing.Icon)temp.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }

    public static string BuildSafeTooltip(
        bool isHidden,
        bool isGhost,
        bool isGameMode,
        MediaInfo? media,
        TimerService timer,
        int openTasks)
    {
        string status;
        if (isGameMode)
            status = "HoshiBar · Игровой режим";
        else if (isHidden)
            status = "HoshiBar · Остров скрыт";
        else if (isGhost)
            status = "HoshiBar · Режим Призрак";
        else if (timer.IsActive)
            status = $"HoshiBar · Фокус {TimerService.Format(timer.Remaining)}";
        else if (media is { IsPlaying: true } && !string.IsNullOrWhiteSpace(media.Title))
            status = $"HoshiBar · ♪ {media.Title}";
        else
            status = openTasks > 0 ? $"HoshiBar · Задач: {openTasks}" : "HoshiBar · На посту";

        // NotifyIcon.Text strictly caps at 63 chars on standard WinForms APIs
        return status.Length <= 62 ? status : status[..61] + "…";
    }

    private static Geometry FrozenGeo(string path)
    {
        var g = Geometry.Parse(path);
        g.Freeze();
        return g;
    }
}
