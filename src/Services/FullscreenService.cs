// Copyright 2026 Takayom1. SPDX-License-Identifier: MIT
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text;
using DIsland.Interop;
namespace DIsland.Services;
public enum FullscreenState {Normal,Fullscreen,OwnWindow,TransientOverlay}
public static class FullscreenService
{
    public static FullscreenState Inspect()
    {
        var hwnd=NativeMethods.GetForegroundWindow();if(hwnd==IntPtr.Zero)return FullscreenState.Normal;
        NativeMethods.GetWindowThreadProcessId(hwnd,out var pid);if(pid==Environment.ProcessId)return FullscreenState.OwnWindow;
        var name=new StringBuilder(128);NativeMethods.GetClassName(hwnd,name,128);
        if(name.ToString() is "Progman" or "WorkerW")return FullscreenState.Normal;
        if(name.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "MultitaskingViewFrame" or "XamlExplorerHostIslandWindow" or "#32768")return FullscreenState.TransientOverlay;
        try{using var process=Process.GetProcessById((int)pid);if(process.ProcessName is "StartMenuExperienceHost" or "SearchHost" or "ShellExperienceHost" or "TextInputHost" or "LockApp")return FullscreenState.TransientOverlay;}
        catch(Exception e) when(e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception){}
        if(IsZoomed(hwnd)&&(GetWindowLong(hwnd,-16)&0x00C00000)!=0)return FullscreenState.Normal;
        if(!NativeMethods.GetWindowRect(hwnd,out var rect))return FullscreenState.Normal;
        var screen=System.Windows.Forms.Screen.FromHandle(hwnd).Bounds;
        return Covers(rect.Left,rect.Top,rect.Right,rect.Bottom,screen.Left,screen.Top,screen.Right,screen.Bottom)?FullscreenState.Fullscreen:FullscreenState.Normal;
    }
    public static bool Covers(int left,int top,int right,int bottom,int screenLeft,int screenTop,int screenRight,int screenBottom)=>left<=screenLeft+2&&top<=screenTop+2&&right>=screenRight-2&&bottom>=screenBottom-2;
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd,int index);
}
