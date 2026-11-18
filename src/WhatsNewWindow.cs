// D-Island - Copyright 2026 h7 (ibuildrigs) and contributors.
// Licensed under the D-Island License 1.0; see LICENSE.txt and NOTICE.txt.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using DIsland.Services;

namespace DIsland;

/// <summary>
/// What changed in each version, shown once after HoshiBar updates (automatically or by running a
/// newer installer) and from Settings. Add an entry here for every release.
/// </summary>
public static class WhatsNew
{
    public sealed record Item(string Glyph, string Color, string Title, string Text);

    public sealed record Release(Version Version, IReadOnlyList<Item> Items);

    public static readonly IReadOnlyList<Release> Releases = new[]
    {
        new Release(new Version(1, 0, 0), new[]
        {
            new Item("", "#FFEAB0CF", "HoshiBar 1.0",
                "Пять самодостаточных визуальных тем, Bento-интерфейс, режим сквозного клика «Призрак» (Ctrl+Alt+G) и новый пульт системного трея."),
            new Item("", "#FF83CFF3", "Автоустановка нейроголоса и мульти-AI",
                "Обновлённый локальный нейроголос Хошино устанавливается автоматически, а в AI-чате доступны Codex, Gemini и Claude."),
            new Item("", "#FFF2D17D", "Полка ⇄ Телефон по Wi-Fi QR",
                "Мгновенный локальный обмен файлами со смартфоном по домашней сети Wi-Fi без облаков и регистрации."),
        }),
        new Release(new Version(0, 17, 0), new[]
        {
            new Item("", "#FFF0B6D3", "Команды через Whisper",
                "Дополнительный локальный распознаватель сохраняет фразу через короткие паузы. Python и модель выбираются отдельно в настройках помощника."),
            new Item("", "#FF64D2FF", "Тише и понятнее",
                "Отдельная громкость Хошино, тихие сигналы и подписи: слушаю, думаю, выполняю команду, отвечаю."),
            new Item("", "#FF30D158", "Остров остаётся на своём экране",
                "Выбери монитор в настройках. Обычное развёрнутое окно и полноэкранное приложение на другом мониторе больше не включают игровой режим."),
            new Item("", "#FFBF5AF2", "HoshiBar",
                "Новое имя проекта и исполняемого файла — HoshiBar.exe. Аккаунт, история, задачи и калибровка сохраняются при обновлении."),
        }),
    };

    /// <summary>Releases newer than <paramref name="since"/> up to this version, newest first.</summary>
    public static IReadOnlyList<Release> Since(Version? since) =>
        Releases.Where(r => (since is null || r.Version > since) && r.Version <= UpdateService.CurrentVersion)
            .OrderByDescending(r => r.Version)
            .ToList();
}

/// <summary>A small dark card in the island's style; built in code like Settings.</summary>
public sealed class WhatsNewWindow : Window
{
    private static readonly Brush Secondary = Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));

    public WhatsNewWindow(IReadOnlyList<WhatsNew.Release> releases, Version? updatedFrom)
    {
        Title = "Что нового в HoshiBar";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13;
        Icon = null;

        var content = new StackPanel { Margin = new Thickness(26, 24, 26, 22) };

        // Header: the island itself, then the title.
        var pill = new Grid { Width = 92, Height = 30, HorizontalAlignment = HorizontalAlignment.Left };
        pill.Children.Add(new Border { CornerRadius = new CornerRadius(15), Background = Brushes.Black, BorderBrush = Frozen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1) });
        pill.Children.Add(new Ellipse
        {
            Width = 9, Height = 9, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 12, 0),
            Fill = Frozen(Color.FromRgb(0x30, 0xD1, 0x58)),
        });
        content.Children.Add(pill);

        var current = UpdateService.CurrentVersion;
        content.Children.Add(new TextBlock
        {
            Text = updatedFrom is null ? "Что нового в HoshiBar" : "HoshiBar обновлён",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 16, 0, 0),
        });
        content.Children.Add(new TextBlock
        {
            Text = updatedFrom is null ? $"Версия {current}" : $"Обновлено с {updatedFrom} до {current}:",
            Foreground = Secondary,
            Margin = new Thickness(0, 4, 0, 8),
        });

        var list = new StackPanel();
        foreach (var release in releases)
        {
            if (releases.Count > 1)
                list.Children.Add(new TextBlock
                {
                    Text = $"VERSION {release.Version}",
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Secondary,
                    Margin = new Thickness(0, 12, 0, 0),
                });
            foreach (var item in release.Items) list.Children.Add(ItemRow(item));
        }
        content.Children.Add(new ScrollViewer
        {
            Content = list,
            MaxHeight = 420,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Focusable = false,
        });

        var notes = new Button { Style = (Style)Application.Current.FindResource("ChipButton"), Content = "Release notes", Margin = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
        notes.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    $"https://github.com/{UpdateService.Repository}/releases/tag/v{current}") { UseShellExecute = true });
            }
            catch (Exception) { /* no browser */ }
        };
        var ok = new Button
        {
            Style = (Style)Application.Current.FindResource("ChipButton"),
            Content = "Got it",
            Padding = new Thickness(22, 7, 22, 7),
            Background = Frozen(Color.FromRgb(0x0A, 0x84, 0xFF)),
            IsDefault = true,
            IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        ok.Click += (_, _) => Close();
        var buttons = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(notes);
        buttons.Children.Add(ok);
        content.Children.Add(buttons);

        var card = new Border
        {
            CornerRadius = new CornerRadius(22),
            Background = Frozen(Color.FromRgb(0x1C, 0x1C, 0x1E)),
            BorderBrush = Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(16), // room for the shadow
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Direction = 270, Opacity = 0.5 },
            Child = content,
        };
        card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        Content = card;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };

        // Arrive like the island: a quick scale and fade.
        card.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform(0.94, 0.94);
        card.RenderTransform = scale;
        card.Opacity = 0;
        Loaded += (_, _) =>
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var time = TimeSpan.FromMilliseconds(260);
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(1, time) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, time) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, time) { EasingFunction = ease });
        };
    }

    private static UIElement ItemRow(WhatsNew.Item item)
    {
        var color = (Color)ColorConverter.ConvertFromString(item.Color);
        var row = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());

        var badge = new Grid { Width = 36, Height = 36, VerticalAlignment = VerticalAlignment.Top };
        badge.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = Frozen(Color.FromArgb(0x2E, color.R, color.G, color.B)) });
        badge.Children.Add(new TextBlock
        {
            Style = (Style)Application.Current.FindResource("Glyph"),
            Text = item.Glyph,
            FontSize = 16,
            Foreground = Frozen(color),
        });
        row.Children.Add(badge);

        var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = item.Title, FontWeight = FontWeights.SemiBold, FontSize = 14 });
        text.Children.Add(new TextBlock { Text = item.Text, Foreground = Secondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
