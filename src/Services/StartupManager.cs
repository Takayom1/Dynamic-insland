// D-Island - Copyright 2026 h7 (ibuildrigs) and contributors.
// Licensed under the D-Island License 1.0; see LICENSE.txt and NOTICE.txt.

using Microsoft.Win32;

namespace DIsland.Services;

/// <summary>Per-user "Start with Windows" via the HKCU Run key.</summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HoshiBar";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string;
            }
            catch (Exception)
            {
                return false;
            }
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
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null) return;
            foreach (var legacyName in new[] { "HoshinoIsland", "DIsland" })
            {
                if (key.GetValue(legacyName) is not string old) continue;
                var path = old.Trim('"');
                try
                {
                    var sameDir = path.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase);
                    var isOurs = legacyName == "HoshinoIsland"
                                 || sameDir
                                 || (System.IO.File.Exists(path) && (System.Diagnostics.FileVersionInfo.GetVersionInfo(path).ProductName?.Contains("Hoshi", StringComparison.OrdinalIgnoreCase) == true));
                    if (!isOurs) continue;
                }
                catch { continue; }
                key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
                key.DeleteValue(legacyName, false);
            }
        }
        catch (Exception) { }
    }
}
