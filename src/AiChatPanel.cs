// Original Hoshino Island code: Copyright (c) 2026 Takayom1
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
    private readonly CodexBridge bridge = AssistantRuntime.Bridge;
    private readonly AssistantVoice voice = new();
    private readonly DIsland.Services.AppSettings design;
    private readonly HoshinoCompanion head;
    private readonly HoshinoCompanion hero;
    private readonly StackPanel heroPanel = new();
    public bool IsListening => voice.Listening;
    public bool MicrophoneActive=>voice.MicrophoneActive;
    public bool VoiceInteraction=>voice.InteractionActive;
    public bool IsCalibrating=>voice.Calibrating;
    public bool IsSpeaking=>voice.IsSpeaking;
    public bool PreparingVoice=>voice.PreparingVoice;
    public bool IsTranscribing=>voice.Transcribing;
    public event Action? VoiceActivated;
    public event Action? VoiceStateChanged;
    public event Action<string>? ActivityChanged;
    private ChatConversation? conversation;
    private ChatMessage? assistantMessage;
    private DateTime lastSave;
    private readonly StackPanel messages = new();
    private readonly ScrollViewer scroll;
    private readonly TextBlock status = new() { Text = "", FontSize = 11, Opacity = .65 };
    private readonly Button send;
    private readonly Button stop;
    private TextBlock? response;
    private string? thread, turn;
    private bool busy, stopRequested;
    public TextBox Input { get; } = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 76, MinHeight = 40, FontSize = 13 };
    public event Action<TextBox>? TypingRequested;

    public AiChatPanel(DIsland.Services.AppSettings? appearance = null)
    {
        design = appearance ?? new();
        AssistantRuntime.Voice=voice;
        head = new HoshinoCompanion(design) { Width = 40, Height = 40, Margin = new Thickness(0,0,12,0), Listening=()=>voice.Listening, Working=()=>busy };
        hero = new HoshinoCompanion(design) { Width=100, Height=100, Listening=()=>voice.Listening, Working=()=>busy };
        heroPanel.Children.Add(hero);
        heroPanel.Children.Add(new TextBlock { Text="Я всегда тебе помогу", Opacity=.6, FontSize=14, MaxWidth=380, TextWrapping=TextWrapping.Wrap, TextAlignment=TextAlignment.Center, HorizontalAlignment=HorizontalAlignment.Center, Margin=new Thickness(0,4,0,12) });
        Height = 450;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };

        var title = new StackPanel();

        title.Children.Add(status); heading.Children.Add(title); Children.Add(heading);
        var tools = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) };
        var settings = MakeButton("Настройки", "Аккаунт, модель, файлы и голос"); settings.Click += (_, _) => AssistantSettingsPanel.Open(); tools.Children.Add(settings);
        var fresh = MakeButton("Новый чат", "Начать новый диалог"); fresh.Click += (_, _) => { if (!busy) ResetConversation(); }; tools.Children.Add(fresh);
        var history = MakeButton("История", "Сохранённые диалоги этого аккаунта"); history.Click += async (_, _) => await ShowHistory(); tools.Children.Add(history);
        title.Children.Add(tools);
        scroll = new ScrollViewer { Content = messages, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 0, 0, 12) };
        SetRow(scroll, 1); Children.Add(scroll);
        messages.Children.Add(heroPanel);
        ApplyDesign();
        var composer = new Border { Background = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)), CornerRadius = new CornerRadius(16), Padding = new Thickness(12, 10, 12, 8) };
        var body = new StackPanel();
        Input.SetResourceReference(StyleProperty, "IslandTextBox");
        Input.ToolTip = "Сообщение Codex · Enter — отправить, Shift+Enter — новая строка";
        System.Windows.Automation.AutomationProperties.SetName(Input, "Сообщение Codex");
        Input.PreviewMouseLeftButtonDown += (_, _) => TypingRequested?.Invoke(Input);
        Input.PreviewKeyDown += async (_, e) => { if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { e.Handled = true; await Send(); } };
        body.Children.Add(Input);
        var actions = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        send = MakeButton("Отправить ↑", "Отправить сообщение"); send.Click += async (_, _) => await Send();
        stop = MakeButton("Стоп ■", "Остановить ответ"); stop.Visibility = Visibility.Collapsed; stop.Click += async (_, _) => await Interrupt();
        var right = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth = 310 };
        var microphone = MakeButton("Микрофон", "Диктовка: нажмите, говорите, затем проверьте текст"); microphone.Click += (_, _) => { try { if (busy) return; voice.Listen(); } catch (Exception e) { status.Text = e.Message; } }; right.Children.Add(microphone);
        var silence = MakeButton("Без звука", "Остановить озвучивание"); silence.Click += (_, _) => {voice.Silence();status.Text="Озвучивание остановлено";}; right.Children.Add(silence); right.Children.Add(stop); right.Children.Add(send); DockPanel.SetDock(right, Dock.Right); actions.Children.Add(right);
        actions.Children.Add(new TextBlock { Text = "Enter ↵", FontSize = 10, Opacity = .45, VerticalAlignment = VerticalAlignment.Center });
        body.Children.Add(actions); composer.Child = body; SetRow(composer, 2); Children.Add(composer);
        var footer = new TextBlock { Text = "Локальная история · доступ и голос в настройках", FontSize = 10, Opacity = .45, Margin = new Thickness(2, 8, 0, 0) }; SetRow(footer, 3); Children.Add(footer);
        MouseLeftButtonUp += (_, e) => e.Handled = true;
        bridge.Event += OnEvent;
        bridge.Request += ApprovalDialog.Handle;
        voice.Recognized += text => Dispatcher.InvokeAsync(() => { Input.Text = text; status.Text = "Диктовка готова — проверьте текст и отправьте"; });
        voice.Status += text => Dispatcher.InvokeAsync(() => {status.Text = text;ActivityChanged?.Invoke(text);});
        voice.Activated+=()=>VoiceActivated?.Invoke();
        voice.StateChanged+=()=>VoiceStateChanged?.Invoke();
        voice.CommandRecognized+=async text=> {
            if(busy)return;
            if(!string.IsNullOrWhiteSpace(Input.Text)){Input.Text+="\n"+text;status.Text="Диктовка добавлена к черновику — отправьте вручную";return;}
            Input.Text=text;await Send();
        };
        Dispatcher.BeginInvoke(()=>voice.Configure());
        AssistantPreferences.Changed += PreferencesChanged;
        ObsidianPreferences.Changed += PreferencesChanged;
        AssistantRuntime.AccountChanged += AccountChanged;
    }

    private static Button MakeButton(string text, string tooltip) => new() { Content = text, ToolTip = tooltip, Style = (Style)Application.Current.FindResource("ChipButton"), MinHeight = 28 };
    private TextBlock Bubble(string text, bool user)
    {
        var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 20, Foreground = Brushes.White };
        var block = new StackPanel(); block.Children.Add(new TextBlock { Text = user ? "ВЫ" : "ХОШИНО", FontSize = 9, Opacity = .5, Margin = new Thickness(0, 0, 0, 5) }); block.Children.Add(label);
        messages.Children.Add(new Border { Child = block, Background = new SolidColorBrush(Color.FromArgb(user ? (byte)28 : (byte)10, 255, 255, 255)), CornerRadius = new CornerRadius(15), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(user ? 28 : 0, 0, user ? 0 : 18, 10) });
        scroll?.ScrollToEnd(); return label;
    }

    private void SetBusy(bool value) { busy = value; voice.PauseForBusy(value); AssistantRuntime.Busy = value; if (!value) { AssistantRuntime.ActiveThread = null; AssistantRuntime.ActiveTurn = null; }else AssistantCues.Play(AssistantCue.Working);ActivityChanged?.Invoke(value?"Думаю…":"Готово");send.IsEnabled = !value; stop.Visibility = value ? Visibility.Visible : Visibility.Collapsed; stop.IsEnabled = value; }
    private Task Connect() => bridge.Start();
    private async Task Send()
    {
        if (busy || string.IsNullOrWhiteSpace(Input.Text)) return;
        var text = Input.Text.Trim(); SetBusy(true); stopRequested = false; status.Text = "Подключение…";
        try
        {
            await Connect();
            await AssistantAccount.Refresh();
            if (AssistantRuntime.AccountKey.Length == 0) throw new InvalidOperationException("Откройте настройки помощника и войдите в свой ChatGPT");
            if (thread == null)
            {
                var prefs = AssistantPreferences.Current;
                var arguments = new { sandbox = prefs.AccessMode, approvalPolicy = "untrusted", approvalsReviewer = "user", cwd = ObsidianSync.EffectiveDirectory, model = string.IsNullOrWhiteSpace(prefs.Model) ? null : prefs.Model, developerInstructions = prefs.Instructions + ObsidianSync.Context() };
                var r = await bridge.Call("thread/start", arguments); thread = r["thread"]!["id"]!.ToString();
                conversation = new ChatConversation { ThreadId = thread, AccountKey = AssistantRuntime.AccountKey, Model = prefs.Model, Title = text.Length > 48 ? text[..48] + "…" : text };
            }
            if (stopRequested) { SetBusy(false); status.Text = "Остановлено"; return; }
            messages.Children.Remove(heroPanel); Input.Clear(); Bubble(text, true); Record("Вы: " + text);
            conversation?.Messages.Add(new ChatMessage { Role = "user", Text = text });
            assistantMessage = new ChatMessage { Role = "assistant", Text = "" }; conversation?.Messages.Add(assistantMessage); SaveConversation();
            response = Bubble("", false); status.Text = "Codex отвечает…";
            AssistantRuntime.ActiveThread = thread;
            var result = await bridge.Call("turn/start", new { threadId = thread, input = new[] { new { type = "text", text } } });
            if (busy) { turn = result["turn"]?["id"]?.ToString(); AssistantRuntime.ActiveTurn = turn; if (stopRequested) await Interrupt(); }
        }
        catch (Exception e) { SetBusy(false); status.Text = "Не удалось выполнить запрос"; ActivityChanged?.Invoke(status.Text); AssistantCues.Play(AssistantCue.Error); Bubble("Ошибка: " + e.Message, false); }
    }
    private async Task Interrupt()
    {
        stopRequested = true; stop.IsEnabled = false; status.Text = "Останавливаем…";
        if (thread == null || turn == null) return;
        try { await bridge.Call("turn/interrupt", new { threadId = thread, turnId = turn }); }
        catch (Exception e) { status.Text = "Не удалось остановить: " + e.Message; stop.IsEnabled = true; }
    }
    private void OnEvent(string method, JsonNode? p) => Dispatcher.InvokeAsync(async () =>
    {
        if (method is "account/updated" or "account/login/completed") { try { status.Text = await AssistantAccount.Refresh(); } catch (Exception e) { status.Text = e.Message; } return; }
        if (method == "glass/disconnected") { SetBusy(false); thread = null; status.Text = "Соединение закрыто. Следующий запрос подключится заново."; return; }
        if (p?["threadId"]?.ToString() != thread) return;
        if (method == "turn/started") { turn = p?["turn"]?["id"]?.ToString(); AssistantRuntime.ActiveTurn = turn; }
        if (method == "item/agentMessage/delta" && response != null) { response.Text += p?["delta"]?.ToString(); scroll.ScrollToEnd();
            if (assistantMessage != null) assistantMessage.Text = response.Text;
            if ((DateTime.UtcNow - lastSave).TotalSeconds > 2) SaveConversation(); }
        if (method == "item/started")
        {
            ApprovalDialog.TrackItem(p?["item"]);
            var type = p?["item"]?["type"]?.ToString();
            if (type == "commandExecution") status.Text = "Выполняется команда…";
            else if (type == "fileChange") status.Text = "Изменяются файлы…";
            if (type is "commandExecution" or "fileChange") ActivityChanged?.Invoke(status.Text);
        }
        if (method == "turn/completed")
        {
            if (response != null) Record("Codex: " + response.Text); SaveConversation(); SetBusy(false); turn = null;
            var turnStatus = p?["turn"]?["status"]?.ToString();
            status.Text = turnStatus == "interrupted" ? "Ответ остановлен" : turnStatus == "failed" ? (p?["turn"]?["error"]?["message"]?.ToString() ?? "Запрос завершился ошибкой") : "Готово";
            ActivityChanged?.Invoke(status.Text);
            if (turnStatus == "failed") AssistantCues.Play(AssistantCue.Error);
            if (turnStatus == "completed" && response != null) try { AssistantCues.Play(AssistantCue.Reply);voice.Speak(response.Text); } catch (Exception e) { status.Text = "Озвучивание недоступно: " + e.Message; }
        }
    });
    private void Record(string text)
    {
        try { var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlassChat", "history.txt"); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.AppendAllText(path, text + Environment.NewLine); }
        catch (IOException) { status.Text = "Не удалось сохранить историю"; }
    }
    public void SetCompanionPhrase(string text) { ((TextBlock)heroPanel.Children[1]).Text=text; }

    public void ApplyDesign()
    {
        head.Visibility = heroPanel.Visibility = design.ShowHoshino ? Visibility.Visible : Visibility.Collapsed;
        hero.Width = hero.Height = Math.Clamp(design.CompanionSize,72,140);
    }
    private void SaveConversation()
    {
        if (conversation == null) return;
        try { ConversationStore.Save(conversation); lastSave = DateTime.UtcNow; }
        catch (IOException) { status.Text = "Не удалось сохранить диалог"; }
    }
    private void ResetConversation()
    {
        voice.Stop(); voice.Silence(); SaveConversation(); thread = null; conversation = null; assistantMessage = null; response = null;
        messages.Children.Clear(); messages.Children.Add(heroPanel); ApplyDesign();
    }
    private void PreferencesChanged() => Dispatcher.InvokeAsync(() => { if (!busy) ResetConversation(); voice.Configure(); });
    private void AccountChanged() => Dispatcher.InvokeAsync(() => { voice.Stop(); voice.Silence(); thread = null; conversation = null; assistantMessage = null; response = null; messages.Children.Clear(); Bubble("Аккаунт изменён. Начните новый диалог или выберите историю.", false); });
    private async Task ShowHistory()
    {
        if (busy) return;
        try
        {
            await AssistantAccount.Refresh();
            if (AssistantRuntime.AccountKey.Length == 0) { status.Text = "Сначала войдите в аккаунт"; return; }
            var records = ConversationStore.Read().Where(c => c.AccountKey == AssistantRuntime.AccountKey).OrderByDescending(c => c.Updated).ToList();
            var window = new Window { Title = "История диалогов", Width = 520, Height = 450, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = new SolidColorBrush(Color.FromRgb(22, 25, 34)), Foreground = Brushes.White };
            var root = new DockPanel { Margin = new Thickness(18) }; window.Content = root;
            var open = new Button { Content = "Продолжить выбранный диалог", Padding = new Thickness(12, 8, 12, 8) }; DockPanel.SetDock(open, Dock.Bottom); root.Children.Add(open);
            var list = new ListBox { ItemsSource = records, DisplayMemberPath = "Title", Background = Brushes.Transparent, Foreground = Brushes.White }; root.Children.Add(list);
            open.Click += async (_, _) =>
            {
                if (list.SelectedItem is not ChatConversation record || AssistantRuntime.Busy) return;
                open.IsEnabled = false;
                try
                {
                    await bridge.Call("thread/resume", new { threadId = record.ThreadId, excludeTurns = true, sandbox = AssistantPreferences.Current.AccessMode, approvalPolicy = "untrusted", approvalsReviewer = "user", cwd = ObsidianSync.EffectiveDirectory, developerInstructions = AssistantPreferences.Current.Instructions + ObsidianSync.Context() });
                    SaveConversation(); conversation = record; thread = record.ThreadId; messages.Children.Clear(); foreach (var m in record.Messages) Bubble(m.Text, m.Role == "user"); window.Close();
                }
                catch (Exception e) { window.Title = e.Message; open.IsEnabled = true; }
            };
            window.Show();
        }
        catch (Exception e) { status.Text = e.Message; }
    }
    public void StopListening() {voice.ReturnToWake();}
    public void SetLowPower(bool value)=>voice.SetLowPower(value);
    public void DisableWake() {AssistantPreferences.Current.WakeEnabled=false;AssistantPreferences.Current.Save(false);voice.Configure();}

    public void Dispose()
    {
        SaveConversation(); if(ReferenceEquals(AssistantRuntime.Voice,voice))AssistantRuntime.Voice=null; voice.Dispose(); bridge.Event -= OnEvent; bridge.Request -= ApprovalDialog.Handle;
        ObsidianPreferences.Changed -= PreferencesChanged;
        AssistantPreferences.Changed -= PreferencesChanged; AssistantRuntime.AccountChanged -= AccountChanged; bridge.Dispose();
    }
}
