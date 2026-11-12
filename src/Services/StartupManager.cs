// D-Island - Copyright 2026 h7 (ibuildrigs) and contributors.
// Licensed under the D-Island License 1.0; see LICENSE.txt and NOTICE.txt.

using Microsoft.Win32;

namespace DIsland.Services;

/// <summary>Per-user "Start with Windows" via the HKCU Run key.</summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HoshinoIsland";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
    public static void MigrateLegacyEntry()
    {
        using var key=Registry.CurrentUser.OpenSubKey(RunKey,writable:true);
        if(key?.GetValue("DIsland") is not string old)return;
        var path=old.Trim('"');
        try{if(!path.StartsWith(AppContext.BaseDirectory,StringComparison.OrdinalIgnoreCase)&&(!System.IO.File.Exists(path)||!System.Diagnostics.FileVersionInfo.GetVersionInfo(path).ProductName!.Contains("Hoshino",StringComparison.OrdinalIgnoreCase)))return;}
        catch{return;}
        key.SetValue(ValueName,$"\"{Environment.ProcessPath}\"");key.DeleteValue("DIsland",false);
    }
}
