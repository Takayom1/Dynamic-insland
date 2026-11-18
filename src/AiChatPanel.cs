// Original HoshiBar code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DIsland;

/// <summary>One panel and one session per island. Hiding it never disposes the conversation.</summary>
public sealed class AiChatPanel : Grid, IDisposable
{
    private readonly AssistantProviderRouter router = AssistantRuntime.Router;
    private readonly AssistantVoice voice = new();
    private readonly DIsland.Services.AppSettings design;
    private readonly HoshinoCompanion head;
    private readonly HoshinoCompanion hero;
    private readonly StackPanel heroPanel = new();
    private readonly Dictionary<string, Button> providerChips = new(StringComparer.OrdinalIgnoreCase);
    public bool IsListening => voice.Listening;
    public bool MicrophoneActive => voice.MicrophoneActive;
    public bool VoiceInteraction => voice.InteractionActive;
    public bool IsCalibrating => voice.Calibrating;
    public bool IsSpeaking => voice.IsSpeaking;
    public bool PreparingVoice => voice.PreparingVoice;
    public bool IsTranscribing => voice.Transcribing;
    public event Action? VoiceActivated;
    public event Action? VoiceStateChanged;
    public event Action<string>? ActivityChanged;
    private ChatConversation? conversation;
    private ChatMessage? assistantMessage;
    private DateTime lastSave;
    private readonly StackPanel messages = new();
    private readonly ScrollViewer scroll;
    private readonly TextBlock status = new() { Text = "", FontSize = 11 };
    private readonly Button send;
    private readonly Button stop;
    private TextBlock? response;
    private string? thread;
    private bool busy, stopRequested;
    public TextBox Input { get; } = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 76, MinHeight = 38, FontSize = 13 };
    public event Action<TextBox>? TypingRequested;

    public AiChatPanel(DIsland.Services.AppSettings? appearance = null)
    {
        design = appearance ?? new();
        AssistantRuntime.Voice = voice;
        status.SetResourceReference(TextBlock.ForegroundProperty, "HoshinoMuted");

        head = new HoshinoCompanion(design) { Width = 40, Height = 40, Margin = new Thickness(0, 0, 12, 0), Listening = () => voice.Listening, Working = () => busy };
        hero = new HoshinoCompanion(design) { Width = 100, Height = 100, Listening = () => voice.Listening, Working = () => busy };
        heroPanel.Children.Add(hero);
        var heroPhrase = new TextBlock
        {
            Text = "Я всегда тебе помогу",
            FontSize = 13.5,
            MaxWidth = 380,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 12),
        };
        heroPhrase.SetResourceReference(TextBlock.ForegroundProperty, "HoshinoMuted");
        heroPanel.Children.Add(heroPhrase);

        Height = 450;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var title = new StackPanel();
        title.Children.Add(status);
        var tools = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
        foreach (var (pid, label, tip) in new[]
        {
            (AssistantProviders.Codex, "Codex", "Провайдер Codex CLI · ChatGPT"),
            (AssistantProviders.Gemini, "Gemini", "Провайдер Gemini CLI · Google"),
            (AssistantProviders.Claude, "Claude", "Провайдер Claude Code · Anthropic"),
        })
        {
            var chip = MakeButton(label, tip);
            chip.Margin = new Thickness(0, 0, 4, 4);
            var captured = pid;
            chip.Click += async (_, _) => await SwitchProviderFromHeader(captured);
            providerChips[pid] = chip;
            tools.Children.Add(chip);
        }

        var divider = new Border { Width = 1, Height = 18, Margin = new Thickness(4, 2, 8, 6), Opacity = 0.4 };
        divider.SetResourceReference(Border.BackgroundProperty, "HoshinoBorder");
        tools.Children.Add(divider);

        var settings = MakeButton("Настройки", "Провайдер AI, аккаунт, модель, файлы и голос");
        settings.Click += (_, _) => AssistantSettingsPanel.Open();
        tools.Children.Add(settings);
        var fresh = MakeButton("Новый чат", "Начать новый диалог");
        fresh.Click += (_, _) => { if (!busy) ResetConversation(); };
        tools.Children.Add(fresh);
        var history = MakeButton("История", "Сохранённые диалоги выбранного провайдера");
        history.Click += async (_, _) => await ShowHistory();
        tools.Children.Add(history);
        title.Children.Add(tools);
        heading.Children.Add(title);
        Children.Add(heading);

        scroll = new ScrollViewer
        {
            Content = messages,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 0, 0, 10),
        };
        SetRow(scroll, 1);
        Children.Add(scroll);
        messages.Children.Add(heroPanel);
        ApplyDesign();

        var composer = new Border
        {
            CornerRadius = new CornerRadius(15),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 9, 12, 8),
        };
        composer.SetResourceReference(Border.BackgroundProperty, "HoshinoSurface");
        composer.SetResourceReference(Border.BorderBrushProperty, "HoshinoBorder");

        var body = new StackPanel();
        Input.SetResourceReference(StyleProperty, "IslandTextBox");
        RefreshProviderVisuals();
        Input.PreviewMouseLeftButtonDown += (_, _) => TypingRequested?.Invoke(Input);
        Input.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                e.Handled = true;
                await Send();
            }
        };
        body.Children.Add(Input);

        var actions = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        send = MakeButton("Отправить ↑", "Отправить сообщение");
        send.Click += async (_, _) => await Send();
        stop = MakeButton("Стоп ■", "Остановить ответ");
        stop.Visibility = Visibility.Collapsed;
        stop.Click += async (_, _) => await Interrupt();
        var right = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth = 320 };
        var microphone = MakeButton("Микрофон", "Диктовка: нажмите, говорите, затем проверьте текст");
        microphone.Click += (_, _) => { try { if (busy) return; voice.Listen(); } catch (Exception e) { status.Text = e.Message; } };
        right.Children.Add(microphone);
        var silence = MakeButton("Без звука", "Остановить озвучивание");
        silence.Click += (_, _) => { voice.Silence(); status.Text = "Озвучивание остановлено"; };
        right.Children.Add(silence);
        right.Children.Add(stop);
        right.Children.Add(send);
        DockPanel.SetDock(right, Dock.Right);
        actions.Children.Add(right);

        var enterHint = new TextBlock { Text = "MomoTalk · Enter ↵", FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
        enterHint.SetResourceReference(TextBlock.ForegroundProperty, "HoshinoMuted");
        actions.Children.Add(enterHint);

        body.Children.Add(actions);
        composer.Child = body;
        SetRow(composer, 2);
        Children.Add(composer);

        var footer = new TextBlock { Text = "Локальная история · защита действий IRON HORUS · доступ и голос в настройках", FontSize = 10, Margin = new Thickness(2, 7, 0, 0) };
        footer.SetResourceReference(TextBlock.ForegroundProperty, "HoshinoMuted");
        SetRow(footer, 3);
        Children.Add(footer);

        MouseLeftButtonUp += (_, e) => e.Handled = true;
        router.Delta += OnRouterDelta;
        router.Activity += OnRouterActivity;
        router.TurnCompleted += OnRouterTurnCompleted;
        router.Disconnected += OnRouterDisconnected;
        router.AccountUpdated += OnRouterAccountUpdated;
        voice.Recognized += text => Dispatcher.InvokeAsync(() => { Input.Text = text; status.Text = "Диктовка готова — проверьте текст и отправьте"; });
        voice.Status += text => Dispatcher.InvokeAsync(() => { status.Text = text; ActivityChanged?.Invoke(text); });
        voice.Activated += () => VoiceActivated?.Invoke();
        voice.StateChanged += () => VoiceStateChanged?.Invoke();
        voice.CommandRecognized += async text =>
        {
            if (busy) return;
            if (!string.IsNullOrWhiteSpace(Input.Text))
            {
                Input.Text += "\n" + text;
                status.Text = "Диктовка добавлена к черновику — отправьте вручную";
                return;
            }
            Input.Text = text;
            await Send();
        };
        Dispatcher.BeginInvoke(() => voice.Configure());
        AssistantPreferences.Changed += PreferencesChanged;
        ObsidianPreferences.Changed += PreferencesChanged;
        AssistantRuntime.AccountChanged += AccountChanged;
    }

    private static Button MakeButton(string text, string tooltip) => new()
    {
        Content = text,
        ToolTip = tooltip,
        Style = (Style)Application.Current.FindResource("ChipButton"),
        MinHeight = 28,
    };

    /// <summary>Creates a MomoTalk-styled message bubble (Sensei bubble on right, Hoshino avatar + bubble on left).</summary>
    private TextBlock Bubble(string text, bool user)
    {
        var spec = IslandSkinEngine.Current;
        var label = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            LineHeight = 19.5,
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "HoshinoText");

        var senderTag = new TextBlock
        {
            Text = user
                ? "СЕНСЕЙ"
                : $"ХОШИНО · {AssistantProviders.ShortName(AssistantPreferences.Current.Provider).ToUpperInvariant()}",
            FontSize = 9.5,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        };
        senderTag.SetResourceReference(TextBlock.ForegroundProperty, user ? "HoshinoSecondaryAccent" : "HoshinoAccent");

        var block = new StackPanel();
        block.Children.Add(senderTag);
        block.Children.Add(label);

        var radius = spec.IsPixelMode
            ? new CornerRadius(2)
            : user
                ? new CornerRadius(14, 14, 3, 14)
                : new CornerRadius(3, 14, 14, 14);

        var bubble = new Border
        {
            Child = block,
            CornerRadius = radius,
            BorderThickness = new Thickness(spec.IsCartoonSticker ? 2.0 : 1.2),
            Padding = new Thickness(12, 9, 12, 9),
        };
        bubble.SetResourceReference(Border.BackgroundProperty, user ? "HoshinoControl" : "HoshinoSurface");
        bubble.SetResourceReference(Border.BorderBrushProperty, user ? "HoshinoSecondaryAccent" : "HoshinoBorder");

        if (user)
        {
            bubble.Margin = new Thickness(42, 0, 2, 10);
            bubble.HorizontalAlignment = HorizontalAlignment.Right;
            bubble.MaxWidth = 420;
            messages.Children.Add(bubble);
        }
        else
        {
            var row = new Grid { Margin = new Thickness(0, 0, 24, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());

            if (design.ShowHoshino)
            {
                var avatar = new Border
                {
                    Width = 30,
                    Height = 30,
                    CornerRadius = new CornerRadius(spec.IsPixelMode ? 2 : 15),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 2, 8, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = IslandChromeRenderer.CreateMiniHoshinoTabIcon(spec.IsPixelMode),
                };
                avatar.SetResourceReference(Border.BackgroundProperty, "HoshinoCompanionSurface");
                avatar.SetResourceReference(Border.BorderBrushProperty, "HoshinoBorder");
                row.Children.Add(avatar);
            }

            Grid.SetColumn(bubble, 1);
            bubble.HorizontalAlignment = HorizontalAlignment.Left;
            row.Children.Add(bubble);
            messages.Children.Add(row);
        }

        scroll?.ScrollToEnd();
        return label;
    }

    private async Task SwitchProviderFromHeader(string providerId)
    {
        if (busy) return;
        var normalized = AssistantProviders.Normalize(providerId);
        if (AssistantProviders.Normalize(AssistantPreferences.Current.Provider) != normalized)
        {
            AssistantPreferences.Current.Provider = normalized;
            AssistantPreferences.Current.Save(notify: true);
        }
        RefreshProviderVisuals();
        try
        {
            status.Text = await AssistantAccount.Refresh();
        }
        catch (Exception e)
        {
            status.Text = e.Message;
        }
    }

    private void RefreshProviderVisuals()
    {
        var active = AssistantProviders.Normalize(AssistantPreferences.Current.Provider);
        var shortName = AssistantProviders.ShortName(active);
        Input.ToolTip = $"Сообщение Хошино ({shortName}) · Enter — отправить, Shift+Enter — новая строка";
        System.Windows.Automation.AutomationProperties.SetName(Input, $"Сообщение Хошино ({shortName})");

        foreach (var (pid, btn) in providerChips)
        {
            var isSelected = string.Equals(pid, active, StringComparison.OrdinalIgnoreCase);
            btn.FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal;
            btn.Opacity = isSelected ? 1.0 : 0.72;
            if (isSelected)
            {
                btn.SetResourceReference(Control.BackgroundProperty, "HoshinoAccent");
                btn.SetResourceReference(Control.ForegroundProperty, "HoshinoOnAccent");
            }
            else
            {
                btn.SetResourceReference(Control.BackgroundProperty, "HoshinoControl");
                btn.SetResourceReference(Control.ForegroundProperty, "HoshinoText");
            }
        }
    }

    private void SetBusy(bool value)
    {
        busy = value;
        voice.PauseForBusy(value);
        AssistantRuntime.Busy = value;
        if (!value)
        {
            AssistantRuntime.ActiveThread = null;
            AssistantRuntime.ActiveTurn = null;
        }
        else AssistantCues.Play(AssistantCue.Working);

        ActivityChanged?.Invoke(value ? "Думаю…" : "Готово");
        send.IsEnabled = !value;
        foreach (var chip in providerChips.Values) chip.IsEnabled = !value;
        stop.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        stop.IsEnabled = value;
    }

    private async Task Send()
    {
        if (busy || string.IsNullOrWhiteSpace(Input.Text)) return;
        var text = Input.Text.Trim();
        var activeBackend = router.Active;
        var shortName = AssistantProviders.ShortName(activeBackend.ProviderId);

        SetBusy(true);
        stopRequested = false;
        status.Text = $"Подключение к {shortName}…";
        try
        {
            var accountInfo = await activeBackend.RefreshAccountAsync();
            if (AssistantRuntime.AccountKey.Length == 0)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(accountInfo)
                        ? $"Откройте настройки помощника и подключите {AssistantProviders.DisplayName(activeBackend.ProviderId)}"
                        : accountInfo);
            }

            if (thread == null)
            {
                var prefs = AssistantPreferences.Current;
                thread = await activeBackend.StartSessionAsync();
                conversation = new ChatConversation
                {
                    Provider = activeBackend.ProviderId,
                    ThreadId = thread,
                    AccountKey = AssistantRuntime.AccountKey,
                    Model = prefs.GetModelFor(activeBackend.ProviderId),
                    Title = text.Length > 48 ? text[..48] + "…" : text,
                };
            }
            if (stopRequested) { SetBusy(false); status.Text = "Остановлено"; return; }
            messages.Children.Remove(heroPanel);
            Input.Clear();
            Bubble(text, true);
            Record("Вы: " + text);
            conversation?.Messages.Add(new ChatMessage { Role = "user", Text = text });
            assistantMessage = new ChatMessage { Role = "assistant", Text = "" };
            conversation?.Messages.Add(assistantMessage);
            SaveConversation();
            response = Bubble("", false);
            status.Text = $"Хошино ({shortName}) отвечает…";
            AssistantRuntime.ActiveThread = thread;

            await activeBackend.StartTurnAsync(thread, text, resolvedSessionId => Dispatcher.InvokeAsync(() =>
            {
                thread = resolvedSessionId;
                if (conversation != null)
                {
                    conversation.ThreadId = resolvedSessionId;
                    SaveConversation();
                }
            }));

            if (busy && stopRequested) await Interrupt();
        }
        catch (Exception e)
        {
            SetBusy(false);
            status.Text = "Не удалось выполнить запрос";
            ActivityChanged?.Invoke(status.Text);
            AssistantCues.Play(AssistantCue.Error);
            Bubble("Ошибка: " + e.Message, false);
        }
    }

    private async Task Interrupt()
    {
        stopRequested = true;
        stop.IsEnabled = false;
        status.Text = "Останавливаем…";
        if (thread == null) return;
        try { await router.Active.InterruptAsync(); }
        catch (Exception e) { status.Text = "Не удалось остановить: " + e.Message; stop.IsEnabled = true; }
    }

    private void OnRouterDelta(string chunk) => Dispatcher.InvokeAsync(() =>
    {
        if (!busy || response == null) return;
        response.Text += chunk;
        scroll.ScrollToEnd();
        if (assistantMessage != null) assistantMessage.Text = response.Text;
        if ((DateTime.UtcNow - lastSave).TotalSeconds > 2) SaveConversation();
    });

    private void OnRouterActivity(string activityText) => Dispatcher.InvokeAsync(() =>
    {
        if (!busy) return;
        status.Text = activityText;
        ActivityChanged?.Invoke(activityText);
    });

    private void OnRouterTurnCompleted(string turnStatus, string? errorMessage) => Dispatcher.InvokeAsync(() =>
    {
        if (!busy) return;
        var shortName = AssistantProviders.ShortName(router.ActiveId);
        if (response != null) Record($"{shortName}: " + response.Text);
        SaveConversation();
        SetBusy(false);

        status.Text = turnStatus == "interrupted"
            ? "Ответ остановлен"
            : turnStatus == "failed"
                ? (errorMessage ?? "Запрос завершился ошибкой")
                : "Готово";
        ActivityChanged?.Invoke(status.Text);

        if (turnStatus == "failed")
        {
            AssistantCues.Play(AssistantCue.Error);
            if (response != null && string.IsNullOrWhiteSpace(response.Text) && !string.IsNullOrWhiteSpace(errorMessage))
            {
                response.Text = "Ошибка: " + errorMessage;
                if (assistantMessage != null) assistantMessage.Text = response.Text;
                SaveConversation();
            }
        }
        else if (turnStatus == "completed" && response != null && !string.IsNullOrWhiteSpace(response.Text))
        {
            try { AssistantCues.Play(AssistantCue.Reply); voice.Speak(response.Text); }
            catch (Exception e) { status.Text = "Озвучивание недоступно: " + e.Message; }
        }
    });

    private void OnRouterDisconnected() => Dispatcher.InvokeAsync(() =>
    {
        SetBusy(false);
        thread = null;
        status.Text = "Соединение закрыто. Следующий запрос подключится заново.";
    });

    private void OnRouterAccountUpdated() => Dispatcher.InvokeAsync(async () =>
    {
        try { status.Text = await AssistantAccount.Refresh(); }
        catch (Exception e) { status.Text = e.Message; }
    });

    private void Record(string text)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlassChat", "history.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, text + Environment.NewLine);
        }
        catch (IOException) { status.Text = "Не удалось сохранить историю"; }
    }

    public void SetCompanionPhrase(string text) => ((TextBlock)heroPanel.Children[1]).Text = text;

    public void ApplyDesign()
    {
        head.Visibility = heroPanel.Visibility = design.ShowHoshino ? Visibility.Visible : Visibility.Collapsed;
        hero.Width = hero.Height = Math.Clamp(design.CompanionSize, 72, 140);
        RefreshProviderVisuals();
    }

    private void SaveConversation()
    {
        if (conversation == null) return;
        try { ConversationStore.Save(conversation); lastSave = DateTime.UtcNow; }
        catch (IOException) { status.Text = "Не удалось сохранить диалог"; }
    }

    private void ResetConversation()
    {
        voice.Stop();
        voice.Silence();
        SaveConversation();
        thread = null;
        conversation = null;
        assistantMessage = null;
        response = null;
        messages.Children.Clear();
        messages.Children.Add(heroPanel);
        ApplyDesign();
    }

    private void PreferencesChanged() => Dispatcher.InvokeAsync(() =>
    {
        RefreshProviderVisuals();
        if (!busy) ResetConversation();
        voice.Configure();
    });

    private void AccountChanged() => Dispatcher.InvokeAsync(() =>
    {
        if (busy) return;
        voice.Stop();
        voice.Silence();
        var hadOpenConversation = conversation != null;
        thread = null;
        conversation = null;
        assistantMessage = null;
        response = null;
        messages.Children.Clear();
        if (hadOpenConversation)
            Bubble("Аккаунт или провайдер изменён. Начните новый диалог или выберите историю.", false);
        else
            messages.Children.Add(heroPanel);
    });

    private async Task ShowHistory()
    {
        if (busy) return;
        try
        {
            var msg = await AssistantAccount.Refresh();
            if (AssistantRuntime.AccountKey.Length == 0) { status.Text = msg; return; }
            var activeProvider = router.ActiveId;
            var shortName = AssistantProviders.ShortName(activeProvider);
            var records = ConversationStore.Read()
                .Where(c => AssistantProviders.MatchesConversation(c, AssistantRuntime.AccountKey, activeProvider))
                .OrderByDescending(c => c.Updated)
                .ToList();
            var window = new Window
            {
                Title = $"История диалогов · {shortName} · MomoTalk",
                Width = 520,
                Height = 450,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };
            window.SetResourceReference(Control.BackgroundProperty, "HoshinoBackground");
            window.SetResourceReference(Control.ForegroundProperty, "HoshinoText");
            AssistantTheme.Apply(window);

            var root = new DockPanel { Margin = new Thickness(18) };
            window.Content = root;
            var open = new Button { Content = "Продолжить выбранный диалог", Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 12, 0, 0) };
            DockPanel.SetDock(open, Dock.Bottom);
            root.Children.Add(open);

            var list = new ListBox
            {
                ItemsSource = records,
                DisplayMemberPath = "Title",
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(1),
            };
            list.SetResourceReference(Control.ForegroundProperty, "HoshinoText");
            list.SetResourceReference(Control.BorderBrushProperty, "HoshinoBorder");
            root.Children.Add(list);

            open.Click += async (_, _) =>
            {
                if (list.SelectedItem is not ChatConversation record || AssistantRuntime.Busy) return;
                open.IsEnabled = false;
                try
                {
                    if (!string.IsNullOrWhiteSpace(record.ThreadId))
                        await router.Active.ResumeSessionAsync(record.ThreadId);
                    SaveConversation();
                    conversation = record;
                    thread = record.ThreadId;
                    messages.Children.Clear();
                    foreach (var m in record.Messages) Bubble(m.Text, m.Role == "user");
                    window.Close();
                }
                catch (Exception e) { window.Title = e.Message; open.IsEnabled = true; }
            };
            window.Show();
        }
        catch (Exception e) { status.Text = e.Message; }
    }

    public void StartDictation()
    {
        if (busy) return;
        try { voice.Listen(); }
        catch (Exception e) { status.Text = e.Message; }
    }

    public void ToggleWakeEnabled()
    {
        AssistantPreferences.Current.WakeEnabled = !AssistantPreferences.Current.WakeEnabled;
        AssistantPreferences.Current.Save(false);
        voice.Configure();
    }

    public void StopListening() => voice.ReturnToWake();
    public void SetLowPower(bool value) => voice.SetLowPower(value);
    public void DisableWake()
    {
        AssistantPreferences.Current.WakeEnabled = false;
        AssistantPreferences.Current.Save(false);
        voice.Configure();
    }

    public void Dispose()
    {
        SaveConversation();
        if (ReferenceEquals(AssistantRuntime.Voice, voice)) AssistantRuntime.Voice = null;
        voice.Dispose();
        router.Delta -= OnRouterDelta;
        router.Activity -= OnRouterActivity;
        router.TurnCompleted -= OnRouterTurnCompleted;
        router.Disconnected -= OnRouterDisconnected;
        router.AccountUpdated -= OnRouterAccountUpdated;
        ObsidianPreferences.Changed -= PreferencesChanged;
        AssistantPreferences.Changed -= PreferencesChanged;
        AssistantRuntime.AccountChanged -= AccountChanged;
    }
}
