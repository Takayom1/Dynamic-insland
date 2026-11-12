// Copyright 2026 Takayom1. SPDX-License-Identifier: MIT
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text;
using DIsland.Interop;
namespace DIsland.Services;
public enum FullscreenState {Normal,Fullscreen,OwnWindow,TransientOverlay}
public static class FullscreenService
{
    private static IntPtr lastFullscreen;
    public static System.Windows.Forms.Screen TargetScreen(string device)=>System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==device)??System.Windows.Forms.Screen.PrimaryScreen??System.Windows.Forms.Screen.AllScreens[0];
    public static FullscreenState Inspect(string device="")
    {
        var target=TargetScreen(device);
        var hwnd=NativeMethods.GetForegroundWindow();if(hwnd==IntPtr.Zero)return FullscreenState.Normal;
        NativeMethods.GetWindowThreadProcessId(hwnd,out var pid);if(pid==Environment.ProcessId)return StillFullscreen(lastFullscreen,target)?FullscreenState.OwnWindow:FullscreenState.Normal;
        var name=new StringBuilder(128);NativeMethods.GetClassName(hwnd,name,128);
        if(name.ToString() is "Progman" or "WorkerW"){lastFullscreen=IntPtr.Zero;return FullscreenState.Normal;}
        if(name.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "MultitaskingViewFrame" or "XamlExplorerHostIslandWindow" or "#32768")return StillFullscreen(lastFullscreen,target)?FullscreenState.TransientOverlay:FullscreenState.Normal;
        try{using var process=Process.GetProcessById((int)pid);if(process.ProcessName is "StartMenuExperienceHost" or "SearchHost" or "ShellExperienceHost" or "TextInputHost" or "LockApp")return StillFullscreen(lastFullscreen,target)?FullscreenState.TransientOverlay:FullscreenState.Normal;}
        catch(Exception e) when(e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception){}
        if(StillFullscreen(hwnd,target)){lastFullscreen=hwnd;return FullscreenState.Fullscreen;}lastFullscreen=IntPtr.Zero;return FullscreenState.Normal;
    }
    private static bool StillFullscreen(IntPtr hwnd,System.Windows.Forms.Screen target)
    {
        if(hwnd==IntPtr.Zero||!NativeMethods.IsWindow(hwnd)||!NativeMethods.IsWindowVisible(hwnd)||NativeMethods.IsIconic(hwnd))return false;
        if(System.Windows.Forms.Screen.FromHandle(hwnd).DeviceName!=target.DeviceName)return false;
        if(IsZoomed(hwnd)&&(GetWindowLong(hwnd,-16)&(0x00C00000|0x00040000))!=0)return false;
        if(!NativeMethods.GetWindowRect(hwnd,out var rect))return false;var screen=target.Bounds;
        return Covers(rect.Left,rect.Top,rect.Right,rect.Bottom,screen.Left,screen.Top,screen.Right,screen.Bottom);
    }
    public static bool Covers(int left,int top,int right,int bottom,int screenLeft,int screenTop,int screenRight,int screenBottom)=>Math.Abs(left-screenLeft)<=2&&Math.Abs(top-screenTop)<=2&&Math.Abs(right-screenRight)<=2&&Math.Abs(bottom-screenBottom)<=2;
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd,int index);
}
