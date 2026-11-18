// D-Island - Copyright 2026 h7 (ibuildrigs) and contributors.
// Licensed under the D-Island License 1.0; see LICENSE.txt and NOTICE.txt.

using System.Drawing;
using System.Windows.Forms;
using System.Windows.Threading;
using DIsland.Services;

namespace DIsland;

public sealed class TrayIcon : IDisposable
{
    private readonly AppSettings _settings;
    private readonly Action _onOpenSettings;
    private readonly Func<bool> _isHidden;
    private readonly Action<bool> _setHidden;
    private readonly Action _onExit;
    private readonly NotifyIcon _notifyIcon;
    private readonly DispatcherTimer _ringPollTimer = new() { Interval = TimeSpan.FromSeconds(10) };

    private Icon? _currentIcon;
    private TrayFlyoutWindow? _flyout;
    private string _lastIconKey = "";
    private bool _disposed;

    public TrayIcon(AppSettings settings, Action onOpenSettings, Func<bool> isHidden, Action<bool> setHidden, Action onExit)
    {
        _settings = settings;
        _onOpenSettings = onOpenSettings;
        _isHidden = isHidden;
        _setHidden = setHidden;
        _onExit = onExit;

        var initialSpec = IslandSkinPresets.Resolve(settings);
        _currentIcon = TrayHoshinoIconRenderer.RenderIcon(initialSpec, TrayVisualState.Idle, 0, 32);

        _notifyIcon = new NotifyIcon
        {
            Icon = _currentIcon,
            Text = "HoshiBar · На посту",
            Visible = true,
        };

        _notifyIcon.MouseUp += OnTrayMouseUp;
        _notifyIcon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _flyout?.Hide();
                _onOpenSettings();
            }
        };

        _settings.Changed += RefreshVisuals;
        _ringPollTimer.Tick += (_, _) => RefreshVisuals();

        if (GetMainWindow() is { } mw)
            AttachMainWindow(mw);

        RefreshVisuals();
    }

    private static MainWindow? GetMainWindow() =>
        System.Windows.Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();

    private void AttachMainWindow(MainWindow mw)
    {
        mw.TrayStateChanged += RefreshVisuals;
        mw.MediaSource.Changed += RefreshVisuals;
        mw.TimerSource.StateChanged += RefreshVisuals;
    }

    private void OnTrayMouseUp(object? sender, MouseEventArgs e)
    {
        var mw = GetMainWindow();

        if (e.Button == MouseButtons.Middle)
        {
            if (_settings.TrayMiddleClickAction == "GhostMode")
                mw?.SetGhostMode(!(mw?.IsGhostMode == true));
            else if (_settings.TrayMiddleClickAction == "ToggleIsland")
                _setHidden(!_isHidden());
            else
                mw?.MediaSource.TogglePlayPause();

            RefreshVisuals();
            return;
        }

        if (e.Button == MouseButtons.Right)
        {
            OpenOrToggleFlyout();
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            switch (_settings.TrayLeftClickAction)
            {
                case "ToggleIsland":
                    _setHidden(!_isHidden());
                    break;
                case "AiChat":
                    if (mw is not null)
                    {
                        if (mw.IsGhostMode) mw.SetGhostMode(false);
                        if (mw.IslandHidden) mw.SetIslandHidden(false);
                        mw.OpenAiChat();
                    }
                    break;
                case "Settings":
                    _onOpenSettings();
                    break;
                default:
                    OpenOrToggleFlyout();
                    break;
            }
            RefreshVisuals();
        }
    }

    private void OpenOrToggleFlyout()
    {
        _flyout ??= new TrayFlyoutWindow(_settings, GetMainWindow, _onOpenSettings, _onExit);
        _flyout.ToggleNearTray();
    }

    public void RefreshVisuals()
    {
        if (_disposed) return;

        var mw = GetMainWindow();
        var spec = IslandSkinPresets.Resolve(_settings);
        var hidden = _isHidden();
        var ghost = mw?.IsGhostMode == true;
        var game = mw?.IsGameSuppressed == true;
        var timer = mw?.TimerSource;
        var media = mw?.MediaSource.Current;
        var voice = mw?.IsVoiceOrAssistantActive == true;

        TrayVisualState state;
        double ratio = 0;

        if (hidden || ghost || game)
        {
            state = TrayVisualState.GhostOrHidden;
        }
        else if (voice)
        {
            state = TrayVisualState.Voice;
        }
        else if (timer is { IsActive: true })
        {
            state = TrayVisualState.FocusRing;
            ratio = Math.Round(timer.RemainingRatio * 20.0) / 20.0; // quantized to 5% steps -> near-zero redraws
        }
        else if (media is { IsPlaying: true })
        {
            state = TrayVisualState.Music;
        }
        else if (HoshinoAtmosphere.IsNightWindow(DateTime.Now))
        {
            state = TrayVisualState.Sleep;
        }
        else
        {
            state = TrayVisualState.Idle;
        }

        // Only run the 10-second ring poll while an active focus timer needs arc progression (and not in GameMode/ReduceMotion)
        var needPoll = state == TrayVisualState.FocusRing && !game && !_settings.ReduceMotion;
        if (needPoll && !_ringPollTimer.IsEnabled) _ringPollTimer.Start();
        else if (!needPoll && _ringPollTimer.IsEnabled) _ringPollTimer.Stop();

        var key = $"{spec.Skin}|{spec.PresetId}|{state}|{ratio:0.00}";
        if (key != _lastIconKey)
        {
            _lastIconKey = key;
            var nextIcon = TrayHoshinoIconRenderer.RenderIcon(spec, state, ratio, 32);
            var oldIcon = _currentIcon;
            _currentIcon = nextIcon;
            _notifyIcon.Icon = nextIcon;
            oldIcon?.Dispose();
        }

        if (timer is not null)
            _notifyIcon.Text = TrayHoshinoIconRenderer.BuildSafeTooltip(hidden, ghost, game, media, timer, mw?.OpenTasksCount ?? 0);

        if (_flyout is { IsVisible: true })
            _flyout.RefreshState();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ringPollTimer.Stop();
        _settings.Changed -= RefreshVisuals;
        if (GetMainWindow() is { } mw)
        {
            mw.TrayStateChanged -= RefreshVisuals;
            mw.MediaSource.Changed -= RefreshVisuals;
            mw.TimerSource.StateChanged -= RefreshVisuals;
        }
        _flyout?.Close();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _currentIcon?.Dispose();
    }
}
