// D-Island - Copyright 2026 h7 (ibuildrigs) and contributors.
// Licensed under the D-Island License 1.0; see LICENSE.txt and NOTICE.txt.

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using DIsland.Services;

namespace DIsland;

public partial class App : Application
{
    private const string MutexName = @"Local\DIsland.SingleInstance";
    private const string OpenSettingsEventName = @"Local\DIsland.OpenSettings";
    private const string InstanceAckEventName = @"Local\HoshiBar.InstanceAck";

    private Mutex? _singleInstance;
    private bool _ownsSingleInstance;
    private bool _startupComplete;
    private volatile bool _exiting;
    private EventWaitHandle? _openSettingsSignal;
    private EventWaitHandle? _instanceAckSignal;
    private RegisteredWaitHandle? _openSettingsWait;
    private EventWaitHandle? _devExitSignal;
    private RegisteredWaitHandle? _devExitWait;
    private AppSettings? _settings;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private SettingsWindow? _settingsWindow;

    /// <summary>The updater, shared by the island (alerts, installing) and Settings (status, "Check now").</summary>
    public static UpdateService? Updates { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--probe-gpu-blur", StringComparer.OrdinalIgnoreCase))
        {
            // Throwaway child process; see GpuBlurBackdrop.ProbeExitCode.
            Environment.Exit(GpuBlurBackdrop.ProbeExitCode());
            return;
        }

        // Never allow non-interactive sandbox desktops (e.g. background CLI/agent desktops) to grab
        // the session-wide SingleInstance mutex and block the user's real desktop from launching.
        if (!IsInteractiveUserDesktop())
        {
            Environment.Exit(0);
            return;
        }

        _singleInstance = new Mutex(false, MutexName);
        var acquired = TryAcquireMutex(_singleInstance, 0);
        if (!acquired)
        {
            // Ask the existing instance's UI thread to reveal the island & acknowledge within 900 ms.
            if (WakeHealthyRunningInstance())
            {
                _singleInstance.Dispose();
                _singleInstance = null;
                Environment.Exit(0);
                return;
            }

            // Previous instance is hung, stuck in exit, crashed mid-startup, or stranded on another desktop:
            // clean up its process tree automatically so launching HoshiBar always works on the first click.
            TerminateStaleInstances();
            acquired = TryAcquireMutex(_singleInstance, 3000);
            if (!acquired)
            {
                _singleInstance.Dispose();
                _singleInstance = null;
                Environment.Exit(0);
                return;
            }
        }
        _ownsSingleInstance = true;

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        base.OnStartup(e);

        _settings = AppSettings.Load();
        StartupManager.MigrateLegacyEntry();
        HoshinoPalette.Apply(_settings);
        // Checks this project only; installer launch requires a user action.
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, ".local-dev"))) Updates = new UpdateService(_settings);
        _window = new MainWindow(_settings);
        _window.SettingsRequested += () => Dispatcher.InvokeAsync(OpenSettings, DispatcherPriority.Background);
        _window.Show();
        HoshinoVoicePack.StartAutoProvisionIfPending();

        // Explicit local diagnostics: counts and access state only, never notification/calendar text.
        var reportIndex = Array.IndexOf(e.Args, "--access-report");
        if (reportIndex >= 0 && reportIndex + 1 < e.Args.Length)
        {
            var reportPath = Path.GetFullPath(e.Args[reportIndex + 1]);
            var reportTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            string? previousReport = null;
            reportTimer.Tick += (_, _) =>
            {
                try
                {
                    var report = System.Text.Json.JsonSerializer.Serialize(_window.AccessReport());
                    if (report != previousReport) { File.WriteAllText(reportPath, report); previousReport = report; }
                }
                catch (IOException) { }
            };
            reportTimer.Start();
            Exit += (_, _) => reportTimer.Stop();
        }

        // Defer until the tray menu has fully closed; opening a window mid-dismissal lets the menu's
        // focus handling push it behind other windows.
        _tray = new TrayIcon(_settings,
            () => Dispatcher.InvokeAsync(OpenSettings, DispatcherPriority.Background),
            () => _window.IslandHidden,
            hidden => _window.SetIslandHidden(hidden),
            Shutdown);

        _instanceAckSignal = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceAckEventName);
        _openSettingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, OpenSettingsEventName);
        _openSettingsWait = ThreadPool.RegisterWaitForSingleObject(_openSettingsSignal,
            (_, _) =>
            {
                if (_exiting || !_startupComplete) return;
                Dispatcher.InvokeAsync(() =>
                {
                    if (_exiting || _window is null) return;
                    try { _instanceAckSignal?.Set(); } catch (ObjectDisposedException) { }
                    _window.RevealOnUserLaunch();
                    OpenSettings();
                }, DispatcherPriority.Send);
            }, null, Timeout.Infinite, executeOnlyOnce: false);

        _devExitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\HoshiBar.DevExit");
        _devExitWait = ThreadPool.RegisterWaitForSingleObject(_devExitSignal,
            (_, _) => Dispatcher.InvokeAsync(() => { if (_window?.CanRestartForDevelopment == true) Shutdown(); }),
            null, Timeout.Infinite, false);

        _startupComplete = true;

        if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase)) OpenSettings();
#if DEBUG
        if (Environment.GetEnvironmentVariable("DISLAND_SETTINGS_SNAPSHOT") is { Length: > 0 } settingsShot)
        {
            // Debug aid: open Settings and render it to a PNG after 3 s (works without a visible screen).
            OpenSettings();
            if (Environment.GetEnvironmentVariable("DISLAND_SETTINGS_SCROLL") == "end")
                _ = Task.Delay(2000).ContinueWith(_ => (_settingsWindow!.FindName("SettingsScroller") as System.Windows.Controls.ScrollViewer)?.ScrollToEnd(),
                    TaskScheduler.FromCurrentSynchronizationContext());
            _ = Task.Delay(3000).ContinueWith(_ =>
            {
                var window = _settingsWindow!;
                var content = (System.Windows.FrameworkElement)window.Content;
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)(content.ActualWidth * dpi.DpiScaleX), (int)(content.ActualHeight * dpi.DpiScaleY),
                    dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
                var visual = new System.Windows.Media.DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(window.Background, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
                    dc.DrawRectangle(new System.Windows.Media.VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
                }
                bitmap.Render(visual);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var file = System.IO.File.Create(settingsShot);
                encoder.Save(file);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
#endif
    }

    private static bool IsInteractiveUserDesktop()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISLAND_SNAPSHOT"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISLAND_SETTINGS_SNAPSHOT")))
        {
            return true;
        }

        var hDesk = GetThreadDesktop(GetCurrentThreadId());
        if (hDesk == IntPtr.Zero) return true;
        var sb = new StringBuilder(128);
        if (!GetUserObjectInformation(hDesk, 2 /* UOI_NAME */, sb, sb.Capacity * 2, out _))
            return true;
        var name = sb.ToString();
        return string.IsNullOrEmpty(name) || string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryAcquireMutex(Mutex mutex, int timeoutMs)
    {
        try
        {
            return mutex.WaitOne(timeoutMs, false);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    private static bool WakeHealthyRunningInstance()
    {
        try
        {
            using var ack = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceAckEventName);
            ack.Reset();
            using (var wake = new EventWaitHandle(false, EventResetMode.AutoReset, OpenSettingsEventName))
                wake.Set();
            return ack.WaitOne(900);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void TerminateStaleInstances()
    {
        var selfId = Environment.ProcessId;
        foreach (var name in new[] { "HoshiBar", "HoshinoIsland", "DIsland" })
        {
            Process[] procs;
            try { procs = Process.GetProcessesByName(name); }
            catch (Exception) { continue; }

            foreach (var proc in procs)
            {
                using (proc)
                {
                    if (proc.Id == selfId) continue;
                    try
                    {
                        proc.Kill(entireProcessTree: true);
                        proc.WaitForExit(1500);
                    }
                    catch (Exception) { }
                }
            }
        }
    }

    private void ReleaseSingleInstanceMutex()
    {
        var mutex = _singleInstance;
        _singleInstance = null;
        if (mutex is null) return;
        try
        {
            if (_ownsSingleInstance)
            {
                _ownsSingleInstance = false;
                mutex.ReleaseMutex();
            }
        }
        catch (ApplicationException) { }
        finally
        {
            mutex.Dispose();
        }
    }

    private void OpenSettings()
    {
        if (_exiting) return;
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settings!);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        BringToFront(_settingsWindow);
    }

    private WhatsNewWindow? _whatsNewWindow;

    public void ShowWhatsNew(Version? updatedFrom)
    {
        if (_exiting) return;
        var releases = WhatsNew.Since(updatedFrom);
        if (releases.Count == 0) releases = WhatsNew.Since(null).Take(1).ToList();
        if (releases.Count == 0) return;
        _whatsNewWindow?.Close();
        _whatsNewWindow = new WhatsNewWindow(releases, updatedFrom);
        _whatsNewWindow.Closed += (_, _) => _whatsNewWindow = null;
        BringToFront(_whatsNewWindow);
    }

    private static void BringToFront(Window window)
    {
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Topmost = true;
        window.Topmost = false;
        window.Activate();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (_exiting)
        {
            e.Handled = true;
            return;
        }

        LogError(e.Exception);

        // Never leave a half-initialized headless background zombie if startup itself failed.
        if (!_startupComplete || _window is null)
        {
            ReleaseSingleInstanceMutex();
            Environment.Exit(1);
            return;
        }

        e.Handled = true; // keep the island alive; the failure is in the log
    }

    public static void LogError(Exception ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DIsland");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "error.log"), $"[{DateTime.Now:u}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true;
        // Release single-instance mutex immediately so quick restarts never see a locked mutex.
        ReleaseSingleInstanceMutex();

        // Watchdog guarantees no background COM/WinRT/audio thread keeps a zombie process in Task Manager.
        _ = Task.Run(async () =>
        {
            await Task.Delay(2500);
            Environment.Exit(0);
        });

        _devExitWait?.Unregister(null);
        _devExitSignal?.Dispose();
        _openSettingsWait?.Unregister(null);
        _openSettingsSignal?.Dispose();
        _instanceAckSignal?.Dispose();
        _settingsWindow?.Close();
        _whatsNewWindow?.Close();
        _tray?.Dispose();
        _window?.Close();
        base.OnExit(e);
        Environment.Exit(0);
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDesktop(uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetUserObjectInformation(IntPtr hObj, int nIndex, StringBuilder pvInfo, int nLength, out int lpnLengthNeeded);
}
