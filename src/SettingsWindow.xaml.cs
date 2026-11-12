// D-Island - Copyright 2026 h7 (ibuildrigs) and contributors.
// Licensed under the D-Island License 1.0; see LICENSE.txt and NOTICE.txt.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DIsland.Services;

namespace DIsland;

/// <summary>Edits <see cref="AppSettings"/> live: every change is applied to the island immediately.</summary>
public partial class SettingsWindow : Window
{
    private static readonly Brush Secondary = Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Faint = Frozen(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Hover = Frozen(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Selected = Frozen(Color.FromArgb(0x3A, 0xFF, 0xFF, 0xFF));

    /// <summary>"1.0.0", from the assembly version (csproj &lt;Version&gt;).</summary>
    private static readonly string AppVersion =
        typeof(SettingsWindow).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "";

    private static readonly string[] Swatches =
    {
        "#0A84FF", "#5E5CE6", "#BF5AF2", "#FF375F", "#FF9F0A", "#FFD60A", "#30D158", "#64D2FF",
    };

    private static readonly (string Family, string Label)[] Fonts =
    {
        ("Segoe UI Variable Text", "Segoe UI Variable"),
        ("Segoe UI", "Segoe UI"),
        ("Helvetica", "Helvetica"),
        ("Bahnschrift", "Bahnschrift"),
        ("Cascadia Mono", "Cascadia Mono"),
    };

    private readonly AppSettings _settings;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private StackPanel? _materialOptions;
    private bool _loading;
    private int _groupId;

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        AssistantTheme.Apply(this);

        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            _settings.Save();
        };
        Closed += (_, _) =>
        {
            _settings.Save();
            if (_updateStatusRefresh is not null && App.Updates is { } updates) updates.Changed -= _updateStatusRefresh;
        };
        WheelScrolling.Attach(SettingsScroller, pixelsPerNotch: 36);
        WheelScrolling.Attach(NavScroller, pixelsPerNotch: 36);
        SettingsScroller.ScrollChanged += (_, _) => UpdateNavSelection();
        Loaded += (_, _) => UpdateNavSelection();

        CategorySelector.SelectionChanged+=(_,_)=> {
            if(CategorySelector.SelectedItem is ComboBoxItem selected && selected.Tag is TextBlock header)
            { var entry=_nav.FirstOrDefault(n=>n.Header==header); if(entry.Item is not null) SelectSection(header,entry.Item); }
        };
        Build();
    }

    // ---------------------------------------------------------------- layout

    private void Build()
    {
        _loading = true;
        Sections.Children.Clear();
        Nav.Children.Clear();
        CategorySelector.Items.Clear();
        _nav.Clear();
        _navSelected = null;
        var s = _settings;

        var design = Section("Хошино и оформление");
        var hero = new DockPanel { Margin = new Thickness(0,0,0,20) };
        var companion = new HoshinoCompanion(s) { Width=100, Height=100 };
        DockPanel.SetDock(companion, Dock.Right); hero.Children.Add(companion);
        var welcome = new StackPanel { VerticalAlignment=VerticalAlignment.Center };
        welcome.Children.Add(new TextBlock { Text="Hoshino Island", FontSize=26, FontWeight=FontWeights.SemiBold });
        welcome.Children.Add(new TextBlock { Text="Твой спокойный уголок на рабочем столе.", TextWrapping=TextWrapping.Wrap, Opacity=.65, Margin=new Thickness(0,8,12,0) });
        hero.Children.Add(welcome); design.Children.Add(hero);
        Choice(design, "Палитра", new[] { ("Hoshino", "Хошино"), ("Graphite", "Графит"), ("Abydos", "Абидос"), ("Custom", "Своя") }, () => s.DesignPreset, v => { s.DesignPreset=v; HoshinoPalette.Apply(s); });
        Note(design, "Розовый, небесный и золотой — цвета Хошино. Выбери настроение острова.");
        CustomDesignColors(design);
        Check(design, "Показывать Хошино", () => s.ShowHoshino, v => s.ShowHoshino=v);
        Check(design, "Живые состояния: сон, музыка и микрофон", () => s.AnimateHoshino, v => s.AnimateHoshino=v);
        Check(design, "Следить глазами за курсором", () => s.HoshinoFollowsCursor, v => s.HoshinoFollowsCursor=v);
        Check(design, "Уменьшить анимации", () => s.ReduceMotion, v => s.ReduceMotion=v);
        Check(design, "Компактные отступы", () => s.CompactDesign, v => s.CompactDesign=v);
        Slider(design, "Размер Хошино в чате", 72,140,4, () => s.CompanionSize, v => s.CompanionSize=v, v=>$"{v:0} px");
        Slider(design, "Ширина раскрытого островка",440,640,20,()=>s.PanelWidth,v=>s.PanelWidth=v,v=>$"{v:0} px");
        Slider(design, "Размер обложки",64,112,8,()=>s.CoverSize,v=>s.CoverSize=v,v=>$"{v:0} px");
        var lyricDesign=Section("Оформление лирики");
        Note(lyricDesign, "Автоматический поиск и провайдеры остаются включены. Здесь меняется только отображение.");
        Slider(lyricDesign,"Ширина островка с лирикой",300,620,20,()=>s.CompactLyricWidth,v=>s.CompactLyricWidth=v,v=>$"{v:0} px");
        Slider(lyricDesign,"Текст на островке",12,22,1,()=>s.LyricFontSize,v=>s.LyricFontSize=v,v=>$"{v:0} pt");
        Slider(lyricDesign,"Текст в плеере",12,24,1,()=>s.ExpandedLyricFontSize,v=>s.ExpandedLyricFontSize=v,v=>$"{v:0} pt");
        Slider(lyricDesign,"Яркость соседних строк",.15,.75,.05,()=>s.LyricInactiveOpacity,v=>s.LyricInactiveOpacity=v,v=>$"{v*100:0}%");
        Check(lyricDesign,"Две строки на островке",()=>s.TwoLineLyrics,v=>s.TwoLineLyrics=v);
        Check(lyricDesign,"Центрировать текст на островке",()=>s.CenterLyrics,v=>s.CenterLyrics=v);
        var obsidian = Section("Obsidian · память и синхронизация");
        obsidian.Children.Add(new ObsidianSettingsPanel());
        var assistant = Section("ASSISTANT");
        assistant.Children.Add(new AssistantSettingsPanel());
        var style = Section("STYLE");
        Choice(style, "Shape", new[] { (IslandMode.DynamicIsland, "Dynamic Island"), (IslandMode.Notch, "Notch") },
            () => s.Mode, v => s.Mode = v);
        Slider(style, "Size", 0.75, 1.5, 0.05, () => s.IslandScale, v => s.IslandScale = v, v => $"{v * 100:0}%");
        Slider(style, "Corner roundness", 0.2, 1, 0.05, () => s.CornerRoundness, v => s.CornerRoundness = v, v => $"{v * 100:0}%");
        Slider(style, "Outline width", 0, 3, 0.25, () => s.OutlineWidth, v => s.OutlineWidth = v, v => v == 0 ? "Off" : $"{v:0.##} px");
        Slider(style, "Outline opacity", 0, 1, 0.05, () => s.OutlineOpacity, v => s.OutlineOpacity = v, v => $"{v * 100:0}%");
        Slider(style, "Shadow", 0, 1, 0.05, () => s.ShadowStrength, v => s.ShadowStrength = v, v => v == 0 ? "Off" : $"{v * 100:0}%");
        Check(style, "Text shadow on see-through materials (keeps text readable)", () => s.TextShadow, v => s.TextShadow = v);

        var material = Section("MATERIAL");
        Choice(material, null, new[]
        {
            (IslandMaterial.Opaque, "Opaque"),
            (IslandMaterial.Translucent, "Translucent"),
            (IslandMaterial.LiquidGlass, "Liquid Glass"),
        }, () => s.Material, v => s.Material = v, rebuildMaterialOptions: true);
        _materialOptions = new StackPanel();
        material.Children.Add(_materialOptions);
        BuildMaterialOptions();

        var adaptive = Section("ADAPTIVE BACKGROUND");
        Check(adaptive, "Fade over the desktop; use the full material when an app is behind the island",
            () => s.AdaptiveBackground, v => s.AdaptiveBackground = v);
        Slider(adaptive, "Opacity over desktop", 0, 1, 0.05, () => s.DesktopOpacity, v => s.DesktopOpacity = v, v => $"{v * 100:0}%");
        Check(adaptive, "Always use the full material while expanded", () => s.SolidWhenExpanded, v => s.SolidWhenExpanded = v);

        var colors = Section("COLORS");
        Choice(colors, "Accent", new[]
        {
            (AccentSource.Auto, "Auto"),
            (AccentSource.MediaApp, "Media app"),
            (AccentSource.Custom, "Custom"),
            (AccentSource.Monochrome, "Mono"),
        }, () => s.AccentSource, v => s.AccentSource = v);

        CustomColorRow(colors);
        Slider(colors, "Background tint", 0, 0.5, 0.02, () => s.BackgroundTint, v => s.BackgroundTint = v, v => v == 0 ? "Off" : $"{v * 100:0}%");

        var text = Section("TEXT & CLOCK");
        Choice(text, "Font", Fonts.Select(f => (f.Family, f.Label)).ToArray(), () => s.FontFamily, v => s.FontFamily = v);
        Choice(text, "Clock", new[]
        {
            (ClockFormat.System, "System"),
            (ClockFormat.TwelveHour, "12-hour"),
            (ClockFormat.TwentyFourHour, "24-hour"),
        }, () => s.ClockFormat, v => s.ClockFormat = v);
        Check(text, "Show seconds", () => s.ShowSeconds, v => s.ShowSeconds = v);
        Check(text, "Show the date in the idle pill", () => s.ShowDateWhenIdle, v => s.ShowDateWhenIdle = v);
        Choice(text, "When idle", new[] { (IdleStyle.Clock, "Show clock"), (IdleStyle.Minimal, "Minimal pill") },
            () => s.IdleStyle, v => s.IdleStyle = v);

        var interaction = Section("INTERACTION");
        Check(interaction,"Игровой режим · скрывать остров поверх полноэкранных приложений",()=>s.GameModeEnabled,v=>s.GameModeEnabled=v);
        Note(interaction,"Останавливает анимации, лирику и захват фона, освобождает модели голоса и GPU. Выбранная фраза продолжает работать; для ответа модели загрузятся заново.");
        Check(interaction, "Expand on hover (click always works)", () => s.ExpandOnHover, v => s.ExpandOnHover = v);
        Slider(interaction, "Hover delay", 0, 1000, 10, () => s.HoverDelayMs, v => s.HoverDelayMs = (int)v, v => $"{v:0} ms");
        Slider(interaction, "Collapse delay", 0, 1500, 10, () => s.CollapseDelayMs, v => s.CollapseDelayMs = (int)v, v => $"{v:0} ms");

        var animation = Section("ANIMATION");
        Slider(animation, "Speed", 0.5, 2.5, 0.1, () => s.AnimationSpeed, v => s.AnimationSpeed = v, v => $"{v:0.0}×");
        Slider(animation, "Bounciness", 0, 1, 0.05, () => s.Bounciness, v => s.Bounciness = v, v => $"{v * 100:0}%");

        var media = Section("MEDIA");
        Check(media, "Show album art banner when expanded", () => s.ShowArtBanner, v => s.ShowArtBanner = v);
        Check(media, "Animated album art from Apple Music", () => s.MotionArtwork, v => s.MotionArtwork = v);
        Note(media,
            "Plays an album's looping motion artwork, when it has one, in the banner while expanded (works for " +
            "Spotify too if the album is on Apple Music). Uses Apple's unofficial web-player access, the same as " +
            "open-source downloaders, so it may stop working if Apple changes it. Each album is fetched once and " +
            "cached (up to 200 MB).");

        var activities = Section("ACTIVITIES");
        Check(activities, "Show a second activity as a bubble beside the island (e.g. music while a timer runs)",
            () => s.ShowSecondActivity, v => s.ShowSecondActivity = v);
        Check(activities, "Music bars follow the actual sound", () => s.RealWaveform, v => s.RealWaveform = v);
        Check(activities, "Clicking the music activity opens the music app (hover still expands)",
            () => s.ClickOpensMediaApp, v => s.ClickOpensMediaApp = v);

        var alarms = Section("ALARMS");
        AlarmList(alarms);

        var pages = Section("EXPANDED ISLAND");
        Choice(pages, "Navigation", new[] { (PageNavigation.Tabs, "Tabs"), (PageNavigation.Dots, "Page dots") },
            () => s.PageNavigation, v => s.PageNavigation = v);
        Note(pages, "The expanded island has pages: Home, Notifications, Calendar, To-Do, Notes, Shelf, Weather, Apps, " +
                    " Turn pages on or off in their sections below. " +
                    "Click a tab or dot, or scroll the mouse wheel over the island to switch.");
        Check(pages, "Show when an app is using the microphone or camera", () => s.PrivacyIndicator, v => s.PrivacyIndicator = v);

        var identity = Section("NOTIFICATIONS & WINDOWS CALENDAR", navLabel: "Notifications");
        IdentityStatus(identity);
        Check(identity, "Show other apps' notifications in the island", () => s.NotificationsEnabled, v => s.NotificationsEnabled = v);
        Check(identity, "Hide the message text (show only which app)", () => s.NotificationsHideText, v => s.NotificationsHideText = v);
        Slider(identity, "Notification alerts", 2, 12, 0.5, () => s.NotificationSeconds, v => s.NotificationSeconds = v, v => $"{v:0.0} s");
        Note(identity, "Alerts are held back while Do Not Disturb is on, like Windows does.");

        var calendar = Section("CALENDAR");
        Check(calendar, "Show the Calendar page", () => s.CalendarEnabled, v => s.CalendarEnabled = v);
        Check(calendar, "Include calendars added to Windows (Outlook, Calendar app accounts)", () => s.WindowsCalendar, v => s.WindowsCalendar = v);
        IcsLinks(calendar);
        Choice(calendar, "Remind me", new[] { (0, "Off"), (1, "1 min"), (5, "5 min"), (10, "10 min"), (15, "15 min") },
            () => s.CalendarReminderMinutes, v => s.CalendarReminderMinutes = v);

        var notes = Section("TO-DO & NOTES", navLabel: "To-Do & Notes");
        Check(notes, "Show the To-Do page", () => s.TodoEnabled, v => s.TodoEnabled = v);
        Check(notes, "Show the Notes page", () => s.NotesEnabled, v => s.NotesEnabled = v);
        Note(notes, "Click a text box in the island to type; press Esc or click anywhere else to give the keyboard " +
                    "back. Tasks and notes are saved only on this PC, as you type.");

        var shelf = Section("FILE SHELF");
        Check(shelf, "Show the Shelf page (drag files onto the island to keep them)", () => s.ShelfEnabled, v => s.ShelfEnabled = v);
        Check(shelf, "Keep files on the shelf after dragging them out", () => s.ShelfKeepAfterDrag, v => s.ShelfKeepAfterDrag = v);
        Note(shelf, "The shelf remembers where files are; it doesn't copy them. Double-click an item to open it.");

        var weather = Section("WEATHER");
        Check(weather, "Show the Weather page", () => s.WeatherEnabled, v => s.WeatherEnabled = v);
        Choice(weather, "Location", new[] { (true, "My location"), (false, "A city") },
            () => s.WeatherUseDeviceLocation, v => s.WeatherUseDeviceLocation = v);
        CitySearch(weather);
        Choice(weather, "Units", new[]
        {
            (TemperatureUnit.Auto, "Auto"),
            (TemperatureUnit.Celsius, "°C"),
            (TemperatureUnit.Fahrenheit, "°F"),
        }, () => s.TemperatureUnit, v => s.TemperatureUnit = v);
        Check(weather, "Show the weather next to the clock", () => s.WeatherInIdlePill, v => s.WeatherInIdlePill = v);
        Note(weather, "Forecasts from Open-Meteo (free, no account), and your town's name from OpenStreetMap. \"My location\" uses Windows location, rounded to " +
                      "about 1 km before it's sent.");

        var quickApps = Section("QUICK APPS");
        Note(quickApps, $"The Apps page. Up to {AppSettings.MaxQuickApps} apps from the Start menu.");
        Slider(quickApps, "Icon size", 16, 48, 2, () => s.QuickAppIconSize, v => s.QuickAppIconSize = v, v => $"{v:0} px");
        _quickAppsPanel = new StackPanel();
        quickApps.Children.Add(_quickAppsPanel);
        BuildQuickApps();

        var alerts = Section("ALERTS");
        Check(alerts, "Show when a device connects or disconnects (USB, Bluetooth, monitors)",
            () => s.DeviceAlerts, v => s.DeviceAlerts = v);
        Check(alerts, "Caps Lock / Num Lock", () => s.LockKeyAlerts, v => s.LockKeyAlerts = v);
        Check(alerts, "Microphone muted, Energy Saver, Wi-Fi and VPN changes", () => s.StatusAlerts, v => s.StatusAlerts = v);
        Check(alerts, "Do Not Disturb turning on or off", () => s.FocusAlerts, v => s.FocusAlerts = v);
        Check(alerts, "Copied to clipboard", () => s.CopyAlerts, v => s.CopyAlerts = v);
        Check(alerts, "Show the copied text in the alert", () => s.ClipboardPreview, v => s.ClipboardPreview = v);
        Check(alerts, "Pasted (Ctrl+V or Shift+Insert)", () => s.PasteAlerts, v => s.PasteAlerts = v);
        Note(alerts, "Password managers mark copied passwords as private; those are never shown. Pasting " +
                     "from a right-click menu can't be detected.");
        Slider(alerts, "Volume / brightness", 1, 5, 0.5, () => s.OsdSeconds, v => s.OsdSeconds = v, v => $"{v:0.0} s");
        Slider(alerts, "Battery & device alerts", 1, 8, 0.5, () => s.AlertSeconds, v => s.AlertSeconds = v, v => $"{v:0.0} s");

        var position = Section("POSITION");
        Slider(position, "Top offset", 0, 120, 1, () => s.TopOffset, v => s.TopOffset = v, v => $"{v:0} px");

        var system = Section("SYSTEM");
        HotkeyRow(system);
        Check(system, "Hide the Windows volume flyout", () => s.HideVolumeFlyout, v => s.HideVolumeFlyout = v);
        var startup = new CheckBox { Content = "Start with Windows", IsChecked = StartupManager.IsEnabled };
        startup.Click += (_, _) =>
        {
            try { StartupManager.SetEnabled(startup.IsChecked == true); }
            catch (Exception) { /* registry write blocked by policy */ }
            startup.IsChecked = StartupManager.IsEnabled;
        };
        system.Children.Add(startup);

        var updates = Section("UPDATES");
        Check(updates,"Проверять обновления ежедневно",()=>s.CheckForUpdates,v=>s.CheckForUpdates=v);
        UpdateControls(updates);

        var about = Section("ABOUT");
        var aboutHero = new DockPanel { Margin=new Thickness(0,0,0,20) };
        var mascot = new HoshinoCompanion(_settings) { Width=150, Height=150, Interactive=true, Playing=()=>true };
        DockPanel.SetDock(mascot,Dock.Right);aboutHero.Children.Add(mascot);
        var projectIdentity=new StackPanel { VerticalAlignment=VerticalAlignment.Center };
        projectIdentity.Children.Add(new TextBlock { Text="Hoshino Island", FontSize=30, FontWeight=FontWeights.SemiBold });
        projectIdentity.Children.Add(new TextBlock { Text="Takayom1 · сделано вместе с Codex", Opacity=.7, Margin=new Thickness(0,8,0,0) });
        projectIdentity.Children.Add(new TextBlock { Text="Твой маленький остров среди большого дня.", TextWrapping=TextWrapping.Wrap, Opacity=.6, Margin=new Thickness(0,8,0,0) });
        aboutHero.Children.Add(projectIdentity);about.Children.Add(aboutHero);
        Note(about,"Нажми на Хошино: она смутится, рассердится или снова станет обычной. Уменьшение анимаций учитывается.");
        var links = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        void Link(string text, string target)
        {
            var button = new Button { Style = (Style)FindResource("ChipButton"), Content = text, Margin = new Thickness(0, 0, 8, 8) };
            button.Click += (_, _) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); }
                catch (Exception) { /* no app for the file type */ }
            };
            links.Children.Add(button);
        }
        string Doc(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, name);
        Link("GitHub · Takayom1", "https://github.com/Takayom1");
        Link("Telegram · @takayom1", "https://t.me/takayom1");
        Link("MIT · наши компоненты",Doc("LICENSE-HOSHINO-MIT.txt"));
        Link("Лицензии и авторство",Doc("LICENSING.md"));
        Link("Приватность",Doc("PRIVACY.txt"));
        about.Children.Add(links);
        var credits=new StackPanel { Margin=new Thickness(0,20,0,0) };
        credits.Children.Add(new TextBlock { Text="Основа и зависимости",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8) });
        credits.Children.Add(new TextBlock { Text="Based on D-Island by h7 (ibuildrigs) - https://github.com/H7gamer/d-island",TextWrapping=TextWrapping.Wrap,FontSize=12 });
        credits.Children.Add(new TextBlock { Text="Модификация Takayom1 с помощью Codex. Не является официальным выпуском D-Island. Унаследованные части: D-Island License 1.0; наши самостоятельные компоненты: MIT. Права на Blue Archive и персонажа принадлежат их правообладателям.",TextWrapping=TextWrapping.Wrap,Opacity=.65,FontSize=12,Margin=new Thickness(0,8,0,0) });
        about.Children.Add(credits);
        CreditText.Text="Hoshino Island · Takayom1";

        _loading = false;
        Dispatcher.InvokeAsync(UpdateNavSelection, DispatcherPriority.Loaded); // after layout
    }

    /// <summary>The slider(s) that apply to the chosen material.</summary>
    private void BuildMaterialOptions()
    {
        if (_materialOptions is null) return;
        var wasLoading = _loading;
        _loading = true;
        _materialOptions.Children.Clear();
        var s = _settings;
        switch (s.Material)
        {
            case IslandMaterial.Translucent:
                Slider(_materialOptions, "Opacity", 0.15, 1, 0.05, () => s.BackgroundOpacity, v => s.BackgroundOpacity = v, v => $"{v * 100:0}%");
                break;
            case IslandMaterial.LiquidGlass:
                Slider(_materialOptions, "Blur", 0, 1, 0.05, () => s.GlassBlur, v => s.GlassBlur = v,
                    v => v == 0 ? "Clear" : v >= 1 ? "Frosted" : $"{v * 100:0}%");
                Slider(_materialOptions, "Shininess", 0, 1, 0.05, () => s.GlassShine, v => s.GlassShine = v,
                    v => v == 0 ? "Matte" : $"{v * 100:0}%");
                Slider(_materialOptions, "Tint", 0, 0.9, 0.02, () => s.GlassTint, v => s.GlassTint = v, v => v == 0 ? "None" : $"{v * 100:0}%");
                Choice(_materialOptions, "Blur engine", new[]
                {
                    (GlassEngine.Auto, "Auto"),
                    (GlassEngine.Gpu, "GPU"),
                    (GlassEngine.ScreenCapture, "Screen capture"),
                }, () => s.GlassEngine, v => s.GlassEngine = v);
                Note(_materialOptions,
                    "GPU: Windows draws what's behind the island and the GPU blurs it. Near-zero CPU, and the island " +
                    "stays visible in screenshots. It uses an undocumented Windows API, so it's tested on each Windows " +
                    "build before use. Screen capture: works everywhere, but uses more CPU and hides the island from " +
                    "screenshots while blurring. Auto picks GPU when the test passed.");
                break;
            default:
                Note(_materialOptions, "Solid background.");
                break;
        }
        _loading = wasLoading;
    }

    // ---------------------------------------------------------------- updates

    private Action? _updateStatusRefresh;

    /// <summary>Current version, update status, and "Check for updates" / "Install now".</summary>
    private void UpdateControls(Panel panel)
    {
        var status = new TextBlock { Foreground = Secondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6) };
        var check = new Button { Style = (Style)FindResource("ChipButton"), Content = "Проверить и скачать обновление", Margin = new Thickness(0, 0, 8, 0) };
        var install = new Button { Style = (Style)FindResource("ChipButton"), Content = "Установить", Margin = new Thickness(0, 0, 8, 0) };
        var open = new Button { Style = (Style)FindResource("ChipButton"), Content = "Страница выпуска", Margin = new Thickness(0, 0, 8, 0) };
        var whatsNew = new Button { Style = (Style)FindResource("ChipButton"), Content = "What's new", Margin = new Thickness(0) };
        whatsNew.Visibility=Visibility.Collapsed;
        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 4), Children = { check, install, open, whatsNew } };
        panel.Children.Add(status);
        panel.Children.Add(row);

        void Refresh()
        {
            var u = App.Updates;
            var current = $"You have version {UpdateService.CurrentVersion}.";
            status.Text = u?.State switch
            {
                UpdateState.Checking => $"{current} Checking…",
                UpdateState.UpToDate => $"{current} It's the latest.",
                
                UpdateState.Available => $"{current} Version {u.Latest?.Version} is available.",
                UpdateState.Downloading => $"{current} Downloading version {u.Latest?.Version}…",
                UpdateState.ReadyToInstall =>
                    $"{current} Version {u.Latest?.Version} is downloaded and verified. Click Install now.",
                UpdateState.Installing => $"Installing version {u.Latest?.Version}…",
                UpdateState.Failed => $"{current} Update check failed: {u.Error}.",
                _ => _settings.LastUpdateCheck is { } last ? $"{current} Last checked {last.ToLocalTime():g}." : current,
            };
            check.IsEnabled = u is not null && u.State is not (UpdateState.Checking or UpdateState.Downloading or UpdateState.Installing);
            install.Visibility = u?.State == UpdateState.ReadyToInstall ? Visibility.Visible : Visibility.Collapsed;
            open.Visibility = u?.Latest is not null && u.Latest.Version > UpdateService.CurrentVersion ? Visibility.Visible : Visibility.Collapsed;
        }

        check.Click += async (_, _) =>
        {
            if (App.Updates is { } u) await u.CheckAsync(manual: true);
        };
        install.Click += (_, _) => {try {App.Updates?.Install();}catch(Exception e){MessageBox.Show(e.Message,"Обновление");}};
        open.Click += (_, _) =>
        {
            var url = App.Updates?.Latest?.PageUrl ?? $"https://github.com/{UpdateService.Repository}/releases";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        };

        if (_updateStatusRefresh is not null && App.Updates is { } previous) previous.Changed -= _updateStatusRefresh;
        _updateStatusRefresh = () => Dispatcher.InvokeAsync(Refresh);
        if (App.Updates is { } updates) updates.Changed += _updateStatusRefresh;
        Refresh();
    }

    // ---------------------------------------------------------------- alarms

    /// <summary>Alarms: time (HH:mm), daily or once, on/off, label; plus "Add alarm".</summary>
    private void AlarmList(Panel panel)
    {
        Note(panel, "Alarms ring in the island (with sound) while D-Island is running. Click the island to stop one.");
        var list = new StackPanel();
        panel.Children.Add(list);

        void Save() => Update(() => _settings.Alarms = _settings.Alarms.ToList()); // notify + save

        void Fill()
        {
            list.Children.Clear();
            foreach (var alarm in _settings.Alarms)
            {
                var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                var remove = SmallButton("", "Delete alarm");
                remove.Click += (_, _) =>
                {
                    _settings.Alarms.Remove(alarm);
                    Save();
                    Fill();
                };
                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);

                var enabled = new CheckBox { IsChecked = alarm.Enabled, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                enabled.Click += (_, _) => { alarm.Enabled = enabled.IsChecked == true; Save(); };
                DockPanel.SetDock(enabled, Dock.Left);
                row.Children.Add(enabled);

                var time = new TextBox { Text = alarm.Time, Width = 64, ToolTip = "24-hour time, e.g. 07:30" };
                time.LostFocus += (_, _) =>
                {
                    if (TimeSpan.TryParse(time.Text.Trim(), out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1))
                    {
                        alarm.Time = $"{t.Hours:00}:{t.Minutes:00}";
                        alarm.Enabled = true;
                        enabled.IsChecked = true;
                        Save();
                    }
                    time.Text = alarm.Time;
                };
                DockPanel.SetDock(time, Dock.Left);
                row.Children.Add(time);

                var daily = new CheckBox { Content = "Daily", IsChecked = alarm.Daily, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                daily.Click += (_, _) => { alarm.Daily = daily.IsChecked == true; Save(); };
                DockPanel.SetDock(daily, Dock.Left);
                row.Children.Add(daily);

                var label = new TextBox { Text = alarm.Label, ToolTip = "Label (optional)" };
                label.LostFocus += (_, _) => { alarm.Label = label.Text.Trim(); Save(); };
                row.Children.Add(label);
                list.Children.Add(row);
            }
        }
        Fill();

        var add = new Button { Style = (Style)FindResource("ChipButton"), Content = "Add alarm", Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += (_, _) =>
        {
            var next = DateTime.Now.AddHours(1);
            _settings.Alarms.Add(new Alarm { Time = $"{next.Hour:00}:00" });
            Save();
            Fill();
        };
        panel.Children.Add(add);
    }

    // ---------------------------------------------------------------- identity & calendar links

    /// <summary>Whether the one-time setup is done, with a button to run it.</summary>
    private void IdentityStatus(Panel panel)
    {
        if (PackageIdentity.HasIdentity)
        {
            string access;
            try
            {
                access = Windows.UI.Notifications.Management.UserNotificationListener.Current.GetAccessStatus() switch
                {
                    Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Allowed => "notification access allowed",
                    Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Denied =>
                        "notification access denied: allow it in Windows Settings > Privacy & security > Notifications",
                    _ => "Windows will ask for notification access",
                };
            }
            catch (Exception)
            {
                access = "notification access unknown";
            }
            Note(panel, $"Set up ✓ ({access}).");
            return;
        }

        Note(panel, "Windows only lets apps read notifications and the Windows calendar if they have a package " +
                    "identity. The one-time setup registers one for D-Island: it downloads Microsoft's packaging " +
                    "tools (about 100 MB, once), creates a signing certificate, and Windows asks for permission once " +
                    "to trust it. D-Island restarts when it's done.");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        var setup = new Button { Style = (Style)FindResource("ChipButton"), Content = "Set up…", Margin = new Thickness(0) };
        setup.Click += (_, _) =>
        {
            PackageIdentity.RunSetup();
            setup.IsEnabled = false;
            setup.Content = "Running… (a console window shows progress)";
        };
        row.Children.Add(setup);
        if (System.IO.File.Exists(PackageIdentity.SetupLog))
        {
            var log = new Button { Style = (Style)FindResource("ChipButton"), Content = "Last setup log", Margin = new Thickness(8, 0, 0, 0) };
            log.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(PackageIdentity.SetupLog) { UseShellExecute = true });
            row.Children.Add(log);
        }
        panel.Children.Add(row);
    }

    /// <summary>The list of iCal links with remove buttons, and a box to add one.</summary>
    private void IcsLinks(Panel panel)
    {
        Note(panel, "iCal links: Google Calendar > Settings > your calendar > \"Secret address in iCal format\"; " +
                    "Outlook.com > Settings > Shared calendars > Publish > ICS; iCloud > Share calendar > Public.");
        var list = new StackPanel();
        panel.Children.Add(list);

        void Fill()
        {
            list.Children.Clear();
            foreach (var url in _settings.CalendarIcsUrls)
            {
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var remove = SmallButton("", "Remove");
                remove.Click += (_, _) =>
                {
                    Update(() => _settings.CalendarIcsUrls = _settings.CalendarIcsUrls.Where(u => u != url).ToList());
                    Fill();
                };
                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
                // Show only the host: the rest of a secret address is a password.
                var host = Uri.TryCreate(url.Replace("webcal://", "https://"), UriKind.Absolute, out var uri) ? uri.Host : url;
                row.Children.Add(new TextBlock { Text = $"{host}  (link hidden)", VerticalAlignment = VerticalAlignment.Center, Foreground = Secondary });
                list.Children.Add(row);
            }
        }
        Fill();

        var add = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        add.ColumnDefinitions.Add(new ColumnDefinition());
        add.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var box = new TextBox { ToolTip = "Paste an iCal / webcal link and press Enter" };
        var button = new Button { Style = (Style)FindResource("ChipButton"), Content = "Add", Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(button, 1);
        add.Children.Add(box);
        add.Children.Add(button);
        panel.Children.Add(add);

        void Add()
        {
            var url = box.Text.Trim();
            if (!(url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)
                  || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                || _settings.CalendarIcsUrls.Contains(url))
                return;
            Update(() => _settings.CalendarIcsUrls = _settings.CalendarIcsUrls.Append(url).ToList());
            box.Text = "";
            Fill();
        }
        button.Click += (_, _) => Add();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) Add();
        };
    }

    // ---------------------------------------------------------------- weather city

    /// <summary>Search box + results; picking one switches weather to that city.</summary>
    private void CitySearch(Panel panel)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var current = new TextBlock
        {
            Text = _settings.WeatherPlace is { Length: > 0 } place ? place : "No city chosen",
            Foreground = Secondary,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        row.Children.Add(current);
        var box = new TextBox { ToolTip = "Type a city and press Enter" };
        Grid.SetColumn(box, 1);
        row.Children.Add(box);
        panel.Children.Add(row);

        var results = new StackPanel { Margin = new Thickness(150, 0, 0, 0) };
        panel.Children.Add(results);
        CancellationTokenSource? pending = null;

        async void Search()
        {
            var query = box.Text.Trim();
            if (query.Length < 2) return;
            pending?.Cancel();
            var cancel = pending = new CancellationTokenSource();
            results.Children.Clear();
            results.Children.Add(new TextBlock { Text = "Searching…", Foreground = Faint });
            IReadOnlyList<Place> places;
            try
            {
                places = await WeatherService.SearchAsync(query, cancel.Token);
            }
            catch (Exception)
            {
                if (cancel.IsCancellationRequested) return;
                places = Array.Empty<Place>();
            }
            if (cancel.IsCancellationRequested) return;
            results.Children.Clear();
            if (places.Count == 0) results.Children.Add(new TextBlock { Text = "No places found", Foreground = Faint });
            foreach (var p in places)
            {
                var item = new Border
                {
                    Background = Brushes.Transparent,
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 4, 8, 4),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Child = new TextBlock
                    {
                        Inlines =
                        {
                            new System.Windows.Documents.Run(p.Name),
                            new System.Windows.Documents.Run($"  {p.Detail}") { Foreground = Secondary },
                        },
                    },
                };
                item.MouseEnter += (_, _) => item.Background = Hover;
                item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
                item.MouseLeftButtonUp += (_, _) =>
                {
                    Update(() =>
                    {
                        _settings.WeatherPlace = p.Name;
                        _settings.WeatherLatitude = p.Latitude;
                        _settings.WeatherLongitude = p.Longitude;
                        _settings.WeatherUseDeviceLocation = false;
                    });
                    Build(); // reflect "A city" and the new name
                };
                results.Children.Add(item);
            }
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) Search();
        };
    }

    // ---------------------------------------------------------------- hide / show shortcut

    /// <summary>Click the box and press a combination; Backspace or Delete clears it.</summary>
    private void HotkeyRow(Panel panel)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new TextBlock { Text = "Hide / show island", VerticalAlignment = VerticalAlignment.Center });

        var box = new TextBox
        {
            Text = string.IsNullOrEmpty(_settings.ToggleHotkey) ? "None" : _settings.ToggleHotkey,
            IsReadOnly = true,
            IsReadOnlyCaretVisible = false,
            Width = 160,
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Click, then press a shortcut (with Ctrl, Alt, Shift or Win). Backspace clears it.",
        };
        var problem = new TextBlock { Foreground = Faint, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        void ShowProblem()
        {
            problem.Text = _settings.HotkeyProblem ?? "";
            problem.Visibility = _settings.HotkeyProblem is null ? Visibility.Collapsed : Visibility.Visible;
        }

        box.GotKeyboardFocus += (_, _) => box.Text = "Press a shortcut…";
        box.LostKeyboardFocus += (_, _) =>
            box.Text = string.IsNullOrEmpty(_settings.ToggleHotkey) ? "None" : _settings.ToggleHotkey;
        box.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            string? text;
            if (key is System.Windows.Input.Key.Back or System.Windows.Input.Key.Delete)
                text = "";
            else if (key is System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl
                     or System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt
                     or System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift
                     or System.Windows.Input.Key.LWin or System.Windows.Input.Key.RWin)
                return; // wait for the actual key
            else if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.None)
                return; // a bare key would take over normal typing everywhere
            else
                text = Hotkey.Format(System.Windows.Input.Keyboard.Modifiers, key);

            Update(() => _settings.ToggleHotkey = text);
            box.Text = text.Length == 0 ? "None" : text;
            ShowProblem();
            System.Windows.Input.Keyboard.ClearFocus();
        };
        Grid.SetColumn(box, 1);
        row.Children.Add(box);
        panel.Children.Add(row);
        panel.Children.Add(problem);
        ShowProblem();
    }

    // ---------------------------------------------------------------- quick apps

    private StackPanel? _quickAppsPanel;
    private IReadOnlyList<InstalledApp>? _installedApps;

    /// <summary>The chosen apps (reorder / remove) and an "Add app" picker.</summary>
    private void BuildQuickApps()
    {
        if (_quickAppsPanel is null) return;
        _quickAppsPanel.Children.Clear();
        var apps = _settings.QuickApps;

        for (var i = 0; i < apps.Count; i++)
        {
            var index = i;
            var id = apps[i];
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };

            var remove = SmallButton("", "Remove");
            remove.Click += (_, _) => EditQuickApps(list => list.RemoveAt(index));
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);

            var up = SmallButton("", "Move left");
            up.IsEnabled = index > 0;
            up.Click += (_, _) => EditQuickApps(list => (list[index - 1], list[index]) = (list[index], list[index - 1]));
            DockPanel.SetDock(up, Dock.Right);
            row.Children.Add(up);

            var icon = new Image { Source = QuickAppsService.GetIcon(id, 32), Width = 20, Height = 20, Margin = new Thickness(0, 0, 10, 0) };
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            row.Children.Add(new TextBlock
            {
                Text = QuickAppsService.GetName(id) ?? "(not installed)",
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            _quickAppsPanel.Children.Add(row);
        }

        if (apps.Count >= AppSettings.MaxQuickApps) return;

        var picker = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
        var add = new Button
        {
            Style = (Style)FindResource("ChipButton"),
            Content = "Add app…",
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        add.Click += async (_, _) =>
        {
            add.Visibility = Visibility.Collapsed;
            picker.Visibility = Visibility.Visible;
            await BuildPickerAsync(picker);
        };
        _quickAppsPanel.Children.Add(add);
        _quickAppsPanel.Children.Add(picker);
    }

    private async Task BuildPickerAsync(StackPanel picker)
    {
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 6) };
        var results = new StackPanel();
        picker.Children.Add(search);
        var resultsScroller = new ScrollViewer
        {
            MaxHeight = 260,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = results,
        };
        WheelScrolling.Attach(resultsScroller, pixelsPerNotch: 36);
        picker.Children.Add(resultsScroller);
        results.Children.Add(new TextBlock { Text = "Loading apps…", Foreground = Faint });
        search.Focus();

        _installedApps ??= await QuickAppsService.ListAsync();

        void Fill()
        {
            results.Children.Clear();
            var query = search.Text.Trim();
            // Icons are extracted per row, so cap the list; typing narrows it.
            var matches = _installedApps
                .Where(a => !_settings.QuickApps.Contains(a.Id)
                            && (query.Length == 0 || a.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
                .Take(40)
                .ToList();
            if (matches.Count == 0) results.Children.Add(new TextBlock { Text = "No matching apps", Foreground = Faint });
            foreach (var app in matches)
            {
                // A plain row rather than a Button: the shared button template centers its content.
                var item = new Border
                {
                    Background = Brushes.Transparent,
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 4, 8, 4),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Child = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children =
                        {
                            new Image { Source = QuickAppsService.GetIcon(app.Id, 32), Width = 20, Height = 20, Margin = new Thickness(0, 0, 10, 0) },
                            new TextBlock { Text = app.Name, VerticalAlignment = VerticalAlignment.Center },
                        },
                    },
                };
                item.MouseEnter += (_, _) => item.Background = Hover;
                item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
                item.MouseLeftButtonUp += (_, _) => EditQuickApps(list => list.Add(app.Id));
                results.Children.Add(item);
            }
        }

        search.TextChanged += (_, _) => Fill();
        Fill();
    }

    private void EditQuickApps(Action<List<string>> edit)
    {
        var list = new List<string>(_settings.QuickApps);
        edit(list);
        Update(() => _settings.QuickApps = list.Take(AppSettings.MaxQuickApps).ToList());
        BuildQuickApps();
    }

    private Button SmallButton(string glyph, string tooltip) => new()
    {
        Style = (Style)FindResource("IconButton"),
        Width = 30,
        Height = 26,
        ToolTip = tooltip,
        Content = new TextBlock { Style = (Style)FindResource("Glyph"), Text = glyph, FontSize = 11 },
    };

    private void CustomColorRow(Panel panel)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        foreach (var hex in Swatches)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var swatch = new Button
            {
                Style = (Style)FindResource("IconButton"),
                Width = 28,
                Height = 28,
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = hex,
                Content = new Ellipse { Width = 18, Height = 18, Fill = new SolidColorBrush(color) },
            };
            swatch.Click += (_, _) => SetCustomColor(hex);
            row.Children.Add(swatch);
        }

        var box = new TextBox { Text = _settings.CustomAccent, Width = 90, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        box.TextChanged += (_, _) =>
        {
            if (_loading) return;
            try
            {
                if (ColorConverter.ConvertFromString(box.Text.Trim()) is Color)
                    SetCustomColor(box.Text.Trim(), updateBox: false);
            }
            catch (FormatException) { /* still typing */ }
        };
        row.Children.Add(box);
        _customColorBox = box;
        panel.Children.Add(row);
    }

    private TextBox? _customColorBox;

    private void SetCustomColor(string hex, bool updateBox = true)
    {
        Update(() =>
        {
            _settings.CustomAccent = hex;
            _settings.AccentSource = AccentSource.Custom;
        });
        if (updateBox && _customColorBox is not null)
        {
            _loading = true;
            _customColorBox.Text = hex;
            _loading = false;
        }
        // Reflect the switch to "Custom" in the accent choice.
        Build();
    }

    // ---------------------------------------------------------------- builders

    private readonly List<(TextBlock Header, Border Item)> _nav = new();
    private Border? _navSelected;

    private void CustomDesignColors(StackPanel panel)
    {
        var row=new WrapPanel { Margin=new Thickness(0,8,0,12) };
        TextBox Field(string title,string value) { var col=new StackPanel{Margin=new Thickness(0,0,10,0)}; col.Children.Add(new TextBlock{Text=title,Opacity=.65,Margin=new Thickness(0,0,0,5)}); var box=new TextBox{Text=value,Width=112}; col.Children.Add(box); row.Children.Add(col); return box; }
        var accent=Field("Акцент",_settings.DesignAccent); var bg=Field("Фон",_settings.DesignBackground); var text=Field("Текст",_settings.DesignText);
        var apply=new Button{Content="Применить цвета",Margin=new Thickness(0,16,0,0)};
        var feedback=new TextBlock{TextWrapping=TextWrapping.Wrap,Opacity=.65};
        apply.Click+=(_,_)=> { if(new[]{accent.Text,bg.Text,text.Text}.Any(v=>!System.Text.RegularExpressions.Regex.IsMatch(v,"^#[0-9a-fA-F]{6}$"))) { feedback.Text="Введите цвета в формате #RRGGBB."; return; } _settings.DesignPreset="Custom"; _settings.DesignAccent=accent.Text; _settings.DesignBackground=bg.Text; _settings.DesignText=text.Text; HoshinoPalette.Apply(_settings); Update(() => {}); feedback.Text="Своя палитра применена."; };
        row.Children.Add(apply);panel.Children.Add(row);panel.Children.Add(feedback);
    }

    private static string SectionName(string title) => title switch
    {
        "MATERIAL" => "Материал", "ADAPTIVE BACKGROUND" => "Прозрачность", "COLORS" => "Цвета", "INTERACTION" => "Управление",
        "ASSISTANT" => "Помощник и аккаунт", "STYLE" => "Внешний вид", "TEXT & CLOCK" => "Текст и часы", "ANIMATION" => "Анимации", "ANIMATIONS" => "Анимации",
        "MEDIA" => "Музыка и текст песни", "ACTIVITIES" => "Таймер и секундомер", "ALARMS" => "Будильники", "WALLPAPER ENGINE" => "Wallpaper Engine", "EXPANDED ISLAND" => "Вкладки островка",
        "NOTIFICATIONS & WINDOWS CALENDAR" => "Уведомления Windows", "CALENDAR" => "Календарь", "TO-DO & NOTES" => "Задачи и заметки", "FILE SHELF" => "Полка файлов", "WEATHER" => "Погода",
        "QUICK APPS" => "Приложения", "ALERTS" => "Сигналы", "POSITION" => "Расположение", "SYSTEM" => "Система", "UPDATES" => "Обновления", "ABOUT" => "О программе", _ => title
    };
    private StackPanel Section(string title, string? navLabel = null)
    {
        var header = new TextBlock { Text = SectionName(title), FontSize = 25, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 18) };
        var panel = new StackPanel();
        var content = new StackPanel(); content.Children.Add(header); content.Children.Add(panel);
        var card = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(22), Margin = new Thickness(0, 18, 0, 0), Child = content, Visibility = Visibility.Collapsed };
        card.SetResourceReference(Border.BackgroundProperty, "HoshinoSurface"); card.SetResourceReference(Border.BorderBrushProperty,"HoshinoBorder"); header.Tag = card; Sections.Children.Add(card); AddNavItem(SectionName(title), header); return panel;
    }

    /// <summary>A sidebar entry that scrolls its section's header to the top.</summary>
    private void AddNavItem(string title, TextBlock header)
    {
        var item = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 2),
            Background = Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = new TextBlock
            {
                Text = title,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
        };
        item.MouseEnter += (_, _) => { if (item != _navSelected) item.Background = Hover; };
        item.MouseLeave += (_, _) => { if (item != _navSelected) item.Background = Brushes.Transparent; };
        item.MouseLeftButtonUp += (_, _) =>
        {
            SelectSection(header, item);
        };
        Nav.Children.Add(item);
        _nav.Add((header, item));
        CategorySelector.Items.Add(new ComboBoxItem { Content=title, Tag=header });
    }

    /// <summary>Highlights the sidebar entry of the section at the top of the view.</summary>
    private void SelectSection(TextBlock header, Border item)
    {
        foreach (var entry in _nav) if (entry.Header.Tag is Border card) card.Visibility = entry.Header == header ? Visibility.Visible : Visibility.Collapsed;
        if (_navSelected is not null) _navSelected.Background = Brushes.Transparent;
        _navSelected = item; item.Background = Selected; SettingsScroller.ScrollToTop();
    }
    private void UpdateNavSelection()
    {
        if (_navSelected is null && _nav.Count > 0 && IsLoaded) { SelectSection(_nav[0].Header, _nav[0].Item); CategorySelector.SelectedIndex=0; }
    }

    private static string LabelName(string value) => value switch
    {
        "Shape" => "Форма",
        "Size" => "Размер",
        "Corner roundness" => "Скругление углов",
        "Outline width" => "Толщина контура",
        "Outline opacity" => "Яркость контура",
        "Shadow" => "Тень",
        "Text shadow on see-through materials (keeps text readable)" => "Тень текста на прозрачной поверхности",
        "Fade over the desktop; use the full material when an app is behind the island" => "Приглушать на рабочем столе; полный материал поверх приложения",
        "Opacity over desktop" => "Яркость на рабочем столе",
        "Always use the full material while expanded" => "Полный материал в раскрытом виде",
        "Accent" => "Акцент",
        "Background tint" => "Оттенок фона",
        "Font" => "Шрифт",
        "Clock" => "Формат часов",
        "Show seconds" => "Показывать секунды",
        "Show the date in the idle pill" => "Показывать дату в компактном виде",
        "When idle" => "Без активных задач",
        "Expand on hover (click always works)" => "Раскрывать при наведении",
        "Hover delay" => "Задержка наведения",
        "Collapse delay" => "Задержка сворачивания",
        "Speed" => "Скорость",
        "Bounciness" => "Упругость",
        "Show album art banner when expanded" => "Баннер обложки в раскрытом виде",
        "Animated album art from Apple Music" => "Анимированные обложки Apple Music",
        "Show a second activity as a bubble beside the island (e.g. music while a timer runs)" => "Вторая активность рядом с островком",
        "Music bars follow the actual sound" => "Индикатор музыки следует звуку",
        "Clicking the music activity opens the music app (hover still expands)" => "Нажатие на музыку открывает плеер",
        "Show wallpaper controls when Wallpaper Engine is running" => "Показывать управление Wallpaper Engine",
        "Mute the wallpaper while music is playing" => "Приглушать обои во время музыки",
        "Navigation" => "Навигация",
        "Show when an app is using the microphone or camera" => "Показывать использование микрофона и камеры",
        "Show other apps' notifications in the island" => "Показывать входящие уведомления в островке",
        "Hide the message text (show only which app)" => "Скрывать текст уведомлений",
        "Notification alerts" => "Длительность уведомления",
        "Show the Calendar page" => "Вкладка календаря",
        "Include calendars added to Windows (Outlook, Calendar app accounts)" => "Подключить системный календарь Windows",
        "Remind me" => "Напоминать за",
        "Show the To-Do page" => "Вкладка задач",
        "Show the Notes page" => "Вкладка заметок",
        "Show the Shelf page (drag files onto the island to keep them)" => "Вкладка полки файлов",
        "Keep files on the shelf after dragging them out" => "Оставлять файлы на полке после перетаскивания",
        "Show the Weather page" => "Вкладка погоды",
        "Location" => "Местоположение",
        "Units" => "Единицы",
        "Show the weather next to the clock" => "Погода рядом с часами",
        "Icon size" => "Размер значков",
        "Show when a device connects or disconnects (USB, Bluetooth, monitors)" => "Подключение и отключение устройств",
        "Microphone muted, Energy Saver, Wi-Fi and VPN changes" => "Микрофон, энергосбережение, Wi-Fi и VPN",
        "Do Not Disturb turning on or off" => "Переключение «Не беспокоить»",
        "Copied to clipboard" => "Копирование в буфер",
        "Show the copied text in the alert" => "Показывать скопированный текст",
        "Pasted (Ctrl+V or Shift+Insert)" => "Вставка (Ctrl+V или Shift+Insert)",
        "Volume / brightness" => "Громкость / яркость",
        "Battery & device alerts" => "Батарея и устройства",
        "Top offset" => "Отступ сверху",
        "Hide the Windows volume flyout" => "Скрыть системную панель громкости",
        "Opacity" => "Прозрачность",
        "Blur" => "Размытие",
        "Shininess" => "Блики",
        "Tint" => "Оттенок",
        "Blur engine" => "Движок размытия",
        "Dynamic Island" => "Остров",
        "Notch" => "Вырез",
        "Opaque" => "Непрозрачный",
        "Translucent" => "Полупрозрачный",
        "Show clock" => "Показывать часы",
        "Minimal pill" => "Минимальный вид",
        "System" => "Системный",
        "Tabs" => "Вкладки",
        "Page dots" => "Точки",
        "Off" => "Выкл.",
        _ => value
    };

    private static void Note(Panel panel, string text) =>
        panel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = Faint,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 4),
        });

    private void Check(Panel panel, string label, Func<bool> get, Action<bool> set)
    {
        var box = new CheckBox { IsChecked = get(), Content = new TextBlock { Text = LabelName(label), TextWrapping = TextWrapping.Wrap } };
        box.Click += (_, _) => Update(() => set(box.IsChecked == true));
        panel.Children.Add(box);
    }

    private void Choice<T>(Panel panel, string? label, (T Value, string Text)[] options, Func<T> get, Action<T> set,
        bool rebuildMaterialOptions = false)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = label is null ? new GridLength(0) : new GridLength(150) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        if (label is not null)
            row.Children.Add(new TextBlock { Text = LabelName(label), VerticalAlignment = VerticalAlignment.Center });

        var group = $"g{_groupId++}";
        var wrap = new WrapPanel();
        foreach (var (value, text) in options)
        {
            var radio = new RadioButton
            {
                GroupName = group,
                Content = LabelName(text),
                IsChecked = EqualityComparer<T>.Default.Equals(get(), value),
            };
            radio.Checked += (_, _) =>
            {
                if (_loading) return;
                Update(() => set(value));
                if (rebuildMaterialOptions) BuildMaterialOptions();
            };
            wrap.Children.Add(radio);
        }
        Grid.SetColumn(wrap, 1);
        row.Children.Add(wrap);
        panel.Children.Add(row);
    }

    private void Slider(Panel panel, string label, double min, double max, double step,
        Func<double> get, Action<double> set, Func<double, string> format)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = get(),
            TickFrequency = step,
            IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
        };
        var value = new TextBlock
        {
            Text = format(slider.Value),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Secondary,
        };
        slider.ValueChanged += (_, e) =>
        {
            value.Text = format(e.NewValue);
            Update(() => set(e.NewValue));
        };

        var text = new TextBlock { Text = LabelName(label), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,10,0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(value, 2);
        row.Children.Add(text);
        row.Children.Add(slider);
        row.Children.Add(value);
        panel.Children.Add(row);
    }

    private void Update(Action apply)
    {
        if (_loading) return;
        apply();
        _settings.NotifyChanged();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        _settings.ResetToDefaults();
        Build();
        _settings.NotifyChanged();
        _settings.Save();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}

