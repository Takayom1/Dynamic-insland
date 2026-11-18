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
/// Skin-adaptive borderless window chrome, native DWM blur/matte translucency, custom caption controls,
/// live interactive Island/Notch preview card, mini-island visual skin cards, lore palette chips,
/// and vector sidebar icons for the 2-pane SettingsWindow ("Твой остров").
/// </summary>
public static class SettingsSkinPresenter
{
    public static readonly (IslandSkin Skin, string Title, string Subtitle)[] SkinCards =
    {
        (IslandSkin.HorusGlass, "Horus Glass", "Тёмное стекло · 5 палитр"),
        (IslandSkin.ShittimChest, "Shittim Chest", "Матовая прозрачность"),
        (IslandSkin.Win7Aero, "Win7 Aero", "Футуристичное стекло Aero"),
        (IslandSkin.ChibiPop, "Chibi Pop", "Мультяшный 2D стикер"),
        (IslandSkin.PixelTamagotchi, "Pixel Tamagotchi", "Пиксельный корпус + LCD"),
    };

    public static readonly (string Id, string Title, Color Bg, Color Accent, Color Secondary)[] LoreSwatches =
    {
        ("SakuraNap", "Sakura Nap", Color.FromRgb(0x18, 0x14, 0x1F), Color.FromRgb(0xEA, 0xB0, 0xCF), Color.FromRgb(0x83, 0xCF, 0xF3)),
        ("AbydosOasis", "Abydos Oasis", Color.FromRgb(0x10, 0x19, 0x24), Color.FromRgb(0x83, 0xCF, 0xF3), Color.FromRgb(0xF2, 0xD1, 0x7D)),
        ("YumesCompass", "Yume's Compass", Color.FromRgb(0x1C, 0x17, 0x13), Color.FromRgb(0xF2, 0xD1, 0x7D), Color.FromRgb(0x83, 0xCF, 0xF3)),
        ("IronHorus", "Iron Horus", Color.FromRgb(0x11, 0x13, 0x18), Color.FromRgb(0xFF, 0x85, 0xB3), Color.FromRgb(0x5C, 0xE1, 0xE6)),
        ("Aquarium", "Aquarium", Color.FromRgb(0x0B, 0x16, 0x23), Color.FromRgb(0x64, 0xDF, 0xDF), Color.FromRgb(0xF0, 0xA6, 0xCA)),
        ("Custom", "Своя палитра", Color.FromRgb(0x22, 0x1C, 0x2B), Color.FromRgb(0xEA, 0xB0, 0xCF), Color.FromRgb(0xF2, 0xD1, 0x7D)),
    };

    public static void ApplyWindowChrome(
        Window window,
        Grid windowOuterGrid,
        Border windowCartoonShadow,
        Border rootShell,
        Border headerBar,
        Border topAccentBar,
        TextBlock headerTitleText,
        StackPanel captionButtonsHost,
        Canvas bgChromeCanvas,
        Border sidebarPane,
        Border footerBar,
        Button doneButton,
        AppSettings settings)
    {
        var spec = IslandSkinEngine.Current;
        var isMaximized = window.WindowState == WindowState.Maximized;

        window.FontFamily = IslandSkinEngine.GetThemeFontFamily(spec);
        TextOptions.SetTextFormattingMode(window, TextFormattingMode.Ideal);
        window.SnapsToDevicePixels = spec.IsPixelMode;
        window.UseLayoutRounding = spec.IsPixelMode;

        // Outer margin & DWM glass/matte translucency
        windowOuterGrid.Margin = !isMaximized && spec.IsCartoonSticker ? new Thickness(8) : new Thickness(0);
        var shellCorner = isMaximized
            ? new CornerRadius(0)
            : new CornerRadius(spec.IsPixelMode ? 0 : spec.IsCartoonSticker ? 20 : 12);

        rootShell.CornerRadius = shellCorner;
        windowCartoonShadow.CornerRadius = shellCorner;
        windowCartoonShadow.Visibility = !isMaximized && spec.IsCartoonSticker ? Visibility.Visible : Visibility.Collapsed;

        DwmWindowBlur.Apply(window, spec, cornerRadiusDip: 12);

        var tint = spec.Skin == IslandSkin.HorusGlass ? Math.Clamp(settings.BackgroundTint, 0, 0.45) : 0;
        var bodyScale = spec.DisableGlassBlur ? 1.0 : spec.IsAeroGlass ? 0.88 : spec.IsShittimTablet ? 0.90 : 0.92;
        rootShell.Background = IslandSkinEngine.CreateIslandBodyBrush(spec, spec.Accent, tint, bodyScale);
        rootShell.BorderBrush = IslandSkinEngine.CreateRimBrush(spec);
        rootShell.BorderThickness = isMaximized
            ? new Thickness(0)
            : new Thickness(spec.IsCartoonSticker ? 2.8 : spec.IsPixelMode ? 2.0 : 1.3);

        // Header bar & Win98 / Aero / Schale / Cartoon / Horus caption chrome
        if (spec.IsPixelMode)
        {
            var win98 = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            win98.GradientStops.Add(new GradientStop(Color.FromRgb(0xA6, 0xD6, 0xF2), 0));
            win98.GradientStops.Add(new GradientStop(Color.FromRgb(0xF5, 0xB4, 0xD4), 1));
            win98.Freeze();
            headerBar.Background = win98;
            headerBar.BorderBrush = IslandSkinEngine.Frozen(Color.FromRgb(0x5B, 0x4B, 0x73));
            headerBar.BorderThickness = new Thickness(0, 0, 0, 2);
            topAccentBar.Visibility = Visibility.Collapsed;
            headerTitleText.Text = "⚙ Твой остров — HoshiBar.exe";
            headerTitleText.Foreground = IslandSkinEngine.Frozen(Color.FromRgb(0x1B, 0x15, 0x28));
        }
        else
        {
            headerBar.Background = spec.IsAeroGlass
                ? IslandSkinEngine.Frozen(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF))
                : spec.IsShittimTablet
                    ? IslandSkinEngine.Frozen(Color.FromArgb(0x65, 0xFF, 0xFF, 0xFF))
                    : Brushes.Transparent;
            headerBar.BorderBrush = IslandSkinEngine.Frozen(spec.Border);
            headerBar.BorderThickness = new Thickness(0, 0, 0, spec.IsCartoonSticker ? 2.0 : 1.0);
            topAccentBar.Visibility = Visibility.Visible;
            topAccentBar.Height = spec.IsCartoonSticker ? 0 : 3;
            topAccentBar.Background = IslandSkinEngine.CreateRimBrush(spec);
            headerTitleText.Text = spec.IsShittimTablet
                ? "SCHALE OS // HOSHIBAR · НАСТРОЙКИ"
                : "HoshiBar · Твой остров";
            headerTitleText.SetResourceReference(TextBlock.ForegroundProperty, "HoshinoText");
        }

        RebuildCaptionButtons(window, captionButtonsHost, spec);

        sidebarPane.Background = spec.IsCartoonSticker
            ? IslandSkinEngine.Frozen(Color.FromRgb(0xFF, 0xEE, 0xF6))
            : spec.IsAeroGlass
                ? IslandSkinEngine.Frozen(Color.FromArgb(0x42, 0x06, 0x1E, 0x38))
                : spec.IsShittimTablet
                    ? IslandSkinEngine.Frozen(Color.FromArgb(0xB8, 0xF7, 0xFB, 0xFF))
                    : spec.IsPixelMode
                        ? IslandSkinEngine.Frozen(Color.FromRgb(0xDC, 0xD1, 0xEC))
                        : IslandSkinEngine.Frozen(Color.FromArgb(0xA8, spec.Surface.R, spec.Surface.G, spec.Surface.B));

        sidebarPane.BorderBrush = IslandSkinEngine.Frozen(spec.Border);
        sidebarPane.BorderThickness = new Thickness(0, 0, spec.IsCartoonSticker ? 2.2 : spec.IsPixelMode ? 2.0 : 1.0, 0);

        footerBar.Background = sidebarPane.Background;
        footerBar.BorderBrush = IslandSkinEngine.Frozen(spec.Border);
        footerBar.BorderThickness = new Thickness(0, spec.IsCartoonSticker ? 2.2 : spec.IsPixelMode ? 2.0 : 1.0, 0, 0);

        // Style primary "Готово" button per skin
        doneButton.Background = spec.IsAeroGlass
            ? IslandSkinEngine.CreateAeroConvexGlassBrush(true)
            : spec.IsShittimTablet
                ? IslandSkinEngine.Frozen(Color.FromRgb(0x29, 0xB6, 0xF6))
                : (Brush)Application.Current.Resources["HoshinoAccent"];
        doneButton.Foreground = spec.IsAeroGlass || spec.IsShittimTablet
            ? Brushes.White
            : (Brush)Application.Current.Resources["HoshinoOnAccent"];
        if (spec.IsShittimTablet)
            doneButton.BorderBrush = IslandSkinEngine.Frozen(Color.FromRgb(0xFF, 0x85, 0xAD));
        else
            doneButton.SetResourceReference(Control.BorderBrushProperty, "HoshinoChipBorderBrush");

        // Decorative background chrome (Aero crystal panes, Schale matte ticks & halftone, Pixel screws)
        bgChromeCanvas.Children.Clear();
        RenderOptions.SetEdgeMode(bgChromeCanvas, spec.IsPixelMode ? EdgeMode.Aliased : EdgeMode.Unspecified);

        if (spec.IsAeroGlass)
        {
            var bands = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0.52) };
            bands.GradientStops.Add(new GradientStop(Color.FromArgb(0x46, 0xFF, 0xFF, 0xFF), 0.00));
            bands.GradientStops.Add(new GradientStop(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 0.22));
            bands.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.24));
            bands.GradientStops.Add(new GradientStop(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF), 0.48));
            bands.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.56));
            bands.GradientStops.Add(new GradientStop(Color.FromArgb(0x24, 0x7C, 0xE8, 0xFF), 1.00));
            bands.Freeze();
            bgChromeCanvas.Children.Add(new Rectangle { Width = 1600, Height = 1000, Fill = bands });
        }
        else if (spec.IsShittimTablet)
        {
            // Matte Schale OS skewed ticks & corner halftone dots (zero mirror glare!)
            var cyan = IslandSkinEngine.Frozen(Color.FromRgb(0x38, 0xBD, 0xF8));
            var pink = IslandSkinEngine.Frozen(Color.FromRgb(0xFF, 0x85, 0xAD));
            for (var i = 0; i < 3; i++)
            {
                var ox = 720 + i * 18;
                bgChromeCanvas.Children.Add(new Polygon
                {
                    Fill = i == 2 ? pink : cyan,
                    Opacity = 0.65,
                    Points = new PointCollection { new(ox, 12), new(ox + 10, 12), new(ox + 5, 18), new(ox - 5, 18) },
                });
            }
            for (var r = 0; r < 5; r++)
            for (var c = 0; c < 6; c++)
            {
                var dot = new Ellipse
                {
                    Width = 3.2,
                    Height = 3.2,
                    Fill = cyan,
                    Opacity = Math.Max(0.06, 0.26 - (r + c) * 0.025),
                };
                Canvas.SetLeft(dot, 16 + c * 9);
                Canvas.SetTop(dot, 670 + r * 9);
                bgChromeCanvas.Children.Add(dot);
            }
        }
    }

    private static void RebuildCaptionButtons(Window window, StackPanel host, SkinPaletteSpec spec)
    {
        host.Children.Clear();

        void AddCaptionBtn(string glyphData, string tip, bool isClose, Action onClick)
        {
            Border btn;
            var strokeBrush = isClose && (spec.IsAeroGlass || spec.IsShittimTablet)
                ? Brushes.White
                : spec.IsCartoonSticker || spec.IsPixelMode
                    ? IslandSkinEngine.Frozen(spec.Text)
                    : spec.IsAeroGlass
                        ? Brushes.White
                        : IslandSkinEngine.Frozen(spec.Text);

            var icon = new Path
            {
                Data = Geometry.Parse(glyphData),
                Width = 10,
                Height = 10,
                Stretch = Stretch.Uniform,
                Stroke = strokeBrush,
                StrokeThickness = spec.IsCartoonSticker ? 1.8 : 1.4,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (spec.IsPixelMode)
                RenderOptions.SetEdgeMode(icon, EdgeMode.Aliased);

            if (spec.IsPixelMode)
            {
                btn = new Border
                {
                    Width = 22,
                    Height = 20,
                    Margin = new Thickness(3, 0, 0, 0),
                    Background = isClose
                        ? IslandSkinEngine.Frozen(Color.FromRgb(0xF5, 0xA8, 0xC8))
                        : IslandSkinEngine.Frozen(Color.FromRgb(0xE6, 0xDF, 0xF0)),
                    BorderBrush = IslandSkinEngine.Frozen(Color.FromRgb(0x2B, 0x20, 0x3D)),
                    BorderThickness = new Thickness(1.5),
                    Cursor = Cursors.Hand,
                    ToolTip = tip,
                    Child = icon,
                };
            }
            else if (spec.IsAeroGlass)
            {
                var closeGrad = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                closeGrad.GradientStops.Add(new GradientStop(Color.FromRgb(0xF6, 0x8A, 0x94), 0.0));
                closeGrad.GradientStops.Add(new GradientStop(Color.FromRgb(0xDF, 0x3B, 0x4A), 0.5));
                closeGrad.GradientStops.Add(new GradientStop(Color.FromRgb(0xB5, 0x1E, 0x2C), 1.0));
                closeGrad.Freeze();

                btn = new Border
                {
                    Width = isClose ? 36 : 28,
                    Height = 22,
                    CornerRadius = new CornerRadius(5),
                    Margin = new Thickness(3, 0, 0, 0),
                    Background = isClose ? closeGrad : IslandSkinEngine.CreateAeroConvexGlassBrush(false),
                    BorderBrush = IslandSkinEngine.Frozen(Color.FromArgb(0xCC, 0xEC, 0xFA, 0xFF)),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    ToolTip = tip,
                    Child = icon,
                };
            }
            else if (spec.IsCartoonSticker)
            {
                btn = new Border
                {
                    Width = 24,
                    Height = 24,
                    CornerRadius = new CornerRadius(12),
                    Margin = new Thickness(5, 0, 0, 0),
                    Background = isClose
                        ? IslandSkinEngine.Frozen(Color.FromRgb(0xFF, 0x85, 0xAD))
                        : IslandSkinEngine.Frozen(Color.FromRgb(0xFF, 0xF5, 0xFA)),
                    BorderBrush = IslandSkinEngine.Frozen(Color.FromRgb(0x2A, 0x1E, 0x2E)),
                    BorderThickness = new Thickness(2.0),
                    Cursor = Cursors.Hand,
                    ToolTip = tip,
                    Child = icon,
                };
            }
            else
            {
                btn = new Border
                {
                    Width = 28,
                    Height = 24,
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(4, 0, 0, 0),
                    Background = isClose && spec.IsShittimTablet
                        ? IslandSkinEngine.Frozen(Color.FromRgb(0xFF, 0x85, 0xAD))
                        : IslandSkinEngine.Frozen(Color.FromArgb(0x28, spec.Text.R, spec.Text.G, spec.Text.B)),
                    BorderBrush = IslandSkinEngine.Frozen(spec.Border),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    ToolTip = tip,
                    Child = icon,
                };
            }

            btn.MouseLeftButtonDown += (_, e) => e.Handled = true;
            btn.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
            host.Children.Add(btn);
        }

        AddCaptionBtn("M 1,5 H 9", "Свернуть", false, () => window.WindowState = WindowState.Minimized);
        AddCaptionBtn("M 1.5,1.5 H 8.5 V 8.5 H 1.5 Z", "Развернуть / восстановить", false, () =>
        {
            window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        });
        AddCaptionBtn("M 1.5,1.5 L 8.5,8.5 M 8.5,1.5 L 1.5,8.5", "Закрыть", true, window.Close);
    }

    public static (FrameworkElement Card, Action Refresh) CreateLivePreviewCard(AppSettings settings)
    {
        var outer = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        var cartoonShadow = new Border
        {
            Background = IslandSkinEngine.Frozen(Color.FromRgb(0x2A, 0x1E, 0x2E)),
            RenderTransform = new TranslateTransform(3.5, 3.5),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        var frame = new Border { Padding = new Thickness(16, 12, 16, 14) };

        var stack = new StackPanel();
        var topRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var title = new TextBlock { Text = "Живой предпросмотр", FontSize = 13.5, FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(TextBlock.ForegroundProperty, "HoshinoText");
        var modeBadge = new TextBlock { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
        modeBadge.SetResourceReference(TextBlock.ForegroundProperty, "HoshinoMuted");
        DockPanel.SetDock(modeBadge, Dock.Right);
        topRow.Children.Add(modeBadge);
        topRow.Children.Add(title);
        stack.Children.Add(topRow);

        var stage = new Border
        {
            Height = 96,
            CornerRadius = new CornerRadius(12),
            ClipToBounds = true,
        };
        var stageGrid = new Grid();
        var pillShadow = new Border
        {
            Width = 264,
            Height = 44,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Background = IslandSkinEngine.Frozen(Color.FromRgb(0x2A, 0x1E, 0x2E)),
            RenderTransform = new TranslateTransform(3, 3),
            Visibility = Visibility.Collapsed,
        };
        var miniPill = new Border
        {
            Width = 264,
            Height = 44,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Padding = new Thickness(12, 0, 16, 0),
        };

        var pillInnerOverlay = new Grid();
        var pixelLcdWell = new Border
        {
            Margin = new Thickness(-4, 5, -6, 5),
            Visibility = Visibility.Collapsed,
        };
        pillInnerOverlay.Children.Add(pixelLcdWell);

        var pillRow = new Grid();
        pillRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pillRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pillRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var previewHoshino = new HoshinoCompanion(settings)
        {
            Width = 31,
            Height = 31,
            IsCompactPill = true,
            SuppressFloatingHalo = true,
            VerticalAlignment = VerticalAlignment.Center,
        };
        pillRow.Children.Add(previewHoshino);

        var centerClock = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
        };
        var clockLbl = new TextBlock
        {
            Text = DateTime.Now.ToString("HH:mm"),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var diamondHost = new ContentControl { VerticalAlignment = VerticalAlignment.Center };
        var weatherHost = new Border { Width = 19, Height = 19, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        var tempLbl = new TextBlock
        {
            Text = "+21°",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        centerClock.Children.Add(clockLbl);
        centerClock.Children.Add(diamondHost);
        centerClock.Children.Add(weatherHost);
        centerClock.Children.Add(tempLbl);
        Grid.SetColumn(centerClock, 1);
        pillRow.Children.Add(centerClock);

        var miniEq = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < 4; i++)
        {
            miniEq.Children.Add(new Rectangle
            {
                Width = 3.2,
                Height = i is 1 or 2 ? 14 : 9,
                RadiusX = 1.5,
                RadiusY = 1.5,
                Margin = new Thickness(1.2, 0, 1.2, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        Grid.SetColumn(miniEq, 2);
        pillRow.Children.Add(miniEq);

        pillInnerOverlay.Children.Add(pillRow);
        miniPill.Child = pillInnerOverlay;

        stageGrid.Children.Add(pillShadow);
        stageGrid.Children.Add(miniPill);
        stage.Child = stageGrid;
        stack.Children.Add(stage);
        frame.Child = stack;

        outer.Children.Add(cartoonShadow);
        outer.Children.Add(frame);

        void Refresh()
        {
            var spec = IslandSkinEngine.Current;
            var radius = new CornerRadius(spec.IsPixelMode ? 0 : 16);
            frame.CornerRadius = radius;
            cartoonShadow.CornerRadius = radius;
            cartoonShadow.Visibility = spec.IsCartoonSticker ? Visibility.Visible : Visibility.Collapsed;
            frame.SetResourceReference(Border.BackgroundProperty, "HoshinoSurface");
            frame.SetResourceReference(Border.BorderBrushProperty, "HoshinoBorder");
            frame.BorderThickness = new Thickness(spec.CardBorderThickness);

            var bgGrad = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            if (spec.IsLight)
            {
                bgGrad.GradientStops.Add(new GradientStop(Color.FromRgb(0xD4, 0xE6, 0xF9), 0));
                bgGrad.GradientStops.Add(new GradientStop(Color.FromRgb(0xF2, 0xDD, 0xEE), 1));
            }
            else
            {
                bgGrad.GradientStops.Add(new GradientStop(Color.FromRgb(0x0D, 0x19, 0x2C), 0));
                bgGrad.GradientStops.Add(new GradientStop(Color.FromRgb(0x24, 0x16, 0x32), 1));
            }
            bgGrad.Freeze();
            stage.CornerRadius = new CornerRadius(spec.IsPixelMode ? 0 : 12);
            stage.Background = bgGrad;
            stage.BorderBrush = IslandSkinEngine.Frozen(spec.Border);
            stage.BorderThickness = new Thickness(spec.IsPixelMode ? 2 : 1);

            var isNotch = settings.Mode == IslandMode.Notch;
            var pillTop = isNotch ? 0 : 20;
            var pillCorner = spec.IsPixelMode
                ? new CornerRadius(0)
                : isNotch
                    ? new CornerRadius(0, 0, 16, 16)
                    : new CornerRadius(22);

            miniPill.Height = spec.IsPixelMode ? 46 : 44;
            pillShadow.Height = miniPill.Height;
            miniPill.Margin = new Thickness(0, pillTop, 0, 0);
            pillShadow.Margin = new Thickness(0, pillTop, 0, 0);
            miniPill.CornerRadius = pillCorner;
            pillShadow.CornerRadius = pillCorner;
            pillShadow.Visibility = spec.IsCartoonSticker ? Visibility.Visible : Visibility.Collapsed;

            pixelLcdWell.Visibility = spec.IsPixelMode ? Visibility.Visible : Visibility.Collapsed;
            if (spec.IsPixelMode)
            {
                pixelLcdWell.Background = IslandSkinEngine.CreateMintLcdGridBrush();
                pixelLcdWell.BorderBrush = IslandSkinEngine.Frozen(Color.FromRgb(0x52, 0x42, 0x6A));
                pixelLcdWell.BorderThickness = new Thickness(2, 2, 1, 1);
            }

            var tint = spec.Skin == IslandSkin.HorusGlass ? Math.Clamp(settings.BackgroundTint, 0, 0.45) : 0;
            miniPill.Background = IslandSkinEngine.CreateIslandBodyBrush(spec, spec.Accent, tint, 1.0);
            miniPill.BorderBrush = IslandSkinEngine.CreateRimBrush(spec);
            miniPill.BorderThickness = isNotch
                ? new Thickness(spec.RimThickness, 0, spec.RimThickness, spec.RimThickness)
                : new Thickness(spec.RimThickness);

            var fg = spec.IsPixelMode
                ? IslandSkinEngine.Frozen(Color.FromRgb(0x14, 0x2D, 0x2A))
                : IslandSkinEngine.Frozen(spec.Text);
            clockLbl.Text = DateTime.Now.ToString("HH:mm");
            clockLbl.Foreground = fg;
            tempLbl.Foreground = fg;
            diamondHost.Content = IslandChromeRenderer.CreateClockDiamondSeparator(spec);
            weatherHost.Child = WeatherIcons.Create(WeatherKind.PartlyCloudy, isDay: true, size: 18);

            var eqBrushes = IslandSkinEngine.CreateEqualizerBrushes(spec);
            var idx = 0;
            foreach (var r in miniEq.Children.OfType<Rectangle>())
            {
                r.Fill = eqBrushes[idx % eqBrushes.Length];
                r.RadiusX = r.RadiusY = spec.IsPixelMode ? 0 : 1.5;
                idx++;
            }

            previewHoshino.Visibility = settings.ShowHoshino ? Visibility.Visible : Visibility.Collapsed;
            previewHoshino.UpdateState();

            var matText = spec.IsAeroGlass ? "Футуристичное стекло"
                : spec.IsShittimTablet ? "Матовая прозрачность"
                : spec.IsCartoonSticker ? "Плоский 2D стикер"
                : spec.IsPixelMode ? "Пиксельный LCD"
                : "Тёмное матовое стекло";
            modeBadge.Text = $"{(isNotch ? "Вырез" : "Остров")} · {matText}";
        }

        Refresh();
        return (outer, Refresh);
    }

    public static FrameworkElement BuildSkinSelectorGrid(AppSettings settings, Action<IslandSkin> onPicked)
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 10) };

        void Rebuild()
        {
            wrap.Children.Clear();
            var activeSpec = IslandSkinEngine.Current;
            foreach (var item in SkinCards)
            {
                var selected = settings.IslandSkin == item.Skin;
                var cell = new Grid { Width = 136, Margin = new Thickness(0, 0, 8, 8) };

                var shadow = new Border
                {
                    CornerRadius = new CornerRadius(activeSpec.IsPixelMode ? 0 : 12),
                    Background = IslandSkinEngine.Frozen(Color.FromRgb(0x2A, 0x1E, 0x2E)),
                    RenderTransform = new TranslateTransform(2.5, 2.5),
                    Visibility = activeSpec.IsCartoonSticker ? Visibility.Visible : Visibility.Collapsed,
                    IsHitTestVisible = false,
                };

                var card = new Border
                {
                    CornerRadius = new CornerRadius(activeSpec.IsPixelMode ? 0 : 12),
                    Padding = new Thickness(10, 9, 10, 9),
                    Cursor = Cursors.Hand,
                    BorderThickness = new Thickness(selected ? 2.2 : activeSpec.CardBorderThickness),
                };
                card.Background = selected
                    ? IslandSkinEngine.Frozen(Color.FromArgb(activeSpec.IsLight ? (byte)0x28 : (byte)0x38, activeSpec.Accent.R, activeSpec.Accent.G, activeSpec.Accent.B))
                    : (Brush)Application.Current.Resources["HoshinoControl"];
                card.BorderBrush = selected
                    ? (Brush)Application.Current.Resources["HoshinoAccent"]
                    : (Brush)Application.Current.Resources["HoshinoBorder"];

                var inner = new StackPanel();
                inner.Children.Add(BuildMiniSkinThumbnail(item.Skin));

                var lbl = new TextBlock
                {
                    Text = item.Title,
                    FontSize = 12.5,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 7, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                lbl.SetResourceReference(TextBlock.ForegroundProperty, "HoshinoText");
                inner.Children.Add(lbl);

                var sub = new TextBlock
                {
                    Text = item.Subtitle,
                    FontSize = 10.5,
                    Margin = new Thickness(0, 2, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                sub.SetResourceReference(TextBlock.ForegroundProperty, "HoshinoMuted");
                inner.Children.Add(sub);

                card.Child = inner;
                card.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    onPicked(item.Skin);
                    Rebuild();
                };

                cell.Children.Add(shadow);
                cell.Children.Add(card);
                wrap.Children.Add(cell);
            }
        }

        Rebuild();
        return wrap;
    }

    private static FrameworkElement BuildMiniSkinThumbnail(IslandSkin skin)
    {
        var stage = new Border
        {
            Height = 36,
            CornerRadius = new CornerRadius(skin == IslandSkin.PixelTamagotchi ? 0 : 8),
            ClipToBounds = true,
            Background = IslandSkinEngine.Frozen(skin is IslandSkin.ShittimChest or IslandSkin.ChibiPop
                ? Color.FromRgb(0xDE, 0xEC, 0xFA)
                : Color.FromRgb(0x13, 0x1A, 0x2A)),
        };

        var canvas = new Canvas { Width = 114, Height = 36 };
        switch (skin)
        {
            case IslandSkin.HorusGlass:
            {
                var rim = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                rim.GradientStops.Add(new GradientStop(Color.FromRgb(0xF2, 0xD1, 0x7D), 0));
                rim.GradientStops.Add(new GradientStop(Color.FromRgb(0xEA, 0xB0, 0xCF), 0.5));
                rim.GradientStops.Add(new GradientStop(Color.FromRgb(0x83, 0xCF, 0xF3), 1));
                rim.Freeze();
                var pill = new Border
                {
                    Width = 88, Height = 20, CornerRadius = new CornerRadius(10),
                    Background = IslandSkinEngine.Frozen(Color.FromArgb(0xDC, 0x18, 0x14, 0x1F)),
                    BorderBrush = rim, BorderThickness = new Thickness(1.5),
                };
                Canvas.SetLeft(pill, 13); Canvas.SetTop(pill, 8);
                canvas.Children.Add(pill);
                break;
            }
            case IslandSkin.ShittimChest:
            {
                var pill = new Border
                {
                    Width = 88, Height = 20, CornerRadius = new CornerRadius(7),
                    Background = IslandSkinEngine.Frozen(Color.FromArgb(0xDC, 0xF0, 0xF7, 0xFE)),
                    BorderBrush = IslandSkinEngine.Frozen(Color.FromRgb(0x38, 0xBD, 0xF8)),
                    BorderThickness = new Thickness(1.5),
                };
                Canvas.SetLeft(pill, 13); Canvas.SetTop(pill, 8);
                canvas.Children.Add(pill);
                canvas.Children.Add(new Polygon
                {
                    Fill = IslandSkinEngine.Frozen(Color.FromRgb(0xFF, 0x85, 0xAD)),
                    Points = new PointCollection { new(82, 6), new(92, 6), new(89, 10), new(79, 10) },
                });
                break;
            }
            case IslandSkin.Win7Aero:
            {
                var aero = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                aero.GradientStops.Add(new GradientStop(Color.FromArgb(0xCC, 0x4A, 0xC8, 0xFF), 0));
                aero.GradientStops.Add(new GradientStop(Color.FromArgb(0xAA, 0x0E, 0x4A, 0x84), 0.6));
                aero.GradientStops.Add(new GradientStop(Color.FromArgb(0xCC, 0x26, 0x98, 0xE8), 1));
                aero.Freeze();
                var pill = new Border
                {
                    Width = 88, Height = 20, CornerRadius = new CornerRadius(8),
                    Background = aero,
                    BorderBrush = Brushes.White, BorderThickness = new Thickness(1.2),
                };
                Canvas.SetLeft(pill, 13); Canvas.SetTop(pill, 8);
                canvas.Children.Add(pill);
                canvas.Children.Add(new Polygon
                {
                    Fill = IslandSkinEngine.Frozen(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                    Points = new PointCollection { new(22, 9), new(46, 9), new(34, 27), new(16, 27) },
                });
                break;
            }
            case IslandSkin.ChibiPop:
            {
                var shadow = new Border
                {
                    Width = 86, Height = 19, CornerRadius = new CornerRadius(9.5),
                    Background = IslandSkinEngine.Frozen(Color.FromRgb(0x2A, 0x1E, 0x2E)),
                };
                Canvas.SetLeft(shadow, 16); Canvas.SetTop(shadow, 10);
                canvas.Children.Add(shadow);
                var pill = new Border
                {
                    Width = 86, Height = 19, CornerRadius = new CornerRadius(9.5),
                    Background = IslandSkinEngine.Frozen(Color.FromRgb(0xFF, 0xF8, 0xFC)),
                    BorderBrush = IslandSkinEngine.Frozen(Color.FromRgb(0x2A, 0x1E, 0x2E)),
                    BorderThickness = new Thickness(2.0),
                };
                Canvas.SetLeft(pill, 13); Canvas.SetTop(pill, 7);
                canvas.Children.Add(pill);
                break;
            }
            case IslandSkin.PixelTamagotchi:
            {
                RenderOptions.SetEdgeMode(canvas, EdgeMode.Aliased);
                var shell = new Border
                {
                    Width = 88, Height = 22,
                    Background = IslandSkinEngine.Frozen(Color.FromRgb(0xE6, 0xDF, 0xF0)),
                    BorderBrush = IslandSkinEngine.Frozen(Color.FromRgb(0x25, 0x1B, 0x36)),
                    BorderThickness = new Thickness(2),
                    Padding = new Thickness(3),
                    Child = new Border
                    {
                        Background = IslandSkinEngine.Frozen(Color.FromRgb(0xC9, 0xED, 0xE3)),
                        BorderBrush = IslandSkinEngine.Frozen(Color.FromRgb(0x52, 0x42, 0x6A)),
                        BorderThickness = new Thickness(1.5),
                    },
                };
                Canvas.SetLeft(shell, 13); Canvas.SetTop(shell, 7);
                canvas.Children.Add(shell);
                break;
            }
        }

        stage.Child = new Viewbox { Child = canvas, Stretch = Stretch.Uniform };
        return stage;
    }

    public static FrameworkElement BuildLorePaletteStrip(AppSettings settings, Action<string> onPicked)
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
        var current = IslandSkinPresets.NormalizePreset(settings.DesignPreset);

        foreach (var p in LoreSwatches)
        {
            var selected = string.Equals(current, p.Id, StringComparison.OrdinalIgnoreCase);
            var chip = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 7, 7),
                Cursor = Cursors.Hand,
                Background = selected
                    ? IslandSkinEngine.Frozen(Color.FromArgb(0x38, p.Accent.R, p.Accent.G, p.Accent.B))
                    : (Brush)Application.Current.Resources["HoshinoControl"],
                BorderBrush = selected
                    ? IslandSkinEngine.Frozen(p.Accent)
                    : (Brush)Application.Current.Resources["HoshinoBorder"],
                BorderThickness = new Thickness(selected ? 1.8 : 1.0),
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = IslandSkinEngine.Frozen(p.Bg), Stroke = IslandSkinEngine.Frozen(p.Accent), StrokeThickness = 1.5, Margin = new Thickness(0, 0, 4, 0) });
            row.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = IslandSkinEngine.Frozen(p.Accent), Margin = new Thickness(0, 0, 7, 0) });
            var txt = new TextBlock { Text = p.Title, FontSize = 12, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal };
            txt.SetResourceReference(TextBlock.ForegroundProperty, "HoshinoText");
            row.Children.Add(txt);
            chip.Child = row;

            chip.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                onPicked(p.Id);
            };
            wrap.Children.Add(chip);
        }

        return wrap;
    }

    public static Path CreateNavIcon(string sectionKey, Brush stroke)
    {
        var data = sectionKey switch
        {
            "Appearance" => "M 8,2 A 6,6 0 1 0 14,8 C 14,9.5 12.8,10.2 11.5,10.2 H 9.8 A 1.4,1.4 0 0 0 8.4,11.6 C 8.4,12.8 9,14 8,14 M 5.5,6.2 A 0.8,0.8 0 1 1 5.49,6.2 Z M 8.0,4.8 A 0.8,0.8 0 1 1 7.99,4.8 Z M 10.6,6.2 A 0.8,0.8 0 1 1 10.59,6.2 Z",
            "IslandGhost" => "M 2,5.5 A 2.5,2.5 0 0 1 4.5,3 H 11.5 A 2.5,2.5 0 0 1 14,5.5 V 7.5 A 2.5,2.5 0 0 1 11.5,10 H 4.5 A 2.5,2.5 0 0 1 2,7.5 Z M 4.5,13 H 11.5",
            "HoshinoFocus" => "M 8,2.2 L 13,8 L 8,13.8 L 3,8 Z M 5.5,8 L 8,5.2 L 10.5,8 L 8,10.8 Z",
            "MediaLyrics" => "M 6,11.5 A 2,2 0 1 1 5.99,11.5 Z M 12,10.2 A 2,2 0 1 1 11.99,10.2 Z M 7.8,11.5 V 3.8 L 13.8,2.5 V 10.2",
            "TraySystem" => "M 2.5,10.5 H 13.5 V 13.5 H 2.5 Z M 5.2,6.5 L 8,9.2 L 10.8,6.5 M 8,2.2 V 9.0",
            "Assistant" => "M 2.2,3.5 H 13.8 A 1.2,1.2 0 0 1 15,4.7 V 10.5 A 1.2,1.2 0 0 1 13.8,11.7 H 6.5 L 3.2,14.2 V 11.7 H 2.2 A 1.2,1.2 0 0 1 1,10.5 V 4.7 A 1.2,1.2 0 0 1 2.2,3.5 Z",
            "Obsidian" => "M 6.5,1.8 L 11.8,4.2 L 13.2,10.2 L 8.5,14.2 L 3.5,11.5 L 2.8,5.5 Z M 6.5,1.8 L 8.2,8.0 L 13.2,10.2 M 8.2,8.0 L 3.5,11.5",
            "Widgets" => "M 2.5,2.5 H 7.2 V 7.2 H 2.5 Z M 8.8,2.5 H 13.5 V 7.2 H 8.8 Z M 2.5,8.8 H 7.2 V 13.5 H 2.5 Z M 8.8,8.8 H 13.5 V 13.5 H 8.8 Z",
            _ => "M 8,2 A 6,6 0 1 1 7.99,2 Z M 8,7.2 V 11.4 M 8,4.8 V 5.2",
        };

        var p = new Path
        {
            Data = Geometry.Parse(data),
            Width = 15,
            Height = 15,
            Stretch = Stretch.Uniform,
            Stroke = stroke,
            StrokeThickness = 1.55,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        if (IslandSkinEngine.Current.IsPixelMode)
            RenderOptions.SetEdgeMode(p, EdgeMode.Aliased);
        return p;
    }
}
