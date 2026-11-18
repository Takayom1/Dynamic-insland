// Original HoshiBar code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DIsland.Interop;
using DIsland.Services;

namespace DIsland;

/// <summary>
/// Skin-adaptive Icon-Only Bento System Tray Flyout across all 5 themes (Horus Glass, Shittim Chest,
/// Win7 Aero, Chibi Pop, Pixel Tamagotchi), matching the approved T04 Bento renders 1:1:
/// free-standing Hoshino header (unified sunken mint LCD screen in Pixel Tamagotchi), always-present
/// Media/Weather/Focus deck with live progress bar, plump square 3x2 icon-only tiles with floating
/// speech-bubble callout on hover, dashed Shelf/QR drop zone, and 4-icon service footer.
/// </summary>
public sealed class TrayFlyoutWindow : Window
{
    private readonly AppSettings settings;
    private readonly Func<MainWindow?> getMainWindow;
    private readonly Action onOpenSettings;
    private readonly Action onExit;
    private readonly DispatcherTimer liveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    private readonly Grid rootGrid = new();
    private readonly Border cartoonOuterShadow = new()
    {
        Background = IslandSkinEngine.Frozen(Color.FromRgb(0x2A, 0x1E, 0x2E)),
        RenderTransform = new TranslateTransform(4.5, 4.5),
        Visibility = Visibility.Collapsed,
        IsHitTestVisible = false,
    };
    private readonly Border shellBorder = new() { Width = 314, ClipToBounds = false };
    private readonly Grid shellGrid = new();
    private readonly Canvas chromeCanvas = new() { IsHitTestVisible = false, ClipToBounds = true };
    private readonly StackPanel contentStack = new() { Margin = new Thickness(14) };

    // Top block (unified sunken mint LCD in Pixel Tamagotchi; open header + Bento player card in smooth skins)
    private readonly Border topScreenWell = new() { Margin = new Thickness(0, 0, 0, 10) };
    private readonly StackPanel topScreenStack = new();
    private readonly HoshinoCompanion miniCompanion;
    private readonly Ellipse statusDot = new() { Width = 7, Height = 7, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock statusTitle = new() { Text = "HoshiBar", FontSize = 15, FontWeight = FontWeights.Bold };
    private readonly TextBlock statusSubtitle = new() { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Border skinBadge = new() { Padding = new Thickness(8, 2.5, 8, 2.5), VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock skinBadgeText = new() { FontSize = 10.5, FontWeight = FontWeights.Bold };

    private readonly Border mediaDeckShadow = new()
    {
        Background = IslandSkinEngine.Frozen(Color.FromRgb(0x2A, 0x1E, 0x2E)),
        RenderTransform = new TranslateTransform(2.8, 2.8),
        Visibility = Visibility.Collapsed,
        IsHitTestVisible = false,
    };
    private readonly Border mediaDeckCard = new() { Padding = new Thickness(11, 9, 11, 9) };
    private readonly TextBlock mediaTrackTitle = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 5) };
    private readonly Border lrcMiniPill = new() { Padding = new Thickness(6, 1.5, 6, 1.5), Margin = new Thickness(5, 0, 0, 0), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock lrcMiniText = new() { Text = "LRC", FontSize = 9.5, FontWeight = FontWeights.Bold };
    private readonly Border miniWeatherHost = new() { Width = 15, Height = 15, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock miniClockTimerText = new() { FontSize = 11.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Typography = { NumeralAlignment = FontNumeralAlignment.Tabular } };
    private readonly Border miniProgTrack = new() { Height = 7, Margin = new Thickness(0, 7, 0, 0), ClipToBounds = true };
    private readonly Border miniProgFill = new() { HorizontalAlignment = HorizontalAlignment.Left, Width = 96 };

    private readonly UniformGrid quickDeck = new() { Columns = 3, Rows = 2, Margin = new Thickness(0, 0, 0, 10) };
    private readonly Grid dropZoneGrid = new() { Height = 40, Margin = new Thickness(0, 0, 0, 8), AllowDrop = true, Cursor = Cursors.Hand, Background = Brushes.Transparent };
    private readonly Rectangle dropDashedRect = new() { StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 } };
    private readonly Path dropDocIcon = new()
    {
        Data = Geometry.Parse("M 3,1.5 H 9.5 L 13,5 V 14.5 H 3 Z M 9.5,1.5 V 5 H 13 M 5.5,8.5 H 10.5 M 5.5,11.2 H 9.2"),
        Width = 15,
        Height = 15,
        Stretch = Stretch.Uniform,
        StrokeThickness = 1.5,
        Margin = new Thickness(0, 0, 8, 0),
        VerticalAlignment = VerticalAlignment.Center,
        StrokeLineJoin = PenLineJoin.Round,
    };
    private readonly TextBlock dropLabel = new() { Text = "Файл → Полка / QR", FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly UniformGrid footerRow = new() { Rows = 1, Columns = 4 };

    // Floating unclipped speech-bubble Popup (freely extends beyond TrayFlyoutWindow's edges!)
    private readonly Popup calloutPopup = new()
    {
        AllowsTransparency = true,
        Focusable = false,
        IsHitTestVisible = false,
        StaysOpen = true,
        Placement = PlacementMode.Custom,
    };

    public TrayFlyoutWindow(AppSettings settings, Func<MainWindow?> getMainWindow, Action onOpenSettings, Action onExit)
    {
        this.settings = settings;
        this.getMainWindow = getMainWindow;
        this.onOpenSettings = onOpenSettings;
        this.onExit = onExit;

        Title = "HoshiBar · Трей";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        SetResourceReference(FontFamilyProperty, "HoshinoFontFamily");

        miniCompanion = new HoshinoCompanion(settings)
        {
            Width = 52,
            Height = 52,
            IsCompactPill = true,
            SuppressFloatingHalo = true,
            Playing = () => getMainWindow()?.MediaSource.Current?.IsPlaying == true,
            Focusing = () => getMainWindow()?.TimerSource.IsActive == true,
        };

        BuildVisualTree();

        liveTimer.Tick += (_, _) =>
        {
            if (IsVisible) UpdateDynamicMediaAndTimer();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) liveTimer.Start();
            else { liveTimer.Stop(); HideCallout(); }
        };
        SourceInitialized += (_, _) => DwmWindowBlur.Apply(this, IslandSkinEngine.Current, cornerRadiusDip: 16);
        SizeChanged += (_, _) => DwmWindowBlur.Apply(this, IslandSkinEngine.Current, cornerRadiusDip: 16);
        Deactivated += (_, _) => { HideCallout(); Hide(); };
        Closed += (_, _) => HideCallout();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Hide();
            }
        };
    }

    private void BuildVisualTree()
    {
        shellGrid.Children.Add(chromeCanvas);
        shellGrid.Children.Add(contentStack);
        shellBorder.Child = shellGrid;

        rootGrid.Children.Add(cartoonOuterShadow);
        rootGrid.Children.Add(shellBorder);
        Content = rootGrid;

        // 1. Header row (free-standing Hoshino portrait + HoshiBar + status dot & screen + skin badge)
        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        miniCompanion.Margin = new Thickness(0, 0, 10, 0);
        headerGrid.Children.Add(miniCompanion);

        var titleCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        var subRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        subRow.Children.Add(statusDot);
        subRow.Children.Add(statusSubtitle);
        titleCol.Children.Add(statusTitle);
        titleCol.Children.Add(subRow);
        Grid.SetColumn(titleCol, 1);
        headerGrid.Children.Add(titleCol);

        skinBadge.Child = skinBadgeText;
        Grid.SetColumn(skinBadge, 2);
        headerGrid.Children.Add(skinBadge);
        topScreenStack.Children.Add(headerGrid);

        // 2. Always-present Media / Weather / Timer Bento card
        var mediaWrap = new Grid();
        mediaWrap.Children.Add(mediaDeckShadow);

        var deckInner = new StackPanel();
        deckInner.Children.Add(mediaTrackTitle);

        var controlsRow = new DockPanel();
        var rightStats = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        rightStats.Children.Add(miniWeatherHost);
        rightStats.Children.Add(miniClockTimerText);
        DockPanel.SetDock(rightStats, Dock.Right);
        controlsRow.Children.Add(rightStats);

        var leftTransport = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        leftTransport.Children.Add(MakeTransportButton("M 2.5,3 V 13 M 12.5,3 L 5,8 L 12.5,13 Z", "Предыдущий трек", () =>
        {
            getMainWindow()?.MediaSource.Previous();
            Dispatcher.InvokeAsync(UpdateDynamicMediaAndTimer);
        }));
        leftTransport.Children.Add(MakeTransportButton("M 4,2.5 L 13,8 L 4,13.5 Z", "Воспроизведение / пауза", () =>
        {
            var mw = getMainWindow();
            if (mw?.TimerSource.IsActive == true && mw.MediaSource.Current is null)
                mw.TimerSource.TogglePause();
            else
                mw?.MediaSource.TogglePlayPause();
            Dispatcher.InvokeAsync(UpdateDynamicMediaAndTimer);
        }));
        leftTransport.Children.Add(MakeTransportButton("M 3.5,3 L 11,8 L 3.5,13 Z M 13.5,3 V 13", "Следующий трек", () =>
        {
            getMainWindow()?.MediaSource.Next();
            Dispatcher.InvokeAsync(UpdateDynamicMediaAndTimer);
        }));

        lrcMiniPill.Child = lrcMiniText;
        BindCallout(lrcMiniPill, "Синхронная лирика LRC");
        lrcMiniPill.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            getMainWindow()?.LyricsSource.ToggleCompact();
            RefreshState();
        };
        leftTransport.Children.Add(lrcMiniPill);
        controlsRow.Children.Add(leftTransport);
        deckInner.Children.Add(controlsRow);

        miniProgTrack.Child = miniProgFill;
        miniProgTrack.SizeChanged += (_, _) => UpdateDynamicMediaAndTimer();
        deckInner.Children.Add(miniProgTrack);

        mediaDeckCard.Child = deckInner;
        mediaWrap.Children.Add(mediaDeckCard);
        topScreenStack.Children.Add(mediaWrap);

        topScreenWell.Child = topScreenStack;
        contentStack.Children.Add(topScreenWell);

        // 3. 3x2 Plump Square Icon-Only Bento Deck
        contentStack.Children.Add(quickDeck);

        // 4. Dashed File -> Shelf / QR Drop Zone
        dropZoneGrid.Children.Add(dropDashedRect);
        var dropInner = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        dropInner.Children.Add(dropDocIcon);
        dropInner.Children.Add(dropLabel);
        dropZoneGrid.Children.Add(dropInner);
        BindCallout(dropZoneGrid, "Перетащи файл или открой Wi-Fi QR");

        dropZoneGrid.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            var mw = getMainWindow();
            if (mw is not null)
            {
                if (mw.IsGhostMode) mw.SetGhostMode(false);
                if (mw.IslandHidden) mw.SetIslandHidden(false);
                Hide();
                mw.OpenShelfQrFromTray();
            }
        };
        dropZoneGrid.DragEnter += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        dropZoneGrid.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        dropZoneGrid.Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                Hide();
                getMainWindow()?.DropFilesFromTray(files);
            }
            e.Handled = true;
        };
        contentStack.Children.Add(dropZoneGrid);

        // 5. Clean 4-icon service footer
        contentStack.Children.Add(footerRow);
    }

    public void ToggleNearTray()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }
        ShowNearTray();
    }

    public void ShowNearTray()
    {
        RefreshState();
        Show();
        UpdateLayout();

        if (NativeMethods.GetCursorPos(out var pt))
        {
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(pt.X, pt.Y));
            var work = screen.WorkingArea;
            var dpi = VisualTreeHelper.GetDpi(this);
            var wDip = ActualWidth > 10 ? ActualWidth : 320;
            var hDip = ActualHeight > 10 ? ActualHeight : 380;

            var workLeft = work.Left / dpi.DpiScaleX;
            var workTop = work.Top / dpi.DpiScaleY;
            var workRight = work.Right / dpi.DpiScaleX;
            var workBottom = work.Bottom / dpi.DpiScaleY;
            var cursorX = pt.X / dpi.DpiScaleX;
            var cursorY = pt.Y / dpi.DpiScaleY;

            var x = Math.Clamp(cursorX - wDip / 2.0, workLeft + 8, workRight - wDip - 8);
            var y = cursorY > (workTop + workBottom) / 2.0
                ? Math.Max(workTop + 8, workBottom - hDip - 8)
                : Math.Min(workBottom - hDip - 8, workTop + 8);

            Left = x;
            Top = y;
        }

        Activate();
    }

    public void RefreshState()
    {
        HoshinoPalette.Apply(settings);
        var spec = IslandSkinEngine.Current;
        var mw = getMainWindow();

        FontFamily = IslandSkinEngine.GetThemeFontFamily(spec);
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        SnapsToDevicePixels = spec.IsPixelMode;
        UseLayoutRounding = spec.IsPixelMode;

        rootGrid.Margin = spec.IsCartoonSticker ? new Thickness(8) : new Thickness(0);
        var outerRadius = new CornerRadius(spec.IsPixelMode ? 0 : spec.IsCartoonSticker ? 22 : 14);
        shellBorder.CornerRadius = outerRadius;
        cartoonOuterShadow.CornerRadius = outerRadius;
        cartoonOuterShadow.Visibility = spec.IsCartoonSticker ? Visibility.Visible : Visibility.Collapsed;

        DwmWindowBlur.Apply(this, spec, cornerRadiusDip: 14);

        var tint = spec.Skin == IslandSkin.HorusGlass ? Math.Clamp(settings.BackgroundTint, 0, 0.45) : 0;
        var alphaScale = spec.DisableGlassBlur ? 1.0 : spec.IsAeroGlass ? 0.82 : spec.IsShittimTablet ? 0.86 : 0.90;
        shellBorder.Background = IslandSkinEngine.CreateIslandBodyBrush(spec, spec.Accent, tint, alphaScale);
        shellBorder.BorderBrush = IslandSkinEngine.CreateRimBrush(spec);
        shellBorder.BorderThickness = new Thickness(spec.IsCartoonSticker ? 2.8 : spec.IsPixelMode ? 2.2 : 1.4);
        shellBorder.Effect = null;

        contentStack.Margin = spec.IsPixelMode ? new Thickness(15, 16, 15, 13) : new Thickness(14);
        PopulateFlyoutChrome(spec);

        // Skin badge ("Horus" / "Shittim" / "Aero" / "Pop" / "Tama")
        skinBadge.CornerRadius = new CornerRadius(spec.IsPixelMode ? 2 : 10);
        skinBadge.Background = spec.IsShittimTablet
            ? IslandSkinEngine.Frozen(Color.FromRgb(0x38, 0xBD, 0xF8))
            : spec.IsAeroGlass
                ? IslandSkinEngine.CreateAeroConvexGlassBrush(false)
                : spec.IsPixelMode
                    ? IslandSkinEngine.Frozen(Color.FromRgb(0x9E, 0x90, 0xB8))
                    : IslandSkinEngine.Frozen(Color.FromArgb(spec.IsLight ? (byte)0x26 : (byte)0x38, spec.Accent.R, spec.Accent.G, spec.Accent.B));
        skinBadge.BorderBrush = IslandSkinEngine.Frozen(spec.Border);
        skinBadge.BorderThickness = new Thickness(spec.IsCartoonSticker ? 1.6 : 1.0);
        skinBadgeText.Text = spec.Skin switch
        {
            IslandSkin.ShittimChest => "Shittim",
            IslandSkin.Win7Aero => "Aero",
            IslandSkin.ChibiPop => "Pop",
            IslandSkin.PixelTamagotchi => "Tama",
            _ => "Horus",
        };
        skinBadgeText.Foreground = spec.IsShittimTablet || spec.IsAeroGlass || spec.IsPixelMode
            ? Brushes.White
            : IslandSkinEngine.Frozen(spec.Text);

        // Unified sunken mint LCD top screen in Pixel Tamagotchi vs open header + Bento card in smooth skins
        if (spec.IsPixelMode)
        {
            topScreenWell.Background = IslandSkinEngine.CreateMintLcdGridBrush();
            topScreenWell.BorderBrush = IslandSkinEngine.Frozen(Color.FromRgb(0x4E, 0x40, 0x66));
            topScreenWell.BorderThickness = new Thickness(2.5, 2.5, 1.5, 1.5);
            topScreenWell.CornerRadius = new CornerRadius(2);
            topScreenWell.Padding = new Thickness(10, 8, 10, 9);

            var lcdInk = IslandSkinEngine.Frozen(Color.FromRgb(0x14, 0x2D, 0x2A));
            statusTitle.Foreground = lcdInk;
            statusSubtitle.Foreground = IslandSkinEngine.Frozen(Color.FromRgb(0x2A, 0x52, 0x4B));
            mediaTrackTitle.Foreground = lcdInk;
            miniClockTimerText.Foreground = lcdInk;

            mediaDeckShadow.Visibility = Visibility.Collapsed;
            mediaDeckCard.Background = Brushes.Transparent;
            mediaDeckCard.BorderBrush = IslandSkinEngine.Frozen(Color.FromArgb(0x44, 0x14, 0x2D, 0x2A));
            mediaDeckCard.BorderThickness = new Thickness(0, 1, 0, 0);
            mediaDeckCard.CornerRadius = new CornerRadius(0);
            mediaDeckCard.Padding = new Thickness(0, 6, 0, 0);
        }
        else
        {
            topScreenWell.Background = Brushes.Transparent;
            topScreenWell.BorderThickness = new Thickness(0);
            topScreenWell.Padding = new Thickness(0);

            statusTitle.SetResourceReference(TextBlock.ForegroundProperty, "HoshinoText");
            statusSubtitle.SetResourceReference(TextBlock.ForegroundProperty, "HoshinoMuted");
            mediaTrackTitle.SetResourceReference(TextBlock.ForegroundProperty, "HoshinoText");
            miniClockTimerText.SetResourceReference(TextBlock.ForegroundProperty, "HoshinoText");

            var deckRadius = new CornerRadius(14);
            mediaDeckCard.CornerRadius = deckRadius;
            mediaDeckShadow.CornerRadius = deckRadius;
            mediaDeckShadow.Visibility = spec.IsCartoonSticker ? Visibility.Visible : Visibility.Collapsed;
            mediaDeckCard.Background = spec.IsCartoonSticker
                ? IslandSkinEngine.Frozen(Color.FromRgb(0xFF, 0xEB, 0xE6))
                : (Brush)Application.Current.Resources["HoshinoSurface"];
            mediaDeckCard.BorderBrush = (Brush)Application.Current.Resources["HoshinoBorder"];
            mediaDeckCard.BorderThickness = new Thickness(spec.CardBorderThickness);
            mediaDeckCard.Padding = new Thickness(11, 8, 11, 9);
        }

        // Mini LRC badge & progress bar styling
        var lrcOn = mw?.LyricsSource.CompactEnabled == true;
        lrcMiniPill.CornerRadius = new CornerRadius(spec.IsPixelMode ? 1 : 6);
        lrcMiniPill.BorderBrush = spec.IsPixelMode
            ? IslandSkinEngine.Frozen(Color.FromRgb(0x14, 0x2D, 0x2A))
            : (Brush)Application.Current.Resources["HoshinoBorder"];
        lrcMiniPill.BorderThickness = new Thickness(spec.IsCartoonSticker ? 1.6 : 1.0);
        lrcMiniPill.Background = lrcOn
            ? (Brush)Application.Current.Resources["HoshinoAccent"]
            : spec.IsPixelMode
                ? Brushes.Transparent
                : (Brush)Application.Current.Resources["HoshinoControl"];
        lrcMiniText.Foreground = spec.IsPixelMode
            ? IslandSkinEngine.Frozen(Color.FromRgb(0x14, 0x2D, 0x2A))
            : lrcOn
                ? (Brush)Application.Current.Resources["HoshinoOnAccent"]
                : (Brush)Application.Current.Resources["HoshinoText"];

        miniProgTrack.CornerRadius = new CornerRadius(spec.IsPixelMode ? 0 : 3.5);
        miniProgTrack.Height = spec.IsPixelMode ? 9 : spec.IsCartoonSticker ? 8 : 6;
        miniProgTrack.Background = (Brush)Application.Current.Resources["HoshinoProgressTrackBrush"];
        miniProgTrack.BorderBrush = spec.IsCartoonSticker
            ? IslandSkinEngine.Frozen(Color.FromRgb(0x2A, 0x1E, 0x2E))
            : spec.IsPixelMode
                ? IslandSkinEngine.Frozen(Color.FromRgb(0x14, 0x2D, 0x2A))
                : Brushes.Transparent;
        miniProgTrack.BorderThickness = new Thickness(spec.IsCartoonSticker ? 1.6 : spec.IsPixelMode ? 1.0 : 0);
        miniProgFill.CornerRadius = new CornerRadius(spec.IsPixelMode ? 0 : 3);
        miniProgFill.Background = (Brush)Application.Current.Resources["HoshinoProgressBrush"];

        // Dashed drop zone
        dropDashedRect.RadiusX = dropDashedRect.RadiusY = spec.IsPixelMode ? 0 : 12;
        dropDashedRect.Fill = spec.IsPixelMode
            ? IslandSkinEngine.Frozen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF))
            : (Brush)Application.Current.Resources["HoshinoSurface"];
        dropDashedRect.Stroke = IslandSkinEngine.Frozen(spec.IsCartoonSticker || spec.IsPixelMode ? spec.Border : spec.Muted);
        dropDashedRect.StrokeThickness = spec.IsCartoonSticker ? 2.0 : 1.4;
        dropDocIcon.Stroke = IslandSkinEngine.Frozen(spec.Text);
        dropLabel.Foreground = IslandSkinEngine.Frozen(spec.Text);

        var hidden = mw?.IslandHidden == true;
        var ghost = mw?.IsGhostMode == true;

        UpdateDynamicMediaAndTimer();
        RebuildQuickDeck(spec, mw, hidden, ghost);
        RebuildFooter(spec);
        HideCallout();
        miniCompanion.UpdateState();
    }

    private void UpdateDynamicMediaAndTimer()
    {
        var spec = IslandSkinEngine.Current;
        var mw = getMainWindow();
        var hidden = mw?.IslandHidden == true;
        var ghost = mw?.IsGhostMode == true;

        var screens = System.Windows.Forms.Screen.AllScreens;
        var targetScreen = FullscreenService.TargetScreen(settings.MonitorDeviceName);
        var screenIdx = Math.Max(0, Array.FindIndex(screens, s => string.Equals(s.DeviceName, targetScreen.DeviceName, StringComparison.OrdinalIgnoreCase))) + 1;

        if (hidden)
        {
            statusDot.Fill = IslandSkinEngine.Frozen(spec.Accent);
            statusSubtitle.Text = $"Скрыт · Экран {screenIdx}";
        }
        else if (ghost)
        {
            statusDot.Fill = IslandSkinEngine.Frozen(spec.SecondaryAccent);
            var rem = mw?.GhostModeUntil is { } u && u > DateTime.Now ? $" {(u - DateTime.Now):mm\\:ss}" : "";
            statusSubtitle.Text = $"Призрак{rem} · Экран {screenIdx}";
        }
        else
        {
            statusDot.Fill = IslandSkinEngine.Frozen(Color.FromRgb(0x4A, 0xDE, 0x80));
            statusSubtitle.Text = $"Активен · Экран {screenIdx}";
        }

        var media = mw?.MediaSource.Current;
        var timer = mw?.TimerSource;
        if (media is not null && !string.IsNullOrWhiteSpace(media.Title))
        {
            mediaTrackTitle.Visibility = Visibility.Visible;
            mediaTrackTitle.Text = media.Title;
        }
        else
        {
            mediaTrackTitle.Visibility = Visibility.Collapsed;
        }

        var weather = mw?.CurrentWeather;
        miniWeatherHost.Child = weather is not null
            ? WeatherIcons.Create(weather.Kind, weather.IsDay, 15)
            : WeatherIcons.Create(WeatherKind.PartlyCloudy, isDay: true, 15);

        miniClockTimerText.Text = timer is { IsActive: true }
            ? TimerService.Format(timer.Remaining)
            : DateTime.Now.ToString("HH:mm");

        double ratio = 0.42;
        if (media is { Duration.Ticks: > 0 })
            ratio = Math.Clamp(media.EstimatePosition(DateTimeOffset.Now) / media.Duration, 0.04, 1.0);
        else if (timer is { IsActive: true })
            ratio = Math.Clamp(timer.RemainingRatio, 0.04, 1.0);

        var trackW = miniProgTrack.ActualWidth - miniProgTrack.BorderThickness.Left - miniProgTrack.BorderThickness.Right;
        if (trackW <= 2) trackW = 250;
        miniProgFill.Width = Math.Max(4, trackW * ratio);
    }

    private void RebuildQuickDeck(SkinPaletteSpec spec, MainWindow? mw, bool hidden, bool ghost)
    {
        quickDeck.Children.Clear();
        var hkToggle = string.IsNullOrWhiteSpace(settings.ToggleHotkey) ? "" : $" · {settings.ToggleHotkey}";
        var hkGhost = string.IsNullOrWhiteSpace(settings.GhostModeHotkey) ? "" : $" · {settings.GhostModeHotkey}";
        var wakeOn = AssistantPreferences.Current.WakeEnabled;

        // 1. Hide / Show Island
        quickDeck.Children.Add(MakeBentoSquareTile(
            spec, slotIndex: 0, active: hidden,
            iconPath: hidden
                ? "M 2,8 C 4.5,4 11.5,4 14,8 C 11.5,12 4.5,12 2,8 Z M 2.5,13.5 L 13.5,2.5"
                : "M 1.8,8 C 4.2,3.8 11.8,3.8 14.2,8 C 11.8,12.2 4.2,12.2 1.8,8 Z M 8,5.6 A 2.4,2.4 0 1 1 7.99,5.6 Z",
            callout: (hidden ? "Показать остров" : "Скрыть остров") + hkToggle,
            onLeftClick: () => { mw?.SetIslandHidden(!hidden); RefreshState(); }));

        // 2. Ghost Mode (Click-Through)
        quickDeck.Children.Add(MakeBentoSquareTile(
            spec, slotIndex: 1, active: ghost,
            iconPath: "M 3.5,13.5 V 7.2 A 4.5,4.5 0 0 1 12.5,7.2 V 13.5 L 10.7,12 L 9.0,13.5 L 7.2,12 L 5.4,13.5 Z M 6.2,6.8 A 0.9,0.9 0 1 1 6.19,6.8 Z M 9.8,6.8 A 0.9,0.9 0 1 1 9.79,6.8 Z",
            callout: $"Режим «Призрак»{hkGhost}",
            dashedIcon: spec.Skin == IslandSkin.HorusGlass && !ghost,
            onLeftClick: () => { mw?.SetGhostMode(!ghost); RefreshState(); },
            onRightClick: () => { mw?.SetGhostMode(true, TimeSpan.FromMinutes(5)); RefreshState(); }));

        // 3. Hey Hoshino Voice Wake
        quickDeck.Children.Add(MakeBentoSquareTile(
            spec, slotIndex: 2, active: wakeOn,
            iconPath: "M 8,2 A 2.4,2.4 0 0 1 10.4,4.4 V 8 A 2.4,2.4 0 0 1 5.6,8 V 4.4 A 2.4,2.4 0 0 1 8,2 Z M 3.8,7.8 A 4.2,4.2 0 0 0 12.2,7.8 M 8,12 V 14.2 M 5.5,14.2 H 10.5",
            callout: "Хей Хошино",
            onLeftClick: () => { Hide(); mw?.TriggerVoiceWakeFromTray(); },
            onRightClick: () =>
            {
                AssistantPreferences.Current.WakeEnabled = !AssistantPreferences.Current.WakeEnabled;
                AssistantPreferences.Current.Save();
                settings.NotifyChanged();
                RefreshState();
            }));

        // 4. Game Mode
        quickDeck.Children.Add(MakeBentoSquareTile(
            spec, slotIndex: 3, active: settings.GameModeEnabled,
            iconPath: "M 3.5,5.5 H 12.5 A 2.5,2.5 0 0 1 14.8,8.5 L 14.0,12.2 A 1.6,1.6 0 0 1 11.4,12.8 L 10.0,11 H 6.0 L 4.6,12.8 A 1.6,1.6 0 0 1 2.0,12.2 L 1.2,8.5 A 2.5,2.5 0 0 1 3.5,5.5 Z M 4.8,7.2 V 9.8 M 3.5,8.5 H 6.1 M 11.2,7.6 A 0.7,0.7 0 1 1 11.19,7.6 Z M 12.6,9.0 A 0.7,0.7 0 1 1 12.59,9.0 Z",
            callout: settings.GameModeEnabled ? "Игровой режим · Вкл" : "Игровой режим · Выкл",
            onLeftClick: () =>
            {
                settings.GameModeEnabled = !settings.GameModeEnabled;
                settings.NotifyChanged();
                settings.Save();
                RefreshState();
            }));

        // 5. Switch Monitor
        var screenCount = System.Windows.Forms.Screen.AllScreens.Length;
        quickDeck.Children.Add(MakeBentoSquareTile(
            spec, slotIndex: 4, active: !string.IsNullOrEmpty(settings.MonitorDeviceName),
            iconPath: "M 2,3 H 14 V 11 H 2 Z M 6,13.5 H 10 M 8,11 V 13.5",
            callout: screenCount > 1 ? $"Сменить экран ({screenCount})" : "Основной экран",
            onLeftClick: () => { mw?.CycleMonitor(); RefreshState(); }));

        // 6. Open AI Chat
        quickDeck.Children.Add(MakeBentoSquareTile(
            spec, slotIndex: 5, active: false,
            iconPath: "M 2.2,3.5 H 13.8 A 1.2,1.2 0 0 1 15,4.7 V 10.5 A 1.2,1.2 0 0 1 13.8,11.7 H 6.5 L 3.2,14.2 V 11.7 H 2.2 A 1.2,1.2 0 0 1 1,10.5 V 4.7 A 1.2,1.2 0 0 1 2.2,3.5 Z M 5.2,7.6 A 0.8,0.8 0 1 1 5.19,7.6 Z M 8.0,7.6 A 0.8,0.8 0 1 1 7.99,7.6 Z M 10.8,7.6 A 0.8,0.8 0 1 1 10.79,7.6 Z",
            callout: "Открыть AI-чат",
            onLeftClick: () =>
            {
                Hide();
                if (mw is not null)
                {
                    if (mw.IsGhostMode) mw.SetGhostMode(false);
                    if (mw.IslandHidden) mw.SetIslandHidden(false);
                    mw.OpenAiChat();
                }
            }));
    }

    private void RebuildFooter(SkinPaletteSpec spec)
    {
        footerRow.Children.Clear();
        var startupOn = StartupManager.IsEnabled;

        footerRow.Children.Add(MakeFooterIconButton(spec, false, false,
            "M 8,5.2 A 2.8,2.8 0 1 1 7.99,5.2 Z M 8,1.5 V 3.2 M 8,12.8 V 14.5 M 1.5,8 H 3.2 M 12.8,8 H 14.5 M 3.4,3.4 L 4.6,4.6 M 11.4,11.4 L 12.6,12.6 M 12.6,3.4 L 11.4,4.6 M 4.6,11.4 L 3.4,12.6",
            "Настройки «Твой остров»",
            () => { Hide(); onOpenSettings(); }));

        footerRow.Children.Add(MakeFooterIconButton(spec, false, false,
            "M 3.2,8 A 4.8,4.8 0 1 1 4.6,11.4 M 3.0,4.2 V 8.0 H 6.8",
            "Сбросить позицию острова",
            () => { getMainWindow()?.ResetPosition(); RefreshState(); }));

        footerRow.Children.Add(MakeFooterIconButton(spec, startupOn, false,
            "M 8.8,1.6 L 3.8,8.6 H 7.8 L 7.0,14.4 L 12.2,7.4 H 8.2 Z",
            startupOn ? "Автозапуск · Вкл" : "Автозапуск · Выкл",
            () =>
            {
                try { StartupManager.SetEnabled(!StartupManager.IsEnabled); }
                catch (Exception) { }
                RefreshState();
            }));

        footerRow.Children.Add(MakeFooterIconButton(spec, false, true,
            "M 8,1.8 V 7.8 M 4.2,3.8 A 5.4,5.4 0 1 0 11.8,3.8",
            "Выход из HoshiBar",
            () => { Hide(); onExit(); }));
    }

    private FrameworkElement MakeBentoSquareTile(
        SkinPaletteSpec spec,
        int slotIndex,
        bool active,
        string iconPath,
        string callout,
        Action onLeftClick,
        Action? onRightClick = null,
        bool dashedIcon = false)
    {
        var cell = new Grid { Height = 64, Margin = new Thickness(4) };
        var corner = new CornerRadius(spec.IsPixelMode ? 0 : 14);

        var shadow = new Border
        {
            CornerRadius = corner,
            Background = IslandSkinEngine.Frozen(Color.FromRgb(0x2A, 0x1E, 0x2E)),
            RenderTransform = new TranslateTransform(3, 3),
            Visibility = spec.IsCartoonSticker ? Visibility.Visible : Visibility.Collapsed,
            IsHitTestVisible = false,
        };

        Brush tileBg;
        Brush tileBorder;
        double borderThick = spec.IsCartoonSticker ? 2.2 : spec.IsPixelMode ? 2.0 : 1.2;
        Brush iconStroke;

        if (spec.IsCartoonSticker)
        {
            // Pastel comic sticker palette per tile (matches Chibi Pop column in t04_bento_4_skins_v2!)
            Color[] chibiPalette =
            {
                Color.FromRgb(0xFF, 0xDF, 0xEF),
                Color.FromRgb(0xDC, 0xF5, 0xFF),
                Color.FromRgb(0xFF, 0xE6, 0x99),
                Color.FromRgb(0xE3, 0xF4, 0xFF),
                Color.FromRgb(0xFF, 0xF9, 0xE2),
                Color.FromRgb(0xFF, 0xC6, 0xDC),
            };
            tileBg = IslandSkinEngine.Frozen(chibiPalette[slotIndex % chibiPalette.Length]);
            tileBorder = IslandSkinEngine.Frozen(Color.FromRgb(0x2A, 0x1E, 0x2E));
            iconStroke = IslandSkinEngine.Frozen(Color.FromRgb(0x2A, 0x1E, 0x2E));
        }
        else if (spec.IsShittimTablet)
        {
            var isBlueSlot = active || slotIndex == 2;
            var isPinkSlot = slotIndex == 5;
            tileBg = isPinkSlot
                ? IslandSkinEngine.Frozen(Color.FromRgb(0xFF, 0x85, 0xAD))
                : isBlueSlot
                    ? IslandSkinEngine.Frozen(Color.FromRgb(0x38, 0xBD, 0xF8))
                    : IslandSkinEngine.Frozen(Color.FromArgb(0xEC, 0xFF, 0xFF, 0xFF));
            tileBorder = isPinkSlot
                ? IslandSkinEngine.Frozen(Color.FromRgb(0xFF, 0x85, 0xAD))
                : active || slotIndex == 0
                    ? IslandSkinEngine.Frozen(Color.FromRgb(0x38, 0xBD, 0xF8))
                    : IslandSkinEngine.Frozen(Color.FromRgb(0xC8, 0xE0, 0xF6));
            borderThick = active ? 2.0 : 1.3;
            iconStroke = isPinkSlot || isBlueSlot ? Brushes.White : IslandSkinEngine.Frozen(spec.Text);
        }
        else if (spec.IsAeroGlass)
        {
            tileBg = IslandSkinEngine.CreateAeroConvexGlassBrush(active);
            tileBorder = IslandSkinEngine.Frozen(Color.FromArgb(active ? (byte)0xF0 : (byte)0xB0, 0xE6, 0xFA, 0xFF));
            iconStroke = Brushes.White;
        }
        else if (spec.IsPixelMode)
        {
            tileBg = active
                ? IslandSkinEngine.Frozen(Color.FromRgb(0xB8, 0xF0, 0xDC))
                : IslandSkinEngine.Frozen(Color.FromRgb(0xD8, 0xCE, 0xE8));
            tileBorder = IslandSkinEngine.Frozen(Color.FromRgb(0x2B, 0x20, 0x3D));
            iconStroke = IslandSkinEngine.Frozen(Color.FromRgb(0x1E, 0x16, 0x2E));
        }
        else
        {
            // Horus Glass: neon sakura / cyan rimmed active tiles on dark smoked glass
            var isGhostSlot = slotIndex == 1;
            var glowColor = isGhostSlot ? spec.SecondaryAccent : spec.Accent;
            tileBg = active
                ? IslandSkinEngine.Frozen(Color.FromArgb(0x48, glowColor.R, glowColor.G, glowColor.B))
                : (Brush)Application.Current.Resources["HoshinoSurface"];
            tileBorder = active
                ? IslandSkinEngine.Frozen(glowColor)
                : (Brush)Application.Current.Resources["HoshinoBorder"];
            borderThick = active ? 1.8 : 1.1;
            iconStroke = active ? IslandSkinEngine.Frozen(glowColor) : IslandSkinEngine.Frozen(spec.Text);
        }

        var card = new Border
        {
            CornerRadius = corner,
            Background = tileBg,
            BorderBrush = tileBorder,
            BorderThickness = new Thickness(borderThick),
            Cursor = Cursors.Hand,
        };

        var innerGrid = new Grid();
        if (spec.IsPixelMode)
        {
            innerGrid.Children.Add(new Border { BorderBrush = Brushes.White, BorderThickness = new Thickness(2.5, 2.5, 0, 0), IsHitTestVisible = false });
            innerGrid.Children.Add(new Border { BorderBrush = IslandSkinEngine.Frozen(Color.FromRgb(0x68, 0x56, 0x84)), BorderThickness = new Thickness(0, 0, 2.5, 2.5), IsHitTestVisible = false });
        }
        else if (spec.IsAeroGlass)
        {
            innerGrid.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(13),
                BorderBrush = IslandSkinEngine.Frozen(Color.FromArgb(0x68, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
            });
        }

        var glyph = new Path
        {
            Data = Geometry.Parse(iconPath),
            Width = 22,
            Height = 22,
            Stretch = Stretch.Uniform,
            Stroke = iconStroke,
            StrokeThickness = spec.IsCartoonSticker ? 2.0 : 1.75,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeDashArray = dashedIcon ? new DoubleCollection { 2.2, 1.5 } : null,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (spec.IsPixelMode)
            RenderOptions.SetEdgeMode(glyph, EdgeMode.Aliased);

        innerGrid.Children.Add(glyph);
        card.Child = innerGrid;

        card.MouseEnter += (_, _) =>
        {
            if (spec.IsPixelMode && !active)
                card.Background = IslandSkinEngine.Frozen(Color.FromRgb(0xC6, 0xF2, 0xE2));
            ShowCalloutOver(card, callout, preferAbove: true);
        };
        card.MouseLeave += (_, _) =>
        {
            card.Background = tileBg;
            HideCallout();
        };
        card.MouseLeftButtonUp += (_, e) => { e.Handled = true; onLeftClick(); };
        if (onRightClick is not null)
            card.MouseRightButtonUp += (_, e) => { e.Handled = true; onRightClick(); };

        cell.Children.Add(shadow);
        cell.Children.Add(card);
        return cell;
    }

    private FrameworkElement MakeFooterIconButton(
        SkinPaletteSpec spec,
        bool active,
        bool isDanger,
        string iconPath,
        string callout,
        Action onClick)
    {
        var wrap = new Border
        {
            Height = 32,
            Margin = new Thickness(4, 0, 4, 0),
            CornerRadius = new CornerRadius(spec.IsPixelMode ? 0 : 10),
            Cursor = Cursors.Hand,
            Background = spec.IsPixelMode
                ? IslandSkinEngine.Frozen(Color.FromRgb(0xD8, 0xCE, 0xE8))
                : spec.IsAeroGlass
                    ? IslandSkinEngine.CreateAeroConvexGlassBrush(active)
                    : active
                        ? IslandSkinEngine.Frozen(Color.FromArgb(0x38, spec.Gold.R, spec.Gold.G, spec.Gold.B))
                        : Brushes.Transparent,
            BorderBrush = spec.IsPixelMode
                ? IslandSkinEngine.Frozen(Color.FromRgb(0x2B, 0x20, 0x3D))
                : active
                    ? IslandSkinEngine.Frozen(spec.Gold)
                    : spec.IsAeroGlass
                        ? IslandSkinEngine.Frozen(Color.FromArgb(0x88, 0xE0, 0xF6, 0xFF))
                        : Brushes.Transparent,
            BorderThickness = new Thickness(spec.IsPixelMode ? 1.6 : active || spec.IsAeroGlass ? 1.0 : 0),
        };

        Brush stroke = isDanger
            ? IslandSkinEngine.Frozen(spec.IsLight ? Color.FromRgb(0xD6, 0x3C, 0x5E) : Color.FromRgb(0xFF, 0x7A, 0x98))
            : active
                ? IslandSkinEngine.Frozen(spec.IsLight ? spec.Text : spec.Gold)
                : spec.IsAeroGlass
                    ? Brushes.White
                    : IslandSkinEngine.Frozen(spec.Text);

        var glyph = new Path
        {
            Data = Geometry.Parse(iconPath),
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform,
            Stroke = stroke,
            StrokeThickness = spec.IsCartoonSticker ? 1.9 : 1.65,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (spec.IsPixelMode)
            RenderOptions.SetEdgeMode(glyph, EdgeMode.Aliased);

        wrap.Child = glyph;
        BindCallout(wrap, callout);
        wrap.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        return wrap;
    }

    private FrameworkElement MakeTransportButton(string pathData, string callout, Action onClick)
    {
        var btn = new Border
        {
            Width = 24,
            Height = 22,
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 3, 0),
        };
        var p = new Path
        {
            Data = Geometry.Parse(pathData),
            Width = 11,
            Height = 11,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        p.SetResourceReference(Shape.FillProperty, "HoshinoText");
        btn.Child = p;
        BindCallout(btn, callout);
        btn.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        return btn;
    }

    private void BindCallout(FrameworkElement target, string text)
    {
        target.MouseEnter += (_, _) => ShowCalloutOver(target, text, preferAbove: true);
        target.MouseLeave += (_, _) => HideCallout();
    }

    /// <summary>Shows an unclipped floating speech-bubble Popup directly above the hovered tile, freely extending past window bounds.</summary>
    private void ShowCalloutOver(FrameworkElement target, string text, bool preferAbove)
    {
        if (!IsVisible || PresentationSource.FromVisual(target) is null) return;
        var spec = IslandSkinEngine.Current;

        var bg = spec.IsCartoonSticker
            ? IslandSkinEngine.Frozen(Color.FromRgb(0xFF, 0xF3, 0xB8))
            : spec.IsPixelMode
                ? IslandSkinEngine.Frozen(Color.FromRgb(0xC9, 0xED, 0xE3))
                : spec.IsShittimTablet
                    ? IslandSkinEngine.Frozen(Color.FromRgb(0xEE, 0xF8, 0xFF))
                    : spec.IsAeroGlass
                        ? IslandSkinEngine.Frozen(Color.FromArgb(0xF2, 0x10, 0x4A, 0x7E))
                        : IslandSkinEngine.Frozen(Color.FromArgb(0xF6, 0x20, 0x2C, 0x3C));

        var borderBrush = spec.IsCartoonSticker
            ? IslandSkinEngine.Frozen(Color.FromRgb(0x2A, 0x1E, 0x2E))
            : spec.IsPixelMode
                ? IslandSkinEngine.Frozen(Color.FromRgb(0x1E, 0x16, 0x2E))
                : spec.IsShittimTablet
                    ? IslandSkinEngine.Frozen(Color.FromRgb(0x38, 0xBD, 0xF8))
                    : IslandSkinEngine.Frozen(spec.SecondaryAccent);

        var fg = spec.IsPixelMode
            ? IslandSkinEngine.Frozen(Color.FromRgb(0x14, 0x2D, 0x2A))
            : spec.IsAeroGlass || !spec.IsLight
                ? Brushes.White
                : IslandSkinEngine.Frozen(spec.Text);

        var strokeW = spec.IsCartoonSticker ? 2.2 : spec.IsPixelMode ? 2.0 : 1.4;

        var bubbleGrid = new Grid();
        if (spec.IsCartoonSticker)
        {
            bubbleGrid.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = IslandSkinEngine.Frozen(Color.FromRgb(0x2A, 0x1E, 0x2E)),
                RenderTransform = new TranslateTransform(2.8, 2.8),
                IsHitTestVisible = false,
            });
        }

        var bubble = new Border
        {
            CornerRadius = new CornerRadius(spec.IsPixelMode ? 0 : 10),
            Padding = new Thickness(12, 6, 12, 6),
            Background = bg,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(strokeW),
            Child = new TextBlock
            {
                Text = text,
                FontFamily = IslandSkinEngine.GetThemeFontFamily(spec),
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Foreground = fg,
                TextWrapping = TextWrapping.NoWrap,
            },
        };
        TextOptions.SetTextFormattingMode(bubble, TextFormattingMode.Ideal);
        bubbleGrid.Children.Add(bubble);

        // Seamless downward speech-bubble tail centered beneath the callout
        var tailCanvas = new Canvas
        {
            Width = 14,
            Height = 7,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, -strokeW + 0.4, 0, 0),
        };
        tailCanvas.Children.Add(new Polygon
        {
            Fill = bg,
            Points = new PointCollection { new(1, 0), new(13, 0), new(7, 6.5) },
        });
        tailCanvas.Children.Add(new Path
        {
            Data = Geometry.Parse("M 1,0.5 L 7,6.5 L 13,0.5"),
            Stroke = borderBrush,
            StrokeThickness = strokeW,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        });

        var stack = new StackPanel
        {
            Margin = new Thickness(4, 4, 8, 6),
            IsHitTestVisible = false,
        };
        if (spec.IsPixelMode)
        {
            stack.SnapsToDevicePixels = true;
            stack.UseLayoutRounding = true;
        }

        // Measure the bubble first so it freely overhangs TrayFlyoutWindow, while keeping its tail pointed
        // dead-center at the hovered tile even when clamped at the physical monitor edge.
        bubbleGrid.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var bubbleW = Math.Max(48, bubbleGrid.DesiredSize.Width);
        var targetW = target.ActualWidth > 1 ? target.ActualWidth : 64;
        var dpi = VisualTreeHelper.GetDpi(target);

        var idealLeftDip = (targetW - bubbleW) / 2.0;
        var actualLeftDip = idealLeftDip;
        try
        {
            var targetScreenTl = target.PointToScreen(new Point(0, 0));
            var mon = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)targetScreenTl.X, (int)targetScreenTl.Y)).WorkingArea;
            var targetScreenLeftDip = targetScreenTl.X / dpi.DpiScaleX;
            var minLeftDip = (mon.Left / dpi.DpiScaleX + 4.0) - targetScreenLeftDip;
            var maxLeftDip = (mon.Right / dpi.DpiScaleX - bubbleW - 8.0) - targetScreenLeftDip;
            if (maxLeftDip >= minLeftDip)
                actualLeftDip = Math.Clamp(idealLeftDip, minLeftDip, maxLeftDip);
        }
        catch
        {
            // Keep ideal centered overhang if screen query fails
        }

        var tailCenterInBubble = Math.Clamp((targetW / 2.0) - actualLeftDip, 15.0, Math.Max(15.0, bubbleW - 15.0));
        tailCanvas.HorizontalAlignment = HorizontalAlignment.Left;
        tailCanvas.Margin = new Thickness(tailCenterInBubble - 7.0, -strokeW + 0.4, 0, 0);

        stack.Children.Add(bubbleGrid);
        stack.Children.Add(tailCanvas);

        calloutPopup.IsOpen = false;
        calloutPopup.Child = stack;
        calloutPopup.PlacementTarget = target;
        calloutPopup.CustomPopupPlacementCallback = (popupSize, targetSize, _) =>
        {
            var xPx = (actualLeftDip - 4.0) * dpi.DpiScaleX;
            var yPx = preferAbove
                ? -popupSize.Height + 2.0 * dpi.DpiScaleY
                : targetSize.Height + 2.0 * dpi.DpiScaleY;
            return new[]
            {
                new CustomPopupPlacement(new Point(xPx, yPx), PopupPrimaryAxis.Horizontal),
            };
        };
        calloutPopup.IsOpen = true;
    }

    private void HideCallout() => calloutPopup.IsOpen = false;

    private void PopulateFlyoutChrome(SkinPaletteSpec spec)
    {
        chromeCanvas.Children.Clear();
        RenderOptions.SetEdgeMode(chromeCanvas, spec.IsPixelMode ? EdgeMode.Aliased : EdgeMode.Unspecified);

        if (spec.IsAeroGlass)
        {
            var bands = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0.55) };
            bands.GradientStops.Add(new GradientStop(Color.FromArgb(0x52, 0xFF, 0xFF, 0xFF), 0.0));
            bands.GradientStops.Add(new GradientStop(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF), 0.25));
            bands.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.27));
            bands.GradientStops.Add(new GradientStop(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), 0.52));
            bands.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.60));
            bands.Freeze();
            chromeCanvas.Children.Add(new Border
            {
                Width = 314,
                Height = 220,
                CornerRadius = new CornerRadius(14),
                Background = bands,
            });
        }
        else if (spec.IsShittimTablet)
        {
            // Diagonal cyan Schale OS stripes in top-right corner (matches Shittim Chest column in t04_bento_4_skins_v2!)
            var cyan = IslandSkinEngine.Frozen(Color.FromRgb(0x64, 0xC8, 0xF8));
            for (var i = 0; i < 3; i++)
            {
                var ox = 258 + i * 14;
                chromeCanvas.Children.Add(new Polygon
                {
                    Fill = cyan,
                    Opacity = 0.55,
                    Points = new PointCollection { new(ox, 0), new(ox + 8, 0), new(ox + 28, 20), new(ox + 20, 20) },
                });
            }
        }
        else if (spec.IsPixelMode)
        {
            // 4 corner pixel screws on the lavender handheld casing
            void Screw(double sx, double sy)
            {
                var r = new Rectangle
                {
                    Width = 7,
                    Height = 7,
                    Fill = IslandSkinEngine.Frozen(Color.FromRgb(0xB9, 0xAC, 0xCF)),
                    Stroke = IslandSkinEngine.Frozen(Color.FromRgb(0x49, 0x3A, 0x62)),
                    StrokeThickness = 1,
                };
                Canvas.SetLeft(r, sx);
                Canvas.SetTop(r, sy);
                chromeCanvas.Children.Add(r);
            }
            Screw(5, 5);
            Screw(300, 5);
        }
    }
}
