// Original HoshiBar code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DIsland.Services;

namespace DIsland;

public sealed class LyricsPanel : StackPanel, IDisposable
{
    private readonly LyricsService service = new();
    private readonly AppSettings design;
    private readonly TextBlock status = new() { FontSize = 11, Opacity = .6, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 2, 8) };
    private readonly StackPanel lines = new();
    private readonly ScrollViewer scroll;
    private readonly StackPanel body = new();
    private CancellationTokenSource? request;
    private string? key;
    private MediaInfo? media;
    private LyricsResult? result;
    private int active = -2;
    private bool enabled = true;
    private bool suspended;
    public void SetSuspended(bool value){if(suspended==value)return;suspended=value;if(value)request?.Cancel();else{key=null;Update(media);}}
    public bool CompactEnabled { get; private set; }
    public string CompactText { get; private set; } = "Ищем текст…";
    public event Action? LayoutChanged;
    public event Action? CompactChanged;
    public event Action? CompactModeChanged;

    public LyricsPanel(AppSettings? settings = null)
    {
        design = settings ?? new();
        service.Searching += message => { status.Text = message; Tick(); };
        Margin = new Thickness(0, 12, 0, 0);
        Visibility=Visibility.Collapsed;
        body.Children.Add(status);
        scroll = new ScrollViewer { Content = lines, MaxHeight = 190, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        body.Children.Add(scroll); Children.Add(body);
        MouseLeftButtonUp += (_, e) => e.Handled = true;
    }

    public void ToggleCompact()
    {
        CompactEnabled=!CompactEnabled; Tick(); CompactModeChanged?.Invoke();
    }

    public void Update(MediaInfo? current)
    {
        media = current;
        var next = current == null ? "" : string.Join("\n", current.Title, current.Artist, current.Album, Math.Round(current.Duration.TotalSeconds));
        if (next != key)
        {
            key = next; request?.Cancel(); result = null; lines.Children.Clear(); active = -2;
            if (enabled&&!suspended) _ = Load();
        }
        Tick();
    }
    private async Task Load()
    {
        request?.Cancel(); request?.Dispose(); request = new(); var cancel = request.Token;
        if (media == null) { status.Text = "Включите музыку в плеере"; Tick(); return; }
        var current = media; status.Text = "Ищем текст…"; result = null; lines.Children.Clear(); Tick();
        try
        {
            var found = await service.Get(current.Title, current.Artist, current.Album, current.Duration.TotalSeconds, cancel);
            if (cancel.IsCancellationRequested) return;
            result = found; status.Text = found.Status; active = -2;
            if (found.Lines.Count > 0) foreach (var line in found.Lines) lines.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(line.Text) ? "♪" : line.Text, TextWrapping = TextWrapping.Wrap, FontSize = Math.Clamp(design.ExpandedLyricFontSize,12,24), FontWeight = FontWeights.SemiBold, Opacity = .4, Margin = new Thickness(2, 7, 2, 7) });
            else if (!string.IsNullOrWhiteSpace(found.Plain)) lines.Children.Add(new TextBlock { Text = found.Plain, TextWrapping = TextWrapping.Wrap, FontSize = 14, Opacity = .8 });
            Tick();
        }
        catch (OperationCanceledException) { if (!cancel.IsCancellationRequested) { status.Text = "LRCLIB не ответил вовремя"; Tick(); } }
        catch (Exception) { if (!cancel.IsCancellationRequested) { status.Text = "Не удалось загрузить текст. Сверните и откройте для повтора"; Tick(); } }
    }
    public void Tick()
    {
        var visible=result is not null && (result.Lines.Count>0 || !string.IsNullOrWhiteSpace(result.Plain));
        var desired=visible ? Visibility.Visible : Visibility.Collapsed;
        if(Visibility!=desired) { Visibility=desired; LayoutChanged?.Invoke(); }
        status.Visibility=Visibility.Collapsed;

        var compact = media == null ? "Нет текущего трека" : !enabled ? "Откройте текст и включите LRCLIB" : result == null ? status.Text : result.Lines.Count == 0 ? (string.IsNullOrWhiteSpace(result.Plain) ? result.Status : "Текст есть, но без таймингов") : media.Duration <= TimeSpan.Zero ? "Плеер не передал позицию" : "♪ Вступление";
        var index = result != null && media is { Duration.Ticks: > 0 } && result.Lines.Count > 0
            ? LrcParser.Current(result.Lines, media.EstimatePosition(DateTimeOffset.UtcNow).TotalSeconds) : -1;
        if (index >= 0) compact = string.IsNullOrWhiteSpace(result!.Lines[index].Text) ? "♪" : result.Lines[index].Text;
        if (CompactText != compact) { CompactText = compact; CompactChanged?.Invoke(); }
        if (result == null || media == null || result.Lines.Count == 0 || body.Visibility != Visibility.Visible) return;
        if (media.Duration <= TimeSpan.Zero) { status.Text = "Текст найден, но плеер не передал позицию"; return; }
        if (index == active) return;
        for (var i = 0; i < lines.Children.Count; i++) ((TextBlock)lines.Children[i]).BeginAnimation(OpacityProperty, new DoubleAnimation(i == index ? 1 : Math.Clamp(design.LyricInactiveOpacity,.15,.75), TimeSpan.FromMilliseconds(design.ReduceMotion ? 0 : 180)));
        active = index;
        if (index >= 0 && lines.Children[index] is FrameworkElement row) { lines.UpdateLayout(); scroll.ScrollToVerticalOffset(row.TranslatePoint(new Point(), lines).Y - 65); }
    }
    public void ApplyDesign()
    {
        scroll.MaxHeight = design.CompactDesign ? 160 : 190;
        foreach (var row in lines.Children.OfType<TextBlock>()) row.FontSize = Math.Clamp(design.ExpandedLyricFontSize,12,24);
        active = -2; Tick();
    }
    public void Dispose() { request?.Cancel(); request?.Dispose(); service.Dispose(); }
}
