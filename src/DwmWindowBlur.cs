// Original HoshiBar code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DIsland;

/// <summary>
/// Applies fixed per-theme native DWM Gaussian blur translucency to borderless popup and settings windows
/// (Win7 Aero futuristic crystal glass, Shittim Chest matte frosted translucency, Horus Glass dark smoked glass;
/// disabled on flat opaque skins Chibi Pop &amp; Pixel Tamagotchi).
/// Uses ACCENT_ENABLE_BLURBEHIND for 144Hz zero-lag window dragging on Windows 10 and Windows 11.
/// </summary>
public static class DwmWindowBlur
{
    private enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_BLURBEHIND = 3,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public uint AccentFlags;
        public uint GradientColor; // AABBGGRR
        public uint AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    private const int WCA_ACCENT_POLICY = 19;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWCP_ROUND = 2;

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr hRgn, [MarshalAs(UnmanagedType.Bool)] bool bRedraw);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    public static void Apply(Window window, SkinPaletteSpec spec, double cornerRadiusDip = 18)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var isWin11 = Environment.OSVersion.Version.Build >= 22000;

        if (spec.DisableGlassBlur)
        {
            SetAccent(hwnd, AccentState.ACCENT_DISABLED, 0);
            if (isWin11)
            {
                var pref = DWMWCP_DONOTROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
            }
            SetWindowRgn(hwnd, IntPtr.Zero, true);
            return;
        }

        // Fixed per-theme DWM blur combined with WPF's skin body layer (crystal Aero, matte frosted Shittim, smoked Horus)
        uint abgr = spec.IsAeroGlass
            ? PackAbgr(0x30, 0x10, 0x4A, 0x82)
            : spec.IsShittimTablet
                ? PackAbgr(0x48, 0xEB, 0xF4, 0xFD)
                : PackAbgr(0x52, spec.Background.R, spec.Background.G, spec.Background.B);

        SetAccent(hwnd, AccentState.ACCENT_ENABLE_BLURBEHIND, abgr);

        if (isWin11)
        {
            SetWindowRgn(hwnd, IntPtr.Zero, true);
            var pref = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        else
        {
            var dpi = VisualTreeHelper.GetDpi(window);
            var wPx = (int)Math.Round(Math.Max(1, window.ActualWidth) * dpi.DpiScaleX);
            var hPx = (int)Math.Round(Math.Max(1, window.ActualHeight) * dpi.DpiScaleY);
            var dPx = (int)Math.Round(cornerRadiusDip * 2 * dpi.DpiScaleX);
            var rgn = CreateRoundRectRgn(0, 0, wPx + 1, hPx + 1, dPx, dPx);
            if (rgn != IntPtr.Zero && SetWindowRgn(hwnd, rgn, true) == 0)
                DeleteObject(rgn);
        }
    }

    private static uint PackAbgr(byte a, byte r, byte g, byte b) =>
        ((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | r;

    private static void SetAccent(IntPtr hwnd, AccentState state, uint abgr)
    {
        var policy = new AccentPolicy
        {
            AccentState = state,
            AccentFlags = state == AccentState.ACCENT_DISABLED ? 0u : 2u,
            GradientColor = abgr,
        };
        var size = Marshal.SizeOf<AccentPolicy>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, ptr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WCA_ACCENT_POLICY,
                SizeOfData = size,
                Data = ptr,
            };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        catch (Exception)
        {
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
