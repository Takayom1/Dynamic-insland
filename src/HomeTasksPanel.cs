// Original HoshiBar code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DIsland.Services;

namespace DIsland;

/// <summary>
/// Bento Quick-Tasks card on HomePage with Obsidian crystal badge, alternating #EAB0CF / #83CFF3
/// checkboxes, Chibi Pop sweeping marker checkmarks + separate vector offset shadow (no blurry Effect!),
/// Pixel Tamagotchi Win98 sub-window styling, and 1-click Coworking focus pinning.
/// </summary>
public sealed class HomeTasksPanel : Grid
{
    private readonly NotesService notes;
    private readonly Func<Guid?>? currentFocusId;
    private readonly Action<Guid>? toggleFocus;
    private readonly Border cartoonShadow = new()
    {
        Background = IslandSkinEngine.Frozen(Color.FromRgb(0x2A, 0x1E, 0x2E)),
        RenderTransform = new TranslateTransform(3.5, 3.5),
        Visibility = Visibility.Collapsed,
        IsHitTestVisible = false,
    };
    private readonly Border cardBorder = new() { Padding = new Thickness(0) };
    private readonly Border aeroInnerRim = new() { BorderThickness = new Thickness(0), IsHitTestVisible = false };
    private readonly StackPanel body = new();
    private readonly StackPanel tasks = new();
    private readonly DockPanel standardHeading = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly Border pixelTitleBar = new() { Visibility = Visibility.Collapsed, Padding = new Thickness(8, 4, 6, 4), Margin = new Thickness(0, 0, 0, 6) };
    private readonly ContentControl obsidianBadgeHost = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid composer = new() { Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
    private readonly Border composerField = new() { CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 2, 8, 2), BorderThickness = new Thickness(1) };
    private readonly TextBox input = new() { MaxLength = 240, FontSize = 12.5, MinHeight = 28 };
    private readonly TextBlock hint = new() { Text = "", Opacity = 0.55, FontSize = 11, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
    private bool composerPinnedOpen;

    public event Action? LayoutChanged;

    public HomeTasksPanel(NotesService notes, Action<TextBox> beginTyping, Func<Guid?>? currentFocusId = null, Action<Guid>? toggleFocus = null)
    {
        this.notes = notes;
        this.currentFocusId = currentFocusId;
        this.toggleFocus = toggleFocus;

        cardBorder.SetResourceReference(Border.BackgroundProperty, "HoshinoTasksSurface");
        cardBorder.SetResourceReference(Border.BorderBrushProperty, "HoshinoBorder");

        var innerOverlayGrid = new Grid();
        innerOverlayGrid.Children.Add(body);
        innerOverlayGrid.Children.Add(aeroInnerRim);
        cardBorder.Child = innerOverlayGrid;

        Children.Add(cartoonShadow);
        Children.Add(cardBorder);

        // Pixel Tamagotchi Win98 inset window titlebar ("💎 Задачи · Obsidian [×]")
        var pxRow = new DockPanel();
        var pxAddBtn = new Border
        {
            Width = 16,
            Height = 16,
            Background = IslandSkinEngine.Frozen(Color.FromRgb(0xE6, 0xDF, 0xF0)),
            BorderBrush = IslandSkinEngine.Frozen(Color.FromRgb(0x3B, 0x2E, 0x4F)),
            BorderThickness = new Thickness(1.2),
            Cursor = Cursors.Hand,
            ToolTip = "Добавить задачу",
            Child = new TextBlock
            {
                Text = "+",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -1, 0, 0),
                Foreground = IslandSkinEngine.Frozen(Color.FromRgb(0x1E, 0x18, 0x2B)),
            },
        };
        pxAddBtn.MouseLeftButtonUp += (_, e) => { e.Handled = true; ToggleComposer(beginTyping); };
        DockPanel.SetDock(pxAddBtn, Dock.Right);
        pxRow.Children.Add(pxAddBtn);

        var pxTitleLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        pxTitleLeft.Children.Add(new Path
        {
            Data = Geometry.Parse("M 5,1 L 9,4 L 8,11 L 3.5,10 L 1.5,5 Z"),
            Fill = IslandSkinEngine.Frozen(Color.FromRgb(0x7D, 0x58, 0xF2)),
            Stroke = IslandSkinEngine.Frozen(Color.FromRgb(0x2A, 0x1F, 0x3D)),
            StrokeThickness = 1,
            Width = 11,
            Height = 12,
            Margin = new Thickness(0, 0, 6, 0),
        });
        var pxTitleText = new TextBlock { Text = "Задачи · Obsidian", FontSize = 12.5, FontWeight = FontWeights.SemiBold };
        pxTitleText.SetResourceReference(TextBlock.ForegroundProperty, "HoshinoText");
        pxTitleLeft.Children.Add(pxTitleText);
        pxRow.Children.Add(pxTitleLeft);
        pixelTitleBar.Child = pxRow;
        pixelTitleBar.BorderThickness = new Thickness(0, 0, 0, 1.5);
        pixelTitleBar.SetResourceReference(Border.BorderBrushProperty, "HoshinoBorder");
        body.Children.Add(pixelTitleBar);

        // Standard Bento heading: "Задачи" on left, "+ " + "[💎 Obsidian]" badge on right
        var rightTools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var quickAddBtn = new Button
        {
            Content = "+",
            Width = 24,
            Height = 24,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Добавить задачу",
            Style = (Style)Application.Current.FindResource("IconButton"),
        };
        quickAddBtn.Click += (_, _) => ToggleComposer(beginTyping);
        rightTools.Children.Add(quickAddBtn);
        rightTools.Children.Add(obsidianBadgeHost);
        DockPanel.SetDock(rightTools, Dock.Right);
        standardHeading.Children.Add(rightTools);

        var headingText = new TextBlock { Text = "Задачи", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        headingText.SetResourceReference(TextBlock.ForegroundProperty, "HoshinoText");
        standardHeading.Children.Add(headingText);
        body.Children.Add(standardHeading);

        body.Children.Add(new ScrollViewer
        {
            Content = tasks,
            MaxHeight = 76,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });

        composer.ColumnDefinitions.Add(new ColumnDefinition());
        composer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        input.Style = null;
        input.Background = Brushes.Transparent;
        input.BorderThickness = new Thickness(0);
        input.SetResourceReference(Control.ForegroundProperty, "HoshinoText");
        input.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, "HoshinoText");
        input.VerticalContentAlignment = VerticalAlignment.Center;
        input.Padding = new Thickness(0);
        input.ToolTip = "Добавить дело · Enter";
        System.Windows.Automation.AutomationProperties.SetName(input, "Новое дело");
        input.PreviewMouseLeftButtonDown += (_, _) => beginTyping(input);
        input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; Add(); }
            else if (e.Key == Key.Escape) { e.Handled = true; composerPinnedOpen = false; UpdateComposerVisibility(); LayoutChanged?.Invoke(); }
        };

        var field = composerField;
        field.SetResourceReference(Border.BackgroundProperty, "HoshinoControl");
        field.SetResourceReference(Border.BorderBrushProperty, "HoshinoBorder");
        var fieldGrid = new Grid();
        field.Child = fieldGrid;
        fieldGrid.Children.Add(input);

        var placeholder = new TextBlock
        {
            Text = "Одно небольшое дело…",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        placeholder.SetResourceReference(TextBlock.ForegroundProperty, "HoshinoMuted");
        fieldGrid.Children.Add(placeholder);
        input.TextChanged += (_, _) => placeholder.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        composer.Children.Add(field);
        var add = new Button
        {
            Content = "+ Дело",
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(9, 4, 9, 4),
            Style = (Style)Application.Current.FindResource("ChipButton"),
        };
        Grid.SetColumn(add, 1);
        add.Click += (_, _) => Add();
        composer.Children.Add(add);

        body.Children.Add(composer);
        body.Children.Add(hint);

        MouseLeftButtonUp += (_, e) => e.Handled = true;
        notes.Changed += Refresh;
        ApplySkin();
    }

    private void ToggleComposer(Action<TextBox> beginTyping)
    {
        composerPinnedOpen = !composerPinnedOpen;
        UpdateComposerVisibility();
        LayoutChanged?.Invoke();
        if (composer.Visibility == Visibility.Visible)
            beginTyping(input);
    }

    private void UpdateComposerVisibility()
    {
        var show = composerPinnedOpen || notes.Todos.Count == 0;
        composer.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Add()
    {
        if (notes.Add(input.Text))
        {
            input.Clear();
            hint.Visibility = Visibility.Collapsed;
            composerPinnedOpen = false;
            UpdateComposerVisibility();
            LayoutChanged?.Invoke();
        }
        else
        {
            hint.Text = string.IsNullOrWhiteSpace(input.Text)
                ? "Напиши одно дело"
                : "Список заполнен — очисти выполненные дела во вкладке задач";
            hint.Visibility = Visibility.Visible;
        }
    }

    public void RefreshFocusState() => Refresh();

    public void ApplySkin()
    {
        var spec = IslandSkinEngine.Current;
        var radius = new CornerRadius(spec.CardCornerRadius);
        cardBorder.CornerRadius = radius;
        cartoonShadow.CornerRadius = radius;
        cartoonShadow.Visibility = spec.IsCartoonSticker ? Visibility.Visible : Visibility.Collapsed;
        cardBorder.BorderThickness = new Thickness(spec.CardBorderThickness);

        aeroInnerRim.CornerRadius = new CornerRadius(Math.Max(0, spec.CardCornerRadius - 1));
        aeroInnerRim.Margin = new Thickness(0);
        aeroInnerRim.BorderThickness = new Thickness(spec.IsAeroGlass ? 1.0 : 0);
        aeroInnerRim.BorderBrush = spec.IsAeroGlass ? IslandSkinEngine.Frozen(Color.FromArgb(0x78, 0xEA, 0xF8, 0xFF)) : Brushes.Transparent;

        cardBorder.Padding = new Thickness(0);
        composerField.CornerRadius = new CornerRadius(spec.IsPixelMode ? 2 : 9);
        if (spec.IsPixelMode)
        {
            body.Margin = new Thickness(0, 0, 0, 8);
            standardHeading.Visibility = Visibility.Collapsed;
            pixelTitleBar.Visibility = Visibility.Visible;
            tasks.Margin = new Thickness(10, 0, 8, 0);
            composer.Margin = new Thickness(10, 6, 10, 0);
        }
        else
        {
            body.Margin = new Thickness(14, 9, 14, 10);
            standardHeading.Visibility = Visibility.Visible;
            pixelTitleBar.Visibility = Visibility.Collapsed;
            obsidianBadgeHost.Content = IslandChromeRenderer.CreateObsidianBadge(spec);
            tasks.Margin = new Thickness(0);
            composer.Margin = new Thickness(0, 4, 0, 0);
        }

        Refresh();
    }

    private void Refresh()
    {
        var spec = IslandSkinEngine.Current;
        tasks.Children.Clear();
        UpdateComposerVisibility();

        var focusedId = currentFocusId?.Invoke();
        var openItems = notes.Todos.Where(t => !t.Done).ToList();
        var visibleItems = openItems.Count >= 2
            ? openItems
            : openItems.Concat(notes.Todos.Where(t => t.Done).Take(2 - openItems.Count)).ToList();

        var index = 0;
        foreach (var item in visibleItems)
        {
            var id = item.Id;
            var isDone = item.Done;
            var isFocused = !isDone && focusedId == id;
            var usePink = index % 2 == 0;
            index++;

            var row = new Grid { Margin = new Thickness(0, 3.5, 0, 3.5), Background = Brushes.Transparent };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var checkVisual = BuildCheckIndicator(spec, isDone, usePink);
            var hitRow = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand };
            hitRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            hitRow.ColumnDefinitions.Add(new ColumnDefinition());
            hitRow.Children.Add(checkVisual);

            var label = new TextBlock
            {
                Text = item.Text,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 12.8,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = isDone ? 0.78 : 1.0,
            };
            if (isFocused) label.SetResourceReference(TextBlock.ForegroundProperty, "HoshinoAccent");
            else label.SetResourceReference(TextBlock.ForegroundProperty, "HoshinoText");
            Grid.SetColumn(label, 1);
            hitRow.Children.Add(label);

            hitRow.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                notes.SetDone(id, !isDone);
            };
            Grid.SetColumnSpan(hitRow, 2);
            row.Children.Add(hitRow);

            if (toggleFocus is not null && !isDone)
            {
                var pinIcon = new Path
                {
                    Data = Geometry.Parse("M 8,2.5 A 5.5,5.5 0 1 1 7.99,2.5 Z M 8,5.8 A 2.2,2.2 0 1 1 7.99,5.8 Z"),
                    StrokeThickness = 1.3,
                    Width = 16,
                    Height = 16,
                    Stretch = Stretch.None,
                };
                pinIcon.SetResourceReference(Shape.StrokeProperty, isFocused ? "HoshinoAccent" : "HoshinoMuted");
                var focusBtn = new Button
                {
                    Style = (Style)Application.Current.FindResource("IconButton"),
                    Width = 24,
                    Height = 22,
                    Margin = new Thickness(4, 0, 0, 0),
                    Opacity = isFocused ? 1.0 : 0.45,
                    ToolTip = isFocused ? "Снять задачу с фокуса" : "Привязать к фокус-таймеру",
                    Content = pinIcon,
                };
                focusBtn.Click += (_, _) => toggleFocus(id);
                row.MouseEnter += (_, _) => { if (!isFocused) focusBtn.Opacity = 0.9; };
                row.MouseLeave += (_, _) => { if (!isFocused) focusBtn.Opacity = 0.45; };
                Grid.SetColumn(focusBtn, 2);
                row.Children.Add(focusBtn);
            }

            tasks.Children.Add(row);
        }

        if (visibleItems.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "Запиши одно спокойное дело ниже:",
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 11.5,
                Margin = new Thickness(0, 0, 0, 2),
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "HoshinoMuted");
            tasks.Children.Add(empty);
        }
    }

    private static FrameworkElement BuildCheckIndicator(SkinPaletteSpec spec, bool isDone, bool usePink)
    {
        var pink = Color.FromRgb(0xEA, 0xB0, 0xCF);
        var sky = Color.FromRgb(0x83, 0xCF, 0xF3);
        if (spec.IsCartoonSticker)
        {
            pink = Color.FromRgb(0xFF, 0x85, 0xAD);
            sky = Color.FromRgb(0x5C, 0xE1, 0xE6);
        }
        else if (spec.IsShittimTablet)
        {
            pink = Color.FromRgb(0xFF, 0x8F, 0xB6);
            sky = Color.FromRgb(0x47, 0xC1, 0xFA);
        }
        var tone = usePink ? pink : sky;

        if (spec.IsPixelMode)
        {
            var box = new Canvas { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            RenderOptions.SetEdgeMode(box, EdgeMode.Aliased);
            var ink = IslandSkinEngine.Frozen(spec.Text);
            box.Children.Add(new Rectangle
            {
                Width = 15,
                Height = 15,
                Fill = Brushes.White,
                Stroke = ink,
                StrokeThickness = 1.6,
            });
            if (isDone)
            {
                box.Children.Add(new Path
                {
                    Data = Geometry.Parse("M 3,8 L 6.5,11.5 L 12.5,4"),
                    Stroke = ink,
                    StrokeThickness = 2.0,
                });
            }
            return box;
        }

        var c = new Canvas { Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        var fillBrush = isDone
            ? IslandSkinEngine.Frozen(tone)
            : IslandSkinEngine.Frozen(Color.FromArgb(0x26, tone.R, tone.G, tone.B));
        var strokeBrush = spec.IsCartoonSticker
            ? IslandSkinEngine.Frozen(spec.Border)
            : IslandSkinEngine.Frozen(tone);

        var circle = new Ellipse
        {
            Width = 17,
            Height = 17,
            Fill = fillBrush,
            Stroke = strokeBrush,
            StrokeThickness = spec.IsCartoonSticker ? 2.0 : 1.5,
        };
        Canvas.SetLeft(circle, 1);
        Canvas.SetTop(circle, 1.5);
        c.Children.Add(circle);

        if (isDone)
        {
            var checkGeo = spec.IsCartoonSticker
                ? Geometry.Parse("M 4.5,10.2 L 8.6,14.2 L 18.2,2.2")
                : Geometry.Parse("M 5.8,10.2 L 8.6,13.0 L 13.6,7.4");
            checkGeo.Freeze();
            var checkInk = spec.IsCartoonSticker
                ? IslandSkinEngine.Frozen(spec.Border)
                : spec.IsShittimTablet
                    ? Brushes.White
                    : IslandSkinEngine.Frozen(Color.FromRgb(0x1B, 0x16, 0x24));

            c.Children.Add(new Path
            {
                Data = checkGeo,
                Stroke = checkInk,
                StrokeThickness = spec.IsCartoonSticker ? 2.4 : 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
            });
        }

        return c;
    }
}
