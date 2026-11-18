// Original HoshiBar code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ellipse = System.Windows.Shapes.Ellipse;
using Microsoft.Win32;

namespace HoshiBarInstaller;

public static class Program
{
    [STAThread]
    public static void Main() =>
        new Application { ShutdownMode = ShutdownMode.OnMainWindowClose }.Run(new InstallerWindow());
}

public sealed class InstallerWindow : Window
{
    private static readonly Brush PinkBrush = InstallerVisuals.Frozen(InstallerVisuals.Pink);
    private static readonly Brush MutedBrush = InstallerVisuals.Frozen(InstallerVisuals.MutedCol);
    private static readonly Brush TextBrush = InstallerVisuals.Frozen(InstallerVisuals.TextCol);

    private readonly TextBox folder = new()
    {
        Height = 36,
        VerticalContentAlignment = VerticalAlignment.Center,
        Padding = new Thickness(10, 0, 10, 0),
    };
    private readonly CheckBox autoVoice = new()
    {
        Content = "Автоматически подключить нейроголос Хошино при первом запуске (офлайн RU)",
        IsChecked = true,
        Margin = new Thickness(0, 10, 0, 0),
        VerticalContentAlignment = VerticalAlignment.Center,
    };
    private readonly CheckBox agree = new()
    {
        Content = "Я принимаю условия лицензий",
        Margin = new Thickness(0, 6, 0, 0),
        VerticalContentAlignment = VerticalAlignment.Center,
    };
    private readonly Button install = new()
    {
        Content = "Поселить Хошино",
        Height = 42,
        MinWidth = 186,
        Background = PinkBrush,
        Foreground = InstallerVisuals.Frozen(Color.FromRgb(0x1E, 0x15, 0x26)),
        FontWeight = FontWeights.SemiBold,
    };

    private readonly TextBlock title = new()
    {
        Text = "Она уже почти рядом",
        FontSize = 25,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 0, 6),
    };
    private readonly TextBlock description = new()
    {
        Text = "Выбери стартовую форму и тему острова — остальное всегда можно поменять в настройках.",
        Foreground = MutedBrush,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = 19,
        Margin = new Thickness(0, 0, 0, 14),
    };
    private readonly TextBlock momoQuote = new()
    {
        Text = "Ухэ~ Сейчас распакуем подушку и устроимся.",
        TextWrapping = TextWrapping.Wrap,
        FontSize = 12,
        LineHeight = 17,
        Foreground = TextBrush,
    };
    private readonly TextBlock status = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = MutedBrush,
        FontSize = 12,
        Margin = new Thickness(0, 8, 0, 0),
    };
    private readonly TextBlock versionLabel = new() { Foreground = MutedBrush, FontSize = 11.5 };

    private readonly StackPanel setupPane = new();
    private readonly StackPanel progressPane = new() { Visibility = Visibility.Collapsed };
    private readonly Border progressFill = new()
    {
        Height = 8,
        CornerRadius = new CornerRadius(4),
        HorizontalAlignment = HorizontalAlignment.Left,
        Width = 0,
        Background = InstallerVisuals.CreateHeteroBrush(),
    };
    private readonly TextBlock progressPercent = new()
    {
        Text = "0%",
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        Foreground = PinkBrush,
        HorizontalAlignment = HorizontalAlignment.Right,
    };
    private readonly (Ellipse Dot, TextBlock Label)[] stepRows;
    private readonly TranslateTransform companionGaze;
    private readonly Action<bool> setCompanionSleepy;

    private string selectedMode = "DynamicIsland";
    private string selectedSkin = "HorusGlass";
    private bool userTouchedStarterChoice;
    private bool installing;
    private bool complete;

    public static string Version => Assembly.GetExecutingAssembly().GetName().Version!.ToString(3);
    public static bool HasPayload => Assembly.GetExecutingAssembly().GetManifestResourceNames().Contains("HoshiBar.CoreSetup.exe");

    public InstallerWindow()
    {
        Title = "HoshiBar — установка";
        Width = 780;
        Height = 576;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Foreground = TextBrush;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Resources = CreateStyles();

        LoadExistingUserChoicesIfAny();

        var shell = new Border
        {
            Background = InstallerVisuals.Frozen(InstallerVisuals.DarkBg),
            BorderBrush = InstallerVisuals.CreateHeteroBrush(),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(24, 20, 24, 22),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 28,
                ShadowDepth = 6,
                Direction = 270,
                Color = Colors.Black,
                Opacity = 0.55,
            },
        };
        Content = shell;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.Child = root;

        // Top header bar: HOSHIBAR · 0.17.0 + Close button
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 16), Background = Brushes.Transparent };
        bar.MouseLeftButtonDown += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };
        var close = new Button
        {
            Content = "✕",
            FontSize = 13,
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            Background = InstallerVisuals.Frozen(InstallerVisuals.Surface),
        };
        DockPanel.SetDock(close, Dock.Right);
        close.Click += (_, _) => { if (!installing) Close(); };
        bar.Children.Add(close);

        var brandRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        brandRow.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = InstallerVisuals.Frozen(InstallerVisuals.Gold), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        brandRow.Children.Add(new TextBlock
        {
            Text = $"HOSHIBAR · {Version}",
            FontSize = 12.5,
            FontWeight = FontWeights.Bold,
            Foreground = PinkBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        bar.Children.Add(brandRow);
        root.Children.Add(bar);

        // Two-column body: Left Bento Companion Card | Right Setup / Cozy Progress Pane
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(216) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        // Left Bento Companion Pod (Eye of Horus + Hoshino + WhalePin + MomoTalk quote card)
        var companionPod = new Border
        {
            Background = InstallerVisuals.Frozen(InstallerVisuals.CompanionBg),
            BorderBrush = InstallerVisuals.Frozen(InstallerVisuals.BorderCol),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(16, 18, 16, 16),
        };
        var podStack = new DockPanel();
        var momoCard = new Border
        {
            Background = InstallerVisuals.Frozen(InstallerVisuals.ControlBg),
            BorderBrush = InstallerVisuals.Frozen(InstallerVisuals.BorderCol),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(11, 9, 11, 9),
        };
        var momoGrid = new Grid();
        momoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        momoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        momoGrid.Children.Add(new Border
        {
            Width = 3.5,
            CornerRadius = new CornerRadius(2),
            Background = PinkBrush,
            Margin = new Thickness(0, 1, 9, 1),
        });
        Grid.SetColumn(momoQuote, 1);
        momoGrid.Children.Add(momoQuote);
        momoCard.Child = momoGrid;
        DockPanel.SetDock(momoCard, Dock.Bottom);
        podStack.Children.Add(momoCard);

        var (sceneEl, gaze, setSleepy) = InstallerVisuals.CreateHoshinoCompanionScene();
        companionGaze = gaze;
        setCompanionSleepy = setSleepy;
        podStack.Children.Add(sceneEl);
        companionPod.Child = podStack;
        body.Children.Add(companionPod);

        MouseMove += (_, e) =>
        {
            if (installing) return;
            var pos = e.GetPosition(companionPod);
            companionGaze.X = Math.Clamp((pos.X - 108) / 45.0, -3.2, 3.2);
            companionGaze.Y = Math.Clamp((pos.Y - 95) / 45.0, -2.4, 2.4);
        };

        // Right details column
        var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(details, 2);
        body.Children.Add(details);
        details.Children.Add(title);
        details.Children.Add(description);

        BuildSetupPane();
        stepRows = BuildProgressPane();
        details.Children.Add(setupPane);
        details.Children.Add(progressPane);
        details.Children.Add(status);

        status.Text = "Аккаунт, Obsidian vault, задачи и калибровка голоса бережно сохраняются.";

        // Bottom bar
        var bottom = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        Grid.SetRow(bottom, 2);
        root.Children.Add(bottom);
        DockPanel.SetDock(install, Dock.Right);
        bottom.Children.Add(install);
        versionLabel.Text = HasPayload
            ? $"Пакет установки v{Version} готов"
            : $"Предпросмотр оболочки v{Version} (пакет не вшит)";
        versionLabel.VerticalAlignment = VerticalAlignment.Center;
        bottom.Children.Add(versionLabel);

        agree.Checked += (_, _) => RefreshButton();
        agree.Unchecked += (_, _) => RefreshButton();
        RefreshButton();

        install.Click += async (_, _) =>
        {
            if (complete)
            {
                OpenIsland();
                Close();
                return;
            }
            await Install();
        };

        Closing += (_, e) => { if (installing) e.Cancel = true; };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && !installing) Close(); };
    }

    private void BuildSetupPane()
    {
        // Starter Shape ("Форма")
        setupPane.Children.Add(MakeSectionLabel("Форма на экране"));
        var modeWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        void RebuildModes()
        {
            modeWrap.Children.Clear();
            foreach (var (id, label) in new[] { ("DynamicIsland", "Плавающий остров"), ("Notch", "Верхний вырез") })
            {
                var active = selectedMode == id;
                var pill = MakeChoicePill(label, active, null);
                pill.MouseLeftButtonUp += (_, _) =>
                {
                    selectedMode = id;
                    userTouchedStarterChoice = true;
                    RebuildModes();
                };
                modeWrap.Children.Add(pill);
            }
        }
        RebuildModes();
        setupPane.Children.Add(modeWrap);

        // Starter Skin ("Стартовая тема")
        setupPane.Children.Add(MakeSectionLabel("Стартовая тема"));
        var skinWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        var skins = new (string Id, string Title, Color Swatch)[]
        {
            ("HorusGlass", "Horus", InstallerVisuals.Pink),
            ("ShittimChest", "Shittim", Color.FromRgb(0x29, 0xB6, 0xF6)),
            ("Win7Aero", "Aero", Color.FromRgb(0x4A, 0xDE, 0xFF)),
            ("ChibiPop", "Chibi", Color.FromRgb(0xFF, 0x85, 0xAD)),
            ("PixelTamagotchi", "Pixel", Color.FromRgb(0xC9, 0xED, 0xE3)),
        };
        void RebuildSkins()
        {
            skinWrap.Children.Clear();
            foreach (var s in skins)
            {
                var active = selectedSkin == s.Id;
                var pill = MakeChoicePill(s.Title, active, s.Swatch);
                pill.MouseLeftButtonUp += (_, _) =>
                {
                    selectedSkin = s.Id;
                    userTouchedStarterChoice = true;
                    RebuildSkins();
                };
                skinWrap.Children.Add(pill);
            }
        }
        RebuildSkins();
        setupPane.Children.Add(skinWrap);

        // Folder path
        setupPane.Children.Add(MakeSectionLabel("Где будет жить HoshiBar"));
        var folderRow = new DockPanel();
        var browse = new Button { Content = "…", Width = 38, Height = 36, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(0) };
        DockPanel.SetDock(browse, Dock.Right);
        folderRow.Children.Add(browse);
        folderRow.Children.Add(folder);
        folder.Text = InstallFolder();
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog { Title = "Выбери папку для HoshiBar" };
            if (dialog.ShowDialog(this) == true)
                folder.Text = Path.Combine(dialog.FolderName, "HoshiBar");
        };
        setupPane.Children.Add(folderRow);
        setupPane.Children.Add(autoVoice);

        // License agreement row
        var legalRow = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        legalRow.Children.Add(agree);
        var legalBtn = new Button
        {
            Content = "Лицензии и авторство",
            Background = Brushes.Transparent,
            Foreground = PinkBrush,
            Padding = new Thickness(10, 10, 4, 0),
            FontSize = 11.5,
        };
        legalBtn.Click += (_, _) => ShowLicenses();
        legalRow.Children.Add(legalBtn);
        setupPane.Children.Add(legalRow);
    }

    private (Ellipse Dot, TextBlock Label)[] BuildProgressPane()
    {
        progressPane.Children.Add(InstallerVisuals.CreateDriftingWhaleBanner());

        var barHeader = new Grid { Margin = new Thickness(0, 0, 0, 5) };
        barHeader.Children.Add(new TextBlock { Text = "Распаковка и настройка", FontSize = 12, Foreground = MutedBrush });
        barHeader.Children.Add(progressPercent);
        progressPane.Children.Add(barHeader);

        var track = new Border
        {
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = InstallerVisuals.Frozen(InstallerVisuals.ControlBg),
            ClipToBounds = true,
            Margin = new Thickness(0, 0, 0, 14),
            Child = progressFill,
        };
        progressPane.Children.Add(track);

        var steps = new[]
        {
            "Проверка пакета SHA-256",
            "Ядро HoshiBar (.NET 8)",
            "Голосовая модель Vosk ru и ресурсы",
            "Ярлыки рабочего стола и настройки",
        };
        var list = new List<(Ellipse, TextBlock)>();
        var stepCard = new Border
        {
            Background = InstallerVisuals.Frozen(InstallerVisuals.Surface),
            BorderBrush = InstallerVisuals.Frozen(InstallerVisuals.BorderCol),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(14, 10, 14, 10),
        };
        var stepStack = new StackPanel();
        foreach (var s in steps)
        {
            var r = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3.5, 0, 3.5) };
            var dot = new Ellipse { Width = 8, Height = 8, Fill = InstallerVisuals.Frozen(InstallerVisuals.BorderCol), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            var lbl = new TextBlock { Text = s, FontSize = 12.2, Foreground = MutedBrush, VerticalAlignment = VerticalAlignment.Center };
            r.Children.Add(dot);
            r.Children.Add(lbl);
            stepStack.Children.Add(r);
            list.Add((dot, lbl));
        }
        stepCard.Child = stepStack;
        progressPane.Children.Add(stepCard);
        return list.ToArray();
    }

    private void SetProgressStep(int activeIndex, double pct)
    {
        var maxWidth = Math.Max(120, progressPane.ActualWidth > 20 ? progressPane.ActualWidth : 460);
        progressFill.Width = maxWidth * Math.Clamp(pct / 100.0, 0, 1);
        progressPercent.Text = $"{pct:0}%";

        for (var i = 0; i < stepRows.Length; i++)
        {
            var (dot, lbl) = stepRows[i];
            if (i < activeIndex)
            {
                dot.Fill = InstallerVisuals.Frozen(InstallerVisuals.Sky);
                lbl.Foreground = TextBrush;
            }
            else if (i == activeIndex)
            {
                dot.Fill = InstallerVisuals.Frozen(InstallerVisuals.Gold);
                lbl.Foreground = PinkBrush;
                lbl.FontWeight = FontWeights.SemiBold;
            }
            else
            {
                dot.Fill = InstallerVisuals.Frozen(InstallerVisuals.BorderCol);
                lbl.Foreground = MutedBrush;
                lbl.FontWeight = FontWeights.Normal;
            }
        }
    }

    private static TextBlock MakeSectionLabel(string text) => new()
    {
        Text = text,
        Foreground = MutedBrush,
        FontSize = 11.5,
        Margin = new Thickness(0, 0, 0, 6),
    };

    private static Border MakeChoicePill(string label, bool active, Color? swatch)
    {
        var pill = new Border
        {
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 7, 6),
            Cursor = Cursors.Hand,
            Background = active
                ? InstallerVisuals.Frozen(Color.FromArgb(0x3D, 0xEA, 0xB0, 0xCF))
                : InstallerVisuals.Frozen(InstallerVisuals.Surface),
            BorderBrush = active
                ? PinkBrush
                : InstallerVisuals.Frozen(InstallerVisuals.BorderCol),
            BorderThickness = new Thickness(active ? 1.6 : 1.0),
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (swatch is { } c)
        {
            row.Children.Add(new Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = InstallerVisuals.Frozen(c),
                Margin = new Thickness(0, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = TextBrush,
        });
        pill.Child = row;
        return pill;
    }

    private static string UserSettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DIsland", "settings.json");

    private void LoadExistingUserChoicesIfAny()
    {
        try
        {
            if (!File.Exists(UserSettingsPath)) return;
            var node = JsonNode.Parse(File.ReadAllText(UserSettingsPath));
            if (node?["Mode"]?.GetValue<string>() is { Length: > 0 } m) selectedMode = m;
            if (node?["IslandSkin"]?.GetValue<string>() is { Length: > 0 } s) selectedSkin = s;
        }
        catch (Exception)
        {
        }
    }

    private void ApplyStarterSettingsSafely()
    {
        try
        {
            if (autoVoice.IsChecked == true)
            {
                var glassDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlassChat");
                Directory.CreateDirectory(glassDir);
                File.WriteAllText(Path.Combine(glassDir, "auto-install-voice.flag"), "v1");
            }
        }
        catch (Exception)
        {
        }

        try
        {
            var path = UserSettingsPath;
            var exists = File.Exists(path);
            if (exists && !userTouchedStarterChoice)
                return; // Preserve 100% of existing user config untouched during quiet upgrades!

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            JsonObject obj;
            if (exists)
            {
                obj = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
            }
            else
            {
                obj = new JsonObject();
            }

            obj["Mode"] = selectedMode;
            obj["IslandSkin"] = selectedSkin;
            File.WriteAllText(path, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
        }
    }

    private void RefreshButton() => install.IsEnabled = !installing && (complete || agree.IsChecked == true) && HasPayload;

    private static string InstallFolder()
    {
        var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "HoshiBar");
        var legacyDefault = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "HoshinoIsland");
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{7AD6B4D0-35EC-44DC-A0A9-E65887F8BFB5}_is1");
            if (key?.GetValue("InstallLocation") is string existing && existing.Length > 0)
            {
                var trimmed = existing.TrimEnd('\\');
                if (!string.Equals(trimmed, legacyDefault, StringComparison.OrdinalIgnoreCase))
                    return trimmed;
            }
        }
        catch (Exception) { }
        return defaultPath;
    }

    public static async Task ExtractPayload(string path)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var expected = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "PayloadSha256").Value;
        await using (var input = assembly.GetManifestResourceStream("HoshiBar.CoreSetup.exe") ?? throw new IOException("Не найден пакет приложения"))
        await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await input.CopyToAsync(output);

        await using var verify = File.OpenRead(path);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(verify));
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Пакет повреждён. Скачай установщик заново.");
    }

    private async Task Install()
    {
        if (agree.IsChecked != true) return;
        string target;
        try
        {
            target = Path.GetFullPath(folder.Text.Trim());
            var protectedFolders = new[]
            {
                Path.GetPathRoot(target),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            };
            if (protectedFolders.Any(p => string.Equals(p?.TrimEnd('\\'), target.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Выбери отдельную папку HoshiBar.");
        }
        catch (Exception e)
        {
            status.Text = e.Message;
            return;
        }

        installing = true;
        RefreshButton();
        setupPane.Visibility = Visibility.Collapsed;
        progressPane.Visibility = Visibility.Visible;
        progressPane.UpdateLayout();
        setCompanionSleepy(true);
        momoQuote.Text = "М-м… пока распаковываются файлы, немножко подремлю рядом с китёнком.";
        title.Text = "Обустраиваем HoshiBar…";
        description.Text = "Осталось совсем немного — можно спокойно выдохнуть.";
        status.Text = "Прежние задачи, история AI и Obsidian не затрагиваются.";
        SetProgressStep(0, 16);

        var cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HoshiBarInstaller");
        var temp = Path.Combine(cacheRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var core = Path.Combine(temp, "setup.exe");

        try
        {
            await ExtractPayload(core);
            SetProgressStep(1, 48);

            var logs = Path.Combine(cacheRoot, "Logs");
            Directory.CreateDirectory(logs);
            var log = Path.Combine(logs, "setup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
            var start = new ProcessStartInfo(core) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS", "/LANG=russian", "/DIR=" + target, "/LOG=" + log })
                start.ArgumentList.Add(arg);

            using var child = Process.Start(start) ?? throw new IOException("Не удалось запустить установку");
            SetProgressStep(2, 78);
            await child.WaitForExitAsync();
            if (child.ExitCode != 0)
                throw new IOException("Установка не завершилась (код " + child.ExitCode + "). Журнал: " + log);
            if (!File.Exists(Path.Combine(target, "HoshiBar.exe")))
                throw new IOException("Не найден установленный HoshiBar.exe");

            SetProgressStep(3, 94);
            ApplyStarterSettingsSafely();
            SetProgressStep(4, 100);

            folder.Text = target;
            complete = true;
            setCompanionSleepy(false);
            momoQuote.Text = "Ухэ~ Всё готово! Пойдём смотреть HoshiBar?";
            title.Text = "Теперь я рядом";
            description.Text = "HoshiBar уютно устроился. Начнём с одного спокойного дела?";
            status.Text = "Готово · Твои настройки, задачи и память сохранены.";
            install.Content = "Открыть HoshiBar";
        }
        catch (Exception e)
        {
            setCompanionSleepy(false);
            momoQuote.Text = "Ничего страшного, сейчас разберёмся без спешки.";
            title.Text = "Пока не получилось";
            description.Text = "Попробуем ещё раз.";
            status.Text = e.Message;
            setupPane.Visibility = Visibility.Visible;
            progressPane.Visibility = Visibility.Collapsed;
            install.Content = "Повторить установку";
        }
        finally
        {
            installing = false;
            RefreshButton();
            try
            {
                var resolved = Path.GetFullPath(temp);
                if (resolved.StartsWith(Path.GetFullPath(cacheRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(resolved, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void OpenIsland()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Path.Combine(folder.Text, "HoshiBar.exe")) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            MessageBox.Show(this, e.Message, "HoshiBar");
        }
    }

    private void ShowLicenses()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var text = string.Join("\n\n", new[] { "LICENSING.md", "LICENSE.txt", "MIT.txt", "THIRD-PARTY-NOTICES.txt", "INNO.txt" }.Select(name =>
        {
            using var stream = assembly.GetManifestResourceStream("HoshiBar." + name)!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }));
        new Window
        {
            Owner = this,
            Title = "Лицензии и авторство",
            Width = 700,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = InstallerVisuals.Frozen(InstallerVisuals.DarkBg),
            Content = new TextBox
            {
                Text = text,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(20),
                Background = InstallerVisuals.Frozen(InstallerVisuals.DarkBg),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
            },
        }.ShowDialog();
    }

    private static ResourceDictionary CreateStyles() => (ResourceDictionary)System.Windows.Markup.XamlReader.Parse("""
    <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
      <Style TargetType="Button">
        <Setter Property="Foreground" Value="#F4EDF6"/>
        <Setter Property="Background" Value="#332B3F"/>
        <Setter Property="Padding" Value="14,8"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="Template">
          <Setter.Value>
            <ControlTemplate TargetType="Button">
              <Border x:Name="Frame" Background="{TemplateBinding Background}" CornerRadius="12" Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="Opacity" Value="0.88"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter TargetName="Frame" Property="Opacity" Value="0.38"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </Style>
      <Style TargetType="TextBox">
        <Setter Property="Background" Value="#241F2E"/>
        <Setter Property="Foreground" Value="#F4EDF6"/>
        <Setter Property="BorderBrush" Value="#4A3D59"/>
        <Setter Property="CaretBrush" Value="#EAB0CF"/>
      </Style>
      <Style TargetType="CheckBox">
        <Setter Property="Foreground" Value="#D5CBD9"/>
        <Setter Property="Cursor" Value="Hand"/>
      </Style>
    </ResourceDictionary>
    """);
}
