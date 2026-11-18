// D-Island - Copyright 2026 h7 (ibuildrigs) and contributors.
// Licensed under the D-Island License 1.0; see LICENSE.txt and NOTICE.txt.

using System.Diagnostics;
using System.Globalization;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DIsland.Interop;
using DIsland.Services;
using Microsoft.Win32;

namespace DIsland;

public partial class MainWindow : Window
{
    // Design-space width of the root; the window is this times the Size setting. The root's height
    // starts at 300 (XAML) and grows if the expanded content needs more (UpdateWindowHeight).
    private const double BaseWidth = 760;

    private const double IslandTopMargin = 8;
    private double ExpandedWidth => 620;
    private const double IslandExpandedRadius = 32;
    private const double NotchCompactRadius = 14;
    private const double NotchExpandedRadius = 26;
    private const double NotchShoulder = 8;
    private const double BaseStiffness = 420;
    private const int LeavePollMs = 50;

    private static readonly Color DefaultMediaAccent = Color.FromRgb(0x30, 0xD1, 0x58);

    private readonly AppSettings _settings;
    private readonly Func<FullscreenState> _fullscreenDetector;
    private readonly StateController _state = new();
    private readonly TimerService _timer = new();
    private readonly MediaService _media = new();
    private readonly VolumeService _volume = new();
    private readonly BrightnessService _brightness = new();
    private readonly VolumeKeyHook _volumeKeys = new();
    private readonly BatteryService _battery = new();
    private readonly DeviceService _devices = new();
    private readonly KeyboardWatcher _keyboard = new();
    private readonly FocusService _focus = new();
    private readonly ClipboardWatcher _clipboard = new();
    private readonly PrivacyService _privacy = new();
    private readonly StopwatchService _stopwatch = new();
    private readonly AlarmService _alarms;
    private readonly StatusService _status = new();
    private System.Media.SoundPlayer? _alarmSound;
    private readonly ShelfService _shelf = new();
    private readonly NotesService _notes = new();
    private readonly NotificationService _notifications = new();
    private readonly CalendarService _calendar;
    private readonly WeatherService _weather;
    private string? _weatherKey;
    private readonly Hotkey _toggleHotkey = new(1);
    private string? _toggleHotkeyText;
    private readonly Hotkey _ghostHotkey = new(2);
    private string? _ghostHotkeyText;
    private readonly DispatcherTimer _ghostTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private ClipboardEvent? _lastCopy;
    private readonly WindowEventWatcher _windows;
    private readonly BackgroundDetector _background;

    // Liquid Glass: the island excludes itself from capture, then blurs a live copy of the screen behind it.
    private const double MaxGlassBlur = 40;
    private readonly ScreenCapture _capture = new();
    private readonly DispatcherTimer _glassTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private WriteableBitmap? _glassBitmap;
    private bool _excludedFromCapture;


    private readonly Spring _width = new(168);
    private readonly Spring _height = new(44);
    private readonly Spring _radius = new(22);
    // 0 = Dynamic Island, 1 = notch: switching shape morphs between the two instead of jumping.
    private readonly Spring _notch = new(0);
    private readonly Stopwatch _frameClock = Stopwatch.StartNew();
    private double _lastFrameTime;
    private double _lastAppliedFrame;
    private bool _animating;

    private readonly FrameworkElement[] _views;
    private FrameworkElement? _currentView;

    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _hoverTimer = new();
    private readonly DispatcherTimer _leavePoll = new() { Interval = TimeSpan.FromMilliseconds(LeavePollMs) };
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private int _msOutside;
    private bool _hoverSuppressed;
    private bool _gameFullscreen,_gameSuppressed,_voiceChatOpen;
    private readonly DispatcherTimer _gameWatch=new(DispatcherPriority.Background){Interval=TimeSpan.FromSeconds(1)};
    private bool _equalizerRunning;
    private bool _osdDimmed;
    private Artwork? _shownArt;

    private readonly SolidColorBrush _pillBrush = new(Colors.Black);
    private readonly DropShadowEffect _shadow = new() { BlurRadius = 22, ShadowDepth = 3, Direction = 270, Color = Colors.Black };
    private Color? _accent;

    public MainWindow(AppSettings settings,Func<FullscreenState>? fullscreenDetector=null)
    {
        StartupMark("window constructor");
        _settings = settings;
        _fullscreenDetector=fullscreenDetector??(()=>FullscreenService.Inspect(_settings.MonitorDeviceName));
        _notch.Reset(IsNotch ? 1 : 0);
        _weather = new WeatherService(settings);
        _calendar = new CalendarService(settings);
        _alarms = new AlarmService(settings);
        InitializeComponent();
        _compactHoshino = new HoshinoCompanion(settings) { IsCompactPill = true, SuppressFloatingHalo = true, Playing = () => _media.Current?.IsPlaying == true, AudioLevel = () => _audioPeak, Listening = () => _aiPanel?.IsListening == true, Working = () => AssistantRuntime.Busy, Focusing = () => _timer.IsActive };
        _homeHoshino = new HoshinoCompanion(settings) { SuppressFloatingHalo = true, Playing = () => _media.Current?.IsPlaying == true, AudioLevel = () => _audioPeak, Listening = () => _aiPanel?.IsListening == true, Working = () => AssistantRuntime.Busy, Focusing = () => _timer.IsActive, Weather = () => _weather.Current, EnableHeadpat = true };
        _lyricsHoshino = new HoshinoCompanion(settings) { IsCompactPill = true, SuppressFloatingHalo = true, Playing = () => _media.Current?.IsPlaying == true, AudioLevel = () => _audioPeak, Listening = () => _aiPanel?.IsListening == true, Working = () => AssistantRuntime.Busy, Focusing = () => _timer.IsActive };
        _homeHoshino.Headpatted += SetCompanionPhraseText;
        CompactHoshinoHost.Content = _compactHoshino;
        HomeHoshinoHost.Content = _homeHoshino;
        LyricsHoshinoHost.Content = _lyricsHoshino;
        _mediaArtDecorator = new IslandMediaArtDecorator(settings);
        MediaArtLargeBackHost.Content = _mediaArtDecorator.LargeBackLayer;
        MediaArtLargeFrontHost.Content = _mediaArtDecorator.LargeFrontLayer;
        MediaArtSmallBackHost.Content = _mediaArtDecorator.SmallBackLayer;
        MediaArtSmallFrontHost.Content = _mediaArtDecorator.SmallFrontLayer;
        _weatherRim = new IslandWeatherRim(settings);
        WeatherRimHost.Content = _weatherRim;
        var (voiceWavePanel, voiceWaveBars) = IslandChromeRenderer.CreateVoiceWaveform();
        _voiceWaveBars = voiceWaveBars;
        VoiceWaveformHost.Content = voiceWavePanel;
        _aquarium = new AbydosAquariumView(settings, () => _media.Current?.IsPlaying == true, () => _aiPanel?.IsListening == true, () => AssistantRuntime.Busy, () => _timer.IsActive, () => _weather.Current);
        AquariumView.Children.Add(_aquarium);
        _aiPanel = new AiChatPanel(settings);
        AiPage.Content = _aiPanel;
        NextPhrase();
        _lyricsPanel = new LyricsPanel(settings);
        LyricsHost.Content = _lyricsPanel;
        _lyricsPanel.LayoutChanged += () => { if(IsLoaded && _state.IsExpanded && _page==ExpandedPage.Home) Render(); };
        _coworking = new CoworkingBar(settings, _notes, _timer);
        CoworkingHost.Content = _coworking;
        _coworking.LayoutChanged += () => { if (IsLoaded && _state.IsExpanded && _page == ExpandedPage.Home) Render(); };
        _homeTasks = new HomeTasksPanel(_notes, BeginTyping, () => _settings.FocusTaskId, id => _coworking.BindTask(id));
        _homeTasks.LayoutChanged += () => { if (IsLoaded && _state.IsExpanded && _page == ExpandedPage.Home) Render(); };
        _coworking.FocusTaskChanged += () => _homeTasks.RefreshFocusState();
        HomeTasksHost.Content = _homeTasks;
        _notes.TaskCompleted += (_, allDone) => { if (allDone) { _homeHoshino.TriggerHappyTaskReaction(); _compactHoshino.TriggerHappyTaskReaction(); } };
        _qrShare = new LocalQrShareService(_shelf);
        _qrPanel = new ShelfQrPanel(_qrShare);
        ShelfQrHost.Content = _qrPanel;
        _qrPanel.LayoutChanged += () => { if (IsLoaded && _state.IsExpanded && _page == ExpandedPage.Shelf) Render(); };
        _obsidian=new ObsidianSync(_notes);
        _journal=new JournalPanel(BeginTyping);JournalHost.Content=_journal;
        _journal.LayoutChanged+=()=>{if(IsLoaded&&_state.IsExpanded&&_page==ExpandedPage.Calendar)Render();};
        _notes.Changed += () => { if(IsLoaded && _state.IsExpanded && _page==ExpandedPage.Home) Render(); };
        _compactLyricsTimer.Tick += (_, _) => { _lyricsPanel.Update(_media.Current); UpdateCompactArtwork(); };
        _lyricsPanel.CompactModeChanged += () =>
        {
            if (_lyricsPanel.CompactEnabled) { _lyricsPanel.Update(_media.Current); _compactLyricsTimer.Start(); }
            else _compactLyricsTimer.Stop();
            StyleLrcToggleButton();
            UpdateSecondActivity();
            Render();
        };
        _lyricsPanel.CompactChanged += UpdateCompactLyric;
        _aiPanel.TypingRequested += BeginTyping;
        _voiceHoshino = new HoshinoCompanion(settings){SuppressFloatingHalo=true,AudioLevel=()=>_audioPeak,Listening=()=>_aiPanel.IsListening,Working=()=>AssistantRuntime.Busy||_aiPanel.PreparingVoice||_aiPanel.IsTranscribing,Playing=()=>_aiPanel.IsSpeaking};
        VoiceFocusHoshino.Content = _voiceHoshino;
        _aiPanel.VoiceActivated+=BeginVoiceFocus;
        _aiPanel.ActivityChanged+=text=>{VoiceActivityText.Text=text;VoiceActivityText.ToolTip=text;};
        _aiPanel.VoiceStateChanged+=()=>{if(!IsLoaded)return;if(_voiceFocus&&!_aiPanel.VoiceInteraction)EndVoiceFocus();UpdatePrivacyIndicators();UpdateVoiceWakeChip();UpdateEqualizer();};
        PrivacyChip.ToolTip="Нажмите, чтобы выключить голосовую активацию";
        PrivacyChip.MouseLeftButtonUp+=(_,e)=>{if(_aiPanel.MicrophoneActive)_aiPanel.DisableWake();UpdateVoiceWakeChip();e.Handled=true;};

        _windows = new WindowEventWatcher(TimeSpan.FromMilliseconds(30));
        _background = new BackgroundDetector(GetIslandScreenRect, _windows);
        _windows.Changed += () =>
        {
            UpdateGameMode();if(_gameSuppressed)return;
            KeepOnTop(); // cheap check; only acts if something now covers the island
            UpdateWinUiBehind();
            if (_gpuActive) _gpuBlur?.SyncWindows();
        };

        _views = new FrameworkElement[]
        {
            VoiceFocusView, AquariumView, ClockView, MediaView, TimerView, StopwatchView, OsdView, BatteryView, TimerDoneView, InfoView, NotificationView,
            ExpandedView, LyricsView,
        };
        foreach (var view in _views)
        {
            view.Opacity = 0;
            view.IsHitTestVisible = false;
            view.RenderTransform = new ScaleTransform(1, 1);
            view.RenderTransformOrigin = view == ExpandedView ? new Point(0.5, 0) : new Point(0.5, 0.5);
        }
        PillShape.Fill = _pillBrush;

        _state.Changed += Render;
        _settings.Changed += ApplySettings;

        _timer.Tick += OnTimerTick;
        _timer.StateChanged += OnTimerStateChanged;
        _timer.Finished += OnTimerFinished;
        _media.Changed += OnMediaChanged;
        ProgressHit.SizeChanged += (_, _) => { if (_state.IsExpanded) UpdateProgress(); };
        ProgressTrackBorder.SizeChanged += (_, _) => { if (_state.IsExpanded) UpdateProgress(); };
        OsdTrackBorder.SizeChanged += (_, _) => UpdateOsdBarWidth();
        _media.TimelineChanged += () =>
        {
            if (!_state.IsExpanded) return;
            var showProgress = _media.Current is { Duration.Ticks: > 0 };
            if (showProgress != (ProgressRow.Visibility == Visibility.Visible)) Render(); // row appears/disappears
            else UpdateProgress();
        };
        _volume.Changed += ShowVolume;
        _brightness.Changed += ShowBrightness;
        _volumeKeys.VolumeKey += OnVolumeKey;
        _battery.Alert += OnBatteryAlert;
        _devices.Changed += OnDeviceChanged;
        _keyboard.LockToggled += OnLockToggled;
        _keyboard.PasteShortcut += OnPasteShortcut;
        _focus.Changed += OnFocusChanged;
        _clipboard.Copied += OnCopied;
        _privacy.Changed += OnPrivacyChanged;
        _toggleHotkey.Pressed += () => SetIslandHidden(!IslandHidden);
        _keyboard.ShortcutPressed += () => SetIslandHidden(!IslandHidden); // same shortcut, when another app owns it
        _ghostHotkey.Pressed += () => SetGhostMode(!IsGhostMode);
        _ghostTimer.Tick += OnGhostTimerTick;
        _weather.Changed += () =>
        {
            UpdateWeather();
            UpdateClock(); // the idle pill may show the temperature
        };
        _background.Changed += ApplyTheme;

        _clockTimer.Tick += (_, _) =>
        {
            UpdateClock();
            // Relative times ("in 5 min", "3m") on an open page.
            if (!_state.IsExpanded || _animating) return;
            if (_page == ExpandedPage.Calendar) BuildCalendar();
            else if (_page == ExpandedPage.Notifications) BuildNotificationList();
        };
        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop();
            SetExpanded(true);
        };
        _leavePoll.Tick += OnLeavePoll;
        _progressTimer.Tick += (_, _) => UpdateProgress();

        Root.MouseEnter += OnRootMouseEnter;
        Root.MouseLeave += OnRootMouseLeave;
        Root.MouseLeftButtonUp += OnRootMouseLeftButtonUp;
        Root.PreviewMouseWheel += OnRootPreviewMouseWheel;
        WheelScrolling.Attach(AppsPage, pixelsPerNotch: 36);
        WheelScrolling.Attach(ShelfScroller, pixelsPerNotch: 36);
        WheelScrolling.Attach(NotificationsScroller, pixelsPerNotch: 36);
        WheelScrolling.Attach(CalendarScroller, pixelsPerNotch: 36);
        InitShelf();
        InitNotes();
        InitEnergySaver();
        _stopwatch.Tick += OnStopwatchTick;
        _stopwatch.StateChanged += OnStopwatchStateChanged;
        _alarms.Ring += OnAlarmRing;
        _status.Changed += OnStatusChanged;
        _state.Changed += UpdateSecondActivity;
        _state.Changed += () =>
        {
            // The alarm rings while its alert is up: clicked away or timed out stops the sound.
            if (_alarmSound is not null && _state.Transient != IslandView.TimerDone) StopAlarmSound();
        };
        _notifications.Arrived += OnNotificationArrived;
        _notifications.Changed += BuildNotificationList;
        _calendar.Changed += BuildCalendar;
        _calendar.Reminder += OnCalendarReminder;
        Root.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            OpenSettings();
        };

        _glassTimer.Tick += (_, _) => CaptureGlass();
        InitMotionArtwork();
        LocationChanged += (_, _) => SyncGpuBounds();
        SizeChanged += (_, _) => SyncGpuBounds();
        DpiChanged += (_, _) => {SyncGpuBounds();Dispatcher.BeginInvoke(Reposition);};

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += OnClosed;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        ApplySettings();
    }

    /// <summary>The gear button or a right-click on the island asked for the settings window.</summary>
    public event Action? SettingsRequested;
    public event Action? TrayStateChanged;

    internal MediaService MediaSource => _media;
    internal TimerService TimerSource => _timer;
    internal WeatherReport? CurrentWeather => _weather.Current;
    internal LyricsPanel LyricsSource => _lyricsPanel;
    internal int OpenTasksCount => _notes.Todos.Count(t => !t.Done);
    internal bool IsVoiceOrAssistantActive => AssistantRuntime.Busy || (_aiPanel?.IsListening ?? false) || (_aiPanel?.IsTranscribing ?? false);
    internal bool IsGameSuppressed => _gameSuppressed;

    public bool IsGhostMode { get; private set; }
    public DateTime? GhostModeUntil { get; private set; }

    public void SetGhostMode(bool enabled, TimeSpan? duration = null)
    {
        if (enabled && IslandHidden) SetIslandHidden(false);
        IsGhostMode = enabled;
        GhostModeUntil = enabled && duration is { } d ? DateTime.Now.Add(d) : null;
        if (GhostModeUntil is not null) _ghostTimer.Start();
        else _ghostTimer.Stop();

        if (enabled)
        {
            _hoverTimer.Stop();
            EndTyping(restoreFocus: true);
            SetExpanded(false);
        }

        ApplyGhostWindowStyle();
        _themeKey = null;
        ApplyTheme();
        ApplyGeometry();
        TrayStateChanged?.Invoke();
    }

    private void OnGhostTimerTick(object? sender, EventArgs e)
    {
        if (!IsGhostMode || GhostModeUntil is not { } until)
        {
            _ghostTimer.Stop();
            return;
        }
        if (DateTime.Now >= until)
            SetGhostMode(false);
        else
            TrayStateChanged?.Invoke();
    }

    private void ApplyGhostWindowStyle()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var style = NativeMethods.GetExStyle(hwnd);
        if (IsGhostMode)
            style |= NativeMethods.WS_EX_TRANSPARENT;
        else
            style &= ~NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetExStyle(hwnd, style);
        Root.IsHitTestVisible = !IslandHidden && !IsGhostMode;
        Root.Opacity = IsGhostMode ? Math.Clamp(_settings.GhostModeOpacity, 0.25, 0.85) : 1.0;
    }

    public void TriggerVoiceWakeFromTray()
    {
        if (IsGhostMode) SetGhostMode(false);
        if (IslandHidden) SetIslandHidden(false);
        OpenAiChat();
        _aiPanel.StartDictation();
        TrayStateChanged?.Invoke();
    }

    public void CycleMonitor()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length <= 1) return;
        var current = FullscreenService.TargetScreen(_settings.MonitorDeviceName);
        var idx = Array.FindIndex(screens, s => string.Equals(s.DeviceName, current.DeviceName, StringComparison.OrdinalIgnoreCase));
        var next = screens[(idx + 1 + screens.Length) % screens.Length];
        _settings.MonitorDeviceName = next.Primary ? "" : next.DeviceName;
        _settings.NotifyChanged();
        _settings.Save();
        TrayStateChanged?.Invoke();
    }

    public void ResetPosition()
    {
        _settings.TopOffset = 0;
        _settings.MonitorDeviceName = "";
        if (IsGhostMode) SetGhostMode(false);
        if (IslandHidden) SetIslandHidden(false);
        _settings.NotifyChanged();
        _settings.Save();
        Reposition();
        TrayStateChanged?.Invoke();
    }

    public void RevealOnUserLaunch()
    {
        if (_gameSuppressed) ApplyGameMode(false);
        if (IsGhostMode) SetGhostMode(false);
        if (IslandHidden) SetIslandHidden(false);
        if (!IsVisible) Show();
        Root.BeginAnimation(OpacityProperty, null);
        Root.Opacity = 1;
        Reposition();
        Render();
        KeepOnTop(force: true);
        TrayStateChanged?.Invoke();
    }

    public void DropFilesFromTray(string[] paths)
    {
        if (paths.Length == 0) return;
        _shelf.Add(paths);
        if (!_settings.ShelfEnabled)
        {
            _settings.ShelfEnabled = true;
            _settings.NotifyChanged();
            _settings.Save();
        }
        if (IsGhostMode) SetGhostMode(false);
        if (IslandHidden) SetIslandHidden(false);
        SetExpanded(true);
        ShowPage(ExpandedPage.Shelf, animate: false);
        _qrPanel.Open(paths, singleItem: paths.Length == 1);
        Render();
    }

    public void OpenShelfQrFromTray()
    {
        if (!_settings.ShelfEnabled)
        {
            _settings.ShelfEnabled = true;
            _settings.NotifyChanged();
            _settings.Save();
        }
        SetExpanded(true);
        ShowPage(ExpandedPage.Shelf, animate: false);
        _qrPanel.Open(_shelf.Items.ToArray(), singleItem: false);
        Render();
    }
    internal bool CanRestartForDevelopment=>!_typing&&!AssistantRuntime.Busy&&!_timer.IsActive&&!_stopwatch.IsActive&&!_qrShare.IsRunning&&!_aiPanel.IsListening&&!_aiPanel.IsTranscribing&&!_aiPanel.IsCalibrating&&!_aiPanel.IsSpeaking&&!_aiPanel.PreparingVoice&&string.IsNullOrWhiteSpace(_aiPanel.Input.Text);

    /// <summary>Hidden with the shortcut or the tray menu: nothing shows, alerts included.</summary>
    public bool IslandHidden { get; private set; }

    // Hiding: the island shrinks into the top edge and fades; the window hides once the springs settle
    // (OnRendering). Showing again mid-way just grows it back.
    private bool _hideAnimating;

    public void SetIslandHidden(bool hidden)
    {
        if(_gameSuppressed&&!hidden){IslandHidden=false;return;}
        if (hidden == IslandHidden) return;
        if (hidden)
        {
            _hoverTimer.Stop();
            EndTyping(restoreFocus: true);
            SetExpanded(false);
            _state.DismissTransient();
            IslandHidden = true; // Render() is skipped from here on
            _aquarium.SetActive(false);
            Root.IsHitTestVisible = false;
            UpdateEqualizer(); // stops the bars' meter loop while nothing can be seen
            if (!IsVisible)
            {
                FinishHide();
                return;
            }
            _hideAnimating = true;
            FadeRoot(0, 160);
            AnimateTo(0, 0, 0);
        }
        else
        {
            IslandHidden = false;
            if (_hideAnimating)
            {
                _hideAnimating = false; // still on screen: grow back from wherever it is
            }
            else
            {
                // Grow out of the top edge, like it went.
                _width.Reset(0);
                _height.Reset(0);
                _radius.Reset(0);
                ApplyGeometry();
                Show();
                _gpuActive = false; // make UpdateGlass show the blur window again
                UpdateGlass();
                KeepOnTop(force: true);
            }
            Root.IsHitTestVisible = !IsGhostMode;
            ApplyGhostWindowStyle();
            FadeRoot(IsGhostMode ? Math.Clamp(_settings.GhostModeOpacity, 0.25, 0.85) : 1, 200);
            Render(); // whatever changed while hidden
            UpdateEqualizer();
        }
        TrayStateChanged?.Invoke();
    }

    private void FinishHide()
    {
        _hideAnimating = false;
        _gpuBlur?.SetVisible(false);
        Hide();
    }

    private void FadeRoot(double opacity, double ms)
    {
        var fade = new DoubleAnimation(opacity, TimeSpan.FromMilliseconds(ms / SpeedFactor))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Timeline.SetDesiredFrameRate(fade, FrameRateCap);
        Root.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>(Re)registers the hide/show shortcut when its setting changes.</summary>
    private void UpdateToggleHotkey()
    {
        if (new WindowInteropHelper(this).Handle == IntPtr.Zero) return; // registered once the window exists
        if (_settings.ToggleHotkey == _toggleHotkeyText) return;
        _toggleHotkeyText = _settings.ToggleHotkey;
        var text = _settings.ToggleHotkey;
        var problem = _toggleHotkey.Set(text);

        // Another app registered the same combination first (RegisterHotKey is first come, first
        // served; graphics, keyboard and overlay utilities often take Ctrl+Alt shortcuts). Recognise it
        // from raw keyboard input instead, so it still works on those PCs.
        _keyboard.Shortcut = _toggleHotkey.TakenByAnotherApp && Hotkey.TryParse(text, out var modifiers, out var key)
            ? (modifiers, KeyInterop.VirtualKeyFromKey(key))
            : null;
        _keyboard.Update();
        _settings.HotkeyProblem = _keyboard.Shortcut is not null
            ? $"Сочетание {text} уже занято другой программой. HoshiBar всё равно перехватывает его, но лучше выбрать свободную комбинацию."
            : problem;

        if (_settings.GhostModeHotkey != _ghostHotkeyText)
        {
            _ghostHotkeyText = _settings.GhostModeHotkey;
            _settings.GhostHotkeyProblem = _ghostHotkey.Set(_settings.GhostModeHotkey);
        }
        TrayStateChanged?.Invoke();
    }

    private bool IsNotch => _settings.Mode == IslandMode.Notch;
    // Fixed, non-editable per-theme blur physics: optical crystal blur on Win7 Aero, heavy matte frosted blur on Shittim Chest,
    // dark smoked blur on Horus Glass, and 0 blur on flat Chibi Pop & Pixel Tamagotchi (disabled in Ghost Mode for 100% click-through).
    private bool IsGlass => !IslandSkinEngine.Current.DisableGlassBlur && !_energySaver && !IsGhostMode && !IslandHidden;
    private double EffectiveGlassBlur => IsGlass ? IslandSkinEngine.Current.FixedBlurRatio : 0;
    /// <summary>How far the shape is between island (0) and notch (1), mid-morph after a shape change.</summary>
    private double NotchAmount => Math.Clamp(_notch.Value, 0, 1);
    private double PillTop => IslandTopMargin * MorphLower(NotchAmount);

    // The island → notch morph, in three overlapping-free phases (reversed for notch → island):
    // slide up to the screen edge, then square off the top corners, then flare the shoulders. The GPU
    // blur's clip is a single rounded rectangle; squaring the corners only once the shape touches the
    // edge lets that clip follow it exactly (its square top hangs above the window, out of sight).
    private static double MorphLower(double t) => Math.Max(0, 1 - t / 0.4);
    private static double MorphSquare(double t) => Math.Clamp((t - 0.4) / 0.3, 0, 1);
    private static double MorphFlare(double t) => Math.Clamp((t - 0.7) / 0.3, 0, 1);
    private double SpeedFactor => Math.Clamp(_settings.AnimationSpeed, 0.25, 4);
    private double Scale => 1.0;
    private double Roundness => 1.0;

    // ---------------------------------------------------------------- settings

    private void ApplySettings()
    {
        // Scaling stiffness by speed² and damping by speed keeps the same curve shape, just faster.
        var stiffness = BaseStiffness * SpeedFactor * SpeedFactor;
        var dampingRatio = 1.0 - 0.55 * Math.Clamp(_settings.Bounciness, 0, 1);
        var damping = 2 * dampingRatio * Math.Sqrt(stiffness);
        foreach (var spring in new[] { _width, _height, _radius })
        {
            spring.Stiffness = stiffness;
            spring.Damping = damping;
        }
        // The shape morph never bounces (critically damped): past either end it would just clamp.
        _notch.Stiffness = stiffness;
        _notch.Damping = 2 * Math.Sqrt(stiffness);
        _notch.Target = IsNotch ? 1 : 0;
        if (!IsLoaded || IslandHidden) _notch.Snap(); // nothing on screen to morph

        _hoverTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, _settings.HoverDelayMs));
        _volumeKeys.Enabled = _settings.HideVolumeFlyout;

        Root.LayoutTransform = Math.Abs(Scale - 1) < 0.001 ? Transform.Identity : new ScaleTransform(Scale, Scale);
        Width = BaseWidth * Scale;
        UpdateWindowHeight();
        HoshinoPalette.Apply(_settings);
        var spec = IslandSkinEngine.Current;
        FontFamily = IslandSkinEngine.GetThemeFontFamily(spec);
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        SnapsToDevicePixels = spec.IsPixelMode;
        UseLayoutRounding = spec.IsPixelMode;
        _homeHoshino.EnableHeadpat = _settings.EnableHeadpat;
        ExpandedView.Width = ExpandedWidth;
        HomeCompanionOuter.Visibility = _settings.ShowHoshino ? Visibility.Visible : Visibility.Collapsed;
        HomeTop.ColumnDefinitions[0].Width = new GridLength(_settings.ShowHoshino ? 242 : 0);
        ExpandedContent.Margin = spec.IsPixelMode
            ? new Thickness(20, IsNotch ? 14 : 24, 20, 14)
            : spec.IsCartoonSticker
                ? new Thickness(18, 12, 22, 16)
                : new Thickness(18, 12, 18, 14);
        if (IsGhostMode) ApplyGhostWindowStyle();

        var hasCompactHoshino = _settings.ShowHoshino;
        var compactH = spec.IsPixelMode ? 46.0 : 44.0;
        MediaView.Height = compactH;
        TimerView.Height = compactH;
        StopwatchView.Height = compactH;
        OsdView.Height = compactH;
        AquariumView.Height = compactH;
        MediaCompactArtHost.Margin = new Thickness(hasCompactHoshino ? 42 : 6, 0, 0, 0);
        MediaView.Width = hasCompactHoshino ? 354 : 308;
        TimerCompactInner.Margin = new Thickness(hasCompactHoshino ? 46 : 18, 0, 20, 0);
        TimerView.Width = hasCompactHoshino ? 196 : 164;
        StopwatchCompactInner.Margin = new Thickness(hasCompactHoshino ? 46 : 18, 0, 20, 0);
        StopwatchView.Width = hasCompactHoshino ? 196 : 164;

        var coverSize = Math.Clamp(_settings.CoverSize, 62, 72);
        MediaArtLarge.Width = MediaArtLarge.Height = coverSize;
        _mediaArtDecorator.UpdateState(_media.Current?.IsPlaying == true, IslandHidden, coverSize, MediaArtLarge, MediaArtSmall);
        ApplySkinLayout(spec);
        _aiPanel.ApplyDesign(); _lyricsPanel.ApplyDesign(); _aquarium.ApplySettings(); _coworking.RefreshGoal(); UpdateCompactLyric();
        UpdateVoiceWakeChip();
        UpdateViewMargins();

        if (_settings.AdaptiveBackground) { if (IsLoaded) _background.Start(); }
        else _background.Stop();

        if (!_settings.DeviceAlerts) _devices.Stop();
        else if (IsLoaded) _devices.Start();
        if (!_settings.FocusAlerts) _focus.Stop();
        else if (IsLoaded) _focus.Start();
        if (!_settings.PrivacyIndicator) _privacy.Stop();
        else if (IsLoaded) _privacy.Start();
        if (IsLoaded) UpdatePrivacyIndicators();
        UpdateWeatherService();
        UpdateNotificationAndCalendarServices();
        _pageNavKey = null; // navigation style may have changed
        if (IsLoaded) BuildPageNav();
        UpdateToggleHotkey();
        _keyboard.LockKeysEnabled = _settings.LockKeyAlerts;
        _keyboard.PasteEnabled = _settings.PasteAlerts;
        _keyboard.Update();
        _clipboard.Enabled = _settings.CopyAlerts;
        _clipboard.ReadText = _settings.ClipboardPreview;
        _clipboard.Update();

        BuildQuickApps();
        _clockWeatherKey = null;
        UpdateClock();
        UpdateEqualizer(); // real waveform setting
        _alarms.Reschedule();
        if (_settings.StatusAlerts) { if (IsLoaded) _status.Start(); }
        else _status.Dispose();
        if (IsLoaded) UpdateWinUiBehind(); // material / engine may have changed
        ApplyTheme();
        UpdateMotionArtwork(_media.Current);

        Reposition();
        if (IsLoaded) Render();
        ApplyGeometry();
    }

    // ---------------------------------------------------------------- window setup

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetExStyle(hwnd);
        style |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        style &= ~NativeMethods.WS_EX_APPWINDOW;
        NativeMethods.SetExStyle(hwnd, style);
        // Hardware rendering measured ~40% cheaper in CPU than software for this window's animations.
        if (HwndSource.FromHwnd(hwnd) is { } source)
        {
            source.AddHook(WndProc);
            _keyboard.Attach(source);
            _clipboard.Attach(source);
            _toggleHotkey.Attach(source);
            _ghostHotkey.Attach(source);
            UpdateToggleHotkey();
        }
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(NativeMethods.MA_NOACTIVATE);
        }
        return IntPtr.Zero;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _gameWatch.Tick+=(_,_)=>UpdateGameMode();_gameWatch.Start();UpdateGameMode();
        StartupMark("loaded");
        KeepOnTop(force: true);
        UpdateWinUiBehind();
        Reposition();
        ApplyTheme();
        UpdateClock();
        _clockTimer.Start();
        SetEqualizerRunning(false);
        Render();

        // What the island shows straight away (volume / battery alerts, wallpaper theme, glass).
        _volume.Start();
        _battery.Start();
        _alarms.Start();
        if (_settings.AdaptiveBackground) _background.Start();
        BuildShelf();
        BuildNotificationList();
        BuildCalendar();
        UpdatePageVisibility();
        StartupMark("core services + first render");
        _ = StartSecondaryServicesAsync();
#if DEBUG
        if (Environment.GetEnvironmentVariable("DISLAND_SNAPSHOT") is { Length: > 0 } snapshotPath)
            _ = SaveSnapshotAsync(snapshotPath);
        if (Environment.GetEnvironmentVariable("DISLAND_ANIM_FRAMES") is { Length: > 0 } framesFolder)
            _ = RecordAnimationFramesAsync(framesFolder);
        if (Environment.GetEnvironmentVariable("DISLAND_MOTION_TEST") is { Length: > 0 } motionTest)
        {
            // Debug aid: "artist|album|output.txt" → look up motion artwork and report the result.
            var parts = motionTest.Split('|');
            _ = Task.Run(async () =>
            {
                string result;
                try
                {
                    using var motion = new MotionArtworkService();
                    var sw = Stopwatch.StartNew();
                    var file = await motion.GetAsync(parts[0], parts[1], "", CancellationToken.None);
                    result = file is null ? $"none ({sw.ElapsedMilliseconds} ms)"
                        : $"{file} {new System.IO.FileInfo(file).Length / 1024} KB ({sw.ElapsedMilliseconds} ms)";
                }
                catch (Exception ex)
                {
                    result = ex.ToString();
                }
                System.IO.File.WriteAllText(parts[2], result);
            });
        }
        if (Environment.GetEnvironmentVariable("DISLAND_MOTION_PLAY") is { Length: > 0 } motionFile)
        {
            // Debug aid: play a cached motion-art file in the expanded island's banner.
            _leavePoll.Tick += (_, _) => _msOutside = 0;
            _debugForceBanner = true;
            SetExpanded(true);
            MotionBanner.Source = new Uri(motionFile);
            MotionBanner.Play();
        }
        if (Environment.GetEnvironmentVariable("DISLAND_ACTIVITIES") is { Length: > 0 } activities)
        {
            // Debug aid: "timer,stopwatch" starts those activities (to see the second-activity bubble).
            if (activities.Contains("timer")) _timer.Start(TimeSpan.FromMinutes(5));
            if (activities.Contains("stopwatch")) _stopwatch.Start();
        }
        if (Environment.GetEnvironmentVariable("DISLAND_ICS") is { Length: > 0 } ics)
        {
            // Debug aid: use this iCal link for this run only (in memory, never saved by this hook).
            _settings.CalendarIcsUrls = new List<string> { ics };
            UpdateNotificationAndCalendarServices();
        }
        if (Environment.GetEnvironmentVariable("DISLAND_NAV") is { Length: > 0 } nav
            && Enum.TryParse<PageNavigation>(nav, true, out var navigation))
        {
            // Debug aid: navigation style for this run only (not saved).
            _settings.PageNavigation = navigation;
            _pageNavKey = null;
        }
        if (Environment.GetEnvironmentVariable("DISLAND_EXPAND") is { Length: > 0 } expand)
        {
            // Debug aid: open the island and keep it open (for snapshots of the expanded view);
            // a page name ("Apps") opens that page.
            _leavePoll.Tick += (_, _) => _msOutside = 0;
            SetExpanded(true);
            if (Enum.TryParse<ExpandedPage>(expand, true, out var page)) ShowPage(page, animate: false);
        }
        if (Environment.GetEnvironmentVariable("DISLAND_DEVICE") is { Length: > 0 } deviceName)
        {
            // Debug aid: "name" or "-name" fakes a device connecting / disconnecting after 1 s.
            _ = Task.Delay(1000).ContinueWith(_ => OnDeviceChanged(new DeviceEvent(deviceName.TrimStart('-'),
                    DeviceKind.Headphones, !deviceName.StartsWith('-'))),
                TaskScheduler.FromCurrentSynchronizationContext());
        }
        if (Environment.GetEnvironmentVariable("DISLAND_BENCH") is { Length: > 0 })
        {
            // Debug aid: expand/collapse continuously so animation cost can be measured.
            var bench = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            bench.Tick += (_, _) => SetExpanded(!_state.IsExpanded);
            _leavePoll.Tick += (_, _) => _msOutside = 0; // don't auto-collapse during the benchmark
            bench.Start();
        }
        if (Environment.GetEnvironmentVariable("DISLAND_STATS") is { Length: > 0 } statsPath)
        {
            // Debug aid: managed heap numbers after startup settles.
            _ = Task.Delay(10000).ContinueWith(_ =>
            {
                var before = GC.GetTotalMemory(false);
                var after = GC.GetTotalMemory(true);
                var info = GC.GetGCMemoryInfo();
                System.IO.File.WriteAllText(statsPath,
                    $"managed before GC {before / 1048576.0:N1} MB, after full GC {after / 1048576.0:N1} MB, " +
                    $"GC committed {info.TotalCommittedBytes / 1048576.0:N1} MB, gen0/1/2 collections " +
                    $"{GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}");
            }, TaskScheduler.Default);
        }
#endif
        StartupMark("before media");
        await _media.StartAsync();
        StartupMark("media started");
        InitUpdates();
    }

    // ---------------------------------------------------------------- updates

    private void InitUpdates()
    {
        if (App.Updates is not { } updates) return;

        // "HoshiBar updated" in the island and the What's New window, once, on the first start after an
        // update (automatic, or a newer installer run by hand; not on a fresh install).
        var current = UpdateService.CurrentVersion;
        var updatedFrom = Version.TryParse(_settings.LastRunVersion, out var last) && last < current ? last : null;
#if DEBUG
        // Debug aid: DISLAND_WHATSNEW=<version> shows it as if updated from that version.
        if (Version.TryParse(Environment.GetEnvironmentVariable("DISLAND_WHATSNEW"), out var debugFrom)) updatedFrom = debugFrom;
#endif
        if (updatedFrom is not null)
        {
            _ = ShowWhatsNewSoonAsync(updatedFrom);
            _ = Task.Delay(3000).ContinueWith(_ => ShowInfo(TransientPriority.Calendar, 300, "", "HoshiBar обновлён",
                    $"Версия {current}", detailBrush: (Brush)FindResource("Green"), seconds: 5),
                TaskScheduler.FromCurrentSynchronizationContext());
        }
        if (_settings.LastRunVersion != current.ToString())
        {
            _settings.LastRunVersion = current.ToString();
            _settings.Save();
        }

        updates.Available += info => ShowInfo(TransientPriority.Device, 320, "", "Update available",
            $"Version {info.Version}", detailBrush: (Brush)FindResource("Green"), seconds: 5);
        updates.Start();
    }

    private async Task ShowWhatsNewSoonAsync(Version updatedFrom)
    {
        await Task.Delay(2500); // let the island settle first
        try
        {
            (Application.Current as App)?.ShowWhatsNew(updatedFrom);
        }
        catch (Exception ex)
        {
            App.LogError(ex); // never silently: this window is the only sign an update happened
        }
    }

    /// <summary>
    /// A verified update is ready. Install at a quiet moment only: not while a timer, stopwatch or alarm is
    /// running, the island is open, or an alert is showing. The updater asks again every 10 minutes.
    /// </summary>
    private static void StartupMark(string phase)
    {
        if (Environment.GetEnvironmentVariable("DISLAND_STARTUP_LOG") is not { Length: > 0 } path) return;
        using var self = Process.GetCurrentProcess();
        System.IO.File.AppendAllText(path,
            $"{(DateTime.Now - self.StartTime).TotalMilliseconds,6:0} ms wall  {self.TotalProcessorTime.TotalMilliseconds,6:0} ms CPU  {phase}\n");
    }

    /// <summary>
    /// Everything that isn't needed for the first frame starts a moment later and one after another,
    /// so the island appears quickly instead of competing with a burst of device enumeration, COM
    /// setup, network requests and WinRT activation at startup.
    /// </summary>
    private async Task StartSecondaryServicesAsync()
    {
        await Task.Delay(1200);
        if (!IsLoaded) return;
        async Task Next(Action start, string name = "")
        {
#if DEBUG
            // Debug aid: DISLAND_SKIP=brightness,devices,… leaves services out (to measure each one's startup cost).
            if (name.Length > 0 && (Environment.GetEnvironmentVariable("DISLAND_SKIP") ?? "").Contains(name)) return;
#endif
            start();
            await Dispatcher.Yield(DispatcherPriority.Background); // let input and rendering run in between
        }
        StartupMark("secondary services: begin");
        await Next(() =>
        {
            if (_settings.PrivacyIndicator) _privacy.Start();
            UpdatePrivacyIndicators();
        }, "privacy");
        StartupMark("privacy");
        await Next(() => { if (_settings.FocusAlerts) _focus.Start(); }, "focus");
        StartupMark("focus");
        await Next(() => { if (_settings.StatusAlerts) _status.Start(); }, "status");
        StartupMark("status");
        await Next(() => { if (_settings.DeviceAlerts) _devices.Start(); }, "devices");
        StartupMark("devices");
        await Next(UpdateNotificationAndCalendarServices, "notifications");
        StartupMark("notifications + calendar");
        await Next(UpdateWeatherService, "weather");
        StartupMark("weather");
        await Next(() => _ = Task.Run(_brightness.Start), "brightness"); // WMI, on a worker thread
        await Task.Delay(3000);
        StartupMark("+3 s (async work settled)");
    }

#if DEBUG
    private bool _debugForceBanner;

    /// <summary>
    /// Debug aid: renders the island's own visual tree to a PNG. Liquid Glass excludes the window from
    /// screen capture, so ordinary screenshots can't show it.
    /// </summary>
    private async Task SaveSnapshotAsync(string path)
    {
        var delay = int.TryParse(Environment.GetEnvironmentVariable("DISLAND_SNAPSHOT_DELAY"), out var ms) ? ms : 3000;
        await Task.Delay(delay);
        SaveSnapshot(path);
    }

    /// <summary>
    /// Debug aid: DISLAND_ANIM_FRAMES=&lt;folder&gt; records the shape morph (island → notch → island) and
    /// hide → show as PNG frames every 40 ms. The shape change is in memory only (not saved).
    /// </summary>
    private async Task RecordAnimationFramesAsync(string folder)
    {
        System.IO.Directory.CreateDirectory(folder);
        await Task.Delay(3000);
        async Task Record(string name, Action start)
        {
            start();
            for (var i = 0; i < 14; i++)
            {
                SaveSnapshot(System.IO.Path.Combine(folder, $"{name}-{i:00}.png"));
                await Task.Delay(40);
            }
            await Task.Delay(800);
        }
        var original = _settings.Mode;
        var other = original == IslandMode.Notch ? IslandMode.DynamicIsland : IslandMode.Notch;
        await Record("1-morph", () => { _settings.Mode = other; ApplySettings(); });
        await Record("2-morph-back", () => { _settings.Mode = original; ApplySettings(); });
        await Record("3-hide", () => SetIslandHidden(true));
        await Record("4-show", () => SetIslandHidden(false));
    }

    private void SaveSnapshot(string path)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = new RenderTargetBitmap((int)(Width * dpi.DpiScaleX), (int)(Height * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(Root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(path);
        encoder.Save(file);
    }
#endif

    private void OnClosed(object? sender, EventArgs e)
    {
        _gameWatch.Stop();
        _mediaArtDecorator.Dispose();
        _weatherRim.Dispose();
        _qrShare.Dispose();
        _aquarium.Dispose();
        _journal.Dispose();
        _obsidian.Dispose();
        _aiPanel.Dispose();
        _compactLyricsTimer.Stop();
        _lyricsPanel.Dispose();
        _settings.Changed -= ApplySettings;
        _background.Dispose();
        _volumeKeys.Dispose();
        _battery.Dispose();
        _devices.Dispose();
        _keyboard.Dispose();
        _focus.Dispose();
        _clipboard.Dispose();
        _privacy.Dispose();
        _weather.Dispose();
        _alarms.Dispose();
        _status.Dispose();
        _meterTimer.Stop();
        _meter.Dispose();
        _alarmSound?.Stop();
        _notifications.Dispose();
        _calendar.Dispose();
        _notes.Dispose(); // writes any unsaved typing
        _toggleHotkey.Dispose();
        _ghostHotkey.Dispose();
        _ghostTimer.Stop();
        _volume.Dispose();
        _brightness.Dispose();
        _media.Dispose();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _clockTimer.Stop();
        _leavePoll.Stop();
        _progressTimer.Stop();
        if (_animating) CompositionTarget.Rendering -= OnRendering;
        _glassTimer.Stop();
        _capture.Dispose();
        _gpuBlur?.Dispose();
        _windows.Dispose();
        _motionCancel?.Cancel();
        MotionBanner.Close();
        _motionArt.Dispose();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(Reposition);

    private void Reposition()
    {
        var screen=FullscreenService.TargetScreen(_settings.MonitorDeviceName);var bounds=screen.Bounds;var hwnd=new WindowInteropHelper(this).Handle;
        if(hwnd==IntPtr.Zero){Left=(SystemParameters.PrimaryScreenWidth-Width)/2;Top=_settings.TopOffset;return;}
        var dpi=VisualTreeHelper.GetDpi(this);if(!NativeMethods.GetWindowRect(hwnd,out var rect))return;
        var left=bounds.Left+(bounds.Width-(rect.Right-rect.Left))/2;var top=bounds.Top+(int)Math.Round(_settings.TopOffset*dpi.DpiScaleY);
        if(rect.Left!=left||rect.Top!=top)NativeMethods.SetWindowPos(hwnd,IntPtr.Zero,left,top,0,0,NativeMethods.SWP_NOSIZE|NativeMethods.SWP_NOZORDER|NativeMethods.SWP_NOACTIVATE);
    }

    // ---------------------------------------------------------------- rendering

    private void Render()
    {
        // Hidden (shortcut / tray): skip layout and animation entirely; catch up when shown.
        if (IslandHidden||_gameSuppressed) return;
        FrameworkElement view;
        double width, height, radius;

        var spec = IslandSkinEngine.Current;
        var expandedRadius = IsNotch ? Math.Min(NotchExpandedRadius, spec.ExpandedHullRadius) : spec.ExpandedHullRadius;

        if (_voiceFocus)
        {
            view=VoiceFocusView;width=320;height=250;radius=expandedRadius;
        }
        else if (_state.IsExpanded)
        {
            UpdateExpandedSections();
            view = ExpandedView;
            width = ExpandedWidth;
            height = MeasureExpandedHeight();
            radius = expandedRadius;
        }
        else if (ShowingCompactLyrics)
        {
            view = LyricsView;
            width = LyricsView.Width;
            height = LyricsView.Height;
            radius = IsNotch ? NotchCompactRadius : Math.Min(24, height / 2);
        }
        else if (ShowingAquarium)
        {
            view = AquariumView;
            width = AquariumView.Width;
            height = AquariumView.Height;
            radius = IsNotch ? NotchCompactRadius : height / 2;
        }
        else
        {
            view = ViewFor(_state.CurrentView);
            width = view.Width;
            height = view.Height;
            radius = IsNotch ? Math.Min(NotchCompactRadius, height / 2) : height / 2;
        }

        foreach(var item in _views)item.Visibility=_voiceFocus&&item!=VoiceFocusView?Visibility.Hidden:Visibility.Visible;
        ActivityBubble.Visibility=_voiceFocus?Visibility.Collapsed:ActivityBubble.Visibility;
        CompactHoshinoHost.Visibility = ShowCompactCompanion ? Visibility.Visible : Visibility.Collapsed;
        _aquarium.SetActive(view == AquariumView && !IslandHidden && !_gameSuppressed);
        SwitchTo(view);
        AnimateTo(width, height, radius * Roundness);
    }

    private FrameworkElement ViewFor(IslandView view) => view switch
    {
        IslandView.Media => MediaView,
        IslandView.Timer => TimerView,
        IslandView.Stopwatch => StopwatchView,
        IslandView.Volume or IslandView.Brightness => OsdView,
        IslandView.Battery => BatteryView,
        IslandView.TimerDone => TimerDoneView,
        IslandView.Info => InfoView,
        IslandView.Notification => NotificationView,
        _ => ClockView,
    };

    private void UpdateExpandedSections()
    {
        var media = _media.Current;
        MediaSection.Visibility = media is not null ? Visibility.Visible : Visibility.Collapsed;
        if (!AvailablePages().Contains(_page)) ShowPage(ExpandedPage.Home, animate: false);
        UpdatePageVisibility();
        ArtBanner.Visibility = false && _page == ExpandedPage.Home && _settings.ShowArtBanner && media is not null
                               && (media.Art is not null || _motionReady)
            ? Visibility.Visible
            : Visibility.Collapsed;
#if DEBUG
        if (_debugForceBanner) ArtBanner.Visibility = Visibility.Visible;
#endif
        ClockSection.Visibility = media is null ? Visibility.Visible : Visibility.Collapsed;
        ProgressRow.Visibility = media is { Duration.Ticks: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        var isPixel = IslandSkinEngine.Current.IsPixelMode;
        TimerPresets.Visibility = isPixel || _timer.IsActive ? Visibility.Collapsed : Visibility.Visible;
        TimerActivePanel.Visibility = isPixel || !_timer.IsActive ? Visibility.Collapsed : Visibility.Visible;
        FocusDockBorder.Visibility = isPixel && !_stopwatch.IsActive ? Visibility.Collapsed : Visibility.Visible;
        StopwatchPanel.Visibility = _stopwatch.IsActive ? Visibility.Visible : Visibility.Collapsed;
        if (_stopwatch.IsActive) StopwatchTextLarge.Text = StopwatchService.Format(_stopwatch.Elapsed, tenths: true);
        UpdateProgress();
    }

    // ---------------------------------------------------------------- expanded pages

    private readonly HoshinoCompanion _compactHoshino;
    private readonly HoshinoCompanion _homeHoshino;
    private readonly HoshinoCompanion _lyricsHoshino;
    private readonly HoshinoCompanion _voiceHoshino;
    private readonly IslandMediaArtDecorator _mediaArtDecorator;
    private readonly IslandWeatherRim _weatherRim;
    private readonly Rectangle[] _voiceWaveBars;
    private double _audioPeak;
    private string? _chromeOverlayKey;
    private readonly AbydosAquariumView _aquarium;
    private readonly CoworkingBar _coworking;
    private readonly HomeTasksPanel _homeTasks;
    private readonly LocalQrShareService _qrShare;
    private readonly ShelfQrPanel _qrPanel;
    private readonly AiChatPanel _aiPanel;
    private readonly ObsidianSync _obsidian;
    private readonly JournalPanel _journal;
    private readonly PhraseDeck _phrases=new();
    private void NextPhrase() => SetCompanionPhraseText(_phrases.Next(DateTime.Now, _settings.WeatherEnabled ? _weather.Current : null));
    private void SetCompanionPhraseText(string phrase)
    {
        HomePhrase.Text = phrase;
        HomePhrase.ToolTip = phrase;
        _aiPanel?.SetCompanionPhrase(phrase);
        if (IsLoaded && _state.IsExpanded && _page == ExpandedPage.Home) Render();
    }

    private void OnHomePhraseClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        NextPhrase();
    }

    private void OnVoiceWakeChipClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenAiChat();
        _aiPanel.StartDictation();
    }

    private void OnVoiceWakeChipRightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _aiPanel.ToggleWakeEnabled();
        UpdateVoiceWakeChip();
    }

    private void UpdateVoiceWakeChip()
    {
        var spec = IslandSkinEngine.Current;
        var wakeOn = AssistantPreferences.Current.WakeEnabled;
        var listening = _aiPanel.IsListening;
        VoiceWakeLabel.Text = listening ? "Слушаю…" : WakeProfile.SelectedPhrase;
        VoiceWakeDot.Fill = listening
            ? IslandSkinEngine.Frozen(spec.Accent)
            : wakeOn
                ? IslandSkinEngine.Frozen(spec.IsShittimTablet || spec.IsAeroGlass ? Color.FromRgb(0x38, 0xDF, 0xC8) : spec.Gold)
                : (Brush)FindResource("HoshinoMuted");
    }

    private void StyleLrcToggleButton()
    {
        var spec = IslandSkinEngine.Current;
        var active = _lyricsPanel.CompactEnabled;
        HomeLyricsToggle.Content = spec.IsPixelMode ? "[LRC]" : "LRC";
        HomeLyricsToggle.Padding = spec.IsPixelMode ? new Thickness(8, 4, 8, 4) : new Thickness(10, 4, 10, 4);
        HomeLyricsToggle.RenderTransform = spec.IsCartoonSticker ? new RotateTransform(-6) : Transform.Identity;
        HomeLyricsToggle.Background = active
            ? (Brush)FindResource("HoshinoAccent")
            : spec.IsAeroGlass
                ? IslandSkinEngine.CreateAeroConvexGlassBrush()
                : spec.IsCartoonSticker
                    ? IslandSkinEngine.Frozen(Color.FromRgb(0x5C, 0xE1, 0xE6))
                    : (Brush)FindResource("HoshinoControl");
        HomeLyricsToggle.Foreground = active && !spec.IsCartoonSticker && !spec.IsPixelMode
            ? (Brush)FindResource("HoshinoOnAccent")
            : spec.IsAeroGlass && !active
                ? Brushes.White
                : (Brush)FindResource("HoshinoText");
        HomeLyricsToggle.ToolTip = active
            ? "Лирика LRC включена — нажмите, чтобы выключить"
            : "Показать синхронную лирику LRC на островке";
    }

    private void ApplySkinLayout(SkinPaletteSpec spec)
    {
        var cardRadius = new CornerRadius(spec.CardCornerRadius);
        var cardBorder = new Thickness(spec.CardBorderThickness);
        var iconBtnStyle = (Style)FindResource("IconButton");
        var chipBtnStyle = (Style)FindResource("ChipButton");

        // Ensure zero DropShadowEffect on text/vector containers so ClearType/Grayscale text stays razor-sharp
        HomeCompanionCard.Effect = null;
        HomePlayerCard.Effect = null;
        FocusDockBorder.Effect = null;
        PageTabsTrack.Effect = null;
        VoiceWakeChip.Effect = null;
        SettingsGearWrap.Effect = null;
        HomeMomoCard.Effect = null;

        HomeCompanionShadow.CornerRadius = cardRadius;
        HomePlayerShadow.CornerRadius = cardRadius;
        PageTabsTrackShadow.CornerRadius = new CornerRadius(spec.IsPixelMode ? 2 : 17);
        VoiceWakeChipShadow.CornerRadius = new CornerRadius(spec.IsPixelMode ? 2 : 16);
        SettingsGearShadow.CornerRadius = new CornerRadius(spec.IsPixelMode ? 2 : 16);
        HomeMomoShadow.CornerRadius = new CornerRadius(spec.IsPixelMode ? 0 : 12);

        // Left Companion Card: sunken Tamagotchi mint LCD well inside dark-plum bezel vs standard/Aero/Sticker card
        if (spec.IsPixelMode)
        {
            HomeCompanionCard.CornerRadius = new CornerRadius(4);
            HomeCompanionCard.BorderBrush = IslandSkinEngine.Frozen(Color.FromRgb(0x24, 0x1A, 0x34));
            HomeCompanionCard.BorderThickness = new Thickness(2);
            HomeCompanionCard.Padding = new Thickness(9);
            HomeCompanionCard.Background = IslandSkinEngine.Frozen(Color.FromRgb(0x65, 0x54, 0x82));

            HomeCompanionWell.CornerRadius = new CornerRadius(2);
            HomeCompanionWell.Background = IslandSkinEngine.CreateMintLcdGridBrush();
            HomeCompanionWell.BorderBrush = IslandSkinEngine.Frozen(Color.FromRgb(0x35, 0x28, 0x4A));
            HomeCompanionWell.BorderThickness = new Thickness(2.5, 2.5, 0, 0);
            HomeCompanionWell.Padding = new Thickness(8, 6, 8, 8);

            HomeCompanionWellBevelBR.CornerRadius = new CornerRadius(2);
            HomeCompanionWellBevelBR.BorderBrush = Brushes.White;
            HomeCompanionWellBevelBR.BorderThickness = new Thickness(0, 0, 2, 2);
            HomeCompanionAeroRim.BorderThickness = new Thickness(0);

            HomeMomoCard.CornerRadius = new CornerRadius(0);
            HomeMomoCard.Background = IslandSkinEngine.Frozen(Color.FromRgb(0xD5, 0xF5, 0xEC));
            HomeMomoCard.BorderBrush = IslandSkinEngine.Frozen(Color.FromRgb(0x1A, 0x35, 0x31));
            HomeMomoCard.BorderThickness = new Thickness(2);
            HomeMomoCard.Padding = new Thickness(8, 6, 8, 6);
        }
        else
        {
            HomeCompanionCard.CornerRadius = cardRadius;
            HomeCompanionCard.SetResourceReference(Border.BorderBrushProperty, "HoshinoBorder");
            HomeCompanionCard.BorderThickness = cardBorder;
            HomeCompanionCard.Padding = new Thickness(11, 9, 11, 9);
            HomeCompanionCard.SetResourceReference(Border.BackgroundProperty, "HoshinoCompanionSurface");

            HomeCompanionWell.CornerRadius = new CornerRadius(0);
            HomeCompanionWell.Background = Brushes.Transparent;
            HomeCompanionWell.BorderThickness = new Thickness(0);
            HomeCompanionWell.Padding = new Thickness(0);
            HomeCompanionWellBevelBR.BorderThickness = new Thickness(0);

            HomeCompanionAeroRim.CornerRadius = new CornerRadius(Math.Max(0, spec.CardCornerRadius - 1));
            HomeCompanionAeroRim.Margin = new Thickness(1);
            HomeCompanionAeroRim.BorderThickness = new Thickness(spec.IsAeroGlass ? 1.0 : 0);
            HomeCompanionAeroRim.BorderBrush = spec.IsAeroGlass
                ? IslandSkinEngine.Frozen(Color.FromArgb(0x78, 0xEA, 0xF8, 0xFF))
                : Brushes.Transparent;

            HomeMomoCard.CornerRadius = new CornerRadius(12);
            HomeMomoCard.SetResourceReference(Border.BackgroundProperty, "HoshinoControl");
            HomeMomoCard.SetResourceReference(Border.BorderBrushProperty, "HoshinoBorder");
            HomeMomoCard.BorderThickness = new Thickness(spec.IsCartoonSticker ? 2.2 : 1.0);
            HomeMomoCard.Padding = new Thickness(10, 6, 10, 6);
        }

        // Right Top Player Card: sunken Winamp sage-mint LCD + physical pixel transport keys vs Aero/Sticker/Glass
        HomePlayerCard.CornerRadius = cardRadius;
        HomePlayerCard.BorderThickness = spec.IsPixelMode ? new Thickness(2) : cardBorder;
        HomePlayerCard.SetResourceReference(Border.BackgroundProperty, "HoshinoPlayerSurface");
        HomePlayerCard.SetResourceReference(Border.BorderBrushProperty, "HoshinoBorder");

        if (spec.IsPixelMode)
        {
            HomePlayerCard.Padding = new Thickness(10, 9, 10, 9);
            PlayerScreenBorder.CornerRadius = new CornerRadius(2);
            PlayerScreenBorder.Background = IslandSkinEngine.CreateWinampLcdBrush();
            PlayerScreenBorder.BorderBrush = IslandSkinEngine.Frozen(Color.FromRgb(0x4E, 0x40, 0x66));
            PlayerScreenBorder.BorderThickness = new Thickness(2, 2, 1.5, 1.5);
            PlayerScreenBorder.Padding = new Thickness(8, 7, 8, 8);
            HomePlayerAeroRim.BorderThickness = new Thickness(0);

            MediaSourceRow.Visibility = Visibility.Collapsed;
            MediaTitleLarge.Foreground = IslandSkinEngine.Frozen(Color.FromRgb(0x14, 0x2D, 0x2A));
            MediaArtistLarge.Foreground = IslandSkinEngine.Frozen(Color.FromRgb(0x2D, 0x4F, 0x4A));

            TransportStack.HorizontalAlignment = HorizontalAlignment.Left;
            foreach (var (btn, w) in new[] { (PrevButton, 46.0), (PlayPauseButton, 50.0), (NextButton, 46.0) })
            {
                btn.Style = chipBtnStyle;
                btn.Width = w;
                btn.Height = 26;
                btn.Padding = new Thickness(0);
                btn.Margin = new Thickness(0, 0, 5, 0);
                btn.SetResourceReference(Control.BackgroundProperty, "HoshinoControl");
            }
        }
        else
        {
            HomePlayerCard.Padding = new Thickness(14, 12, 14, 11);
            PlayerScreenBorder.CornerRadius = new CornerRadius(0);
            PlayerScreenBorder.Background = Brushes.Transparent;
            PlayerScreenBorder.BorderThickness = new Thickness(0);
            PlayerScreenBorder.Padding = new Thickness(0);

            HomePlayerAeroRim.CornerRadius = new CornerRadius(Math.Max(0, spec.CardCornerRadius - 1));
            HomePlayerAeroRim.Margin = new Thickness(1);
            HomePlayerAeroRim.BorderThickness = new Thickness(spec.IsAeroGlass ? 1.0 : 0);
            HomePlayerAeroRim.BorderBrush = spec.IsAeroGlass
                ? IslandSkinEngine.Frozen(Color.FromArgb(0x78, 0xEA, 0xF8, 0xFF))
                : Brushes.Transparent;

            MediaSourceRow.Visibility = Visibility.Visible;
            MediaTitleLarge.SetResourceReference(TextBlock.ForegroundProperty, "HoshinoText");
            MediaArtistLarge.SetResourceReference(TextBlock.ForegroundProperty, "Secondary");

            TransportStack.HorizontalAlignment = HorizontalAlignment.Center;
            PrevButton.Style = iconBtnStyle;
            PrevButton.Width = 36;
            PrevButton.Height = 30;
            PrevButton.Margin = new Thickness(0);
            PrevButton.Background = Brushes.Transparent;
            PrevButton.BorderThickness = new Thickness(0);

            NextButton.Style = iconBtnStyle;
            NextButton.Width = 36;
            NextButton.Height = 30;
            NextButton.Margin = new Thickness(0);
            NextButton.Background = Brushes.Transparent;
            NextButton.BorderThickness = new Thickness(0);

            PlayPauseButton.Style = spec.IsAeroGlass || spec.IsCartoonSticker ? chipBtnStyle : iconBtnStyle;
            PlayPauseButton.Width = spec.IsAeroGlass || spec.IsCartoonSticker ? 36 : 44;
            PlayPauseButton.Height = spec.IsAeroGlass || spec.IsCartoonSticker ? 34 : 32;
            PlayPauseButton.Padding = new Thickness(0);
            PlayPauseButton.Margin = new Thickness(8, 0, 8, 0);
            PlayPauseButton.Background = spec.IsAeroGlass
                ? IslandSkinEngine.CreateAeroConvexGlassBrush(true)
                : spec.IsCartoonSticker
                    ? IslandSkinEngine.Frozen(Color.FromRgb(0xFF, 0xB5, 0xD0))
                    : Brushes.Transparent;
        }

        // Skin-specific player progress bar & OSD bar (discrete LCD cubes / cylindrical 3D gel / candy-striped ink tube)
        if (spec.IsPixelMode)
        {
            ProgressTrackBorder.Height = 9;
            ProgressTrackBorder.CornerRadius = new CornerRadius(0);
            ProgressTrackBorder.BorderThickness = new Thickness(0);
            ProgressFill.CornerRadius = new CornerRadius(0);
            ProgressThumb.Visibility = Visibility.Collapsed;

            OsdTrackBorder.Height = 9;
            OsdTrackBorder.CornerRadius = new CornerRadius(0);
            OsdTrackBorder.BorderThickness = new Thickness(0);
            OsdFill.CornerRadius = new CornerRadius(0);
        }
        else if (spec.IsAeroGlass)
        {
            ProgressTrackBorder.Height = 9;
            ProgressTrackBorder.CornerRadius = new CornerRadius(4.5);
            ProgressTrackBorder.BorderThickness = new Thickness(1);
            ProgressTrackBorder.BorderBrush = IslandSkinEngine.Frozen(Color.FromArgb(0xAA, 0xEE, 0xFA, 0xFF));
            ProgressFill.CornerRadius = new CornerRadius(4);

            ProgressThumb.Width = 8;
            ProgressThumb.Height = 15;
            ProgressThumb.CornerRadius = new CornerRadius(3.5);
            ProgressThumb.Background = IslandSkinEngine.CreateAeroConvexGlassBrush(true);
            ProgressThumb.BorderBrush = IslandSkinEngine.Frozen(Color.FromArgb(0xCC, 0x08, 0x2A, 0x4C));
            ProgressThumb.BorderThickness = new Thickness(1);

            OsdTrackBorder.Height = 10;
            OsdTrackBorder.CornerRadius = new CornerRadius(5);
            OsdTrackBorder.BorderThickness = new Thickness(1);
            OsdTrackBorder.BorderBrush = IslandSkinEngine.Frozen(Color.FromArgb(0xAA, 0xEE, 0xFA, 0xFF));
            OsdFill.CornerRadius = new CornerRadius(4);
        }
        else if (spec.IsCartoonSticker)
        {
            var ink = IslandSkinEngine.Frozen(Color.FromRgb(0x2A, 0x1E, 0x2E));
            ProgressTrackBorder.Height = 10;
            ProgressTrackBorder.CornerRadius = new CornerRadius(5);
            ProgressTrackBorder.BorderThickness = new Thickness(2);
            ProgressTrackBorder.BorderBrush = ink;
            ProgressFill.CornerRadius = new CornerRadius(3.5);

            ProgressThumb.Width = 14;
            ProgressThumb.Height = 14;
            ProgressThumb.CornerRadius = new CornerRadius(7);
            ProgressThumb.Background = IslandSkinEngine.Frozen(Color.FromRgb(0xFF, 0xE2, 0xEE));
            ProgressThumb.BorderBrush = ink;
            ProgressThumb.BorderThickness = new Thickness(2.2);

            OsdTrackBorder.Height = 12;
            OsdTrackBorder.CornerRadius = new CornerRadius(6);
            OsdTrackBorder.BorderThickness = new Thickness(2.2);
            OsdTrackBorder.BorderBrush = ink;
            OsdFill.CornerRadius = new CornerRadius(4);
        }
        else
        {
            ProgressTrackBorder.Height = 6.5;
            ProgressTrackBorder.CornerRadius = new CornerRadius(3.25);
            ProgressTrackBorder.BorderThickness = new Thickness(0);
            ProgressFill.CornerRadius = new CornerRadius(3.25);

            ProgressThumb.Width = 11;
            ProgressThumb.Height = 11;
            ProgressThumb.CornerRadius = new CornerRadius(5.5);
            ProgressThumb.Background = spec.IsShittimTablet
                ? IslandSkinEngine.Frozen(Color.FromRgb(0x9C, 0xA8, 0xF8))
                : IslandSkinEngine.Frozen(Color.FromRgb(0xCC, 0xBE, 0xDC));
            ProgressThumb.BorderBrush = spec.IsShittimTablet
                ? Brushes.White
                : IslandSkinEngine.Frozen(spec.Border);
            ProgressThumb.BorderThickness = new Thickness(1.2);

            OsdTrackBorder.Height = 10;
            OsdTrackBorder.CornerRadius = new CornerRadius(5);
            OsdTrackBorder.BorderThickness = new Thickness(spec.IsShittimTablet ? 0 : 1);
            OsdTrackBorder.BorderBrush = spec.IsShittimTablet
                ? Brushes.Transparent
                : IslandSkinEngine.CreateRimBrush(spec);
            OsdFill.CornerRadius = new CornerRadius(spec.IsShittimTablet ? 5 : 4);
        }

        // Integrated Edge-to-Edge Bottom Focus Strip (1:1 with all 5 expanded design renders)
        if (spec.IsPixelMode)
        {
            FocusDockBorder.CornerRadius = new CornerRadius(0);
            FocusDockBorder.BorderThickness = new Thickness(0);
            FocusDockBorder.Background = Brushes.Transparent;
            FocusDockBorder.Padding = new Thickness(0);
            FocusDockBorder.Margin = new Thickness(_settings.ShowHoshino ? 254 : 0, 2, 0, -2);
        }
        else if (spec.IsCartoonSticker)
        {
            FocusDockBorder.CornerRadius = new CornerRadius(0);
            FocusDockBorder.BorderBrush = IslandSkinEngine.Frozen(Color.FromRgb(0x2A, 0x1E, 0x2E));
            FocusDockBorder.BorderThickness = new Thickness(0, 2.4, 0, 0);
            FocusDockBorder.Background = IslandSkinEngine.Frozen(Color.FromRgb(0xFF, 0xF3, 0xDC));
            FocusDockBorder.Padding = new Thickness(18, 9, 22, 13);
            FocusDockBorder.Margin = new Thickness(-18, 2, -22, -16);
        }
        else
        {
            FocusDockBorder.CornerRadius = new CornerRadius(0);
            FocusDockBorder.BorderBrush = spec.IsAeroGlass
                ? IslandSkinEngine.Frozen(Color.FromArgb(0x66, 0xEA, 0xF8, 0xFF))
                : spec.IsShittimTablet
                    ? IslandSkinEngine.Frozen(Color.FromArgb(0xAA, 0xBE, 0xDC, 0xF6))
                    : (Brush)FindResource("HoshinoDivider");
            FocusDockBorder.BorderThickness = new Thickness(0, 1.1, 0, 0);
            FocusDockBorder.Background = spec.IsAeroGlass
                ? IslandSkinEngine.Frozen(Color.FromArgb(0x24, 0x05, 0x1E, 0x38))
                : spec.IsShittimTablet
                    ? IslandSkinEngine.Frozen(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF))
                    : IslandSkinEngine.Frozen(Color.FromArgb(0x24, 0x00, 0x00, 0x00));
            FocusDockBorder.Padding = new Thickness(18, 9, 18, 13);
            FocusDockBorder.Margin = new Thickness(-18, 2, -18, -14);
        }

        PageTabsTrack.CornerRadius = new CornerRadius(spec.IsPixelMode ? 2 : 17);
        PageTabsTrack.BorderThickness = cardBorder;

        VoiceWakeChip.CornerRadius = new CornerRadius(spec.IsPixelMode ? 2 : 16);
        VoiceWakeChip.BorderThickness = spec.IsPixelMode ? new Thickness(1.6) : cardBorder;
        VoiceWakeChip.Background = spec.IsAeroGlass
            ? IslandSkinEngine.CreateAeroConvexGlassBrush()
            : spec.IsPixelMode
                ? (Brush)FindResource("HoshinoControl")
                : (Brush)FindResource("HoshinoSurface");

        if (spec.IsPixelMode)
        {
            SettingsGearWrap.Background = Brushes.Transparent;
            SettingsGearWrap.BorderThickness = new Thickness(0);
            SettingsGearButton.Style = chipBtnStyle;
            SettingsGearButton.Width = 30;
            SettingsGearButton.Height = 28;
            SettingsGearButton.Padding = new Thickness(0);
            SettingsGearButton.Margin = new Thickness(0);
            SettingsGearButton.SetResourceReference(Control.BackgroundProperty, "HoshinoControl");
        }
        else
        {
            SettingsGearWrap.CornerRadius = new CornerRadius(16);
            SettingsGearWrap.BorderThickness = cardBorder;
            SettingsGearWrap.Background = spec.IsAeroGlass
                ? IslandSkinEngine.CreateAeroConvexGlassBrush()
                : (Brush)FindResource("HoshinoSurface");
            SettingsGearButton.Style = iconBtnStyle;
            SettingsGearButton.Width = 30;
            SettingsGearButton.Height = 30;
            SettingsGearButton.Margin = new Thickness(0);
            SettingsGearButton.Background = Brushes.Transparent;
        }

        // MomoTalk / Seamless Comic Bubble / Tamagotchi RPG Dialogue Box
        ComicBubbleTail.Visibility = spec.IsCartoonSticker ? Visibility.Visible : Visibility.Collapsed;
        HomeMomoBar.Visibility = spec.IsPixelMode ? Visibility.Collapsed : Visibility.Visible;
        PixelPhraseArrow.Visibility = spec.IsPixelMode ? Visibility.Visible : Visibility.Collapsed;

        // Tamagotchi top status icon strip
        if (spec.IsPixelMode)
        {
            TamagotchiStatusHost.Content = IslandSkinEngine.CreateTamagotchiStatusStrip(
                HoshinoAtmosphere.IsDrowsy(DateTime.Now),
                _media.Current?.IsPlaying == true,
                _timer.IsActive);
            TamagotchiStatusHost.Visibility = Visibility.Visible;
        }
        else
        {
            TamagotchiStatusHost.Visibility = Visibility.Collapsed;
        }

        // Crisp Eye of Horus backdrop behind Hoshino (expanded Home & VoiceFocus)
        HomeHorusBackdropHost.Content = HoshinoAccessories.CreateEyeOfHorusBackdrop(spec, includeRadarArcs: false);
        VoiceFocusHorusHost.Content = HoshinoAccessories.CreateEyeOfHorusBackdrop(spec, includeRadarArcs: true);

        // Vector Focus diamonds & Clock diamond separator
        FocusDiamondHost.Content = IslandChromeRenderer.CreateFocusDiamondIcon(spec);
        FocusActiveDiamondHost.Content = IslandChromeRenderer.CreateFocusDiamondIcon(spec);
        CompactTimerDiamondHost.Content = IslandChromeRenderer.CreateFocusDiamondIcon(spec);
        ClockDiamondHost.Content = IslandChromeRenderer.CreateClockDiamondSeparator(spec);

        // Compact LRC badge styling (tilted sticker in Chibi Pop, [LRC] button in Pixel Tamagotchi)
        CompactLrcText.Text = spec.IsPixelMode ? "[LRC]" : "LRC";
        CompactLrcBadge.CornerRadius = new CornerRadius(spec.IsPixelMode ? 2 : 8);
        CompactLrcBadge.BorderThickness = new Thickness(spec.IsCartoonSticker ? 1.8 : spec.IsPixelMode ? 1.6 : 1.0);
        CompactLrcBadge.RenderTransform = spec.IsCartoonSticker ? new RotateTransform(-7) : Transform.Identity;
        CompactLrcBadge.Background = spec.IsCartoonSticker
            ? IslandSkinEngine.Frozen(Color.FromRgb(0x5C, 0xE1, 0xE6))
            : spec.IsPixelMode
                ? (Brush)FindResource("HoshinoControl")
                : spec.IsAeroGlass
                    ? IslandSkinEngine.CreateAeroConvexGlassBrush()
                    : (Brush)FindResource("HoshinoSecondaryAccent");
        CompactLrcText.Foreground = spec.IsPixelMode || spec.IsCartoonSticker
            ? (Brush)FindResource("HoshinoText")
            : spec.IsAeroGlass
                ? Brushes.White
                : (Brush)FindResource("HoshinoOnAccent");

        // Style preset chips (Aero convex glass / Shittim active cyan 25m / Pixel color-coded raised keys / ChibiPop yellow 25m)
        foreach (var child in TimerPresetButtons.Children.OfType<Button>())
        {
            var is25 = Equals(child.Tag, "25");
            var isShort = Equals(child.Tag, "5") || Equals(child.Tag, "15");
            child.Background = spec.IsAeroGlass
                ? IslandSkinEngine.CreateAeroConvexGlassBrush(is25)
                : spec.IsShittimTablet && is25
                    ? IslandSkinEngine.Frozen(Color.FromRgb(0x29, 0xB6, 0xF6))
                    : spec.IsPixelMode
                        ? is25
                            ? IslandSkinEngine.Frozen(Color.FromRgb(0xF2, 0xB1, 0xD0))
                            : isShort
                                ? IslandSkinEngine.Frozen(Color.FromRgb(0xF5, 0xE4, 0x9C))
                                : (Brush)FindResource("HoshinoControl")
                        : spec.IsCartoonSticker && is25
                            ? IslandSkinEngine.Frozen(spec.Gold)
                            : (Brush)FindResource("HoshinoControl");
            child.Foreground = spec.IsShittimTablet && is25
                ? Brushes.White
                : spec.IsAeroGlass
                    ? Brushes.White
                    : (Brush)FindResource("HoshinoText");
        }

        if (_media.Current is { } activeMedia)
        {
            var title = string.IsNullOrWhiteSpace(activeMedia.Title) ? "Без названия" : activeMedia.Title;
            MediaTitleLarge.Text = spec.IsPixelMode && !title.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ? $"{title}.mp3" : title;
            MediaArtistLarge.Text = spec.IsPixelMode
                ? $"{(string.IsNullOrEmpty(activeMedia.Artist) ? "Track" : activeMedia.Artist)} · 128kbps"
                : string.IsNullOrEmpty(activeMedia.Album) ? activeMedia.Artist : $"{activeMedia.Artist} — {activeMedia.Album}";
            UpdateProgress();
        }

        StyleLrcToggleButton();
        _homeTasks.ApplySkin();
        RebuildPixelFocusDock();
        _chromeOverlayKey = null;
    }

    private TextBlock? _pixelTimerText;

    private void RebuildPixelFocusDock()
    {
        if (IslandSkinEngine.Current.IsPixelMode)
        {
            (PixelFocusHost.Content, _pixelTimerText) = IslandChromeRenderer.CreatePixelFocusDock(_timer);
            PixelFocusHost.Visibility = Visibility.Visible;
            HomeTop.Margin = new Thickness(0);
            FocusDockBorder.Visibility = _stopwatch.IsActive ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            PixelFocusHost.Visibility = Visibility.Collapsed;
            PixelFocusHost.Content = null;
            _pixelTimerText = null;
            HomeTop.Margin = new Thickness(0, 0, 0, 8);
            FocusDockBorder.Visibility = Visibility.Visible;
        }
    }
    private uint? _lastRenderedNotificationId;
    internal object AccessReport() => new
    {
        hasPackageIdentity = PackageIdentity.HasIdentity,
        notificationsEnabled = _settings.NotificationsEnabled,
        notificationAccess = _notifications.AccessStatus,
        notificationMode = _notifications.Mode,
        notificationProblem = _notifications.Problem,
        notificationCount = _notifications.Items.Count,
        receivedSinceStart = _notifications.ReceivedCount,
        lastRenderedNotificationId = _lastRenderedNotificationId,
        notificationViewVisible = _currentView == NotificationView && NotificationView.IsHitTestVisible && NotificationView.Opacity > .5,
        islandHidden = IslandHidden,
        focusMode = _focus.IsOn,
        calendarEnabled = _settings.CalendarEnabled,
        windowsCalendarEnabled = _settings.WindowsCalendar,
        windowsCalendarStoreReady = _calendar.WindowsStoreReady,
        calendarProblem = _calendar.Problem,
        calendarCount = _calendar.Items.Count
    };
    private readonly LyricsPanel _lyricsPanel;
    private readonly DispatcherTimer _compactLyricsTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool ShowingAquarium => !_voiceFocus && !_state.IsExpanded && !_lyricsPanel.CompactEnabled && _state.Transient is null && _state.CurrentView == IslandView.Clock && _settings.AbydosAquarium;
    private bool ShowCompactCompanion => !_voiceFocus && !_state.IsExpanded && _settings.ShowHoshino && _state.Transient is null && !_lyricsPanel.CompactEnabled && !ShowingAquarium;
    private bool ShowingCompactLyrics => !_state.IsExpanded && _lyricsPanel.CompactEnabled && _state.Transient is null;
    private Artwork? _compactArtwork;

    private void UpdateCompactArtwork()
    {
        var artwork = _media.Current?.Art;
        if (ReferenceEquals(artwork, _compactArtwork)) return;
        _compactArtwork = artwork;
        SetArt(CompactLyricArt, CompactLyricArtFallback, artwork);
    }

    private void UpdateCompactLyric()
    {
        var showHoshino = _settings.ShowHoshino;
        LyricsHoshinoHost.Visibility = showHoshino ? Visibility.Visible : Visibility.Collapsed;
        CompactLyricArt.Visibility = showHoshino ? Visibility.Collapsed : Visibility.Visible;

        LyricsView.Width = Math.Clamp(_settings.CompactLyricWidth, 300, 620);
        CompactLyric.FontSize = Math.Clamp(_settings.LyricFontSize, 12, 22);
        CompactLyric.LineHeight = CompactLyric.FontSize * 1.5;
        CompactLyric.MaxHeight = CompactLyric.LineHeight * (_settings.TwoLineLyrics ? 2 : 1);
        CompactLyric.TextAlignment = _settings.CenterLyrics ? TextAlignment.Center : TextAlignment.Left;

        var raw = _lyricsPanel.CompactText ?? "";
        CompactLyric.Inlines.Clear();
        if (raw.Length > 0)
        {
            var words = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var spec = IslandSkinEngine.Current;
            var highlightPink = IslandSkinEngine.Frozen(spec.IsShittimTablet ? Color.FromRgb(0xEC, 0x62, 0x98) : spec.Accent);
            var highlightSky = IslandSkinEngine.Frozen(spec.IsShittimTablet ? Color.FromRgb(0x1D, 0x8F, 0xC9) : spec.SecondaryAccent);
            var normalText = (Brush)FindResource("HoshinoText");

            for (var i = 0; i < words.Length; i++)
            {
                if (i > 0) CompactLyric.Inlines.Add(new System.Windows.Documents.Run(" "));
                var w = words[i];
                Brush fg = normalText;
                if (words.Length >= 3)
                {
                    if (i >= 1 && i <= Math.Max(1, words.Length / 2)) fg = highlightPink;
                    else if (i == words.Length - 1) fg = highlightSky;
                }
                CompactLyric.Inlines.Add(new System.Windows.Documents.Run(w) { Foreground = fg });
            }
        }
        CompactLyric.ToolTip = raw;
        CompactLyric.Measure(new Size(Math.Max(120, LyricsView.Width - 110), double.PositiveInfinity));
        LyricsView.Height = Math.Clamp(CompactLyric.DesiredSize.Height + 16, 44, 84);
        CompactLyric.BeginAnimation(OpacityProperty, new DoubleAnimation(.45, 1, TimeSpan.FromMilliseconds(_settings.ReduceMotion ? 0 : 150)));
        if (ShowingCompactLyrics) Render();
    }

    private bool _voiceFocus;
    private void BeginVoiceFocus()
    {
        _voiceFocus=true;_voiceChatOpen=false;UpdateGameMode();if(IslandHidden)SetIslandHidden(false);EndTyping(restoreFocus:true);_hoverTimer.Stop();_leavePoll.Stop();_progressTimer.Stop();
        foreach(var view in _views){view.BeginAnimation(OpacityProperty,null);view.Opacity=0;view.IsHitTestVisible=false;}
        _currentView=null;Render();
    }
    private void EndVoiceFocus()
    {
        _voiceFocus=false;_voiceChatOpen=true;_msOutside=0;OpenAiChat();UpdateSecondActivity();UpdateGameMode();
    }
    private void OnVoiceFocusClick(object sender,MouseButtonEventArgs e){_voiceFocus=false;_voiceChatOpen=true;OpenAiChat();e.Handled=true;}
    private void OnHomeLyricsToggle(object sender, RoutedEventArgs e) => _lyricsPanel.ToggleCompact();

    public void OpenAiChat()
    {
        _hoverTimer.Stop();
        _alertPage = null;
        ShowPage(ExpandedPage.Ai, animate: _state.IsExpanded);
        SetExpanded(true);
    }

    private void UpdateGameMode()
    {
        if(!IsLoaded)return;
        var foreground=_fullscreenDetector();
        ApplyGameMode(_settings.GameModeEnabled&&(foreground is FullscreenState.OwnWindow or FullscreenState.TransientOverlay?_gameFullscreen:foreground==FullscreenState.Fullscreen));
    }
    private void ApplyGameMode(bool fullscreen)
    {
        _gameFullscreen=fullscreen;var hide=fullscreen&&!_voiceFocus&&!_voiceChatOpen;
        if(hide==_gameSuppressed){if(hide){_clockTimer.Stop();_glassTimer.Stop();_compactLyricsTimer.Stop();SetEqualizerRunning(false);}return;}_gameSuppressed=hide;
        _aiPanel.SetLowPower(hide);_lyricsPanel.SetSuspended(hide);_compactHoshino.SetSuspended(hide);_homeHoshino.SetSuspended(hide);_lyricsHoshino.SetSuspended(hide);_voiceHoshino.SetSuspended(hide);_aquarium.SetSuspended(hide);_mediaArtDecorator.SetSuspended(hide);_weatherRim.SetSuspended(hide);
        if(hide){
            EndTyping(false);_hoverTimer.Stop();_leavePoll.Stop();_progressTimer.Stop();_clockTimer.Stop();_compactLyricsTimer.Stop();_glassTimer.Stop();_shadowRestore?.Stop();
            SetEqualizerRunning(false);_meter.Dispose();_capture.Dispose();_gpuBlur?.Dispose();_gpuBlur=null;_gpuActive=false;GlassImage.Source=null;_glassBitmap=null;
            _motionCancel?.Cancel();MotionBanner.Close();_motionKey=null;_motionReady=false;
            if(_animating){CompositionTarget.Rendering-=OnRendering;_animating=false;}Hide();
            GC.Collect(GC.MaxGeneration,GCCollectionMode.Optimized,blocking:false,compacting:false);
        }else{
            UpdateClock();_clockTimer.Start();if(_lyricsPanel.CompactEnabled)_compactLyricsTimer.Start();
            if(!IslandHidden){Show();Root.BeginAnimation(OpacityProperty,null);Root.Opacity=1;Render();UpdateGlass();KeepOnTop(true);}
            if(_state.IsExpanded){_leavePoll.Start();_progressTimer.Start();}UpdateEqualizer();UpdateMotionArtwork(_media.Current);
        }
    }


    private enum ExpandedPage
    {
        Home,
        Notifications,
        Calendar,
        Todo,
        Notes,
        Shelf,
        Weather,
        Apps,
        Ai,
    }

    private ExpandedPage _page = ExpandedPage.Home;
    private FrameworkElement? _leavingPage;
    private string? _pageNavKey;
    private int _wheelDelta;
    private long _lastWheelEvent;
    private int _lastWheelDeltaValue;
    private double _wheelSpeed;
    private long _lastWheelFlip;
    private bool _wheelGestureScrolled;
    private bool _wheelGestureFlipped;
    private double _wheelPeak;
    private bool _wheelMomentumFaded;

    private List<ExpandedPage> AvailablePages()
    {
        var pages = new List<ExpandedPage> { ExpandedPage.Home, ExpandedPage.Ai };
        if (_settings.NotificationsEnabled) pages.Add(ExpandedPage.Notifications);
        if (_settings.CalendarEnabled) pages.Add(ExpandedPage.Calendar);
        if (_settings.TodoEnabled) pages.Add(ExpandedPage.Todo);
        if (_settings.NotesEnabled) pages.Add(ExpandedPage.Notes);
        if (_settings.ShelfEnabled) pages.Add(ExpandedPage.Shelf);
        if (_settings.WeatherEnabled) pages.Add(ExpandedPage.Weather);
        pages.Add(ExpandedPage.Apps);
        var order=_settings.PageOrder ?? new List<string>();
        return pages.OrderBy(p=> { var index=order.IndexOf(p.ToString()); return index<0 ? 1000+pages.IndexOf(p) : index; }).ToList();
    }

    private void MoveNavigation(ExpandedPage from, ExpandedPage to)
    {
        if(from==to)return;
        var order=AvailablePages();order.Remove(from);order.Insert(order.IndexOf(to),from);
        _settings.PageOrder=order.Select(p=>p.ToString()).Concat((_settings.PageOrder??new()).Where(n=>!order.Any(p=>p.ToString()==n))).Distinct().ToList();
        _settings.Save();_pageNavKey=null;BuildPageNav();
    }

    private FrameworkElement PageElement(ExpandedPage page) => page switch
    {
        ExpandedPage.Ai => AiPage,
        ExpandedPage.Notifications => NotificationsPage,
        ExpandedPage.Calendar => CalendarPage,
        ExpandedPage.Todo => TodoPage,
        ExpandedPage.Notes => NotesPage,
        ExpandedPage.Shelf => ShelfPage,
        ExpandedPage.Weather => WeatherPage,
        ExpandedPage.Apps => AppsPage,
        _ => HomePage,
    };

    private static string PageGlyph(ExpandedPage page) => page switch
    {
        ExpandedPage.Ai => "",
        ExpandedPage.Notifications => "",
        ExpandedPage.Calendar => "",
        ExpandedPage.Todo => "", // checklist
        ExpandedPage.Notes => "", // quick note
        ExpandedPage.Shelf => "",
        ExpandedPage.Weather => "",
        ExpandedPage.Apps => "",
        _ => "",
    };

    private static string PageTitle(ExpandedPage page) => page switch
    {
        ExpandedPage.Ai => "Чат с Хошино",
        ExpandedPage.Notifications => "Уведомления",
        ExpandedPage.Calendar => "Календарь и дневник",
        ExpandedPage.Todo => "Задачи",
        ExpandedPage.Notes => "Заметки",
        ExpandedPage.Shelf => "Полка файлов",
        ExpandedPage.Weather => "Погода",
        ExpandedPage.Apps => "Приложения",
        _ => "Главная",
    };

    /// <summary>Only the current page (and one sliding out) is visible; the rest are collapsed.</summary>
    private void UpdatePageVisibility()
    {
        foreach (var page in Enum.GetValues<ExpandedPage>())
        {
            var element = PageElement(page);
            element.Visibility = page == _page || element == _leavingPage ? Visibility.Visible : Visibility.Collapsed;
        }
        BuildPageNav();
    }

    /// <summary>Icon-only Bento capsule tabs, physical Pixel Tamagotchi keys, or dots; preserves drag-reorder and context menu.</summary>
    private void BuildPageNav()
    {
        var pages = AvailablePages();
        var dots = _settings.PageNavigation == PageNavigation.Dots;
        var spec = IslandSkinEngine.Current;
        var key = $"{dots}|{_page}|{spec.Skin}|{spec.PresetId}|{string.Join(',', pages)}";
        if (key == _pageNavKey) return;
        _pageNavKey = key;

        PageTabs.Children.Clear();
        PageTabsTrackShadow.Visibility = spec.IsCartoonSticker && !dots ? Visibility.Visible : Visibility.Collapsed;
        PageTabsTrack.Background = dots || spec.IsPixelMode ? Brushes.Transparent : (Brush)FindResource("HoshinoSurface");
        PageTabsTrack.BorderThickness = dots || spec.IsPixelMode ? new Thickness(0) : new Thickness(spec.CardBorderThickness);
        PageTabsTrack.Padding = dots || spec.IsPixelMode ? new Thickness(0) : new Thickness(4, 3, 4, 3);
        PageTabs.HorizontalAlignment = dots ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        PageTabs.Margin = new Thickness(0);

        var iconStyle = (Style)FindResource("IconButton");
        var chipStyle = (Style)FindResource("ChipButton");

        foreach (var page in pages)
        {
            var selected = page == _page;
            var usePixelKey = !dots && spec.IsPixelMode;
            var button = new Button
            {
                Style = usePixelKey ? chipStyle : iconStyle,
                ToolTip = PageTitle(page),
                Width = dots ? 18 : usePixelKey ? 30 : 32,
                Height = dots ? 18 : usePixelKey ? 26 : 28,
                Padding = new Thickness(0),
                Margin = new Thickness(dots ? 1 : usePixelKey ? 0 : 1.5, 0, dots ? 1 : usePixelKey ? 3 : 1.5, 0),
                Content = dots
                    ? new Ellipse
                    {
                        Width = 6,
                        Height = 6,
                        Fill = selected ? (Brush)FindResource("HoshinoAccent") : (Brush)FindResource("HoshinoMuted"),
                    }
                    : IslandChromeRenderer.CreateTabIcon(page.ToString(), selected, spec),
            };

            System.Windows.Automation.AutomationProperties.SetName(button, PageTitle(page));
            if (!dots)
            {
                if (spec.IsPixelMode)
                {
                    button.Background = selected
                        ? IslandSkinEngine.Frozen(Color.FromRgb(0xF5, 0xBE, 0xD8))
                        : (Brush)FindResource("HoshinoControl");
                }
                else if (selected)
                {
                    if (spec.IsAeroGlass)
                    {
                        button.Background = page == ExpandedPage.Home
                            ? IslandSkinEngine.CreateStartOrbBrush()
                            : IslandSkinEngine.CreateAeroConvexGlassBrush(true);
                        button.BorderBrush = IslandSkinEngine.Frozen(Color.FromArgb(0xCC, 0xEE, 0xFA, 0xFF));
                        button.BorderThickness = new Thickness(1);
                    }
                    else if (spec.IsCartoonSticker)
                    {
                        button.Background = IslandSkinEngine.Frozen(Color.FromRgb(0xFF, 0xB5, 0xD0));
                        button.BorderBrush = IslandSkinEngine.Frozen(Color.FromRgb(0x2A, 0x1E, 0x2E));
                        button.BorderThickness = new Thickness(1.8);
                    }
                    else
                    {
                        button.Background = (Brush)FindResource("HoshinoAccent");
                    }
                }
            }

            var target = page;
            Point dragStart = default;
            button.PreviewMouseLeftButtonDown += (_, e) => dragStart = e.GetPosition(button);
            button.PreviewMouseMove += (_, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed) return;
                var point = e.GetPosition(button);
                if ((point - dragStart).Length < 8) return;
                DragDrop.DoDragDrop(button, new DataObject("Hoshino.Navigation", target.ToString()), DragDropEffects.Move);
                e.Handled = true;
            };
            button.AllowDrop = true;
            button.DragOver += (_, e) =>
            {
                e.Effects = e.Data.GetDataPresent("Hoshino.Navigation") ? DragDropEffects.Move : DragDropEffects.None;
                e.Handled = true;
            };
            button.Drop += (_, e) =>
            {
                if (e.Data.GetData("Hoshino.Navigation") is string name && Enum.TryParse<ExpandedPage>(name, out var source))
                    MoveNavigation(source, target);
                e.Handled = true;
            };

            var menu = new ContextMenu();
            foreach (var delta in new[] { -1, 1 })
            {
                var step = delta;
                var item = new MenuItem { Header = step < 0 ? "Переместить влево" : "Переместить вправо" };
                item.Click += (_, _) =>
                {
                    var list = AvailablePages();
                    var index = list.IndexOf(target);
                    var next = index + step;
                    if (next < 0 || next >= list.Count) return;
                    var other = list[next];
                    if (step < 0) MoveNavigation(target, other);
                    else MoveNavigation(other, target);
                };
                menu.Items.Add(item);
            }
            button.ContextMenu = menu;
            button.Click += (_, _) => ShowPage(target, animate: true);
            PageTabs.Children.Add(button);
        }
    }

    /// <summary>Switches page, sliding in the direction of travel; the island resizes to the new page.</summary>
    private void ShowPage(ExpandedPage page, bool animate)
    {
        if (page == _page) return;
        EndTyping(restoreFocus: true); // the text box being typed in is leaving the screen
        var pages = AvailablePages();
        var direction = pages.IndexOf(page) >= pages.IndexOf(_page) ? 1 : -1;
        var from = PageElement(_page);
        var to = PageElement(page);
        _page = page;

        // A page still sliding out from an earlier switch is dropped at once.
        if (_leavingPage is { } stale && stale != to) FinishLeaving(stale);
        _leavingPage = null;

        to.Margin = new Thickness(0);
        to.IsHitTestVisible = true;
        if (animate && _state.IsExpanded && !_settings.ReduceMotion)
        {
            // The outgoing page keeps drawing but takes no layout space (negative bottom margin), so
            // the island sizes to the incoming page while both are on screen.
            PageHost.ClipToBounds = true;
            _leavingPage = from;
            from.Margin = new Thickness(0, 0, 0, -from.ActualHeight);
            from.IsHitTestVisible = false;
            SlidePage(from, 0, -36 * direction, 1, 0, 140, () =>
            {
                if (_leavingPage == from) FinishLeaving(from);
            });
            SlidePage(to, 36 * direction, 0, 0, 1, 220, null);
        }
        else
        {
            FinishLeaving(from);
            to.Opacity = 1;
            to.RenderTransform = Transform.Identity;
        }

        SuspendTextShadow();
        UpdateMotionPlayback();
        if (_state.IsExpanded) Render(); // new height and banner
        else UpdatePageVisibility();
    }

    private void FinishLeaving(FrameworkElement page)
    {
        if (page == _leavingPage) _leavingPage = null;
        PageHost.ClipToBounds = false;
        page.BeginAnimation(OpacityProperty, null);
        page.Opacity = 1;
        page.RenderTransform = Transform.Identity;
        page.Margin = new Thickness(0);
        if (page != PageElement(_page)) page.Visibility = Visibility.Collapsed;
    }

    private void SlidePage(FrameworkElement page, double fromX, double toX, double fromOpacity, double toOpacity,
        double ms, Action? completed)
    {
        var time = TimeSpan.FromMilliseconds(ms / SpeedFactor);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var slide = new TranslateTransform(fromX, 0);
        page.RenderTransform = slide;
        var move = new DoubleAnimation(fromX, toX, time) { EasingFunction = ease };
        var fade = new DoubleAnimation(fromOpacity, toOpacity, time) { EasingFunction = ease };
        Timeline.SetDesiredFrameRate(move, FrameRateCap);
        Timeline.SetDesiredFrameRate(fade, FrameRateCap);
        if (completed is not null) fade.Completed += (_, _) => completed();
        slide.BeginAnimation(TranslateTransform.XProperty, move);
        page.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>
    /// Mouse wheel / touchpad over the expanded island flips pages, unless the list under the cursor
    /// can still scroll that way (then it scrolls).
    /// <list type="bullet">
    /// <item>A gesture that scrolled a list never falls through into a flip when the list reaches its
    /// end; scrolling again (after a short pause) flips.</item>
    /// <item>Mouse wheel (whole notches): one page per notch.</item>
    /// <item>Touchpad (small deltas): one page per swipe. A new swipe is told apart from the previous
    /// swipe's momentum by a pause, a change of direction, or a jump in speed after the momentum has
    /// faded (momentum only decays), so quick successive swipes each flip, and one swipe flips once.</item>
    /// </list>
    /// (The first version allowed one flip per 400 ms stream of events, so quick flicks, and swipes
    /// started during momentum, merged into one gesture and "stopped switching".)
    /// </summary>
    private void OnRootPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_voiceFocus) { e.Handled = true; return; }
        if (!_state.IsExpanded || _page == ExpandedPage.Ai) return;
        var now = Environment.TickCount64;
        var delta = e.Delta;
        var mouse = delta % Mouse.MouseWheelDeltaForOneLine == 0;
        var gap = now - _lastWheelEvent;
        var speed = Math.Abs(delta);
        // A touchpad swipe ramps up fast at its start (8 → 30 → 60 → 90…); only once its momentum has
        // clearly faded (below 40% of the swipe's peak) can a speed-up mean a new swipe. Without that
        // check the ramp itself read as a second swipe and one swipe flipped two pages.
        var freshSwipe = !mouse && _wheelMomentumFaded && speed >= 30 && speed >= 2 * _wheelSpeed
                         && now - _lastWheelFlip > 250;
        var newGesture = gap > (mouse ? 300 : 220)
                         || Math.Sign(delta) != Math.Sign(_lastWheelDeltaValue)
                         || freshSwipe;
        _wheelSpeed = newGesture ? speed : 0.7 * _wheelSpeed + 0.3 * speed;
        _lastWheelEvent = now;
        _lastWheelDeltaValue = delta;
        if (newGesture)
        {
            _wheelGestureScrolled = false;
            _wheelGestureFlipped = false;
            _wheelDelta = 0;
            _wheelPeak = speed;
            _wheelMomentumFaded = false;
        }
        else
        {
            _wheelPeak = Math.Max(_wheelPeak, speed);
            if (_wheelSpeed < 0.4 * _wheelPeak) _wheelMomentumFaded = true;
        }

        for (var d = e.OriginalSource as DependencyObject; d is not null && d != Root; d = VisualTreeHelper.GetParent(d))
        {
            if (d is ScrollViewer { ScrollableHeight: > 0 } scroller
                && (delta < 0 ? scroller.VerticalOffset < scroller.ScrollableHeight - 0.5 : scroller.VerticalOffset > 0.5))
            {
                _wheelGestureScrolled = true; // reaching the end mid-gesture won't flip
                return; // let it scroll
            }
        }

        e.Handled = true;
        if (_wheelGestureScrolled) return;
        if (!mouse && _wheelGestureFlipped) return;      // touchpad: one page per swipe
        if (mouse && now - _lastWheelFlip < 100) return; // mouse: one page per notch, ignoring double-reported notches

        _wheelDelta += delta;
        if (Math.Abs(_wheelDelta) < Mouse.MouseWheelDeltaForOneLine) return;
        var step = _wheelDelta < 0 ? 1 : -1;
        _wheelDelta = 0;

        var pages = AvailablePages();
        var index = pages.IndexOf(_page) + step;
        if (index < 0 || index >= pages.Count) return;
        _wheelGestureFlipped = true;
        _lastWheelFlip = now;
        ShowPage(pages[index], animate: true);
    }

    private double MeasureExpandedHeight()
    {
        // Content changed deep inside a page (e.g. forecast rows added) only invalidates that panel;
        // its ancestors keep a cached size until the next layout pass. Run it now, then measure.
        ExpandedView.UpdateLayout();
        ExpandedView.Measure(new Size(ExpandedWidth, double.PositiveInfinity));
        return ExpandedView.DesiredSize.Height - PillTop;
    }

    private void SwitchTo(FrameworkElement target)
    {
        if (target == _currentView) return;
        var time = _settings.ReduceMotion ? 0 : 1 / SpeedFactor;

        if (_currentView is { } previous)
        {
            previous.IsHitTestVisible = false;
            AnimateView(previous, opacity: 0, fromScale: null, toScale: 0.96, durationMs: 100 * time, delayMs: 0);
        }

        // The text shadow lives on the visible view only (an effect costs per pixel of its element),
        // and is dropped while views fade/scale: it can't be seen in motion, and re-blurring every
        // frame measured about a third of the animation cost. It returns once things settle.
        SetTextShadow(null);
        _currentView = target;
        SuspendTextShadow();
        target.IsHitTestVisible = true;
        UpdateEqualizer();
        AnimateView(target, opacity: 1, fromScale: 0.94, toScale: 1, durationMs: 200 * time, delayMs: 35 * time);
    }

    private void AnimateView(FrameworkElement view, double opacity, double? fromScale, double toScale,
        double durationMs, double delayMs)
    {
        var duration = TimeSpan.FromMilliseconds(durationMs);
        var begin = TimeSpan.FromMilliseconds(delayMs);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var fade = new DoubleAnimation(opacity, duration) { BeginTime = begin, EasingFunction = ease };
        var scale = new DoubleAnimation(toScale, duration) { BeginTime = begin, EasingFunction = ease };
        if (fromScale is { } from) scale.From = from;
        Timeline.SetDesiredFrameRate(fade, FrameRateCap); // same cap as the spring
        Timeline.SetDesiredFrameRate(scale, FrameRateCap);
        view.BeginAnimation(OpacityProperty, fade);
        var transform = (ScaleTransform)view.RenderTransform;
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
    }

    // Collapsed states fit in a short window. A transparent WPF window is redrawn and pushed to the
    // screen as a whole every animation frame, so keeping it short while collapsed cuts that work
    // (and the GPU blur area, which follows the window) by about 3×.
    private const double CompactWindowHeight = 90;

    /// <summary>Grows the window before the pill grows; shrinks it only once the pill has settled small.</summary>
    private void UpdateWindowHeight()
    {
        var needed = PillTop + Math.Max(_height.Value, _height.Target) + 30; // overshoot + shadow room

        // Expanded content can outgrow the base height (quick apps + wallpaper controls). The root
        // only ever grows, in 20-unit steps: resizing the window rebuilds the GPU blur's layers, so
        // it must not follow the spring frame by frame.
        if (needed > Root.Height) Root.Height = Math.Ceiling(needed / 20) * 20;

        var height = (needed > CompactWindowHeight ? Root.Height : CompactWindowHeight) * Scale;
        if (Math.Abs(Height - height) > 0.5) Height = height;
    }

    private void AnimateTo(double width, double height, double radius)
    {
        _width.Target = width;
        _height.Target = height;
        _radius.Target = radius;
        UpdateWindowHeight();
        if (_animating) return;

        _animating = true;
        _lastFrameTime = _frameClock.Elapsed.TotalSeconds;
        _lastAppliedFrame = 0;
        SetTextShadow(null); // shadow returns when settled
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = _frameClock.Elapsed.TotalSeconds;
        var dt = Math.Min(now - _lastFrameTime, 1.0 / 20);
        _lastFrameTime = now;

        if (_settings.ReduceMotion) { _width.Snap(); _height.Snap(); _radius.Snap(); _notch.Snap(); }
        _width.Step(dt);
        _height.Step(dt);
        _radius.Step(dt);
        _notch.Step(dt);

        var settled = _width.IsSettled && _height.IsSettled && _radius.IsSettled && _notch.IsSettled;
        // Cap shape updates at 60 fps (30 under Energy Saver): on 120/144 Hz displays this halves the
        // window redraws while the motion looks the same at this size. (Physics still steps every tick.)
        if (!settled && now - _lastAppliedFrame < 1.0 / FrameRateCap - 0.002) return;
        _lastAppliedFrame = now;

        if (settled)
        {
            _width.Snap();
            _height.Snap();
            _radius.Snap();
            _notch.Snap();
            CompositionTarget.Rendering -= OnRendering;
            _animating = false;
            UpdateWindowHeight();
            if (_shadowRestore?.IsEnabled != true) RestoreTextShadow();
        }

        ApplyGeometry();
        if (settled && _hideAnimating) FinishHide();
    }

    private double _viewsTop = double.NaN;

    /// <summary>Views sit centered on the pill's top edge; compact companion space is reserved inside each compact view.</summary>
    private void UpdateViewMargins()
    {
        var top = PillTop;
        if (Math.Abs(top - _viewsTop) < 0.01) return;
        _viewsTop = top;
        foreach (var view in _views) view.Margin = new Thickness(0, top, 0, 0);
    }

    private void ApplyGeometry()
    {
        var w = Math.Max(0, _width.Value);
        var h = Math.Max(0, _height.Value);
        var r = Math.Clamp(_radius.Value, 0, Math.Min(w, h) / 2);
        var spec = IslandSkinEngine.Current;

        UpdateViewMargins();
        var compactLeftPad = spec.IsPixelMode ? 12.0 : 11.0;
        CompactHoshinoHost.Margin = new Thickness((BaseWidth - w) / 2 + compactLeftPad, PillTop + Math.Max(0, (h - 31) / 2), 0, 0);
        var t = NotchAmount;
        var geo = IslandChromeRenderer.BuildShapeAndRim(BaseWidth, IslandTopMargin, NotchShoulder, w, h, r, t, Roundness, spec);
        var shape = geo.Body;

        RenderOptions.SetEdgeMode(PillShape, spec.IsPixelMode ? EdgeMode.Aliased : EdgeMode.Unspecified);
        RenderOptions.SetEdgeMode(IslandRimPath, spec.IsPixelMode ? EdgeMode.Aliased : EdgeMode.Unspecified);
        PillShape.Data = shape;
        PillCartoonShadow.Data = shape;
        PillCartoonShadow.Visibility = spec.IsCartoonSticker ? Visibility.Visible : Visibility.Collapsed;
        GlassSheen.Data = shape;
        SkinClippedOverlay.Clip = shape;
        IslandRimPath.Data = geo.Rim;

        NotchTopSeal.Visibility = IsNotch && PillTop < .01 ? Visibility.Visible : Visibility.Collapsed;
        NotchTopSeal.Fill = PillShape.Fill;
        NotchTopSeal.Data = new RectangleGeometry(new Rect((BaseWidth - w) / 2 - NotchShoulder, 0, w + 2 * NotchShoulder, 2));
        NotchTopSeal.Clip = shape;
        GlassLayer.Clip = shape;
        ContentLayer.Clip = shape;
        UpdateGpuShape();

        var isExpanded = _state.IsExpanded || _voiceFocus;
        var overlayKey = FormattableString.Invariant($"{spec.Skin}|{Math.Round(w):0}|{Math.Round(h):0}|{isExpanded}|{IsNotch}|{Math.Round(t, 2):0.00}");
        if (overlayKey != _chromeOverlayKey)
        {
            _chromeOverlayKey = overlayKey;
            IslandChromeRenderer.PopulateSkinChrome(SkinClippedOverlay, SkinOuterOverlay, geo, spec, isExpanded, IsNotch);
        }

        // Perimeter Focus Timer along the collapsed island / open-top notch contour
        var showPerimeter = _settings.PerimeterFocusTimer && _timer.IsActive && !isExpanded && !IslandHidden;
        IslandChromeRenderer.UpdatePerimeterFocus(
            PerimeterTimerTrack, PerimeterTimerArc, PerimeterTimerDot,
            geo.Rim, spec, showPerimeter, _timer.RemainingRatio);

        // Top-rim weather atmosphere (snow / frost / raindrops)
        _weatherRim.Update(_weather.Current, BaseWidth, w, PillTop, IsNotch, IslandHidden);

        // Second-activity bubble (~8px clean gap right of the pill)
        const double bubbleGap = 8;
        var right = (BaseWidth + w) / 2;
        if (ActivityBubble.Visibility == Visibility.Visible)
        {
            var size = Math.Max(34, h);
            ActivityBubble.Width = ActivityBubble.Height = size;
            ActivityBubble.Margin = new Thickness(right + bubbleGap, PillTop + (h - size) / 2, 0, 0);
            RefreshActivityBubbleVisuals(size);
            right += bubbleGap + size;
        }
        if (PrivacyDot.Visibility == Visibility.Visible)
            PrivacyDot.Margin = new Thickness(right + 7, PillTop + h / 2 - PrivacyDot.Height / 2, 0, 0);
    }

    // ---------------------------------------------------------------- mic / camera in use

    private static readonly Brush CameraBrush = FrozenBrush(Color.FromRgb(0x30, 0xD1, 0x58));
    private static readonly Brush MicrophoneBrush = FrozenBrush(Color.FromRgb(0xFF, 0x9F, 0x0A));

    private static Brush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void OnPrivacyChanged(SensorUse old, SensorUse now)
    {
        UpdatePrivacyIndicators();

        // Announce a start (not a stop), like the iPhone; camera wins when both start together.
        var cameraStarted = now.CameraApps.Except(old.CameraApps).ToList();
        var micStarted = now.MicrophoneApps.Except(old.MicrophoneApps).ToList();
        if (cameraStarted.Count > 0)
            ShowInfo(TransientPriority.Device, 340, "", "Камера активна", string.Join(", ", cameraStarted),
                glyphBrush: CameraBrush, detailBrush: CameraBrush);
        else if (micStarted.Count > 0)
            ShowInfo(TransientPriority.Device, 340, "", "Микрофон активен", string.Join(", ", micStarted),
                glyphBrush: MicrophoneBrush, detailBrush: MicrophoneBrush);
    }

    /// <summary>Dot beside the collapsed pill; chip with the app names in the expanded island's nav row.</summary>
    private void UpdatePrivacyIndicators()
    {
        var use = _privacy.Current;
        var active = use.CameraInUse || use.MicrophoneInUse || _aiPanel.MicrophoneActive;
        var brush = use.CameraInUse ? CameraBrush : MicrophoneBrush;

        PrivacyDot.Fill = brush;
        PrivacyDot.Visibility = active && (!_state.IsExpanded||_voiceFocus) ? Visibility.Visible : Visibility.Collapsed;
        PrivacyChip.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        PrivacyChipDot.Fill = brush;
        var what = use.CameraInUse && use.MicrophoneInUse ? "Камера и микрофон"
            : use.CameraInUse ? "Камера"
            : "Микрофон";
        PrivacyChipText.Text = _aiPanel.MicrophoneActive ? (_aiPanel.IsCalibrating ? "Калибровка · микрофон" : _aiPanel.IsListening ? "Хошино · слушаю команду" : "Микрофон · "+WakeProfile.SelectedPhrase) : $"{what} · {string.Join(", ", use.CameraApps.Concat(use.MicrophoneApps).Distinct())}";
        ApplyGeometry();
    }

    private static Geometry BuildPill(double w, double h, double r)
    {
        var geometry = new RectangleGeometry(new Rect((BaseWidth - w) / 2, IslandTopMargin, w, h), r, r);
        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// Flush with the top edge: square top, rounded bottom corners, and small concave "shoulders"
    /// that flare into the screen edge so it reads as part of the bezel.
    /// <para><paramref name="t"/> below 1 is mid-morph from the island (see <see cref="MorphLower"/>).</para>
    /// </summary>
    private Geometry BuildNotch(double w, double h, double r, double t)
    {
        var x = (BaseWidth - w) / 2;
        var top = IslandTopMargin * MorphLower(t);
        var bottom = top + h;
        var topRadius = r * (1 - MorphSquare(t));
        var s = Math.Min(NotchShoulder * Math.Max(Roundness, 0.25), h / 2) * MorphFlare(t);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            if (s > 0)
            {
                ctx.BeginFigure(new Point(x - s, top), isFilled: true, isClosed: true);
                ctx.QuadraticBezierTo(new Point(x, top), new Point(x, top + s), true, true);
            }
            else
            {
                ctx.BeginFigure(new Point(x, top + topRadius), isFilled: true, isClosed: true);
            }
            ctx.LineTo(new Point(x, bottom - r), true, true);
            ctx.ArcTo(new Point(x + r, bottom), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, true);
            ctx.LineTo(new Point(x + w - r, bottom), true, true);
            ctx.ArcTo(new Point(x + w, bottom - r), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, true);
            if (s > 0)
            {
                ctx.LineTo(new Point(x + w, top + s), true, true);
                ctx.QuadraticBezierTo(new Point(x + w, top), new Point(x + w + s, top), true, true);
            }
            else
            {
                ctx.LineTo(new Point(x + w, top + topRadius), true, true);
                if (topRadius > 0)
                    ctx.ArcTo(new Point(x + w - topRadius, top), new Size(topRadius, topRadius), 0, false,
                        SweepDirection.Counterclockwise, true, true);
                ctx.LineTo(new Point(x + topRadius, top), true, true);
                if (topRadius > 0)
                    ctx.ArcTo(new Point(x, top + topRadius), new Size(topRadius, topRadius), 0, false,
                        SweepDirection.Counterclockwise, true, true);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    // ---------------------------------------------------------------- Liquid Glass blur engines

    /// <summary>
    /// Picks and drives the blur engine. GPU (DWM shared visual + DirectComposition blur) is preferred:
    /// near-zero CPU and the island stays capturable. Screen capture is the fallback; it needs the
    /// island excluded from capture so the copy shows what's behind it rather than the island.
    /// </summary>
    private void UpdateGlass()
    {
        var blurAmount = EffectiveGlassBlur;
        var wantBlur = IsGlass && IsLoaded && !_gameSuppressed && blurAmount > 0.001;

        var gpu = wantBlur && !_winUiBehind && GpuBlurVerified() && EnsureGpuBlur();
        if (gpu != _gpuActive)
        {
            _gpuBlur?.SetVisible(gpu);
            if (gpu) KeepOnTop(force: true);
            _themeKey = null;
        }
        _gpuActive = gpu;
        if (gpu) UpdateGpuShape();

        // Never exclude the island from screen capture so screenshots always capture HoshiBar cleanly!
        SetExcludedFromCapture(false);
        GlassLayer.Visibility = Visibility.Collapsed;
        _glassTimer.Stop();
        GlassImage.Source = null;
        _glassBitmap = null;
    }

    private void SetExcludedFromCapture(bool exclude)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (exclude == _excludedFromCapture || hwnd == IntPtr.Zero) return;
        var ok = NativeMethods.SetWindowDisplayAffinity(hwnd,
            exclude ? NativeMethods.WDA_EXCLUDEFROMCAPTURE : NativeMethods.WDA_NONE);
        _excludedFromCapture = exclude && ok;
    }

    // ---- GPU engine

    private GpuBlurBackdrop? _gpuBlur;
    private bool _gpuActive;
    private bool _gpuFailed;
    private bool _gpuProbeRunning;
    private double _glassStrength = 1;

    /// <summary>"10.0.26200.6899": DWM changes ship in cumulative updates, so the revision is part of the key.</summary>
    private static string WindowsBuildKey()
    {
        var revision = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR", 0);
        var v = Environment.OSVersion.Version;
        return $"v4|{v.Major}.{v.Minor}.{v.Build}.{revision}"; // bump when the engine's API usage changes
    }

    /// <summary>True when the probe passed on this build; starts the probe (in a child process) if never run.</summary>
    private bool GpuBlurVerified()
    {
        if (_gpuFailed) return false;
        var key = WindowsBuildKey();
        if (_settings.GpuBlurProbe == $"{key}:ok") return true;
        if (_settings.GpuBlurProbe == $"{key}:fail") return false;
        if (!_gpuProbeRunning) RunGpuProbe(key);
        return false;
    }

    private async void RunGpuProbe(string key)
    {
        _gpuProbeRunning = true;
        var ok = await Task.Run(() =>
        {
            try
            {
                using var probe = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--probe-gpu-blur")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                if (probe is null) return false;
                if (!probe.WaitForExit(15000))
                {
                    probe.Kill();
                    return false;
                }
                return probe.ExitCode == 0;
            }
            catch (Exception)
            {
                return false;
            }
        });
        _gpuProbeRunning = false;
        _settings.GpuBlurProbe = $"{key}:{(ok ? "ok" : "fail")}";
        _settings.Save();
        UpdateGlass();
    }

    // ---------------------------------------------------------------- Energy Saver

    private bool _energySaver;

    /// <summary>
    /// Like the iPhone's Low Power Mode: while Windows' Energy Saver is on, island animations run at
    /// 30 fps instead of 60 and the music bars refresh at half rate. Everything else is unchanged.
    /// </summary>
    private int FrameRateCap => _energySaver ? 30 : 60;

    private void InitEnergySaver()
    {
        _energySaver = StatusService.EnergySaverOn;
#if DEBUG
        // Debug aid: DISLAND_ENERGYSAVER=1 runs the island's Energy Saver mode without changing Windows' setting.
        if (Environment.GetEnvironmentVariable("DISLAND_ENERGYSAVER") == "1")
        {
            _energySaver = true;
            _meterTimer.Interval = TimeSpan.FromMilliseconds(66);
            return;
        }
#endif
        try
        {
            Windows.System.Power.PowerManager.EnergySaverStatusChanged += (_, _) => Dispatcher.InvokeAsync(() =>
            {
                _energySaver = StatusService.EnergySaverOn;
                _meterTimer.Interval = TimeSpan.FromMilliseconds(_energySaver ? 66 : 33);
                if (_equalizerRunning) // restart the animated bars at the new rate
                {
                    SetEqualizerRunning(false);
                    UpdateEqualizer();
                }
            });
        }
        catch (Exception) { /* not available on this Windows */ }
        _meterTimer.Interval = TimeSpan.FromMilliseconds(_energySaver ? 66 : 33);
    }

    private bool _winUiBehind;

    /// <summary>
    /// Is the top window behind the island a WinUI 3 window (Apple Music, new Notepad, …)? The GPU
    /// engine's thumbnails of those are empty (checked with every source window and flag value), so
    /// Auto switches to the capture engine while one is there. Only matters while Liquid Glass is on.
    /// </summary>
    private void UpdateWinUiBehind()
    {
        if (!IsGlass || _settings.GlassEngine != GlassEngine.Auto) return;
        var island = new WindowInteropHelper(this).Handle;
        if (island == IntPtr.Zero || !NativeMethods.GetWindowRect(island, out var bounds)) return;

        var winUi = false;
        var className = new System.Text.StringBuilder(64);
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (hwnd == island || hwnd == _gpuBlur?.Handle || !NativeMethods.IsShownOnScreen(hwnd)) return true;
            if (!NativeMethods.GetWindowRect(hwnd, out var rect) || !NativeMethods.Intersects(rect, bounds)) return true;
            className.Clear();
            NativeMethods.GetClassName(hwnd, className, className.Capacity);
            winUi = className.ToString() == "WinUIDesktopWin32WindowClass";
            return false; // only the top one decides
        }, IntPtr.Zero);

        if (winUi == _winUiBehind) return;
        _winUiBehind = winUi;
        UpdateGlass();
    }

    private bool EnsureGpuBlur()
    {
        if (_gpuBlur is not null) return true;
        try
        {
            _gpuBlur = new GpuBlurBackdrop();
            var hwnd = new WindowInteropHelper(this).Handle;
            _gpuBlur.Exclude(hwnd);
            // Not owner/owned: changing an already-shown window's owner leaves Windows' topmost
            // bookkeeping inconsistent, and the island could end up below ordinary app windows.
            // The pair is ordered explicitly instead (see KeepOnTop).
            KeepOnTop(force: true);
            SyncGpuBounds();
            return true;
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            _gpuBlur?.Dispose();
            _gpuBlur = null;
            _gpuFailed = true; // fall back to capture for this session
            return false;
        }
    }

    /// <summary>
    /// Keeps the island at the top of the always-on-top layer, with the GPU blur window directly
    /// beneath it. Called on window events; does nothing unless another visible window actually
    /// overlaps the island from above (so it never fights over z-order needlessly).
    /// </summary>
    private void KeepOnTop(bool force = false)
    {
        if(_gameSuppressed)return;
        var island = new WindowInteropHelper(this).Handle;
        if (island == IntPtr.Zero) return;
        if (!force && !IsCoveredFromAbove(island)) return;

        const uint flags = NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE
                           | NativeMethods.SWP_NOOWNERZORDER;
        NativeMethods.SetWindowPos(island, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, flags);
        // insertAfter = island places the blur window immediately below it.
        if (_gpuBlur is not null) NativeMethods.SetWindowPos(_gpuBlur.Handle, island, 0, 0, 0, 0, flags);
    }

    /// <summary>Is any other visible window above the island in z-order and overlapping it?</summary>
    private static bool IsCoveredFromAbove(IntPtr island)
    {
        if (!NativeMethods.GetWindowRect(island, out var mine)) return false;
        for (var w = NativeMethods.GetWindow(island, NativeMethods.GW_HWNDPREV); w != IntPtr.Zero;
             w = NativeMethods.GetWindow(w, NativeMethods.GW_HWNDPREV))
        {
            if (NativeMethods.IsShownOnScreen(w) && NativeMethods.GetWindowRect(w, out var r) && NativeMethods.Intersects(r, mine))
                return true;
        }
        return false;
    }

    private void SyncGpuBounds()
    {
        if (_gpuBlur is null) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var rect)) return;
        _gpuBlur.SetBounds(rect);
        UpdateGpuShape();
    }

    /// <summary>Clip/blur/opacity of the GPU backdrop, in its window pixels. Called every animation frame.</summary>
    private void UpdateGpuShape()
    {
        if (!_gpuActive || _gpuBlur is null) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var f = dpi.DpiScaleX * Scale;
        var w = Math.Max(0, _width.Value);
        var h = Math.Max(0, _height.Value);
        var r = Math.Clamp(_radius.Value, 0, Math.Min(w, h) / 2);
        // Square top corners in notch mode: the clip extends above the window's top edge, where its
        // rounded corners are cut off. Mid-morph this only happens once the shape touches the edge.
        var extra = r * MorphSquare(NotchAmount);
        _gpuBlur.SetShape(
            (float)((BaseWidth - w) / 2 * f), (float)((PillTop - extra) * f),
            (float)(w * f), (float)((h + extra) * f), (float)(r * f),
            (float)(EffectiveGlassBlur * MaxGlassBlur * f),
            (float)_glassStrength);
    }

    /// <summary>Copies the screen behind the pill (plus room for the blur to spread) into the glass image.</summary>
    private void CaptureGlass()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var window)) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var f = dpi.DpiScaleX * Scale; // design units → screen pixels
        var pad = ((BlurEffect)GlassImage.Effect).Radius + 4;
        var w = Math.Max(0, _width.Value);
        var h = Math.Max(0, _height.Value);

        var left = Math.Max(0, (BaseWidth - w) / 2 - pad);
        var top = Math.Max(0, PillTop - pad);
        var right = Math.Min(BaseWidth, (BaseWidth + w) / 2 + pad);
        var bottom = Math.Min(Root.Height, PillTop + h + pad);

        var px = (int)Math.Floor(left * f);
        var py = (int)Math.Floor(top * f);
        var pw = (int)Math.Ceiling(right * f) - px;
        var ph = (int)Math.Ceiling(bottom * f) - py;
        // Full rate only while the island is animating; half rate is plenty for what's behind it.
        _glassTimer.Interval = TimeSpan.FromMilliseconds(_animating ? 33 : 66);
        if (!_capture.Capture(window.Left + px, window.Top + py, pw, ph)) return;

        if (_glassBitmap is null || _glassBitmap.PixelWidth != pw || _glassBitmap.PixelHeight != ph)
        {
            _glassBitmap = new WriteableBitmap(pw, ph, 96, 96, PixelFormats.Bgr32, null);
            GlassImage.Source = _glassBitmap;
        }
        _glassBitmap.WritePixels(new Int32Rect(0, 0, pw, ph), _capture.Bits, _capture.BufferSize, _capture.Stride);

        Canvas.SetLeft(GlassImage, px / f);
        Canvas.SetTop(GlassImage, py / f);
        GlassImage.Width = pw / f;
        GlassImage.Height = ph / f;
    }

    /// <summary>The island's target rectangle in screen pixels, for background detection.</summary>
    private NativeMethods.RECT? GetIslandScreenRect()
    {
        if (!IsLoaded || PresentationSource.FromVisual(this) is null) return null;
        var w = _width.Target;
        var x = (BaseWidth - w) / 2;
        var topLeft = Root.PointToScreen(new Point(x, PillTop));
        var bottomRight = Root.PointToScreen(new Point(x + w, PillTop + _height.Target));
        return new NativeMethods.RECT
        {
            Left = (int)topLeft.X,
            Top = (int)topLeft.Y,
            Right = (int)bottomRight.X,
            Bottom = (int)bottomRight.Y,
        };
    }

    // ---------------------------------------------------------------- theme & material

    /// <summary>Accent color per the user's chosen source; null means "neutral" (white bars, black background).</summary>
    private Color? ResolveAccent() => _settings.DesignPreset != "Original" ? HoshinoPalette.Accent : _settings.AccentSource switch
    {
        AccentSource.Custom => ParseColor(_settings.CustomAccent),
        AccentSource.MediaApp => _media.Current?.Accent,
        AccentSource.Monochrome => null,
        _ => null, // Wallpaper Engine integration removed
    };

    private static Color? ParseColor(string? text)
    {
        try { return ColorConverter.ConvertFromString(text ?? "") is Color c ? c : null; }
        catch (FormatException) { return null; }
    }

    /// <summary>
    /// 1 when the material should be shown in full; the "desktop opacity" when adaptive and only the
    /// wallpaper is behind (unless expanded and the user wants it solid while expanded).
    /// </summary>
    private double MaterialStrength =>
        !_settings.AdaptiveBackground || _background.AppBehind || (_settings.SolidWhenExpanded && _state.IsExpanded)
            ? 1
            : Math.Clamp(_settings.DesktopOpacity, 0, 1);

    /// <summary>Specular rim: bright top-left edge fading around to a faint bottom-right glint.</summary>
    private static Brush CreateGlassRim(double shine)
    {
        byte A(double a) => (byte)Math.Round(255 * Math.Clamp(a * shine, 0, 1));
        var rim = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        rim.GradientStops.Add(new GradientStop(Color.FromArgb(A(0.75), 0xFF, 0xFF, 0xFF), 0));
        rim.GradientStops.Add(new GradientStop(Color.FromArgb(A(0.12), 0xFF, 0xFF, 0xFF), 0.35));
        rim.GradientStops.Add(new GradientStop(Color.FromArgb(A(0.05), 0xFF, 0xFF, 0xFF), 0.65));
        rim.GradientStops.Add(new GradientStop(Color.FromArgb(A(0.4), 0xFF, 0xFF, 0xFF), 1));
        rim.Freeze();
        return rim;
    }

    /// <summary>Top sheen: a soft highlight across the upper part of the glass.</summary>
    private static Brush CreateGlassSheen(double shine)
    {
        byte A(double a) => (byte)Math.Round(255 * Math.Clamp(a * shine, 0, 1));
        var sheen = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        sheen.GradientStops.Add(new GradientStop(Color.FromArgb(A(0.22), 0xFF, 0xFF, 0xFF), 0));
        sheen.GradientStops.Add(new GradientStop(Color.FromArgb(A(0.05), 0xFF, 0xFF, 0xFF), 0.4));
        sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 0.6));
        sheen.GradientStops.Add(new GradientStop(Color.FromArgb(A(0.06), 0xFF, 0xFF, 0xFF), 1));
        sheen.Freeze();
        return sheen;
    }

    private readonly DropShadowEffect _textShadow = new()
    {
        BlurRadius = 6,
        ShadowDepth = 0,
        Opacity = 0.7,
        Color = Colors.Black,
        RenderingBias = RenderingBias.Performance,
    };

    /// <summary>
    /// Applies material (fill, blur, rim, sheen, outline, shadow) and accents. Called whenever settings,
    /// wallpaper, media app, expansion or what's behind the island changes.
    /// </summary>
    private string? _themeKey;
    private bool _useTextShadow;
    private DispatcherTimer? _shadowRestore;

    /// <summary>
    /// Puts the shadow on the visible view's text. For the expanded view that is its content panel,
    /// not the whole view: the view also holds the (possibly video) art banner, and an effect over it
    /// would re-blur on every video frame.
    /// </summary>
    private void SetTextShadow(Effect? effect)
    {
        if (_currentView is null) return;
        var target = _currentView == ExpandedView ? ExpandedContent : _currentView;
        if (target.Effect != effect) target.Effect = effect;
    }

    private void SuspendTextShadow()
    {
        SetTextShadow(null);
        if (!_useTextShadow) return;
        _shadowRestore ??= CreateShadowRestoreTimer();
        _shadowRestore.Stop();
        _shadowRestore.Interval = TimeSpan.FromMilliseconds(260 / SpeedFactor); // after the view fade-in
        _shadowRestore.Start();
    }

    private DispatcherTimer CreateShadowRestoreTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background);
        timer.Tick += (_, _) =>
        {
            if (_animating) return; // keep waiting; the spring's settle calls RestoreTextShadow
            timer.Stop();
            RestoreTextShadow();
        };
        return timer;
    }

    private void RestoreTextShadow()
    {
        var settled = !_animating && _shadowRestore?.IsEnabled != true;
        SetTextShadow(_useTextShadow && settled && !_state.IsExpanded ? _textShadow : null);
    }

    private void ApplyTheme()
    {
        var accent = ResolveAccent();
        _accent = accent;
        var spec = IslandSkinEngine.Current;
        var rawStrength = MaterialStrength;
        var strength = spec.DisableGlassBlur
            ? 1.0
            : spec.IsLight
                ? Math.Max(0.88, rawStrength)
                : spec.IsAeroGlass
                    ? Math.Max(0.76, rawStrength)
                    : rawStrength;
        var glass = IsGlass;

        // The blur engine has its own state (probe finishing, window loaded); always let it update.
        _glassStrength = strength;
        UpdateGlass();

        // ApplyTheme is called on many events (media updates, window events); skip rebuilding brushes
        // and restarting animations when nothing that affects the look has changed.
        var s = _settings;
        var key = string.Join('|', s.IslandSkin, s.DesignPreset, s.DesignBackground, s.DesignText, accent, strength, _gpuActive, IsGhostMode, s.BackgroundTint,
            s.OutlineWidth, s.OutlineOpacity, s.ShadowStrength, s.TextShadow, s.AccentSource,
            _media.Current?.Accent, _osdDimmed);
        if (key == _themeKey) return;
        _themeKey = key;

        // Sync local Window.Resources brushes so every legacy consumer adapts to dark & light skins live
        if (Resources["Secondary"] is SolidColorBrush secBrush && !secBrush.IsFrozen) secBrush.Color = spec.Muted;
        if (Resources["ArtPlaceholder"] is SolidColorBrush artPh && !artPh.IsFrozen)
            artPh.Color = spec.IsLight ? IslandSkinPresets.Mix(spec.Control, spec.Border, 0.35) : Color.FromRgb(0x2B, 0x26, 0x35);
        if (Resources["Orange"] is SolidColorBrush orangeBrush && !orangeBrush.IsFrozen)
            orangeBrush.Color = spec.IsLight ? Color.FromRgb(0xD9, 0x8A, 0x18) : spec.Gold;
        if (Resources["Green"] is SolidColorBrush greenBrush && !greenBrush.IsFrozen)
            greenBrush.Color = spec.Accent;
        if (Resources["Cyan"] is SolidColorBrush cyanBrush && !cyanBrush.IsFrozen)
            cyanBrush.Color = spec.IsLight && spec.IsShittimTablet ? Color.FromRgb(0x1D, 0x9B, 0xF0) : spec.SecondaryAccent;

        // Fixed per-theme body brush: blends over live GPU Gaussian blur when _gpuActive is ready,
        // or falls back to higher body opacity when GPU blur is inactive (e.g. Ghost Mode / Energy Saver).
        var tint = spec.Skin == IslandSkin.HorusGlass ? Math.Clamp(_settings.BackgroundTint, 0, 0.45) : 0;
        var effectiveStrength = Math.Max(0.84, strength);
        var bodyAlphaScale = spec.DisableGlassBlur
            ? 1.0
            : _gpuActive
                ? effectiveStrength
                : spec.IsAeroGlass ? 0.92 : 0.95;
        PillShape.Fill = IslandSkinEngine.CreateIslandBodyBrush(spec, accent, tint, bodyAlphaScale);
        NotchTopSeal.Fill = PillShape.Fill;
        GlassLayer.Visibility = Visibility.Collapsed;
        GlassSheen.Visibility = Visibility.Collapsed;

        // Compact LCD segment foregrounds in Pixel Tamagotchi vs normal HoshinoText
        var compactTextBrush = spec.IsPixelMode
            ? IslandSkinEngine.Frozen(Color.FromRgb(0x14, 0x2D, 0x2A))
            : (Brush)FindResource("HoshinoText");
        ClockText.Foreground = compactTextBrush;
        ClockWeatherTemp.Foreground = compactTextBrush;
        MediaTitleSmall.Foreground = compactTextBrush;
        CompactLyric.Foreground = compactTextBrush;
        TimerTextSmall.Foreground = compactTextBrush;
        TimerTextLarge.Foreground = (Brush)FindResource("HoshinoText");
        OsdPercent.Foreground = compactTextBrush;

        // Crisp subtle shadow on Aero white text; disabled on light skins to avoid muddy gray halos
        _useTextShadow = spec.IsAeroGlass;
        RestoreTextShadow();

        // Keep PillShape.Stroke empty so Notch never paints a line across y = 0; IslandRimPath draws open-top Notch & heterochromatic Island contours!
        PillShape.Stroke = null;
        PillShape.StrokeThickness = 0;

        var extraOutline = Math.Clamp(_settings.OutlineWidth, 0, 4);
        IslandRimPath.Stroke = IslandSkinEngine.CreateRimBrush(spec);
        IslandRimPath.StrokeThickness = Math.Max(spec.RimThickness, extraOutline);
        IslandRimPath.StrokeDashArray = IsGhostMode ? new DoubleCollection { 4, 3 } : null;
        IslandRimPath.Opacity = spec.IsCartoonSticker || spec.IsPixelMode ? 1.0 : Math.Max(0.88, effectiveStrength);

        // Shadow: Chibi Pop uses pure vector PillCartoonShadow Path; other skins get soft drop shadow
        if (spec.IsCartoonSticker)
        {
            PillShape.Effect = null;
        }
        else
        {
            var shadow = Math.Max(spec.IsLight ? 0.18 : 0.22, Math.Clamp(_settings.ShadowStrength, 0, 1)) * effectiveStrength;
            _shadow.Opacity = shadow;
            PillShape.Effect = shadow > 0.01 ? _shadow : null;
        }

        // Progress & OSD fills use skin-aware HoshinoProgressBrush
        var progressBrush = (Brush)Application.Current.Resources["HoshinoProgressBrush"];
        ProgressFill.Background = progressBrush;
        if (!_osdDimmed) OsdFill.Background = progressBrush;

        // 4-band heterochromatic / stepped pixel equalizer bars
        var eqBrushes = IslandSkinEngine.CreateEqualizerBrushes(spec);
        var barIdx = 0;
        foreach (var bar in Equalizer.Children.OfType<Rectangle>())
        {
            bar.Fill = eqBrushes[barIdx % eqBrushes.Length];
            bar.RadiusX = bar.RadiusY = spec.IsPixelMode ? 0 : 1.5;
            bar.Width = spec.IsPixelMode ? 4.5 : 3.5;
            barIdx++;
        }
        MediaAppDot.Fill = eqBrushes[1];
    }

    // ---------------------------------------------------------------- hover / click

    private void OnRootMouseEnter(object sender, MouseEventArgs e)
    {
        if(_voiceFocus)return;
        if (_settings.ExpandOnHover && !_state.IsExpanded && !_hoverSuppressed) _hoverTimer.Start();
    }

    private void OnRootMouseLeave(object sender, MouseEventArgs e)
    {
        _hoverTimer.Stop();
        _hoverSuppressed = false;
    }

    private void OnRootMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_voiceFocus) { e.Handled = true; return; }
        _hoverTimer.Stop();

        if (!_state.IsExpanded && _currentView == TimerDoneView && _state.Transient == IslandView.TimerDone)
        {
            _state.DismissTransient(); // also silences a ringing alarm
            return;
        }

        // Like tapping on iPhone: a click on the collapsed music activity (or its bubble) opens the
        // music app instead of expanding; hovering still expands.
        if (!_state.IsExpanded && _settings.ClickOpensMediaApp && _state.Transient is null
            && ((_currentView == MediaView && e.OriginalSource is DependencyObject source && !IsInside(source, ActivityBubble))
                || (_state.SecondaryView == IslandView.Media && e.OriginalSource is DependencyObject b && IsInside(b, ActivityBubble))))
        {
            _hoverSuppressed = true;
            OpenMediaApp();
            return;
        }

        if (_state.IsExpanded)
        {
            // Don't let the hover timer immediately re-open what the user just closed.
            _hoverSuppressed = true;
            SetExpanded(false);
        }
        else
        {
            SetExpanded(true);
        }
    }

    private static bool IsInside(DependencyObject element, DependencyObject container)
    {
        for (var d = element; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d == container) return true;
        return false;
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        _hoverTimer.Stop();
        SetExpanded(false);
        SettingsRequested?.Invoke();
    }

    private void SetExpanded(bool expanded)
    {
        if(!expanded)_voiceChatOpen=false;
        _msOutside = 0;
        if (!expanded) { _aiPanel.StopListening(); EndTyping(restoreFocus: true); if(_state.IsExpanded) ShowPage(ExpandedPage.Home, animate:false); }
        if (expanded)
        {
            if (_currentView == LyricsView) ShowPage(ExpandedPage.Home, animate: false);
            _leavePoll.Start();
            _progressTimer.Start();
        }
        else
        {
            _leavePoll.Stop();
            _progressTimer.Stop();
        }
        var changed = _state.IsExpanded != expanded;
        if(changed && !expanded)NextPhrase();
        // Opening while (or just after) an alert is shown goes to that alert's page.
        if (expanded && changed && _alertPage is { } alertPage && DateTime.Now < _alertPageUntil
            && AvailablePages().Contains(alertPage))
            ShowPage(alertPage, animate: false);
        _alertPage = null;
        if (!expanded && changed && (_calendarSelected is not null || _calendarMonth != new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)))
        {
            // Next time the calendar opens on this month's upcoming list.
            _calendarSelected = null;
            _calendarMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            BuildCalendar();
        }
        _state.IsExpanded = expanded;
        if (changed) UpdatePrivacyIndicators(); // the dot hides while expanded (the chip shows instead)
        UpdateMotionPlayback();
        if (changed && _settings.SolidWhenExpanded && _settings.AdaptiveBackground) ApplyTheme();
        UpdateGameMode();
    }

    /// <summary>
    /// The window is mostly transparent click-through space, so WM_MOUSELEAVE isn't always reliable;
    /// poll the cursor against the pill's target rectangle while expanded.
    /// </summary>
    private void OnLeavePoll(object? sender, EventArgs e)
    {
        // Typing in a note or scanning an open QR bridge: stay open wherever the mouse goes.
        if (_typing || _page == ExpandedPage.Ai || (_page == ExpandedPage.Shelf && _qrPanel.IsOpen) || IsCursorOverPill())
        {
            _msOutside = 0;
            return;
        }

        _msOutside += LeavePollMs;
        if (_msOutside >= _settings.CollapseDelayMs) SetExpanded(false);
    }

    private bool IsCursorOverPill()
    {
        if (!NativeMethods.GetCursorPos(out var pt)) return true;
        var p = Root.PointFromScreen(new Point(pt.X, pt.Y)); // design units, scale and DPI undone
        var w = _width.Target;
        var x = (BaseWidth - w) / 2;
        const double slop = 6;
        return p.X >= x - slop && p.X <= x + w + slop && p.Y >= 0 && p.Y <= PillTop + _height.Target + slop;
    }

    // ---------------------------------------------------------------- to-do and notes

    private void InitNotes()
    {
        _notes.Changed += BuildTodoList;
        NoteBox.Text = _notes.Note;
        UpdateNoteStatus();
        BuildTodoList();
        Deactivated += (_, _) => EndTyping(restoreFocus: false); // clicked into another app
    }

    private void BuildTodoList()
    {
        TodoList.Children.Clear();
        var secondary = (Brush)FindResource("Secondary");
        var green = (Brush)FindResource("Green");
        foreach (var item in _notes.Todos)
        {
            var id = item.Id;
            var row = new Grid { Margin = new Thickness(0, 1, 0, 1), Background = Brushes.Transparent };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Check circle: an outline when open, a filled green tick when done.
            var check = new Button
            {
                Style = (Style)FindResource("IconButton"),
                Width = 30,
                Height = 30,
                VerticalAlignment = VerticalAlignment.Top,
                ToolTip = item.Done ? "Вернуть в работу" : "Отметить выполненным",
                Content = item.Done
                    ? new Grid
                    {
                        Children =
                        {
                            new Ellipse { Width = 18, Height = 18, Fill = green },
                            new TextBlock { Style = (Style)FindResource("Glyph"), Text = "", FontSize = 10, Foreground = Brushes.Black },
                        },
                    }
                    : new Ellipse { Width = 18, Height = 18, Stroke = secondary, StrokeThickness = 1.5 },
            };
            var done = !item.Done;
            check.Click += (_, _) => _notes.SetDone(id, done);
            row.Children.Add(check);

            var text = new TextBlock
            {
                Text = item.Text,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(6, 6, 6, 6),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = item.Done ? secondary : (Brush)FindResource("HoshinoText"),
                TextDecorations = item.Done ? TextDecorations.Strikethrough : null,
            };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            // Delete appears on hover, so the list stays calm.
            var delete = new Button
            {
                Style = (Style)FindResource("IconButton"),
                Width = 28,
                Height = 28,
                Opacity = 0,
                VerticalAlignment = VerticalAlignment.Top,
                ToolTip = "Удалить задачу",
                Content = new TextBlock { Style = (Style)FindResource("Glyph"), Text = "", FontSize = 10, Foreground = secondary },
            };
            delete.Click += (_, _) => _notes.Remove(id);
            row.MouseEnter += (_, _) => delete.Opacity = 1;
            row.MouseLeave += (_, _) => delete.Opacity = 0;
            Grid.SetColumn(delete, 2);
            row.Children.Add(delete);
            TodoList.Children.Add(row);
        }

        var open = _notes.OpenCount;
        var finished = _notes.DoneCount;
        TodoEmpty.Visibility = open == 0 && finished == 0 ? Visibility.Visible : Visibility.Collapsed;
        TodoFooter.Visibility = open + finished > 0 ? Visibility.Visible : Visibility.Collapsed;
        TodoCount.Text = open == 0 ? "Всё выполнено" : finished == 0 ? $"Осталось: {open}" : $"Осталось: {open} · Готово: {finished}";
        TodoClearDone.Visibility = finished > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (IsLoaded && _state.IsExpanded && _page == ExpandedPage.Todo) Render(); // height may change
    }

    private void OnTodoInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (_notes.Add(TodoInput.Text))
            {
                TodoInput.Clear();
                TodoScroller.ScrollToEnd(); // new tasks go to the end of the open ones
            }
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            EndTyping(restoreFocus: true);
        }
    }

    private void OnTodoInputChanged(object sender, TextChangedEventArgs e) =>
        TodoPlaceholder.Visibility = TodoInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnTodoClearDoneClick(object sender, RoutedEventArgs e) => _notes.ClearDone();

    private void OnNoteChanged(object sender, TextChangedEventArgs e)
    {
        _notes.Note = NoteBox.Text;
        UpdateNoteStatus();
    }

    private void UpdateNoteStatus()
    {
        var length = NoteBox.Text.Length;
        NotePlaceholder.Visibility = length == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoteStatus.Text = length == 0 ? "Сохраняется автоматически на этом ПК" : $"{length:N0} симв. · сохранено локально";
        ResetNoteClear();
    }

    private void OnNoteKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        EndTyping(restoreFocus: true);
    }

    private void OnNoteCopyClick(object sender, RoutedEventArgs e)
    {
        if (NoteBox.Text.Length == 0) return;
        try { Clipboard.SetText(NoteBox.Text); }
        catch (Exception) { return; /* clipboard busy */ }
        ShowInfo(TransientPriority.Clipboard, 300, "", "Заметка скопирована", null, seconds: 1.5);
    }

    // Clearing asks for a second click within 3 seconds.
    private DispatcherTimer? _noteClearArm;

    private void OnNoteClearClick(object sender, RoutedEventArgs e)
    {
        if (NoteBox.Text.Length == 0) return;
        if (_noteClearArm is null)
        {
            NoteClearButton.Content = "Нажми ещё раз";
            _noteClearArm = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _noteClearArm.Tick += (_, _) => ResetNoteClear();
            _noteClearArm.Start();
            return;
        }
        NoteBox.Clear(); // Ctrl+Z in the note brings it back while typing continues
        _notes.Flush();
        ResetNoteClear();
    }

    private void ResetNoteClear()
    {
        _noteClearArm?.Stop();
        _noteClearArm = null;
        NoteClearButton.Content = "Очистить";
    }

    // Clicking a field's padding (not just the text box itself) starts typing too.
    private void OnTodoFieldMouseDown(object sender, MouseButtonEventArgs e)
    {
        BeginTyping(TodoInput);
        e.Handled = true;
    }

    private void OnNoteFieldMouseDown(object sender, MouseButtonEventArgs e)
    {
        BeginTyping(NoteBox);
        NoteBox.CaretIndex = NoteBox.Text.Length;
        e.Handled = true;
    }

    private void OnTypingBoxMouseDown(object sender, MouseButtonEventArgs e) => BeginTyping((TextBox)sender);

    /// <summary>Clicks on these pages don't collapse the island (Root's click handler would).</summary>
    private void OnTypingPageMouseUp(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private bool _typing;
    private IntPtr _foregroundBeforeTyping;

    /// <summary>
    /// The island never takes the keyboard (WS_EX_NOACTIVATE), so clicking it doesn't pull focus from
    /// the user's app. To type into a note it becomes a normal, activatable window while a text box is
    /// in use, then gives the keyboard back to the app that had it (Esc, collapsing, hiding), or simply
    /// lets go when the user clicks into another app.
    /// </summary>
    private void BeginTyping(TextBox box)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        if (!_typing)
        {
            _typing = true;
            var foreground = NativeMethods.GetForegroundWindow();
            _foregroundBeforeTyping = foreground == hwnd ? IntPtr.Zero : foreground;
            NativeMethods.SetExStyle(hwnd, NativeMethods.GetExStyle(hwnd) & ~NativeMethods.WS_EX_NOACTIVATE);
            _hoverTimer.Stop();
        }
        // Allowed: this process just received the mouse click (Windows' foreground rules).
        NativeMethods.SetForegroundWindow(hwnd);
        Activate();
        box.Focus();
        Keyboard.Focus(box);
    }

    private void EndTyping(bool restoreFocus)
    {
        if (!_typing) return;
        _typing = false;
        _notes.Flush();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        FocusManager.SetFocusedElement(this, null);
        Keyboard.ClearFocus();
        NativeMethods.SetExStyle(hwnd, NativeMethods.GetExStyle(hwnd) | NativeMethods.WS_EX_NOACTIVATE);
        if (restoreFocus && NativeMethods.GetForegroundWindow() == hwnd
            && _foregroundBeforeTyping != IntPtr.Zero && NativeMethods.IsWindow(_foregroundBeforeTyping))
            NativeMethods.SetForegroundWindow(_foregroundBeforeTyping);
        _foregroundBeforeTyping = IntPtr.Zero;
        KeepOnTop(); // activation can reorder the topmost band
    }

    // ---------------------------------------------------------------- clock

    private string? _clockWeatherKey;
    private string? _clockWeatherIconKey;

    private void UpdateClock()
    {
        var now = DateTime.Now;
        var seconds = _settings.ShowSeconds;

        // Wake exactly at the next second/minute boundary instead of every second: without seconds
        // shown, the clock does one update per minute.
        var next = seconds
            ? now.AddTicks(TimeSpan.TicksPerSecond - now.Ticks % TimeSpan.TicksPerSecond)
            : now.AddTicks(TimeSpan.TicksPerMinute - now.Ticks % TimeSpan.TicksPerMinute);
        _clockTimer.Interval = next - now + TimeSpan.FromMilliseconds(15);

        var time = _settings.ClockFormat switch
        {
            ClockFormat.TwelveHour => now.ToString(seconds ? "h:mm:ss tt" : "h:mm tt"),
            ClockFormat.TwentyFourHour => now.ToString(seconds ? "HH:mm:ss" : "HH:mm"),
            _ => now.ToString(seconds ? "T" : "t"),
        };

        var minimal = _settings.IdleStyle == IdleStyle.Minimal;
        var hasCompanion = _settings.ShowHoshino;
        var idleText = _settings.ShowDateWhenIdle ? $"{now:ddd d MMM}   {time}" : time;
        var weather = _settings.WeatherEnabled && _settings.WeatherInIdlePill ? _weather.Current : null;
        var iconKey = weather is null ? null : $"{weather.Kind}|{weather.IsDay}|{_settings.IslandSkin}";
        var signedTemp = weather is null ? "" : SignedDegrees(weather.Temperature);
        var weatherKey = $"{hasCompanion}|{_settings.IslandSkin}|{iconKey}|{signedTemp}";
        var visibility = minimal ? Visibility.Collapsed : Visibility.Visible;
        if (idleText == ClockText.Text && visibility == ClockRow.Visibility && time == BigTime.Text
            && weatherKey == _clockWeatherKey)
            return;

        ClockRow.Visibility = visibility;
        ClockText.Text = idleText;
        BigTime.Text = time;
        DateText.Text = now.ToString("dddd, d MMMM");

        // "◆ icon +14°" after the time; the icon is rebuilt when condition or active skin changes.
        if (weatherKey != _clockWeatherKey)
        {
            if (weather is not null && iconKey != _clockWeatherIconKey)
                ClockWeatherIcon.Child = WeatherIcons.Create(weather.Kind, weather.IsDay, 19);
            _clockWeatherIconKey = iconKey;
            _clockWeatherKey = weatherKey;
            ClockWeatherTemp.Text = signedTemp;
            ClockWeather.Visibility = weather is null ? Visibility.Collapsed : Visibility.Visible;
        }

        // Size the idle pill cleanly around both CompactHoshinoHost (left) and ClockRow (right) with generous breathing room.
        var isPixel = IslandSkinEngine.Current.IsPixelMode;
        var targetIdleHeight = isPixel ? 46.0 : 44.0;
        double width, height;
        if (minimal)
        {
            (width, height) = (hasCompanion ? 128 : 108, hasCompanion ? targetIdleHeight : 28);
        }
        else
        {
            var padL = hasCompanion ? 50 : 20;
            const double padR = 20;
            ClockRow.Margin = new Thickness(padL, 0, padR, 0);
            ClockRow.InvalidateMeasure();
            ClockRow.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            (width, height) = (Math.Max(hasCompanion ? 168 : 132, Math.Ceiling(ClockRow.DesiredSize.Width) + 8), targetIdleHeight);
        }

        if (Math.Abs(ClockView.Width - width) < 0.5 && Math.Abs(ClockView.Height - height) < 0.5) return;
        ClockView.Width = width;
        ClockView.Height = height;
        if (IsLoaded && _currentView == ClockView && !_state.IsExpanded) Render();
    }

    // ---------------------------------------------------------------- volume / brightness OSD

    private double _osdLevel = 0.5;

    private void ShowVolume(float level, bool muted) =>
        ShowOsd(IslandView.Volume, level, isVolume: true, dimmed: muted);

    private void ShowBrightness(int level) =>
        ShowOsd(IslandView.Brightness, level / 100.0, isVolume: false, dimmed: false);

    private void ShowOsd(IslandView kind, double level, bool isVolume, bool dimmed)
    {
        level = Math.Clamp(level, 0, 1);
        _osdLevel = level;
        _osdDimmed = dimmed;

        var spec = IslandSkinEngine.Current;
        OsdIconHost.Content = IslandChromeRenderer.CreateOsdVectorIcon(isVolume, (float)level, dimmed, spec);
        OsdPercent.Text = $"{(int)Math.Round(level * 100)}%";
        OsdFill.Background = dimmed
            ? (Brush)FindResource("Secondary")
            : (Brush)Application.Current.Resources["HoshinoProgressBrush"];

        UpdateOsdBarWidth(animate: _currentView == OsdView && !_settings.ReduceMotion);
        _state.ShowTransient(kind, TransientPriority.Osd, TimeSpan.FromSeconds(_settings.OsdSeconds));
    }

    private void UpdateOsdBarWidth(bool animate = false)
    {
        var innerW = OsdTrackBorder.ActualWidth - OsdTrackBorder.BorderThickness.Left - OsdTrackBorder.BorderThickness.Right;
        if (innerW <= 1) innerW = 200;
        var targetW = Math.Max(0, innerW * _osdLevel);
        if (animate)
        {
            OsdFill.BeginAnimation(WidthProperty,
                new DoubleAnimation(targetW, TimeSpan.FromMilliseconds(130)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        else
        {
            OsdFill.BeginAnimation(WidthProperty, null);
            OsdFill.Width = targetW;
        }
    }

    private void OnVolumeKey(int vk)
    {
        switch (vk)
        {
            case NativeMethods.VK_VOLUME_UP: _volume.StepUp(); break;
            case NativeMethods.VK_VOLUME_DOWN: _volume.StepDown(); break;
            case NativeMethods.VK_VOLUME_MUTE: _volume.ToggleMute(); break;
        }
        // Show even when nothing changed (e.g. already at 100%), like the native flyout does.
        ShowVolume(_volume.Level, _volume.Muted);
    }

    // ---------------------------------------------------------------- battery

    private const double BatteryFillMax = 22;

    private void OnBatteryAlert(BatteryAlert alert, int percent)
    {
        var green = (Brush)FindResource("Green");
        var orange = (Brush)FindResource("Orange");
        var red = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));
        var low = percent <= BatteryService.LowThreshold;

        var (title, accent, priority) = alert switch
        {
            BatteryAlert.PluggedIn => ("Зарядка", green, TransientPriority.Battery),
            BatteryAlert.Unplugged => ("От батареи", low ? red : (Brush)FindResource("HoshinoText"), TransientPriority.Battery),
            BatteryAlert.Low => ("Низкий заряд", orange, TransientPriority.LowBattery),
            _ => ("Критический заряд", red, TransientPriority.LowBattery),
        };

        BatteryTitle.Text = title;
        BatteryBolt.Visibility = alert == BatteryAlert.PluggedIn ? Visibility.Visible : Visibility.Collapsed;
        BatteryPercent.Text = $"{percent}%";
        BatteryPercent.Foreground = accent;
        BatteryFill.Background = accent;

        var target = Math.Max(2, BatteryFillMax * percent / 100.0);
        if (alert == BatteryAlert.PluggedIn)
        {
            // Fill sweeps up to the current level as the pill opens.
            BatteryFill.BeginAnimation(WidthProperty, new DoubleAnimation(2, target, TimeSpan.FromMilliseconds(700))
            {
                BeginTime = TimeSpan.FromMilliseconds(150),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        }
        else
        {
            BatteryFill.BeginAnimation(WidthProperty, null);
            BatteryFill.Width = target;
        }

        _state.ShowTransient(IslandView.Battery, priority, TimeSpan.FromSeconds(_settings.AlertSeconds));
    }

    // ---------------------------------------------------------------- info alerts

    /// <summary>Fills in and shows the shared info pill, unless a higher-priority alert is on screen.</summary>
    private void ShowInfo(int priority, double width, string glyph, string title, string? detail,
        Brush? detailBrush = null, Brush? glyphBrush = null, double? seconds = null)
    {
        // Hidden: nothing to show, and animating an invisible window would only cost CPU.
        if (IslandHidden || !_state.CanShowTransient(priority)) return;
        InfoGlyph.Text = glyph;
        InfoGlyph.Foreground = glyphBrush ?? (Brush)FindResource("HoshinoText");
        InfoTitle.Text = title;
        InfoDetail.Text = detail ?? "";
        InfoDetail.Foreground = detailBrush ?? (Brush)FindResource("Secondary");
        InfoView.Width = width;
        _state.ShowTransient(IslandView.Info, priority, TimeSpan.FromSeconds(seconds ?? _settings.AlertSeconds));
    }

    private void OnLockToggled(LockKey key, bool on) =>
        ShowInfo(TransientPriority.LockKey, 230,
            "", // keyboard
            key == LockKey.CapsLock ? "Caps Lock" : "Num Lock",
            on ? "Вкл" : "Выкл",
            detailBrush: on ? (Brush)FindResource("Green") : null,
            seconds: Math.Min(_settings.AlertSeconds, 1.5));

    private void OnFocusChanged(bool on) =>
        ShowInfo(TransientPriority.Focus, 280, "", "Не беспокоить", on ? "Вкл" : "Выкл",
            detailBrush: on ? new SolidColorBrush(Color.FromRgb(0xBF, 0x5A, 0xF2)) : null,
            glyphBrush: on ? new SolidColorBrush(Color.FromRgb(0xBF, 0x5A, 0xF2)) : null);

    private void OnCopied(ClipboardEvent copy)
    {
        _lastCopy = copy;
        if (_typing) return; // copying inside the island's own note: no alert over it
        var (glyph, detail) = copy.Kind switch
        {
            ClipboardKind.Files => ("", copy.Preview),
            ClipboardKind.Image => ("", "Изображение"),
            ClipboardKind.Text => ("", copy.Preview),
            _ => ("", null),
        };
        ShowInfo(TransientPriority.Clipboard, 340, glyph, "Скопировано", detail, seconds: Math.Min(_settings.AlertSeconds, 1.5));
    }

    private void OnPasteShortcut()
    {
        if (!_typing) ShowInfo(TransientPriority.Clipboard, 340, "", "Вставлено",
            _lastCopy?.Kind == ClipboardKind.Image ? "Изображение" : _lastCopy?.Preview,
            seconds: Math.Min(_settings.AlertSeconds, 1.5));
    }

    private void OnDeviceChanged(DeviceEvent device)
    {
        var glyph = device.Kind switch
        {
            DeviceKind.Headphones => "",
            DeviceKind.Speaker => "",
            DeviceKind.Mouse => "",
            DeviceKind.Keyboard => "",
            DeviceKind.GameController => "",
            DeviceKind.Phone => "",
            DeviceKind.Storage => "",
            DeviceKind.Monitor => "",
            DeviceKind.Camera => "",
            _ => "",
        };
        var secondary = (Brush)FindResource("Secondary");
        var status = device.Connected ? device.Battery is { } battery ? $"Подключено · {battery}%" : "Подключено" : "Отключено";
        ShowInfo(TransientPriority.Device, 340, glyph, device.Name, status,
            detailBrush: device.Connected ? (Brush)FindResource("Green") : secondary,
            glyphBrush: device.Connected ? null : secondary);
    }

    // ---------------------------------------------------------------- notifications & calendar

    private ExpandedPage? _alertPage;
    private DateTime _alertPageUntil;

    /// <summary>Clicking / hovering the island soon after an alert opens the alert's page.</summary>
    private void SetAlertPage(ExpandedPage page, double seconds)
    {
        if (_state.IsExpanded) return;
        _alertPage = page;
        _alertPageUntil = DateTime.Now.AddSeconds(seconds + 3);
    }

    private void UpdateNotificationAndCalendarServices()
    {
        if (!IsLoaded) return;
#if DEBUG
        var skip = Environment.GetEnvironmentVariable("DISLAND_SKIP") ?? "";
        if (!skip.Contains("notif-only"))
#endif
        if (_settings.NotificationsEnabled) _notifications.Start();
        else _notifications.Stop();
#if DEBUG
        if (skip.Contains("calendar-only")) return;
#endif

        if (!_settings.CalendarEnabled)
        {
            _calendar.Stop();
            _calendarKey = null;
            return;
        }
        var key = $"{string.Join('\n', _settings.CalendarIcsUrls)}|{_settings.WindowsCalendar}|{_settings.CalendarReminderMinutes}";
        if (key == _calendarKey) return;
        var first = _calendarKey is null;
        _calendarKey = key;
        if (first) _calendar.Start();
        else _calendar.Refresh();
    }

    private string? _calendarKey;

    private void OnNotificationArrived(IslandNotification n)
    {
        // Windows holds toasts back during Do Not Disturb; so does the island.
        if (!_settings.NotificationsEnabled || IslandHidden || _focus.IsOn) return;
        if (!_state.CanShowTransient(TransientPriority.Notification)) return;

        NotifIcon.Background = n.AppIcon is { } icon
            ? new ImageBrush(icon) { Stretch = Stretch.UniformToFill }
            : (Brush)FindResource("ArtPlaceholder");
        var hide = _settings.NotificationsHideText;
        NotifTitle.Text = hide ? n.AppName : n.Title;
        NotifBody.Text = hide ? "Новое уведомление" : FirstLine(n.Body);
        NotifBody.Visibility = NotifBody.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        NotifApp.Text = hide ? "сейчас" : n.AppName;
        var seconds = Math.Clamp(_settings.NotificationSeconds, 2, 15);
        SetAlertPage(ExpandedPage.Notifications, seconds);
        if (_state.ShowTransient(IslandView.Notification, TransientPriority.Notification, TimeSpan.FromSeconds(seconds)))
            _lastRenderedNotificationId = n.Id;
    }

    private static string FirstLine(string text) => text.Split('\n', 2)[0].Trim();

    private static string TimeAgo(DateTimeOffset time)
    {
        var age = DateTimeOffset.Now - time;
        return age.TotalMinutes < 1 ? "сейчас"
            : age.TotalHours < 1 ? $"{(int)age.TotalMinutes} м"
            : age.TotalDays < 1 ? $"{(int)age.TotalHours} ч"
            : time.LocalDateTime.ToString("ddd");
    }

    private void OnNotificationsClearClick(object sender, RoutedEventArgs e) => _notifications.ClearAll();

    private void OnIdentitySetupClick(object sender, RoutedEventArgs e)
    {
        SetExpanded(false);
        PackageIdentity.RunSetup();
    }

    /// <summary>The Notifications page: newest first; click opens the app, ✕ dismisses.</summary>
    private void BuildNotificationList()
    {
        var items = _notifications.Items;
        var secondary = (Brush)FindResource("Secondary");
        NotificationList.Children.Clear();
        NotificationsToolbar.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NotificationsScroller.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NotificationsCount.Text = $"Уведомлений: {items.Count}";
        NotificationsSetup.Visibility = PackageIdentity.HasIdentity ? Visibility.Collapsed : Visibility.Visible;
        NotificationsEmpty.Text = _notifications.Problem ?? (items.Count == 0 ? "Новых уведомлений нет" : "");
        NotificationsEmpty.Visibility = NotificationsEmpty.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var n in items)
        {
            var dismiss = new Button
            {
                Style = (Style)FindResource("IconButton"),
                Width = 24,
                Height = 24,
                VerticalAlignment = VerticalAlignment.Top,
                ToolTip = "Скрыть",
                Content = new TextBlock { Style = (Style)FindResource("Glyph"), Text = "", FontSize = 9, Foreground = secondary },
            };
            var id = n.Id;
            dismiss.Click += (_, _) => _notifications.Dismiss(id);

            var text = new StackPanel { Margin = new Thickness(10, 0, 6, 0) };
            text.Children.Add(new TextBlock
            {
                Text = $"{n.AppName} · {TimeAgo(n.Time)}",
                FontSize = 11,
                Foreground = secondary,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            if (!_settings.NotificationsHideText)
            {
                text.Children.Add(new TextBlock { Text = n.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                if (n.Body.Length > 0)
                    text.Children.Add(new TextBlock
                    {
                        Text = n.Body,
                        FontSize = 12,
                        Foreground = secondary,
                        TextWrapping = TextWrapping.Wrap,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxHeight = 34, // two lines
                    });
            }

            var row = new Grid
            {
                Margin = new Thickness(0, 0, 0, 10),
                Background = Brushes.Transparent,
                Cursor = n.AppId is { Length: > 0 } ? Cursors.Hand : null,
                ToolTip = n.AppId is { Length: > 0 } ? $"Открыть {n.AppName}" : null,
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(7),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 0, 0),
                Background = n.AppIcon is { } image ? new ImageBrush(image) { Stretch = Stretch.UniformToFill } : (Brush)FindResource("ArtPlaceholder"),
            };
            Grid.SetColumn(text, 1);
            Grid.SetColumn(dismiss, 2);
            row.Children.Add(icon);
            row.Children.Add(text);
            row.Children.Add(dismiss);
            row.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (n.AppId is not { Length: > 0 } appId) return;
                _hoverSuppressed = true;
                SetExpanded(false);
                QuickAppsService.Launch(appId);
            };
            NotificationList.Children.Add(row);
        }

        if (IsLoaded && _state.IsExpanded && _page == ExpandedPage.Notifications) Render();
    }

    private void OnCalendarReminder(CalendarItem item)
    {
        if (IslandHidden) return;
        var minutes = Math.Max(1, (int)Math.Round((item.Start - DateTime.Now).TotalMinutes));
        SetAlertPage(ExpandedPage.Calendar, _settings.AlertSeconds + 3);
        var color = item.Color is { } c ? new SolidColorBrush(c) : (Brush)FindResource("Orange");
        ShowInfo(TransientPriority.Calendar, 340, "", item.Title, $"через {minutes} мин",
            detailBrush: color, glyphBrush: color, seconds: _settings.AlertSeconds + 3);
    }

    private DateTime _calendarMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime? _calendarSelected;

    /// <summary>Whether the event covers any part of <paramref name="day"/> (all-day ends are exclusive).</summary>
    private static bool OccursOn(CalendarItem item, DateTime day) =>
        item.Start < day.AddDays(1) && (item.End > day || item.Start.Date == day);

    private void OnCalendarPrevMonth(object sender, RoutedEventArgs e) => ShiftCalendarMonth(-1);
    private void OnCalendarNextMonth(object sender, RoutedEventArgs e) => ShiftCalendarMonth(1);

    private void ShiftCalendarMonth(int months)
    {
        _calendarMonth = _calendarMonth.AddMonths(months);
        BuildCalendar();
    }

    /// <summary>
    /// The month grid: weekday initials (in the culture's week order), the month's dates with the
    /// neighbouring months' days dimmed, today filled with the accent, a dot under days with events.
    /// Clicking a day lists its events; clicking it again goes back to the upcoming list.
    /// </summary>
    private void BuildMonth()
    {
        var culture = CultureInfo.CurrentCulture.DateTimeFormat;
        var secondary = (Brush)FindResource("Secondary");
        var accent = _accent is { } a ? new SolidColorBrush(a) : (Brush)FindResource("Orange");
        var today = DateTime.Today;
        var month = _calendarMonth;
        CalendarMonthTitle.Text = month.ToString(month.Year == today.Year ? "MMMM" : "MMMM yyyy");

        var firstDay = culture.FirstDayOfWeek;
        if (CalendarWeekdays.Children.Count == 0)
        {
            for (var i = 0; i < 7; i++)
                CalendarWeekdays.Children.Add(new TextBlock
                {
                    Text = culture.ShortestDayNames[((int)firstDay + i) % 7],
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = secondary,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 3),
                });
        }

        // Days that have events (multi-day events mark every day they cover).
        var busy = new HashSet<DateTime>();
        foreach (var item in _calendar.Items)
        {
            var last = (item.End > item.Start ? item.End.AddTicks(-1) : item.Start).Date;
            for (var d = item.Start.Date; d <= last && busy.Count < 400; d = d.AddDays(1)) busy.Add(d);
        }

        CalendarDays.Children.Clear();
        var start = month.AddDays(-(((int)month.DayOfWeek - (int)firstDay + 7) % 7));
        var weeks = (int)Math.Ceiling(((month - start).Days + DateTime.DaysInMonth(month.Year, month.Month)) / 7.0);
        for (var i = 0; i < weeks * 7; i++)
        {
            var date = start.AddDays(i);
            var inMonth = date.Month == month.Month;
            var isToday = date == today;
            var isSelected = date == _calendarSelected;

            var cell = new Grid { Height = 23, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            cell.Children.Add(new Ellipse
            {
                Width = 22,
                Height = 22,
                Fill = isToday ? accent : isSelected ? (Brush)FindResource("HoshinoControl") : null,
                Stroke = isSelected && isToday ? (Brush)FindResource("HoshinoText") : null,
                StrokeThickness = 1.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
            });
            cell.Children.Add(new TextBlock
            {
                Text = date.Day.ToString(),
                FontSize = 11,
                FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
                Foreground = isToday ? (Brush)FindResource("HoshinoOnAccent") : inMonth ? (Brush)FindResource("HoshinoText") : secondary,
                Opacity = inMonth || isToday ? 1 : 0.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 3.5, 0, 0),
            });
            if (busy.Contains(date) && !isToday)
                cell.Children.Add(new Ellipse
                {
                    Width = 3.5,
                    Height = 3.5,
                    Fill = inMonth ? accent : secondary,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 0.5),
                });
            cell.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true; // don't collapse the island
                _calendarSelected = _calendarSelected == date ? null : date;
                if (date.Month != _calendarMonth.Month || date.Year != _calendarMonth.Year)
                    _calendarMonth = new DateTime(date.Year, date.Month, 1);
                BuildCalendar();
            };
            CalendarDays.Children.Add(cell);
        }
    }

    /// <summary>The Calendar page: grouped by day, with "Сейчас" / "через N мин" and a button for online meetings.</summary>
    private void BuildCalendar()
    {
        var secondary = (Brush)FindResource("Secondary");
        var now = DateTime.Now;
        BuildMonth();
        _journal.SelectDate(_calendarSelected ?? DateTime.Today);
        CalendarList.Children.Clear();

        // A picked day lists everything on it (past events too); otherwise the upcoming week.
        var items = _calendarSelected is { } selected
            ? _calendar.Items.Where(i => OccursOn(i, selected)).ToList()
            : _calendar.Items.Where(i => (i.End > now || (i.AllDay && OccursOn(i, now.Date)))
                                         && i.Start < now.Date.AddDays(CalendarService.DaysAhead)).ToList();

        string? empty = null;
        if (!_calendar.HasSources)
            empty = PackageIdentity.HasIdentity || !_settings.WindowsCalendar
                ? "Календари подключаются в настройках"
                : "Событий пока нет. Можно вести ежедневник справа.";
        else if (items.Count == 0)
            empty = _calendarSelected is { } day0 ? $"На {day0:dd MMM} событий нет" : "На ближайшие 7 дней событий нет";
        if (_calendar.Problem is { } problem) empty = empty is null ? problem : $"{empty}\n{problem}";
        CalendarEmpty.Text = empty ?? "";
        CalendarEmpty.Visibility = empty is null ? Visibility.Collapsed : Visibility.Visible;
        CalendarScroller.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        DateTime? day = null;
        foreach (var item in items.Take(40))
        {
            var itemDay = _calendarSelected
                          ?? (item.AllDay || item.Start < now ? (item.Start < now.Date ? now.Date : item.Start.Date) : item.Start.Date);
            if (itemDay != day)
            {
                day = itemDay;
                CalendarList.Children.Add(new TextBlock
                {
                    Text = itemDay == now.Date ? "СЕГОДНЯ" : itemDay == now.Date.AddDays(1) ? "ЗАВТРА" : itemDay.ToString("dddd d MMM").ToUpperInvariant(),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = secondary,
                    Margin = new Thickness(0, CalendarList.Children.Count == 0 ? 0 : 8, 0, 5),
                });
            }

            var color = item.Color is { } c ? new SolidColorBrush(c) : (Brush)FindResource("Orange");
            var row = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Background = color, Margin = new Thickness(0, 1, 10, 1) });

            var time = item.AllDay ? "Весь день" : $"{item.Start:t} – {item.End:t}";
            var text = new StackPanel { Children =
            {
                new TextBlock { Text = item.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock
                {
                    Text = item.Location is { } location && item.JoinUrl is null ? $"{time}  ·  {location}" : time,
                    FontSize = 11,
                    Foreground = secondary,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            } };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (item.IsNow(now))
                right.Children.Add(new TextBlock { Text = "Сейчас", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = color, VerticalAlignment = VerticalAlignment.Center });
            else if (!item.AllDay && item.Start > now && item.Start - now < TimeSpan.FromHours(1))
                right.Children.Add(new TextBlock { Text = $"через {(int)Math.Ceiling((item.Start - now).TotalMinutes)} мин", FontSize = 11, Foreground = color, VerticalAlignment = VerticalAlignment.Center });
            if (item.JoinUrl is { } url && (item.IsNow(now) || (item.Start > now && item.Start - now < TimeSpan.FromHours(12))))
            {
                var join = new Button { Style = (Style)FindResource("ChipButton"), Content = "Войти", Margin = new Thickness(8, 0, 0, 0), ToolTip = url };
                join.Click += (_, _) =>
                {
                    _hoverSuppressed = true;
                    SetExpanded(false);
                    OpenPath(url);
                };
                right.Children.Add(join);
            }
            Grid.SetColumn(right, 2);
            row.Children.Add(right);
            CalendarList.Children.Add(row);
        }

        if (IsLoaded && _state.IsExpanded && _page == ExpandedPage.Calendar) Render();
    }

    // ---------------------------------------------------------------- file shelf

    private string? _shelfKey;
    private string? _selectedShelfPath;
    private bool _draggingOut;
    private Point? _dragStart;

    private void InitShelf()
    {
        _shelf.Changed += BuildShelf;
        Root.AllowDrop = true;
        Root.DragEnter += OnShelfDragOver;
        Root.DragOver += OnShelfDragOver;
        Root.DragLeave += OnShelfDragLeave;
        Root.Drop += OnShelfDrop;
        StartDragOnMove(ShelfDragAll, () => _shelf.Items.ToList());
    }

    /// <summary>Files dragged over the island: open it on the Shelf page and offer to keep them.</summary>
    private void OnShelfDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_draggingOut || !_settings.ShelfEnabled || IslandHidden || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        // "Link" is honest (the shelf keeps a reference, not a copy); fall back to Copy if not offered.
        e.Effects = e.AllowedEffects.HasFlag(DragDropEffects.Link) ? DragDropEffects.Link : DragDropEffects.Copy;

        _homeHoshino.SetFileDragHover(true);
        _compactHoshino.SetFileDragHover(true);
        _hoverTimer.Stop();
        if (!_state.IsExpanded) SetExpanded(true);
        if (_page != ExpandedPage.Shelf) ShowPage(ExpandedPage.Shelf, animate: true);
        if (ShelfDropOverlay.Visibility != Visibility.Visible)
        {
            ShelfDropRect.Stroke = (Brush)FindResource("HoshinoAccent");
            ShelfDropOverlay.Visibility = Visibility.Visible;
        }
    }

    private void OnShelfDragLeave(object sender, DragEventArgs e)
    {
        // DragLeave also fires moving between elements inside the island; only react on really leaving.
        if (!IsCursorOverPill())
        {
            ShelfDropOverlay.Visibility = Visibility.Collapsed;
            _homeHoshino.SetFileDragHover(false);
            _compactHoshino.SetFileDragHover(false);
        }
    }

    private void OnShelfDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        ShelfDropOverlay.Visibility = Visibility.Collapsed;
        _homeHoshino.SetFileDragHover(false);
        _compactHoshino.SetFileDragHover(false);
        if (_draggingOut || !_settings.ShelfEnabled || e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        _shelf.Add(paths);
    }

    private void OnShelfClearClick(object sender, RoutedEventArgs e)
    {
        _selectedShelfPath = null;
        _shelf.Clear();
    }

    private void OnShelfQrClick(object sender, RoutedEventArgs e)
    {
        if (_selectedShelfPath is { } single && _shelf.Items.Contains(single, StringComparer.OrdinalIgnoreCase) && System.IO.File.Exists(single))
            _qrPanel.Open(new[] { single }, singleItem: true);
        else
            _qrPanel.Open(_shelf.Items, singleItem: false);
    }

    /// <summary>Rebuilds the shelf tiles; thumbnails load afterwards at background priority.</summary>
    private void BuildShelf()
    {
        if (_selectedShelfPath is not null && !_shelf.Items.Contains(_selectedShelfPath, StringComparer.OrdinalIgnoreCase))
            _selectedShelfPath = null;

        var f = Scale * VisualTreeHelper.GetDpi(this).DpiScaleX;
        var key = $"{f}|{_selectedShelfPath}\n{string.Join('\n', _shelf.Items)}";
        if (key == _shelfKey) return;
        _shelfKey = key;

        var items = _shelf.Items;
        ShelfItems.Children.Clear();
        ShelfEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShelfToolbar.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ShelfScroller.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ShelfCount.Text = $"Файлов: {items.Count}";
        ShelfQrButton.Content = _selectedShelfPath is not null ? "На телефон · 1 (QR)" : "На телефон (QR)";

        const double tileWidth = 76, thumb = 44;
        var pixels = (int)Math.Round(thumb * f);
        var secondary = (Brush)FindResource("Secondary");
        foreach (var path in items)
        {
            var exists = System.IO.File.Exists(path) || System.IO.Directory.Exists(path);
            var image = new Image { Width = thumb, Height = thumb, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
            var name = new TextBlock
            {
                Text = System.IO.Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } fileName ? fileName : path,
                FontSize = 11,
                Margin = new Thickness(0, 5, 0, 0),
                MaxWidth = tileWidth - 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = secondary,
            };
            var remove = new Button
            {
                Style = (Style)FindResource("IconButton"),
                Width = 20,
                Height = 20,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x3A, 0x3A, 0x3C)),
                Visibility = Visibility.Hidden,
                ToolTip = "Убрать с полки",
                Content = new TextBlock { Style = (Style)FindResource("Glyph"), Text = "", FontSize = 8 },
            };
            remove.Click += (_, _) => _shelf.Remove(path);

            var isSelected = string.Equals(_selectedShelfPath, path, StringComparison.OrdinalIgnoreCase);
            var selectionFrame = new Border
            {
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(isSelected ? 1.5 : 0),
                Margin = new Thickness(2),
                IsHitTestVisible = false,
            };
            if (isSelected)
            {
                selectionFrame.SetResourceReference(Border.BorderBrushProperty, "HoshinoAccent");
                selectionFrame.SetResourceReference(Border.BackgroundProperty, "HoshinoControl");
                selectionFrame.Opacity = 0.55;
            }

            var tile = new Grid
            {
                Width = tileWidth,
                Height = thumb + 34,
                Background = Brushes.Transparent, // hit-testable for dragging
                Opacity = exists ? 1 : 0.4,
                ToolTip = exists ? path : $"{path}\n(файл перемещён или удалён)",
                Cursor = Cursors.Hand,
                Children =
                {
                    selectionFrame,
                    new StackPanel
                    {
                        Margin = new Thickness(0, 4, 0, 0),
                        Children =
                        {
                            // Generic document glyph behind the thumbnail: shows for missing files and
                            // until the thumbnail has loaded.
                            new Grid
                            {
                                Width = thumb,
                                Height = thumb,
                                Children =
                                {
                                    new TextBlock { Style = (Style)FindResource("Glyph"), Text = "", FontSize = 26, Foreground = secondary },
                                    image,
                                },
                            },
                            name,
                        },
                    },
                    remove,
                },
            };
            if (exists && System.IO.File.Exists(path))
            {
                var tileMenu = new ContextMenu();
                var qrItem = new MenuItem { Header = "На телефон (QR)" };
                qrItem.Click += (_, _) => _qrPanel.Open(new[] { path }, singleItem: true);
                var openItem = new MenuItem { Header = "Открыть файл" };
                openItem.Click += (_, _) => OpenPath(path);
                tileMenu.Items.Add(qrItem);
                tileMenu.Items.Add(openItem);
                tile.ContextMenu = tileMenu;
            }
            tile.MouseEnter += (_, _) => remove.Visibility = Visibility.Visible;
            tile.MouseLeave += (_, _) => remove.Visibility = Visibility.Hidden;
            tile.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2 && exists) OpenPath(path);
            };
            tile.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true; // a click on a tile doesn't collapse the island
                if (!_draggingOut && System.IO.File.Exists(path))
                {
                    _selectedShelfPath = string.Equals(_selectedShelfPath, path, StringComparison.OrdinalIgnoreCase) ? null : path;
                    BuildShelf();
                }
            };
            StartDragOnMove(tile, () => new List<string> { path });
            ShelfItems.Children.Add(tile);

            // Thumbnails (video, large photos) can take a moment; don't hold up the layout for them.
            if (exists)
            {
                var glyph = (UIElement)((Grid)image.Parent).Children[0];
                Dispatcher.InvokeAsync(() =>
                {
                    image.Source = QuickAppsService.GetFileImage(path, pixels);
                    if (image.Source is not null) glyph.Visibility = Visibility.Collapsed;
                }, DispatcherPriority.Background);
            }
        }

        if (IsLoaded && _state.IsExpanded && _page == ExpandedPage.Shelf) Render();
    }

    private static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            App.LogError(ex);
        }
    }

    /// <summary>Starts a file drag from <paramref name="source"/> once the mouse moves far enough with the button down.</summary>
    private void StartDragOnMove(UIElement source, Func<List<string>> paths)
    {
        source.PreviewMouseLeftButtonDown += (_, e) => _dragStart = e.GetPosition(Root);
        source.PreviewMouseMove += (_, e) =>
        {
            if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed || _draggingOut) return;
            var delta = e.GetPosition(Root) - start;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            _dragStart = null;
            DragOut(source, paths());
        };
        source.PreviewMouseLeftButtonUp += (_, _) => _dragStart = null;
    }

    /// <summary>
    /// Drags files out of the shelf to Explorer, an app, a chat… A successful drop removes them from
    /// the shelf (unless the setting keeps them); files that were moved away are removed regardless.
    /// </summary>
    private void DragOut(UIElement source, List<string> paths)
    {
        paths = paths.Where(p => System.IO.File.Exists(p) || System.IO.Directory.Exists(p)).ToList();
        if (paths.Count == 0) return;
        _draggingOut = true;
        DragDropEffects result;
        try
        {
            var data = new DataObject(DataFormats.FileDrop, paths.ToArray());
            result = DragDrop.DoDragDrop(source, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }
        finally
        {
            _draggingOut = false;
        }
        if (result != DragDropEffects.None && !_settings.ShelfKeepAfterDrag)
            foreach (var path in paths) _shelf.Remove(path);
        _shelf.RemoveMissing();
    }

    // ---------------------------------------------------------------- weather

    /// <summary>Starts / stops the service, and refetches when the location or unit changes.</summary>
    private void UpdateWeatherService()
    {
        if (!IsLoaded) return;
        if (!_settings.WeatherEnabled)
        {
            _weather.Stop();
            _weatherKey = null;
            return;
        }
        var s = _settings;
        var key = $"{s.WeatherUseDeviceLocation}|{s.WeatherLatitude}|{s.WeatherLongitude}|{s.TemperatureUnit}";
        if (key == _weatherKey) return;
        var first = _weatherKey is null;
        _weatherKey = key;
        if (first)
        {
            UpdateWeather(); // "Loading weather…" until the first report
            _weather.Start();
        }
        else
        {
            _weather.Refresh();
        }
    }

    private static string Degrees(double value) => $"{Math.Round(value):0}°";
    private static string SignedDegrees(double value)
    {
        var rounded = (int)Math.Round(value);
        return rounded > 0 ? $"+{rounded}°" : $"{rounded}°";
    }

    /// <summary>Fills the Weather page from the latest report.</summary>
    private void UpdateWeather()
    {
        var report = _weather.Current;
        WeatherProblemText.Text = _weather.Problem ?? "";
        WeatherProblemText.Visibility = _weather.Problem is null ? Visibility.Collapsed : Visibility.Visible;
        if (report is null)
        {
            WeatherNowIcon.Child = null;
            WeatherNowTemp.Text = "";
            WeatherCondition.Text = _weather.Problem is null ? "Загрузка погоды…" : "Погода недоступна";
            WeatherDetail.Text = WeatherPlaceText.Text = "";
            WeatherHours.Children.Clear();
            WeatherDays.Children.Clear();
            if (_state.IsExpanded && _page == ExpandedPage.Weather) Render();
            return;
        }

        var secondary = (Brush)FindResource("Secondary");
        WeatherNowIcon.Child = WeatherIcons.Create(report.Kind, report.IsDay, 50);
        WeatherNowTemp.Text = SignedDegrees(report.Temperature);
        WeatherCondition.Text = report.Condition;
        var today = report.Days.FirstOrDefault();
        WeatherDetail.Text = today is null
            ? $"Ощущается как {SignedDegrees(report.FeelsLike)}"
            : $"Макс {SignedDegrees(today.Max)}  Мин {SignedDegrees(today.Min)}  ·  Ощущается как {SignedDegrees(report.FeelsLike)}";
        WeatherPlaceText.Text = report.Place;
        WeatherAttribution.Text = _settings.WeatherUseDeviceLocation && report.Place == _settings.WeatherDevicePlace
            ? "Погода: Open-Meteo.com · локация © OpenStreetMap"
            : "Погода: Open-Meteo.com";

        // Every second hour for the next 12: time, icon, temperature.
        WeatherHours.Children.Clear();
        var hourFormat = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('H') ? "HH:mm" : "h tt";
        foreach (var hour in report.Hours.Where((_, i) => i % 2 == 0).Take(6))
        {
            WeatherHours.Children.Add(new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = hour.Time.ToString(hourFormat), FontSize = 11, Foreground = secondary, HorizontalAlignment = HorizontalAlignment.Center },
                    Centered(WeatherIcons.Create(hour.Kind, hour.IsDay, 22), new Thickness(0, 5, 0, 5)),
                    new TextBlock { Text = Degrees(hour.Temperature), FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
                },
            });
        }

        // Days: name, icon, low, a range bar placed within the week's span (star columns, no measuring), high.
        WeatherDays.Children.Clear();
        var weekMin = report.Days.Min(d => d.Min);
        var span = Math.Max(1, report.Days.Max(d => d.Max) - weekMin);
        var fill = _accent is { } accent ? new SolidColorBrush(accent) : (Brush)FindResource("Orange");
        foreach (var day in report.Days)
        {
            var row = new Grid { Height = 24 };
            foreach (var width in new[] { 64.0, 30, 38 }) row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });

            var name = new TextBlock
            {
                Text = day.Date.Date == DateTime.Today ? "Сегодня" : day.Date.ToString("ddd"),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var icon = Centered(WeatherIcons.Create(day.Kind, true, 18), new Thickness(0));
            var low = new TextBlock { Text = Degrees(day.Min), FontSize = 13, Foreground = secondary, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var high = new TextBlock { Text = Degrees(day.Max), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };

            var bar = new Grid { Height = 4, Margin = new Thickness(10, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            var before = (day.Min - weekMin) / span;
            var width2 = Math.Max(0.04, (day.Max - day.Min) / span);
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(before, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width2, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, 1 - before - width2), GridUnitType.Star) });
            var track = new Border { CornerRadius = new CornerRadius(2), Background = (Brush)FindResource("HoshinoTrack") };
            Grid.SetColumnSpan(track, 3);
            var range = new Border { CornerRadius = new CornerRadius(2), Background = fill };
            Grid.SetColumn(range, 1);
            bar.Children.Add(track);
            bar.Children.Add(range);

            Grid.SetColumn(icon, 1);
            Grid.SetColumn(low, 2);
            Grid.SetColumn(bar, 3);
            Grid.SetColumn(high, 4);
            row.Children.Add(name);
            row.Children.Add(icon);
            row.Children.Add(low);
            row.Children.Add(bar);
            row.Children.Add(high);
            WeatherDays.Children.Add(row);
        }

        if (_state.IsExpanded && _page == ExpandedPage.Weather) Render(); // height may change
    }

    private static FrameworkElement Centered(FrameworkElement element, Thickness margin)
    {
        element.HorizontalAlignment = HorizontalAlignment.Center;
        element.VerticalAlignment = VerticalAlignment.Center;
        element.Margin = margin;
        return element;
    }

    // ---------------------------------------------------------------- quick apps

    private string? _quickAppsKey;

    /// <summary>Rebuilds the Apps page's tiles (icon + name) when the app list or icon size changes.</summary>
    private void BuildQuickApps()
    {
        var apps = _settings.QuickApps.Take(AppSettings.MaxQuickApps).ToList();
        var dipSize = Math.Clamp(_settings.QuickAppIconSize, 16, 48);
        var f = Scale * VisualTreeHelper.GetDpi(this).DpiScaleX; // design units → screen pixels
        var key = $"{dipSize}|{f}\n{string.Join('\n', apps)}"; // icons are rendered for the size
        if (key == _quickAppsKey) return;
        _quickAppsKey = key;

        // Draw icons 1:1 in screen pixels at a standard icon size: any scaling on the way to the
        // screen makes them soft.
        var pixels = QuickAppsService.SnapSize(dipSize * f);
        var shown = pixels / f;
        var tileWidth = Math.Round(Math.Max(dipSize + 24, 74));
        var tileHeight = Math.Round(dipSize + 36); // icon + name

        QuickAppsRow.Children.Clear();
        QuickAppsRow.UseLayoutRounding = true;
        foreach (var id in apps)
        {
            if (QuickAppsService.GetIcon(id, pixels) is not { } icon) continue; // uninstalled
            var image = new Image { Source = icon, Width = shown, Height = shown, Stretch = Stretch.Fill };
            var button = AppTile(image, QuickAppsService.GetName(id) ?? "", tileWidth, tileHeight);
            button.Click += (_, _) =>
            {
                _hoverSuppressed = true; // don't re-open under the cursor as the island shrinks
                SetExpanded(false);
                QuickAppsService.Launch(id);
            };
            QuickAppsRow.Children.Add(button);
        }

        // "+" to pick apps in Settings.
        if (QuickAppsRow.Children.Count < AppSettings.MaxQuickApps)
        {
            var add = AppTile(new Grid
                {
                    Width = shown,
                    Height = shown,
                    Children = { new TextBlock { Style = (Style)FindResource("Glyph"), Text = "", FontSize = Math.Max(14, dipSize * 0.5), Foreground = (Brush)FindResource("Secondary") } },
                }, "Добавить", tileWidth, tileHeight);
            add.Click += (_, _) => OpenSettings();
            QuickAppsRow.Children.Add(add);
        }

        // WrapPanel can't center its rows; give it exactly a whole number of tiles' width instead.
        var perRow = Math.Clamp((int)((ExpandedWidth - 36) / tileWidth), 1, Math.Max(1, QuickAppsRow.Children.Count));
        QuickAppsRow.Width = perRow * tileWidth;
        if (IsLoaded && _state.IsExpanded) Render();
    }

    private Button AppTile(FrameworkElement icon, string name, double width, double height)
    {
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        return new Button
        {
            Style = (Style)FindResource("IconButton"),
            Width = width,
            Height = height,
            ToolTip = name,
            Content = new StackPanel
            {
                Children =
                {
                    icon,
                    new TextBlock
                    {
                        Text = name,
                        FontSize = 11,
                        Margin = new Thickness(0, 5, 0, 0),
                        MaxWidth = width - 8,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Foreground = (Brush)FindResource("Secondary"),
                    },
                },
            },
        };
    }

    // ---------------------------------------------------------------- timer

    private void OnTimerTick()
    {
        var text = TimerService.Format(_timer.Remaining);
        TimerTextSmall.Text = text;
        TimerTextLarge.Text = text;
        if (_pixelTimerText is not null) _pixelTimerText.Text = text;
        if (ActivityBubble.Visibility == Visibility.Visible && _state.SecondaryView == IslandView.Timer)
        {
            ActivityBubbleText.Text = TimerService.FormatCompact(_timer.Remaining);
            ActivityBubble.ToolTip = text;
            RefreshActivityBubbleVisuals(ActivityBubble.Width);
        }
        if (_settings.PerimeterFocusTimer && _timer.IsActive && !_state.IsExpanded && !_voiceFocus && !IslandHidden && IslandRimPath.Data is PathGeometry rim)
        {
            IslandChromeRenderer.UpdatePerimeterFocus(
                PerimeterTimerTrack, PerimeterTimerArc, PerimeterTimerDot,
                rim, IslandSkinEngine.Current, visible: true, _timer.RemainingRatio);
        }
    }

    private void OnTimerStateChanged()
    {
        TimerPauseGlyph.Text = _timer.IsPaused ? "" : "";
        TimerTextSmall.Opacity = _timer.IsPaused ? 0.55 : 1;
        TimerTextLarge.Opacity = _timer.IsPaused ? 0.55 : 1;
        RebuildPixelFocusDock();
        if (IslandSkinEngine.Current.IsPixelMode)
        {
            TamagotchiStatusHost.Content = IslandSkinEngine.CreateTamagotchiStatusStrip(
                HoshinoAtmosphere.IsDrowsy(DateTime.Now),
                _media.Current?.IsPlaying == true,
                _timer.IsActive);
        }
        OnTimerTick();
        _compactHoshino.UpdateState();
        _homeHoshino.UpdateState();
        _state.TimerActive = _timer.IsActive;
        UpdateSecondActivity();
        ApplyGeometry();
        if (_state.IsExpanded) Render();
    }

    private void OnTimerFinished()
    {
        TimerDoneGlyph.Text = ""; // the view is shared with alarms
        var goal = _settings.FocusTaskId is { } id ? _notes.Todos.FirstOrDefault(t => !t.Done && t.Id == id)?.Text : null;
        TimerDoneText.Text = string.IsNullOrWhiteSpace(goal) ? "Фокус завершён" : $"Готово · {goal}";
        SystemSounds.Exclamation.Play();
        _state.ShowTransient(IslandView.TimerDone, TransientPriority.TimerDone, TimeSpan.FromSeconds(5));
    }

    private void OnTimerPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var minutes))
            _timer.Start(TimeSpan.FromMinutes(minutes));
    }

    private void OnTimerAddClick(object sender, RoutedEventArgs e) => _timer.Add(TimeSpan.FromMinutes(1));
    private void OnTimerPauseClick(object sender, RoutedEventArgs e) => _timer.TogglePause();
    private void OnTimerCancelClick(object sender, RoutedEventArgs e) => _timer.Cancel();

    // ---------------------------------------------------------------- stopwatch & alarms

    private void OnStopwatchTick()
    {
        // Tenths only in the expanded view; the compact pill changes once a second.
        StopwatchTextSmall.Text = StopwatchService.Format(_stopwatch.Elapsed, tenths: false);
        if (_state.IsExpanded) StopwatchTextLarge.Text = StopwatchService.Format(_stopwatch.Elapsed, tenths: true);
    }

    private void OnStopwatchStateChanged()
    {
        StopwatchPauseGlyph.Text = _stopwatch.IsRunning ? "" : "";
        StopwatchTextSmall.Opacity = _stopwatch.IsRunning ? 1 : 0.55;
        StopwatchTextLarge.Opacity = _stopwatch.IsRunning ? 1 : 0.55;
        StopwatchTextLarge.Text = StopwatchService.Format(_stopwatch.Elapsed, tenths: true);
        if (IslandSkinEngine.Current.IsPixelMode)
            FocusDockBorder.Visibility = _stopwatch.IsActive ? Visibility.Visible : Visibility.Collapsed;
        OnStopwatchTick();
        _state.StopwatchActive = _stopwatch.IsActive;
        if (_state.IsExpanded) Render();
    }

    private void OnStopwatchStartClick(object sender, RoutedEventArgs e) => _stopwatch.Start();
    private void OnStopwatchPauseClick(object sender, RoutedEventArgs e) => _stopwatch.TogglePause();
    private void OnStopwatchResetClick(object sender, RoutedEventArgs e) => _stopwatch.Reset();

    /// <summary>Rings until clicked (or a minute passes): looping alarm sound and the alert pill.</summary>
    private void OnAlarmRing(Alarm alarm)
    {
        TimerDoneGlyph.Text = "";
        TimerDoneText.Text = string.IsNullOrWhiteSpace(alarm.Label) ? $"Будильник  {alarm.Time}" : $"{alarm.Label}  {alarm.Time}";
        try
        {
            var file = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Alarm01.wav");
            _alarmSound?.Stop();
            _alarmSound = System.IO.File.Exists(file) ? new System.Media.SoundPlayer(file) : null;
            if (_alarmSound is not null) _alarmSound.PlayLooping();
            else SystemSounds.Exclamation.Play();
        }
        catch (Exception) { SystemSounds.Exclamation.Play(); }

        if (IslandHidden) SetIslandHidden(false); // an alarm must be seen
        _state.ShowTransient(IslandView.TimerDone, TransientPriority.Alarm, TimeSpan.FromMinutes(1));
    }

    private void StopAlarmSound()
    {
        _alarmSound?.Stop();
        _alarmSound = null;
    }

    // ---------------------------------------------------------------- status alerts

    private void OnStatusChanged(StatusChange change)
    {
        if (!_settings.StatusAlerts) return;
        var green = (Brush)FindResource("Green");
        var red = FrozenBrush(Color.FromRgb(0xFF, 0x45, 0x3A));
        var secondary = (Brush)FindResource("Secondary");
        switch (change.Kind)
        {
            case StatusKind.MicMuted:
                ShowInfo(TransientPriority.LockKey, 250, "", "Микрофон", "Выкл", detailBrush: red, glyphBrush: red, seconds: 1.8);
                break;
            case StatusKind.MicUnmuted:
                ShowInfo(TransientPriority.LockKey, 250, "", "Микрофон", "Вкл", detailBrush: green, seconds: 1.8);
                break;
            case StatusKind.EnergySaverOn:
                ShowInfo(TransientPriority.Battery, 260, "", "Экономия заряда", "Вкл", detailBrush: green, glyphBrush: green);
                break;
            case StatusKind.EnergySaverOff:
                ShowInfo(TransientPriority.Battery, 260, "", "Экономия заряда", "Выкл");
                break;
            case StatusKind.WifiConnected:
                ShowInfo(TransientPriority.Device, 320, "", change.Detail ?? "Wi-Fi", "Подключено", detailBrush: green);
                break;
            case StatusKind.WifiDisconnected:
                ShowInfo(TransientPriority.Device, 320, "", change.Detail ?? "Wi-Fi", "Отключено", glyphBrush: secondary);
                break;
            case StatusKind.VpnConnected:
                ShowInfo(TransientPriority.Device, 320, "", change.Detail ?? "VPN", "VPN вкл", detailBrush: green);
                break;
            case StatusKind.VpnDisconnected:
                ShowInfo(TransientPriority.Device, 320, "", change.Detail ?? "VPN", "VPN выкл", glyphBrush: secondary);
                break;
            case StatusKind.Offline:
                ShowInfo(TransientPriority.Device, 280, "", "Нет сети", null, glyphBrush: red);
                break;
        }
    }

    // ---------------------------------------------------------------- second activity bubble

    /// <summary>
    /// The second running activity (music while a timer runs, …) as a bubble beside the collapsed pill:
    /// album art for music, a timer / stopwatch glyph otherwise. Hidden while expanded or during alerts.
    /// </summary>
    private Artwork? _bubbleArt;

    private void RefreshActivityBubbleVisuals(double size)
    {
        var spec = IslandSkinEngine.Current;
        var secondary = _state.SecondaryView;
        var isTimer = secondary == IslandView.Timer && _timer.IsActive;

        // Sibling ActivityBubbleShadow draws ChibiPop ink shadow without rasterizing ActivityBubbleText
        ActivityBubble.Effect = null;
        ActivityBubbleBack.StrokeThickness = spec.IsCartoonSticker ? 2.4 : 1.3;

        if (spec.IsPixelMode)
        {
            ActivityBubbleBack.Visibility = Visibility.Collapsed;
            ActivityBubbleRingTrack.Visibility = Visibility.Collapsed;
            ActivityBubbleArc.Visibility = Visibility.Collapsed;
            ActivityBubbleKeychainHost.Content = IslandChromeRenderer.CreateTamagotchiKeychainBubble(size);
            ActivityBubbleKeychainHost.Visibility = Visibility.Visible;
            if (isTimer)
            {
                ActivityBubbleGlyph.Visibility = Visibility.Collapsed;
                ActivityBubbleText.Visibility = Visibility.Visible;
                ActivityBubbleText.FontSize = 9.5;
                ActivityBubbleText.Foreground = IslandSkinEngine.Frozen(Color.FromRgb(0x14, 0x2D, 0x2A));
            }
            return;
        }

        ActivityBubbleKeychainHost.Visibility = Visibility.Collapsed;
        ActivityBubbleBack.Visibility = Visibility.Visible;

        if (isTimer)
        {
            // Match Row 2 of all compact renders: yellow sticker ring + pink inner arc in Chibi Pop,
            // glowing glass ring in Win7 Aero, frosted white + cyan arc in Shittim, smoked + gold arc in Horus!
            ActivityBubbleBack.Fill = spec.IsCartoonSticker
                ? IslandSkinEngine.Frozen(Color.FromRgb(0xFF, 0xE0, 0x66))
                : spec.IsAeroGlass
                    ? IslandSkinEngine.CreateAeroConvexGlassBrush(false)
                    : (Brush)FindResource("HoshinoBackground");

            ActivityBubbleRingTrack.Visibility = Visibility.Visible;
            ActivityBubbleRingTrack.Fill = spec.IsCartoonSticker
                ? IslandSkinEngine.Frozen(Color.FromRgb(0xFF, 0xF8, 0xFC))
                : Brushes.Transparent;
            ActivityBubbleRingTrack.Stroke = spec.IsCartoonSticker
                ? IslandSkinEngine.Frozen(Color.FromRgb(0x2A, 0x1E, 0x2E))
                : (Brush)FindResource("HoshinoTrack");
            ActivityBubbleRingTrack.StrokeThickness = spec.IsCartoonSticker ? 1.8 : 2.2;
            ActivityBubbleRingTrack.Margin = new Thickness(spec.IsCartoonSticker ? 5.2 : 3.6);

            ActivityBubbleArc.Visibility = Visibility.Visible;
            ActivityBubbleArc.StrokeThickness = spec.IsCartoonSticker ? 3.2 : 2.6;
            ActivityBubbleArc.Stroke = spec.IsShittimTablet || spec.IsAeroGlass
                ? IslandSkinEngine.Frozen(Color.FromRgb(0x38, 0xBD, 0xF8))
                : spec.IsCartoonSticker
                    ? IslandSkinEngine.Frozen(Color.FromRgb(0xFF, 0x85, 0xAD))
                    : IslandSkinEngine.Frozen(spec.Gold);
            ActivityBubbleArc.Data = IslandChromeRenderer.BuildCircularProgressArc(size, spec.IsCartoonSticker ? 3.8 : 4.2, _timer.RemainingRatio);
            ActivityBubbleGlyph.Visibility = Visibility.Collapsed;
            ActivityBubbleText.Visibility = Visibility.Visible;
            ActivityBubbleText.FontSize = 11;
            ActivityBubbleText.Foreground = spec.IsAeroGlass ? Brushes.White : (Brush)FindResource("HoshinoText");
        }
        else
        {
            ActivityBubbleBack.SetResourceReference(Shape.FillProperty, "HoshinoBackground");
            ActivityBubbleRingTrack.Visibility = Visibility.Collapsed;
            ActivityBubbleArc.Visibility = Visibility.Collapsed;
            ActivityBubbleText.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateSecondActivity()
    {
        var secondary = !_voiceFocus && _settings.ShowSecondActivity && !_state.IsExpanded && !_lyricsPanel.CompactEnabled && _state.Transient is null
            ? _state.SecondaryView
            : null;
        var show = secondary is not null;
        var wasShown = ActivityBubble.Visibility == Visibility.Visible;

        if (secondary == IslandView.Media)
        {
            if (!ReferenceEquals(_bubbleArt, _shownArt))
            {
                _bubbleArt = _shownArt;
                ActivityBubbleArt.Fill = _shownArt?.CreateBrush();
            }
            var art = ActivityBubbleArt.Fill;
            ActivityBubbleArt.Visibility = art is null ? Visibility.Collapsed : Visibility.Visible;
            ActivityBubbleText.Visibility = Visibility.Collapsed;
            ActivityBubbleGlyph.Text = "";
            ActivityBubbleGlyph.Foreground = (Brush)FindResource("Secondary");
            ActivityBubbleGlyph.Visibility = art is null ? Visibility.Visible : Visibility.Collapsed;
            ActivityBubble.ToolTip = _media.Current?.Title;
        }
        else if (secondary is { } activity)
        {
            ActivityBubbleArt.Visibility = Visibility.Collapsed;
            if (activity == IslandView.Timer)
            {
                ActivityBubbleText.Text = TimerService.FormatCompact(_timer.Remaining);
                ActivityBubble.ToolTip = TimerService.Format(_timer.Remaining);
            }
            else
            {
                ActivityBubbleText.Visibility = Visibility.Collapsed;
                ActivityBubbleGlyph.Visibility = Visibility.Visible;
                ActivityBubbleGlyph.Text = "";
                ActivityBubbleGlyph.Foreground = (Brush)FindResource("Cyan");
                ActivityBubble.ToolTip = "Секундомер";
            }
        }

        if (show) RefreshActivityBubbleVisuals(ActivityBubble.Width);
        if (show == wasShown) return;
        ActivityBubble.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            var time = TimeSpan.FromMilliseconds(320 / SpeedFactor);
            var pop = new DoubleAnimation(0.2, 1, time) { EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut } };
            Timeline.SetDesiredFrameRate(pop, FrameRateCap);
            var scale = (ScaleTransform)ActivityBubble.RenderTransform;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }
        ApplyGeometry();
    }

    /// <summary>Opens the app that's playing (click on the collapsed music activity, when enabled).</summary>
    private void OpenMediaApp()
    {
        if (_media.Current?.AppId is not { Length: > 0 } appId) return;
        if (appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            // Desktop players (Spotify.exe, …): bring the running window forward. The click on the
            // island is the user input that lets this process hand over the foreground.
            foreach (var process in Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(appId)))
            {
                var hwnd = process.MainWindowHandle;
                process.Dispose();
                if (hwnd == IntPtr.Zero) continue;
                if (NativeMethods.IsIconic(hwnd)) NativeMethods.ShowWindow(hwnd, 9 /* SW_RESTORE */);
                SetForegroundWindow(hwnd);
                return;
            }
        }
        QuickAppsService.Launch(appId); // Store apps (Apple Music, …) by AppUserModelID
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    // ---------------------------------------------------------------- media

    private void OnMediaChanged()
    {
        _lyricsPanel.Update(_media.Current);
        UpdateCompactArtwork();
        var media = _media.Current;
        var spec = IslandSkinEngine.Current;
        if (media is not null)
        {
            var title = string.IsNullOrWhiteSpace(media.Title) ? "Без названия" : media.Title;

            MediaTitleSmall.Text = title;
            MediaTitleLarge.Text = spec.IsPixelMode && !title.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ? $"{title}.mp3" : title;
            MediaArtistLarge.Text = spec.IsPixelMode
                ? $"{(string.IsNullOrEmpty(media.Artist) ? "Track" : media.Artist)} · 128kbps"
                : string.IsNullOrEmpty(media.Album) ? media.Artist : $"{media.Artist} — {media.Album}";
            MediaAppName.Text = media.AppName;
            if (!ReferenceEquals(media.Art, _shownArt))
            {
                _shownArt = media.Art;
                SetArt(MediaArtSmall, MediaArtSmallFallback, media.Art);
                SetArt(MediaArtLarge, MediaArtLargeFallback, media.Art);
                ArtBanner.Background = media.Art?.CreateBrush();
            }
            PlayPauseGlyph.Text = media.IsPlaying ? "" : "";
            PrevButton.IsEnabled = media.CanPrevious;
            NextButton.IsEnabled = media.CanNext;
            ProgressHit.Cursor = media.CanSeek ? Cursors.Hand : null;
        }

        _state.MediaPlaying = media?.IsPlaying == true;
        _mediaArtDecorator.UpdateState(media?.IsPlaying == true, IslandHidden, Math.Clamp(_settings.CoverSize, 64, 96), MediaArtLarge, MediaArtSmall);
        UpdateSecondActivity();
        UpdateMotionArtwork(media);
        ApplyTheme();
        UpdateEqualizer();
        if (_state.IsExpanded) Render();
    }

    private void UpdateEqualizer()
    {
        var playing = _media.Current?.IsPlaying == true;
        var needAudio = !IslandHidden && !_gameSuppressed && (playing || _voiceFocus || _aiPanel.IsSpeaking);
        SetEqualizerRunning(needAudio);
    }

    private void SetProgressFraction(double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        var spec = IslandSkinEngine.Current;
        var innerW = ProgressTrackBorder.ActualWidth - ProgressTrackBorder.BorderThickness.Left - ProgressTrackBorder.BorderThickness.Right;
        if (innerW <= 1) innerW = ProgressHit.ActualWidth > 1 ? ProgressHit.ActualWidth : 210;
        ProgressFill.Width = Math.Max(0, innerW * fraction);

        ProgressThumb.Visibility = spec.IsPixelMode ? Visibility.Collapsed : Visibility.Visible;
        if (ProgressHit.ActualWidth > 0 && !spec.IsPixelMode)
        {
            var thumbW = ProgressThumb.Width > 0 ? ProgressThumb.Width : 11;
            ProgressThumb.Margin = new Thickness(
                Math.Clamp(ProgressHit.ActualWidth * fraction - thumbW / 2, 0, Math.Max(0, ProgressHit.ActualWidth - thumbW)),
                0, 0, 0);
        }
    }

    private void UpdateProgress()
    {
        _lyricsPanel.Update(_media.Current);
        if (_media.Current is not { Duration.Ticks: > 0 } media) return;

        var position = media.EstimatePosition(DateTimeOffset.Now);
        ElapsedText.Text = FormatMediaTime(position);
        RemainingText.Text = "-" + FormatMediaTime(media.Duration - position);
        SetProgressFraction(position / media.Duration);
    }

    private void OnProgressMouseUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; // don't collapse the island
        if (_media.Current is not { CanSeek: true, Duration.Ticks: > 0 } media || ProgressHit.ActualWidth <= 0) return;

        var fraction = Math.Clamp(e.GetPosition(ProgressHit).X / ProgressHit.ActualWidth, 0, 1);
        SetProgressFraction(fraction);
        _media.Seek(media.Duration * fraction);
    }

    private static string FormatMediaTime(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    private void SetArt(Border target, TextBlock fallback, Artwork? art)
    {
        target.Background = art is null
            ? (Brush)FindResource("ArtPlaceholder")
            : art.CreateBrush();
        fallback.Visibility = art is null ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- real waveform: bars follow the output level (left / right peaks), 30 fps while visible.
    private readonly AudioMeter _meter = new();
    private readonly DispatcherTimer _meterTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly double[] _barLevels = new double[4];
    private bool _meterRunning;

    private void OnMeterTick(object? sender, EventArgs e)
    {
        var (left, right) = _meter.Read();
        static double Db(float peak) => peak <= 0.0001f ? 0 : Math.Clamp((20 * Math.Log10(peak) + 48) / 48, 0, 1);
        var l = Db(left);
        var r = Db(right);
        var combined = Math.Max(l, r);
        _audioPeak += (combined - _audioPeak) * 0.45;

        var t = _frameClock.Elapsed.TotalSeconds;
        var pixel = IslandSkinEngine.Current.IsPixelMode;

        if (_currentView == MediaView)
        {
            var i = 0;
            foreach (UIElement bar in Equalizer.Children)
            {
                var mix = i switch { 0 => l, 1 => 0.6 * l + 0.4 * r, 2 => 0.4 * l + 0.6 * r, _ => r };
                var wobble = 0.82 + 0.18 * Math.Sin(t * (7.3 + 2.9 * i) + i * 1.7);
                var target = Math.Clamp(mix * wobble, 0, 1);
                var rate = target > _barLevels[i] ? 0.65 : 0.18 + 0.05 * i;
                _barLevels[i] += (target - _barLevels[i]) * rate;
                var sy = 0.22 + 0.78 * _barLevels[i];
                if (pixel) sy = Math.Max(0.2, Math.Round(sy * 5.0) / 5.0);
                ((ScaleTransform)bar.RenderTransform).ScaleY = sy;
                i++;
            }
        }

        if (_voiceFocus && !_settings.ReduceMotion)
        {
            var voiceEnergy = _aiPanel.IsListening ? Math.Max(0.35, combined) : _aiPanel.IsSpeaking ? Math.Max(0.45, combined) : 0.22;
            for (var vi = 0; vi < _voiceWaveBars.Length; vi++)
            {
                var wave = 0.35 + 0.65 * Math.Abs(Math.Sin(t * 7.5 + vi * 0.62)) * voiceEnergy;
                if (_voiceWaveBars[vi].RenderTransform is ScaleTransform vst)
                    vst.ScaleY = Math.Clamp(wave, 0.22, 1.0);
            }
        }
    }

    private void SetMeterRunning(bool running)
    {
        if (running == _meterRunning) return;
        _meterRunning = running;
        if (running)
        {
            _meterTimer.Tick -= OnMeterTick;
            _meterTimer.Tick += OnMeterTick;
            _meterTimer.Start();
        }
        else
        {
            _meterTimer.Stop();
            _audioPeak = 0;
        }
    }

    private void SetEqualizerRunning(bool running)
    {
        running&=!_gameSuppressed;
        // Real waveform: a meter-driven loop instead of the looping animation.
        var real = running && _settings.RealWaveform;
        var meterWasRunning = _meterRunning;
        SetMeterRunning(real);
        if (meterWasRunning && !real)
        {
            Array.Clear(_barLevels);
            foreach (UIElement bar in Equalizer.Children)
                if (bar.RenderTransform is ScaleTransform rest) rest.ScaleY = 0.35;
        }
        if (real)
        {
            foreach (UIElement bar in Equalizer.Children)
            {
                if (bar.RenderTransform is not ScaleTransform scale)
                {
                    bar.RenderTransform = scale = new ScaleTransform(1, 0.35);
                    bar.RenderTransformOrigin = new Point(0.5, 0.5);
                }
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            }
            _equalizerRunning = false; // the animated variant is off
            return;
        }

        if (running == _equalizerRunning) return;
        _equalizerRunning = running;

        var i = 0;
        foreach (UIElement bar in Equalizer.Children)
        {
            if (bar.RenderTransform is not ScaleTransform scale)
            {
                scale = new ScaleTransform(1, 0.35);
                bar.RenderTransform = scale;
                bar.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            if (running)
            {
                // Different periods per bar so they never line up.
                var period = TimeSpan.FromMilliseconds(380 + 110 * (i % 3) + 45 * i);
                var bars = new DoubleAnimation(0.3, 1.0, period)
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                // Runs for as long as music plays: 30 fps looks the same for bars this size and
                // halves the redraws of the window.
                Timeline.SetDesiredFrameRate(bars, FrameRateCap / 2);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, bars);
            }
            else
            {
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleY = 0.35;
            }
            i++;
        }
    }

    // ---------------------------------------------------------------- motion artwork

    private readonly MotionArtworkService _motionArt = new();
    private CancellationTokenSource? _motionCancel;
    private string? _motionKey;
    private bool _motionReady;

    private void InitMotionArtwork()
    {
        MotionBanner.MediaOpened += (_, _) =>
        {
            _motionReady = true;
            MotionBanner.Visibility = Visibility.Visible;
            // The large artwork square mirrors the same video (no second decoder).
            MediaArtLarge.Background = new VisualBrush(MotionBanner) { Stretch = Stretch.UniformToFill };
            MediaArtLargeFallback.Visibility = Visibility.Collapsed;
            if (_state.IsExpanded) Render(); // the banner may have just become visible
            UpdateMotionPlayback();
        };
        MotionBanner.MediaEnded += (_, _) =>
        {
            MotionBanner.Position = TimeSpan.Zero; // loop
            MotionBanner.Play();
        };
        MotionBanner.MediaFailed += (_, e) =>
        {
            App.LogError(e.ErrorException);
            ClearMotion();
        };
    }

    /// <summary>Starts a lookup when the album changes (cached albums resolve immediately).</summary>
    private void UpdateMotionArtwork(MediaInfo? media)
    {
        if(_gameSuppressed)return;
        var key = media is null || !_settings.MotionArtwork
            ? null
            : $"{media.Artist}|{media.Album}|{(string.IsNullOrEmpty(media.Album) ? media.Title : "")}";
        if (key == _motionKey) return;

        _motionKey = key;
        _motionCancel?.Cancel();
        ClearMotion();
        if (key is null || media is null) return;

        var cancel = new CancellationTokenSource();
        _motionCancel = cancel;
        _ = LoadMotionAsync(media, key, cancel.Token);
    }

    private async Task LoadMotionAsync(MediaInfo media, string key, CancellationToken cancel)
    {
        string? file;
        try
        {
            file = await _motionArt.GetAsync(media.Artist, media.Album, media.Title, cancel);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (cancel.IsCancellationRequested || key != _motionKey || file is null) return;

        MotionBanner.Source = new Uri(file);
        MotionBanner.Play(); // opens the file; MediaOpened then pauses it unless the island is expanded
    }

    /// <summary>Decode video only while it can be seen.</summary>
    private void UpdateMotionPlayback()
    {
        if (!_motionReady) return;
        if (_state.IsExpanded && ArtBanner.Visibility == Visibility.Visible) MotionBanner.Play();
        else MotionBanner.Pause();
    }

    private void ClearMotion()
    {
        var wasShowing = _motionReady;
        _motionReady = false;
        MotionBanner.Stop();
        MotionBanner.Close();
        MotionBanner.Source = null;
        MotionBanner.Visibility = Visibility.Collapsed;
        if (wasShowing) SetArt(MediaArtLarge, MediaArtLargeFallback, _shownArt);
    }

    private void OnPreviousClick(object sender, RoutedEventArgs e) => _media.Previous();
    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => _media.TogglePlayPause();
    private void OnNextClick(object sender, RoutedEventArgs e) => _media.Next();

}
