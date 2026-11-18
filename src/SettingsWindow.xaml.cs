// D-Island - Copyright 2026 h7 (ibuildrigs) and contributors.
// Licensed under the D-Island License 1.0; see LICENSE.txt and NOTICE.txt.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DIsland.Services;

namespace DIsland;

/// <summary>
/// Two-pane skin-adaptive SettingsWindow ("Твой остров") across all 5 visual skins
/// (Horus Glass, Shittim Chest, Win7 Aero, Chibi Pop, Pixel Tamagotchi).
/// Preserves all core, T01, T02, T03, and Ghost/Tray settings with live preview.
/// </summary>
public partial class SettingsWindow : Window
{
    private static Brush Secondary => (Brush?)Application.Current?.Resources["HoshinoMuted"] ?? Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
    private static Brush Faint => (Brush?)Application.Current?.Resources["HoshinoMuted"] ?? Frozen(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
    private static Brush Hover => (Brush?)Application.Current?.Resources["HoshinoControlHover"] ?? Frozen(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));

    private static readonly string AppVersion =
        typeof(SettingsWindow).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.17.0";

    private readonly AppSettings _settings;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly List<NavEntry> _nav = new();

    private StackPanel? _horusPaletteOptions;
    private StackPanel? _quickAppsPanel;
    private IReadOnlyList<InstalledApp>? _installedApps;
    private Action? _previewRefresh;
    private Action? _updateStatusRefresh;
    private string _selectedSectionKey = "Appearance";
    private bool _loading;
    private int _groupId;

    private sealed record NavEntry(
        string Key,
        string Title,
        Grid PageWrapper,
        Border NavPill,
        Border NavShadow,
        TextBlock NavLabel,
        ContentControl NavIconHost);

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
            if (_updateStatusRefresh is not null && App.Updates is { } updates)
                updates.Changed -= _updateStatusRefresh;
        };

        WheelScrolling.Attach(SettingsScroller, pixelsPerNotch: 36);
        WheelScrolling.Attach(NavScroller, pixelsPerNotch: 36);
        SourceInitialized += (_, _) => ApplyWindowThemeChrome();
        SizeChanged += (_, _) => DwmWindowBlur.Apply(this, IslandSkinEngine.Current, cornerRadiusDip: 12);
        StateChanged += (_, _) => ApplyWindowThemeChrome();
        Build();
    }

    private static MainWindow? GetMainWindow() =>
        Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            e.Handled = true;
            return;
        }
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;
            try { DragMove(); }
            catch (InvalidOperationException) { }
        }
    }

    private void OnResizeThumbDragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (WindowState == WindowState.Maximized) return;
        Width = Math.Max(MinWidth, Width + e.HorizontalChange);
        Height = Math.Max(MinHeight, Height + e.VerticalChange);
    }

    private void ApplyWindowThemeChrome()
    {
        HoshinoPalette.Apply(_settings);
        SettingsSkinPresenter.ApplyWindowChrome(
            this, WindowOuterGrid, WindowCartoonShadow, RootShell, HeaderBar, TopAccentBar,
            HeaderTitleText, CaptionButtonsHost, BgChromeCanvas, SidebarPane, FooterBar, DoneButton, _settings);
        RefreshNavStyles();
        _previewRefresh?.Invoke();
    }

    // ---------------------------------------------------------------- 2-pane build

    private void Build()
    {
        _loading = true;
        Sections.Children.Clear();
        Nav.Children.Clear();
        _nav.Clear();
        var s = _settings;

        HoshinoPalette.Apply(s);

        // 1. Оформление и скины
        var appearance = CreatePage("Appearance", "Оформление", "Пять визуальных тем, палитры Хошино, аксессуары и часы.");
        var (previewCard, refreshPreview) = SettingsSkinPresenter.CreateLivePreviewCard(s);
        _previewRefresh = refreshPreview;
        appearance.Children.Add(previewCard);

        var skinCard = SubCard(appearance, "Скин острова и окон");
        skinCard.Children.Add(SettingsSkinPresenter.BuildSkinSelectorGrid(s, picked =>
        {
            Update(() => s.IslandSkin = picked);
            Build();
        }));
        _horusPaletteOptions = new StackPanel();
        skinCard.Children.Add(_horusPaletteOptions);
        BuildHorusPaletteOptions();

        var accCard = SubCard(appearance, "Образ и атмосфера");
        Choice(accCard, "Аксессуар Хошино", HoshinoAccessories.Choices, () => s.HoshinoAccessory, v =>
        {
            s.HoshinoAccessory = v;
            _previewRefresh?.Invoke();
        });
        Check(accCard, "Тематические обложки треков (CD-диск Aero / Винил Chibi / Кассета Pixel)", () => s.ThemedMediaCovers, v => s.ThemedMediaCovers = v);
        Check(accCard, "Погодная кромка острова (снежный иней и капли дождя)", () => s.WeatherRimEnabled, v => s.WeatherRimEnabled = v);

        var clockCard = SubCard(appearance, "Часы и капсула покоя");
        Note(clockCard, "Шрифт и физика прозрачности / размытия подбираются автоматически под выбранную тему.");
        Choice(clockCard, "Формат времени", new[]
        {
            (ClockFormat.System, "Системный"),
            (ClockFormat.TwelveHour, "12 часов"),
            (ClockFormat.TwentyFourHour, "24 часа"),
        }, () => s.ClockFormat, v => s.ClockFormat = v);
        Choice(clockCard, "В режиме покоя", new[]
        {
            (IdleStyle.Clock, "Часы и Хошино"),
            (IdleStyle.Minimal, "Минимальная капсула"),
        }, () => s.IdleStyle, v => s.IdleStyle = v);
        Check(clockCard, "Показывать секунды", () => s.ShowSeconds, v => s.ShowSeconds = v);
        Check(clockCard, "Показывать дату рядом с часами", () => s.ShowDateWhenIdle, v => s.ShowDateWhenIdle = v);

        // 2. Остров, вырез и Режим «Призрак»
        var islandGhost = CreatePage("IslandGhost", "Остров, вырез и Призрак", "Геометрия плавающего острова и верхнего выреза, экран и сквозной клик.");
        var shapeCard = SubCard(islandGhost, "Форма и геометрия");
        Choice(shapeCard, "Режим формы", new[]
        {
            (IslandMode.DynamicIsland, "Плавающий остров"),
            (IslandMode.Notch, "Верхний вырез (Notch)"),
        }, () => s.Mode, v =>
        {
            s.Mode = v;
            _previewRefresh?.Invoke();
        });
        Check(shapeCard, "Периметр фокуса (живой таймер по кантику острова)", () => s.PerimeterFocusTimer, v => s.PerimeterFocusTimer = v);
        Choice(shapeCard, "Монитор острова",
            new[] { ("", "Основной монитор") }
                .Concat(System.Windows.Forms.Screen.AllScreens.Select((screen, i) =>
                    (screen.DeviceName, $"Экран {i + 1}" + (screen.Primary ? " · основной" : ""))))
                .ToArray(),
            () => s.MonitorDeviceName, v => s.MonitorDeviceName = v);
        Note(shapeCard, "Размеры, пропорции и скругление острова зафиксированы строго по эталонному дизайну выбранной темы.");
        Slider(shapeCard, "Отступ сверху экрана", 0, 120, 1, () => s.TopOffset, v => s.TopOffset = v, v => $"{v:0} px");

        var ghostCard = SubCard(islandGhost, "Режим «Призрак» (Click-Through)");
        Note(ghostCard, "Пропускает 100% кликов и прокрутки мыши сквозь свёрнутый остров или вырез прямо во вкладки браузера, редактора и игры.");
        BuildGhostModeControls(ghostCard);

        var motionCard = SubCard(islandGhost, "Наведение, игры и пружина");
        Check(motionCard, "Игровой режим · скрывать остров поверх полноэкранных игр", () => s.GameModeEnabled, v => s.GameModeEnabled = v);
        Check(motionCard, "Раскрывать при наведении курсора (клик работает всегда)", () => s.ExpandOnHover, v => s.ExpandOnHover = v);
        Slider(motionCard, "Задержка раскрытия", 0, 1000, 10, () => s.HoverDelayMs, v => s.HoverDelayMs = (int)v, v => $"{v:0} мс");
        Slider(motionCard, "Задержка сворачивания", 0, 1500, 10, () => s.CollapseDelayMs, v => s.CollapseDelayMs = (int)v, v => $"{v:0} мс");
        Slider(motionCard, "Скорость анимации", 0.5, 2.5, 0.1, () => s.AnimationSpeed, v => s.AnimationSpeed = v, v => $"{v:0.0}×");
        Slider(motionCard, "Упругость пружины", 0, 1, 0.05, () => s.Bounciness, v => s.Bounciness = v, v => $"{v * 100:0}%");

        // 3. Живая Хошино и Коворкинг
        var hoshinoFocus = CreatePage("HoshinoFocus", "Живая Хошино и Фокус", "Поведение компаньона, поглаживание, мини-океанариум Абидоса и таймеры.");
        var compCard = SubCard(hoshinoFocus, "Таканаши Хошино");
        Check(compCard, "Показывать Хошино на островке", () => s.ShowHoshino, v => { s.ShowHoshino = v; _previewRefresh?.Invoke(); });
        Check(compCard, "Показывать нимб Хошино", () => s.ShowHoshinoHalo, v => { s.ShowHoshinoHalo = v; _previewRefresh?.Invoke(); });
        Check(compCard, "Поглаживание по макушке (румянец и тёплые реплики)", () => s.EnableHeadpat, v => s.EnableHeadpat = v);
        Check(compCard, "Живые реакции: пружинящий хохолок под музыку, дрёма zZz и радость по задачам", () => s.AnimateHoshino, v => s.AnimateHoshino = v);
        Check(compCard, "Следить гетерохромными глазами за курсором", () => s.HoshinoFollowsCursor, v => s.HoshinoFollowsCursor = v);
        Check(compCard, "Мини-океанариум Абидоса в режиме покоя", () => s.AbydosAquarium, v => s.AbydosAquarium = v);
        Check(compCard, "Уменьшить анимации (экономия энергии)", () => s.ReduceMotion, v => s.ReduceMotion = v);
        Slider(compCard, "Размер Хошино в AI-чате", 72, 140, 4, () => s.CompanionSize, v => s.CompanionSize = v, v => $"{v:0} px");

        var focusCard = SubCard(hoshinoFocus, "Вторая активность и будильники");
        Check(focusCard, "Показывать вторую активность отдельным пузырьком / брелоком Тамагочи рядом с капсулой", () => s.ShowSecondActivity, v => s.ShowSecondActivity = v);
        AlarmList(focusCard);

        // 4. Музыка и Лирика LRC
        var mediaLyrics = CreatePage("MediaLyrics", "Музыка и Лирика LRC", "Плеер, обложки треков и синхронный текст песен.");
        var playerCard = SubCard(mediaLyrics, "Плеер и обложки");
        Slider(playerCard, "Размер обложки в плеере", 64, 96, 4, () => s.CoverSize, v => s.CoverSize = v, v => $"{v:0} px");
        Check(playerCard, "Полоски эквалайзера двигаются в такт реальному звуку", () => s.RealWaveform, v => s.RealWaveform = v);
        Check(playerCard, "Клик по компактному плееру открывает музыкальное приложение", () => s.ClickOpensMediaApp, v => s.ClickOpensMediaApp = v);
        Check(playerCard, "Показывать баннер обложки в раскрытом виде", () => s.ShowArtBanner, v => s.ShowArtBanner = v);
        Check(playerCard, "Анимированные обложки Apple Music", () => s.MotionArtwork, v => s.MotionArtwork = v);

        var lrcCard = SubCard(mediaLyrics, "Синхронная лирика LRC");
        Note(lrcCard, "Включается кнопкой LRC в карточке плеера на главной вкладке.");
        Slider(lrcCard, "Ширина капсулы с лирикой", 300, 620, 20, () => s.CompactLyricWidth, v => s.CompactLyricWidth = v, v => $"{v:0} px");
        Slider(lrcCard, "Размер текста на островке", 12, 22, 1, () => s.LyricFontSize, v => s.LyricFontSize = v, v => $"{v:0} pt");
        Slider(lrcCard, "Размер текста в раскрытом плеере", 12, 24, 1, () => s.ExpandedLyricFontSize, v => s.ExpandedLyricFontSize = v, v => $"{v:0} pt");
        Slider(lrcCard, "Яркость соседних строк", 0.15, 0.75, 0.05, () => s.LyricInactiveOpacity, v => s.LyricInactiveOpacity = v, v => $"{v * 100:0}%");
        Check(lrcCard, "Две строки на свёрнутом островке", () => s.TwoLineLyrics, v => s.TwoLineLyrics = v);
        Check(lrcCard, "Центрировать строки лирики", () => s.CenterLyrics, v => s.CenterLyrics = v);

        // 5. Системный трей и Уведомления
        var traySystem = CreatePage("TraySystem", "Трей и Сигналы", "Иконочный Bento-пульт трея, жесты мыши, автозапуск и системные уведомления.");
        var trayCard = SubCard(traySystem, "Системный трей Хошино");
        Choice(trayCard, "Одиночный клик ЛКМ", new[]
        {
            ("Flyout", "Иконочный пульт"),
            ("ToggleIsland", "Скрыть / показать"),
            ("AiChat", "Открыть AI-чат"),
            ("Settings", "Настройки"),
        }, () => s.TrayLeftClickAction, v => s.TrayLeftClickAction = v);
        Choice(trayCard, "Средний клик СКМ", new[]
        {
            ("PlayPause", "Play / Pause музыки"),
            ("GhostMode", "Режим «Призрак»"),
            ("ToggleIsland", "Скрыть / показать"),
        }, () => s.TrayMiddleClickAction, v => s.TrayMiddleClickAction = v);
        HotkeyRow(trayCard, "Скрыть / показать остров", () => s.ToggleHotkey, v => s.ToggleHotkey = v, () => s.HotkeyProblem);
        Check(trayCard, "Скрыть стандартный ползунок громкости Windows", () => s.HideVolumeFlyout, v => s.HideVolumeFlyout = v);

        var startup = new CheckBox { Content = "Запускать HoshiBar вместе с Windows", IsChecked = StartupManager.IsEnabled };
        startup.Click += (_, _) =>
        {
            try { StartupManager.SetEnabled(startup.IsChecked == true); }
            catch (Exception) { }
            startup.IsChecked = StartupManager.IsEnabled;
        };
        trayCard.Children.Add(startup);

        var alertsCard = SubCard(traySystem, "Системные сигналы острова");
        Check(alertsCard, "Подключение и отключение устройств (USB, Bluetooth, мониторы)", () => s.DeviceAlerts, v => s.DeviceAlerts = v);
        Check(alertsCard, "Индикация Caps Lock / Num Lock", () => s.LockKeyAlerts, v => s.LockKeyAlerts = v);
        Check(alertsCard, "Статус микрофона, энергосбережения, Wi-Fi и VPN", () => s.StatusAlerts, v => s.StatusAlerts = v);
        Check(alertsCard, "Режим «Не беспокоить» Windows", () => s.FocusAlerts, v => s.FocusAlerts = v);
        Check(alertsCard, "Копирование в буфер обмена", () => s.CopyAlerts, v => s.CopyAlerts = v);
        Check(alertsCard, "Показывать первую строку скопированного текста", () => s.ClipboardPreview, v => s.ClipboardPreview = v);
        Check(alertsCard, "Вставка (Ctrl+V / Shift+Insert)", () => s.PasteAlerts, v => s.PasteAlerts = v);
        Slider(alertsCard, "Длительность громкости / яркости", 1, 5, 0.5, () => s.OsdSeconds, v => s.OsdSeconds = v, v => $"{v:0.0} с");
        Slider(alertsCard, "Длительность системных тостов", 1, 8, 0.5, () => s.AlertSeconds, v => s.AlertSeconds = v, v => $"{v:0.0} с");

        // 6. Помощник AI и Голос
        var assistantPage = CreatePage("Assistant", "Помощник AI и Голос", "CLI-нейросети (Codex, Gemini CLI, Claude Code), голос Хошино (RVC), Whisper и фраза «Хей Хошино».");
        var aiCard = SubCard(assistantPage, null);
        aiCard.Children.Add(new AssistantSettingsPanel());

        // 7. Obsidian · Память
        var obsidianPage = CreatePage("Obsidian", "Obsidian · Память", "Хранилище заметок, дневника и общих задач с Хошино.");
        var obsCard = SubCard(obsidianPage, null);
        obsCard.Children.Add(new ObsidianSettingsPanel());

        // 8. Виджеты и Полка файлов
        var widgetsPage = CreatePage("Widgets", "Виджеты и Полка", "Вкладки раскрытого острова, погода, календарь, полка с Wi-Fi QR и быстрые приложения.");
        var navModeCard = SubCard(widgetsPage, "Навигация и приватность");
        Choice(navModeCard, "Стиль шапки", new[]
        {
            (PageNavigation.Tabs, "Значки вкладок"),
            (PageNavigation.Dots, "Компактные точки"),
        }, () => s.PageNavigation, v => s.PageNavigation = v);
        Check(navModeCard, "Показывать точку активности микрофона и камеры", () => s.PrivacyIndicator, v => s.PrivacyIndicator = v);
        Check(navModeCard, "Вкладка задач (To-Do)", () => s.TodoEnabled, v => s.TodoEnabled = v);
        Check(navModeCard, "Вкладка быстрых заметок", () => s.NotesEnabled, v => s.NotesEnabled = v);

        var shelfCard = SubCard(widgetsPage, "Полка файлов и Wi-Fi QR");
        Check(shelfCard, "Включить вкладку Полки (перетаскивание файлов + передача на телефон по QR)", () => s.ShelfEnabled, v => s.ShelfEnabled = v);
        Check(shelfCard, "Оставлять файлы на полке после перетаскивания наружу", () => s.ShelfKeepAfterDrag, v => s.ShelfKeepAfterDrag = v);

        var weatherCard = SubCard(widgetsPage, "Погода (Open-Meteo)");
        Check(weatherCard, "Показывать вкладку погоды", () => s.WeatherEnabled, v => s.WeatherEnabled = v);
        Check(weatherCard, "Показывать погоду рядом с часами в свёрнутой капсуле", () => s.WeatherInIdlePill, v => s.WeatherInIdlePill = v);
        Choice(weatherCard, "Локация", new[] { (true, "Геолокация ПК"), (false, "Выбранный город") }, () => s.WeatherUseDeviceLocation, v => s.WeatherUseDeviceLocation = v);
        CitySearch(weatherCard);
        Choice(weatherCard, "Единицы", new[]
        {
            (TemperatureUnit.Auto, "Авто"),
            (TemperatureUnit.Celsius, "°C"),
            (TemperatureUnit.Fahrenheit, "°F"),
        }, () => s.TemperatureUnit, v => s.TemperatureUnit = v);

        var calNotifCard = SubCard(widgetsPage, "Уведомления Windows и Календарь");
        IdentityStatus(calNotifCard);
        Check(calNotifCard, "Показывать уведомления других приложений на островке", () => s.NotificationsEnabled, v => s.NotificationsEnabled = v);
        Check(calNotifCard, "Скрывать текст сообщений в уведомлениях", () => s.NotificationsHideText, v => s.NotificationsHideText = v);
        Slider(calNotifCard, "Время показа уведомления", 2, 12, 0.5, () => s.NotificationSeconds, v => s.NotificationSeconds = v, v => $"{v:0.0} с");
        Check(calNotifCard, "Включить вкладку Календаря и Дневника", () => s.CalendarEnabled, v => s.CalendarEnabled = v);
        Check(calNotifCard, "Подключить системные календари Windows", () => s.WindowsCalendar, v => s.WindowsCalendar = v);
        IcsLinks(calNotifCard);
        Choice(calNotifCard, "Напоминание до встречи", new[] { (0, "Выкл"), (1, "1 мин"), (5, "5 мин"), (10, "10 мин"), (15, "15 мин") },
            () => s.CalendarReminderMinutes, v => s.CalendarReminderMinutes = v);

        var appsCard = SubCard(widgetsPage, "Быстрый запуск приложений");
        Slider(appsCard, "Размер значков приложений", 16, 48, 2, () => s.QuickAppIconSize, v => s.QuickAppIconSize = v, v => $"{v:0} px");
        _quickAppsPanel = new StackPanel();
        appsCard.Children.Add(_quickAppsPanel);
        BuildQuickApps();

        // 9. О программе и Обновления
        var aboutPage = CreatePage("About", "О программе", "Версия острова, обновления и открытые лицензии.");
        var aboutCard = SubCard(aboutPage, null);
        var aboutHero = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var mascot = new HoshinoCompanion(_settings) { Width = 136, Height = 136, Interactive = true, Playing = () => true };
        DockPanel.SetDock(mascot, Dock.Right);
        aboutHero.Children.Add(mascot);

        var projectIdentity = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        projectIdentity.Children.Add(new TextBlock { Text = $"HoshiBar · {AppVersion}", FontSize = 26, FontWeight = FontWeights.SemiBold });
        projectIdentity.Children.Add(new TextBlock { Text = "Takayom1 · твой спокойный островок среди большого дня.", Opacity = 0.72, Margin = new Thickness(0, 6, 12, 0), TextWrapping = TextWrapping.Wrap });
        Note(projectIdentity, "Нажми на Хошино справа — она смутится, нахмурится или улыбнётся.");
        aboutHero.Children.Add(projectIdentity);
        aboutCard.Children.Add(aboutHero);

        var updCard = SubCard(aboutPage, "Обновления");
        Check(updCard, "Проверять обновления раз в день", () => s.CheckForUpdates, v => s.CheckForUpdates = v);
        UpdateControls(updCard);

        var licCard = SubCard(aboutPage, "Лицензии и авторство");
        var links = new WrapPanel { Margin = new Thickness(0, 2, 0, 12) };
        void Link(string text, string target)
        {
            var button = new Button { Style = (Style)FindResource("ChipButton"), Content = text, Margin = new Thickness(0, 0, 8, 8) };
            button.Click += (_, _) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); }
                catch (Exception) { }
            };
            links.Children.Add(button);
        }
        string Doc(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, name);
        Link("GitHub · Takayom1", "https://github.com/Takayom1");
        Link("Telegram · @takayom1", "https://t.me/takayom1");
        Link("MIT · наши компоненты", Doc("LICENSE-HOSHIBAR-MIT.txt"));
        Link("Лицензии и авторство", Doc("LICENSING.md"));
        Link("Приватность", Doc("PRIVACY.txt"));
        licCard.Children.Add(links);
        licCard.Children.Add(new TextBlock
        {
            Text = "Based on D-Island by h7 (ibuildrigs) - https://github.com/H7gamer/d-island",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
        });
        licCard.Children.Add(new TextBlock
        {
            Text = "Модификация Takayom1. Не является официальным выпуском D-Island. Унаследованные части распространяются по D-Island License 1.0; самостоятельные модули HoshiBar — по лицензии MIT. Права на Blue Archive и персонажа принадлежат их правообладателям.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.68,
            FontSize = 12,
            Margin = new Thickness(0, 6, 0, 0),
        });

        CreditText.Text = $"HoshiBar · {AppVersion} · Takayom1";

        _loading = false;
        ApplyWindowThemeChrome();
        SelectSectionByKey(_selectedSectionKey);
    }

    private void BuildHorusPaletteOptions()
    {
        if (_horusPaletteOptions is null) return;
        var wasLoading = _loading;
        _loading = true;
        _horusPaletteOptions.Children.Clear();
        var s = _settings;

        if (s.IslandSkin == IslandSkin.HorusGlass)
        {
            var lbl = new TextBlock { Text = "Лорная палитра Horus Glass", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 4) };
            _horusPaletteOptions.Children.Add(lbl);
            _horusPaletteOptions.Children.Add(SettingsSkinPresenter.BuildLorePaletteStrip(s, picked =>
            {
                Update(() => s.DesignPreset = picked);
                Build();
            }));
            Slider(_horusPaletteOptions, "Оттенок акцента в фоне", 0, 0.45, 0.03, () => s.BackgroundTint, v =>
            {
                s.BackgroundTint = v;
                ApplyWindowThemeChrome();
            }, v => v == 0 ? "Выкл" : $"{v * 100:0}%");

            if (IslandSkinPresets.NormalizePreset(s.DesignPreset) == "Custom")
                CustomDesignColors(_horusPaletteOptions);
        }
        else
        {
            var desc = s.IslandSkin switch
            {
                IslandSkin.ShittimChest => "Матовая молочно-голубая прозрачность планшета Сенсея (Schale OS · шрифт Exo 2) без стеклянных бликов.",
                IslandSkin.Win7Aero => "Футуристичное оптическое сапфировое стекло Windows 7 Aero с живым размытием, диагональными гранями и Start-Orb.",
                IslandSkin.ChibiPop => "Плоский пастельный 2D стикер-скин «Постеры Юмэ» (шрифт Rounded Mplus 1c) без размытия и с чётким контуром.",
                IslandSkin.PixelTamagotchi => "Пиксельный ретро-гаджет со ступенчатым корпусом, мятным LCD-экраном Тамагочи и чётким читаемым шрифтом Bahnschrift.",
                _ => "",
            };
            if (desc.Length > 0) Note(_horusPaletteOptions, desc);
        }
        _loading = wasLoading;
    }

    private void BuildGhostModeControls(Panel panel)
    {
        var mw = GetMainWindow();
        var activeBox = new CheckBox
        {
            Content = "Включить режим «Призрак» прямо сейчас",
            IsChecked = mw?.IsGhostMode == true,
        };
        activeBox.Click += (_, _) =>
        {
            GetMainWindow()?.SetGhostMode(activeBox.IsChecked == true);
        };
        panel.Children.Add(activeBox);

        Slider(panel, "Прозрачность в режиме «Призрак»", 0.25, 0.85, 0.05,
            () => _settings.GhostModeOpacity,
            v =>
            {
                _settings.GhostModeOpacity = v;
                if (GetMainWindow() is { IsGhostMode: true } w) w.SetGhostMode(true, w.GhostModeUntil is { } u ? u - DateTime.Now : null);
            },
            v => $"{v * 100:0}%");

        HotkeyRow(panel, "Хоткей режима «Призрак»", () => _settings.GhostModeHotkey, v => _settings.GhostModeHotkey = v, () => _settings.GhostHotkeyProblem);

        var quickRow = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var fiveMin = new Button
        {
            Style = (Style)FindResource("ChipButton"),
            Content = "Призрак на 5 минут",
            Margin = new Thickness(0, 0, 8, 4),
        };
        fiveMin.Click += (_, _) =>
        {
            GetMainWindow()?.SetGhostMode(true, TimeSpan.FromMinutes(5));
            activeBox.IsChecked = true;
        };
        quickRow.Children.Add(fiveMin);
        panel.Children.Add(quickRow);
    }

    // ---------------------------------------------------------------- page & card builders

    private StackPanel CreatePage(string key, string title, string subtitle)
    {
        var pageStack = new StackPanel();
        var heading = new StackPanel { Margin = new Thickness(2, 0, 0, 16) };
        heading.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = subtitle, FontSize = 12.5, Foreground = Secondary, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        pageStack.Children.Add(heading);

        var wrapper = new Grid { Visibility = Visibility.Collapsed };
        wrapper.Children.Add(pageStack);
        Sections.Children.Add(wrapper);

        // Sidebar item with unclipped RenderTransform sticker shadow for Chibi Pop
        var navCell = new Grid { Margin = new Thickness(0, 0, 0, 5) };
        var navShadow = new Border
        {
            Background = IslandSkinEngine.Frozen(Color.FromRgb(0x2A, 0x1E, 0x2E)),
            RenderTransform = new TranslateTransform(2.5, 2.5),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        var navPill = new Border
        {
            Padding = new Thickness(12, 9, 12, 9),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Transparent,
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var iconHost = new ContentControl { VerticalAlignment = VerticalAlignment.Center };
        var navLabel = new TextBlock { Text = title, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        row.Children.Add(iconHost);
        row.Children.Add(navLabel);
        navPill.Child = row;

        navCell.Children.Add(navShadow);
        navCell.Children.Add(navPill);

        var entry = new NavEntry(key, title, wrapper, navPill, navShadow, navLabel, iconHost);
        navPill.MouseEnter += (_, _) => { if (_selectedSectionKey != key) navPill.Background = Hover; };
        navPill.MouseLeave += (_, _) => { if (_selectedSectionKey != key) navPill.Background = Brushes.Transparent; };
        navPill.MouseLeftButtonUp += (_, _) => SelectSectionByKey(key);

        Nav.Children.Add(navCell);
        _nav.Add(entry);
        return pageStack;
    }

    private StackPanel SubCard(Panel page, string? heading)
    {
        var spec = IslandSkinEngine.Current;
        var outer = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        var shadow = new Border
        {
            CornerRadius = new CornerRadius(spec.IsPixelMode ? 2 : 16),
            Background = IslandSkinEngine.Frozen(Color.FromRgb(0x2A, 0x1E, 0x2E)),
            RenderTransform = new TranslateTransform(3.5, 3.5),
            Visibility = spec.IsCartoonSticker ? Visibility.Visible : Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        var card = new Border
        {
            CornerRadius = new CornerRadius(spec.IsPixelMode ? 2 : 16),
            BorderThickness = new Thickness(spec.CardBorderThickness),
            Padding = new Thickness(18, 15, 18, 15),
        };
        card.SetResourceReference(Border.BackgroundProperty, "HoshinoSurface");
        card.SetResourceReference(Border.BorderBrushProperty, "HoshinoBorder");

        var body = new StackPanel();
        if (!string.IsNullOrEmpty(heading))
        {
            body.Children.Add(new TextBlock
            {
                Text = heading,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10),
            });
        }

        card.Child = body;
        outer.Children.Add(shadow);
        outer.Children.Add(card);
        page.Children.Add(outer);
        return body;
    }

    private void SelectSectionByKey(string key)
    {
        if (_nav.Count == 0) return;
        var target = _nav.FirstOrDefault(n => n.Key == key) ?? _nav[0];
        _selectedSectionKey = target.Key;

        foreach (var n in _nav)
            n.PageWrapper.Visibility = n.Key == _selectedSectionKey ? Visibility.Visible : Visibility.Collapsed;

        RefreshNavStyles();
        SettingsScroller.ScrollToTop();
    }

    private void RefreshNavStyles()
    {
        var spec = IslandSkinEngine.Current;
        var radius = new CornerRadius(spec.IsPixelMode ? 2 : 11);

        foreach (var n in _nav)
        {
            var selected = n.Key == _selectedSectionKey;
            n.NavPill.CornerRadius = radius;
            n.NavShadow.CornerRadius = radius;
            n.NavShadow.Visibility = selected && spec.IsCartoonSticker ? Visibility.Visible : Visibility.Collapsed;

            if (selected)
            {
                n.NavPill.Background = spec.IsAeroGlass
                    ? IslandSkinEngine.CreateAeroConvexGlassBrush(true)
                    : (Brush)Application.Current.Resources["HoshinoAccent"];
                n.NavPill.BorderBrush = spec.IsCartoonSticker
                    ? IslandSkinEngine.Frozen(Color.FromRgb(0x2A, 0x1E, 0x2E))
                    : spec.IsPixelMode
                        ? IslandSkinEngine.Frozen(Color.FromRgb(0x24, 0x1A, 0x34))
                        : (Brush)Application.Current.Resources["HoshinoAccent"];
                n.NavPill.BorderThickness = new Thickness(spec.IsCartoonSticker ? 2.0 : spec.IsPixelMode ? 1.8 : 1.0);
                var fg = spec.IsAeroGlass ? Brushes.White : (Brush)Application.Current.Resources["HoshinoOnAccent"];
                n.NavLabel.Foreground = fg;
                n.NavLabel.FontWeight = FontWeights.SemiBold;
                n.NavIconHost.Content = SettingsSkinPresenter.CreateNavIcon(n.Key, fg);
            }
            else
            {
                n.NavPill.Background = spec.IsPixelMode
                    ? (Brush)Application.Current.Resources["HoshinoSurface"]
                    : Brushes.Transparent;
                n.NavPill.BorderBrush = spec.IsPixelMode
                    ? (Brush)Application.Current.Resources["HoshinoBorder"]
                    : Brushes.Transparent;
                n.NavPill.BorderThickness = new Thickness(spec.IsPixelMode ? 1.4 : 1.0);
                var fg = (Brush)Application.Current.Resources["HoshinoText"];
                var muted = (Brush)Application.Current.Resources["HoshinoMuted"];
                n.NavLabel.Foreground = fg;
                n.NavLabel.FontWeight = FontWeights.Normal;
                n.NavIconHost.Content = SettingsSkinPresenter.CreateNavIcon(n.Key, muted);
            }
        }
    }

    // ---------------------------------------------------------------- reusable controls

    private void CustomDesignColors(StackPanel panel)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        TextBox Field(string title, string value)
        {
            var col = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            col.Children.Add(new TextBlock { Text = title, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 4), FontSize = 12 });
            var box = new TextBox { Text = value, Width = 108 };
            col.Children.Add(box);
            row.Children.Add(col);
            return box;
        }
        var accent = Field("Акцент", _settings.DesignAccent);
        var bg = Field("Фон", _settings.DesignBackground);
        var text = Field("Текст", _settings.DesignText);
        var apply = new Button { Style = (Style)FindResource("ChipButton"), Content = "Применить цвета", Margin = new Thickness(0, 18, 0, 0) };
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12 };
        apply.Click += (_, _) =>
        {
            if (new[] { accent.Text, bg.Text, text.Text }.Any(v => !System.Text.RegularExpressions.Regex.IsMatch(v, "^#[0-9a-fA-F]{6}$")))
            {
                feedback.Text = "Введите цвета в формате #RRGGBB.";
                return;
            }
            _settings.DesignPreset = "Custom";
            _settings.DesignAccent = accent.Text;
            _settings.DesignBackground = bg.Text;
            _settings.DesignText = text.Text;
            Update(() => { });
            ApplyWindowThemeChrome();
            feedback.Text = "Своя палитра применена.";
        };
        row.Children.Add(apply);
        panel.Children.Add(row);
        panel.Children.Add(feedback);
    }

    private void UpdateControls(Panel panel)
    {
        var status = new TextBlock { Foreground = Secondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8) };
        var check = new Button { Style = (Style)FindResource("ChipButton"), Content = "Проверить обновления", Margin = new Thickness(0, 0, 8, 0) };
        var install = new Button { Style = (Style)FindResource("ChipButton"), Content = "Установить", Margin = new Thickness(0, 0, 8, 0) };
        var open = new Button { Style = (Style)FindResource("ChipButton"), Content = "Страница релиза", Margin = new Thickness(0, 0, 8, 0) };
        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 4), Children = { check, install, open } };
        panel.Children.Add(status);
        panel.Children.Add(row);

        void Refresh()
        {
            var u = App.Updates;
            var current = $"Установлена версия {UpdateService.CurrentVersion}.";
            status.Text = u?.State switch
            {
                UpdateState.Checking => $"{current} Проверяю обновления…",
                UpdateState.UpToDate => $"{current} У вас самая свежая версия.",
                UpdateState.Available => $"{current} Доступна версия {u.Latest?.Version}.",
                UpdateState.Downloading => $"{current} Скачиваю версию {u.Latest?.Version}…",
                UpdateState.ReadyToInstall => $"{current} Версия {u.Latest?.Version} скачана и проверена. Нажмите «Установить».",
                UpdateState.Installing => $"Устанавливаю версию {u.Latest?.Version}…",
                UpdateState.Failed => $"{current} Не удалось проверить обновления: {u.Error}.",
                _ => u is null
                    ? $"{current} Локальная сборка (.local-dev) — автообновление отключено."
                    : _settings.LastUpdateCheck is { } last ? $"{current} Последняя проверка: {last.ToLocalTime():g}." : current,
            };
            check.IsEnabled = u is not null && u.State is not (UpdateState.Checking or UpdateState.Downloading or UpdateState.Installing);
            install.Visibility = u?.State == UpdateState.ReadyToInstall ? Visibility.Visible : Visibility.Collapsed;
            open.Visibility = u?.Latest is not null && u.Latest.Version > UpdateService.CurrentVersion ? Visibility.Visible : Visibility.Collapsed;
        }

        check.Click += async (_, _) =>
        {
            if (App.Updates is { } u) await u.CheckAsync(manual: true);
        };
        install.Click += (_, _) =>
        {
            try { App.Updates?.Install(); }
            catch (Exception e) { MessageBox.Show(e.Message, "Обновление"); }
        };
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

    private void AlarmList(Panel panel)
    {
        Note(panel, "Будильники звонят на островке. Нажмите на капсулу острова, чтобы остановить сигнал.");
        var list = new StackPanel();
        panel.Children.Add(list);

        void Save() => Update(() => _settings.Alarms = _settings.Alarms.ToList());

        void Fill()
        {
            list.Children.Clear();
            foreach (var alarm in _settings.Alarms)
            {
                var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                var remove = SmallButton("", "Удалить будильник");
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

                var time = new TextBox { Text = alarm.Time, Width = 68, ToolTip = "24-часовой формат, например 07:30" };
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

                var daily = new CheckBox { Content = "Ежедневно", IsChecked = alarm.Daily, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                daily.Click += (_, _) => { alarm.Daily = daily.IsChecked == true; Save(); };
                DockPanel.SetDock(daily, Dock.Left);
                row.Children.Add(daily);

                var label = new TextBox { Text = alarm.Label, ToolTip = "Подпись (необязательно)" };
                label.LostFocus += (_, _) => { alarm.Label = label.Text.Trim(); Save(); };
                row.Children.Add(label);
                list.Children.Add(row);
            }
        }
        Fill();

        var add = new Button { Style = (Style)FindResource("ChipButton"), Content = "+ Добавить будильник", Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += (_, _) =>
        {
            var next = DateTime.Now.AddHours(1);
            _settings.Alarms.Add(new Alarm { Time = $"{next.Hour:00}:00" });
            Save();
            Fill();
        };
        panel.Children.Add(add);
    }

    private void IdentityStatus(Panel panel)
    {
        if (PackageIdentity.HasIdentity)
        {
            string access;
            try
            {
                access = Windows.UI.Notifications.Management.UserNotificationListener.Current.GetAccessStatus() switch
                {
                    Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Allowed => "доступ к уведомлениям разрешён",
                    Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Denied => "доступ запрещён в параметрах Windows",
                    _ => "Windows запросит доступ при первом чтении",
                };
            }
            catch (Exception)
            {
                access = "статус неизвестен";
            }
            Note(panel, $"Пакетная идентификация настроена ✓ ({access}).");
            return;
        }

        Note(panel, "Для чтения системных уведомлений и календаря Windows требуется разовая регистрация локального пакета HoshiBar.");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 6) };
        var setup = new Button { Style = (Style)FindResource("ChipButton"), Content = "Настроить доступ…", Margin = new Thickness(0) };
        setup.Click += (_, _) =>
        {
            PackageIdentity.RunSetup();
            setup.IsEnabled = false;
            setup.Content = "Выполняется настройка…";
        };
        row.Children.Add(setup);
        if (System.IO.File.Exists(PackageIdentity.SetupLog))
        {
            var log = new Button { Style = (Style)FindResource("ChipButton"), Content = "Журнал настройки", Margin = new Thickness(8, 0, 0, 0) };
            log.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(PackageIdentity.SetupLog) { UseShellExecute = true });
            row.Children.Add(log);
        }
        panel.Children.Add(row);
    }

    private void IcsLinks(Panel panel)
    {
        Note(panel, "Ссылки iCal (.ics / webcal): Google Календарь, Outlook или iCloud.");
        var list = new StackPanel();
        panel.Children.Add(list);

        void Fill()
        {
            list.Children.Clear();
            foreach (var url in _settings.CalendarIcsUrls)
            {
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var remove = SmallButton("", "Удалить");
                remove.Click += (_, _) =>
                {
                    Update(() => _settings.CalendarIcsUrls = _settings.CalendarIcsUrls.Where(u => u != url).ToList());
                    Fill();
                };
                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
                var host = Uri.TryCreate(url.Replace("webcal://", "https://"), UriKind.Absolute, out var uri) ? uri.Host : url;
                row.Children.Add(new TextBlock { Text = $"{host}  (ссылка скрыта)", VerticalAlignment = VerticalAlignment.Center, Foreground = Secondary });
                list.Children.Add(row);
            }
        }
        Fill();

        var add = new Grid { Margin = new Thickness(0, 4, 0, 6) };
        add.ColumnDefinitions.Add(new ColumnDefinition());
        add.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var box = new TextBox { ToolTip = "Вставьте ссылку iCal / webcal и нажмите Enter" };
        var button = new Button { Style = (Style)FindResource("ChipButton"), Content = "Добавить", Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(button, 1);
        add.Children.Add(box);
        add.Children.Add(button);
        panel.Children.Add(add);

        void Add()
        {
            var url = box.Text.Trim();
            if (!(url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                  || url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)
                  || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                || _settings.CalendarIcsUrls.Contains(url))
                return;
            Update(() => _settings.CalendarIcsUrls = _settings.CalendarIcsUrls.Append(url).ToList());
            box.Text = "";
            Fill();
        }
        button.Click += (_, _) => Add();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Add(); };
    }

    private void CitySearch(Panel panel)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(165) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var current = new TextBlock
        {
            Text = _settings.WeatherPlace is { Length: > 0 } place ? place : "Город не выбран",
            Foreground = Secondary,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        row.Children.Add(current);
        var box = new TextBox { ToolTip = "Введите название города и нажмите Enter" };
        Grid.SetColumn(box, 1);
        row.Children.Add(box);
        panel.Children.Add(row);

        var results = new StackPanel { Margin = new Thickness(165, 0, 0, 0) };
        panel.Children.Add(results);
        CancellationTokenSource? pending = null;

        async void Search()
        {
            var query = box.Text.Trim();
            if (query.Length < 2) return;
            pending?.Cancel();
            var cancel = pending = new CancellationTokenSource();
            results.Children.Clear();
            results.Children.Add(new TextBlock { Text = "Поиск города…", Foreground = Faint });
            IReadOnlyList<Place> places;
            try { places = await WeatherService.SearchAsync(query, cancel.Token); }
            catch (Exception)
            {
                if (cancel.IsCancellationRequested) return;
                places = Array.Empty<Place>();
            }
            if (cancel.IsCancellationRequested) return;
            results.Children.Clear();
            if (places.Count == 0) results.Children.Add(new TextBlock { Text = "Ничего не найдено", Foreground = Faint });
            foreach (var p in places)
            {
                var item = new Border
                {
                    Background = Brushes.Transparent,
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 4, 8, 4),
                    Cursor = Cursors.Hand,
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
                    Build();
                };
                results.Children.Add(item);
            }
        }

        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Search(); };
    }

    private void HotkeyRow(Panel panel, string label, Func<string> get, Action<string> set, Func<string?> getProblem)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });

        var box = new TextBox
        {
            Text = string.IsNullOrEmpty(get()) ? "Нет" : get(),
            IsReadOnly = true,
            IsReadOnlyCaretVisible = false,
            Width = 170,
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = Cursors.Hand,
            ToolTip = "Нажмите и введите комбинацию с Ctrl, Alt, Shift или Win. Backspace — очистить.",
        };
        var problem = new TextBlock { Foreground = Faint, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        void ShowProblem()
        {
            var p = getProblem();
            problem.Text = p ?? "";
            problem.Visibility = string.IsNullOrEmpty(p) ? Visibility.Collapsed : Visibility.Visible;
        }

        box.GotKeyboardFocus += (_, _) => box.Text = "Нажмите клавиши…";
        box.LostKeyboardFocus += (_, _) => box.Text = string.IsNullOrEmpty(get()) ? "Нет" : get();
        box.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            string? text;
            if (key is Key.Back or Key.Delete)
                text = "";
            else if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                     or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
                return;
            else if (Keyboard.Modifiers == ModifierKeys.None)
                return;
            else
                text = Hotkey.Format(Keyboard.Modifiers, key);

            Update(() => set(text));
            box.Text = text.Length == 0 ? "Нет" : text;
            ShowProblem();
            Keyboard.ClearFocus();
        };
        Grid.SetColumn(box, 1);
        row.Children.Add(box);
        panel.Children.Add(row);
        panel.Children.Add(problem);
        ShowProblem();
    }

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

            var remove = SmallButton("", "Убрать");
            remove.Click += (_, _) => EditQuickApps(list => list.RemoveAt(index));
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);

            var up = SmallButton("", "Сдвинуть выше");
            up.IsEnabled = index > 0;
            up.Click += (_, _) => EditQuickApps(list => (list[index - 1], list[index]) = (list[index], list[index - 1]));
            DockPanel.SetDock(up, Dock.Right);
            row.Children.Add(up);

            var icon = new Image { Source = QuickAppsService.GetIcon(id, 32), Width = 20, Height = 20, Margin = new Thickness(0, 0, 10, 0) };
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            row.Children.Add(new TextBlock
            {
                Text = QuickAppsService.GetName(id) ?? "(не установлено)",
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
            Content = "+ Добавить приложение…",
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
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 6), ToolTip = "Поиск по меню Пуск" };
        var results = new StackPanel();
        picker.Children.Add(search);
        var resultsScroller = new ScrollViewer
        {
            MaxHeight = 240,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = results,
        };
        WheelScrolling.Attach(resultsScroller, pixelsPerNotch: 36);
        picker.Children.Add(resultsScroller);
        results.Children.Add(new TextBlock { Text = "Загружаю список приложений…", Foreground = Faint });
        search.Focus();

        _installedApps ??= await QuickAppsService.ListAsync();

        void Fill()
        {
            results.Children.Clear();
            var query = search.Text.Trim();
            var matches = _installedApps
                .Where(a => !_settings.QuickApps.Contains(a.Id)
                            && (query.Length == 0 || a.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
                .Take(40)
                .ToList();
            if (matches.Count == 0) results.Children.Add(new TextBlock { Text = "Приложения не найдены", Foreground = Faint });
            foreach (var app in matches)
            {
                var item = new Border
                {
                    Background = Brushes.Transparent,
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 4, 8, 4),
                    Cursor = Cursors.Hand,
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

    private static void Note(Panel panel, string text) =>
        panel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = Faint,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 6),
        });

    private void Check(Panel panel, string label, Func<bool> get, Action<bool> set)
    {
        var box = new CheckBox { IsChecked = get(), Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap } };
        box.Click += (_, _) => Update(() => set(box.IsChecked == true));
        panel.Children.Add(box);
    }

    private void Choice<T>(Panel panel, string? label, (T Value, string Text)[] options, Func<T> get, Action<T> set)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = label is null ? new GridLength(0) : new GridLength(175) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        if (label is not null)
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });

        var group = $"g{_groupId++}";
        var wrap = new WrapPanel();
        foreach (var (value, text) in options)
        {
            var radio = new RadioButton
            {
                GroupName = group,
                Content = text,
                IsChecked = EqualityComparer<T>.Default.Equals(get(), value),
            };
            radio.Checked += (_, _) =>
            {
                if (_loading) return;
                Update(() => set(value));
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
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(185) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

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

        var text = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
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
