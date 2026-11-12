// Original Hoshino Island code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace DIsland;
public sealed class AssistantSettingsPanel : StackPanel
{
    private readonly TextBlock account = new() { Text = "Аккаунт не проверен", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock feedback = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), Foreground = Brushes.LightSkyBlue };
    private readonly ComboBox model = new() { MinHeight = 32, DisplayMemberPath = "Label", SelectedValuePath = "Id" };
    private readonly ComboBox access = new() { MinHeight = 32, DisplayMemberPath = "Label", SelectedValuePath = "Id" };
    private readonly ComboBox culture = new() { MinHeight = 32, IsEditable = true };
    private readonly TextBox work = new(), vault = new(), executable = new(), instructions = new();
    private readonly CheckBox neural=new(){Content="Нейросетевая русская речь вместо голоса Windows"};
    private readonly TextBox neuralModel=new();
    private readonly ComboBox neuralSpeaker=new();
    private readonly CheckBox rvc=new(){Content="Преобразовывать речь через локальный RVC"};
    private readonly TextBox rvcPython=new(),rvcEngine=new(),rvcModel=new(),rvcIndex=new();
    private readonly CheckBox wake=new(){Content="Ждать «Хей Хошино» — микрофон включён постоянно"};
    private readonly ComboBox voices=new(){MinHeight=32};
    private readonly Slider rate=new(){Minimum=-5,Maximum=5,TickFrequency=1,IsSnapToTickEnabled=true};
    private readonly CheckBox speech = new() { Content = "Озвучивать готовый ответ голосом Windows", Foreground = Brushes.White };
    public record Choice(string Id, string Label) { public override string ToString() => Label; }
    public AssistantSettingsPanel()
    {
        AssistantTheme.Apply(this);
        var prefs = AssistantPreferences.Current;
        Label("Аккаунт и модель"); Children.Add(account);
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 12) }; Children.Add(actions);
        Button(actions, "Войти через ChatGPT", async () => { await AssistantAccount.Login(); feedback.Text = "Завершите вход в браузере, затем нажмите «Обновить»"; });
        Button(actions, "Обновить", Refresh);
        Button(actions, "Выйти / сменить аккаунт", async () => { await AssistantAccount.Logout(); account.Text = "Аккаунт отключён. Теперь можно войти в другой."; model.ItemsSource = null; });
        Text("Модель — после входа нажмите «Обновить», затем сохраните выбор."); Children.Add(model);
        Text("Друг подключает собственный ChatGPT. В архив приложения не входят ваш вход, диалоги и личные настройки. Список моделей загружается из его аккаунта; сторонние API-провайдеры в этом интерфейсе пока не поддерживаются.");
        Text("Запросы и прочитанное помощником содержимое файлов могут передаваться OpenAI. История сохраняется на этом компьютере обычным текстом, без шифрования. Не давайте помощнику доступ к файлам, которые не хотите передавать модели.");
        Label("Файлы и доступ к компьютеру");
        access.ItemsSource = new[] { new Choice("read-only", "Только чтение и диагностика"), new Choice("workspace-write", "Работа с файлами в выбранной папке"), new Choice("danger-full-access", "Весь ПК: права текущего пользователя") }; access.SelectedValue = prefs.AccessMode; Children.Add(access);
        Text("Команды, не признанные доверенными, и запросы расширения прав показываются для подтверждения. Полный доступ не даёт права администратора и ослабляет изоляцию; не каждое изменение автоматически вызывает запрос. Перед опасными действиями помощнику предписано запросить согласие.");
        Folder("Рабочая папка", work, prefs.WorkingDirectory);
        vault.Text=prefs.Vault;
        Text("Синхронизация Obsidian, память и ежедневник настраиваются в отдельном разделе Obsidian основного окна настроек. Подключённый там vault становится рабочей папкой Codex.");
        Label("Голос"); culture.Text = prefs.RecognitionCulture; Children.Add(culture); RefreshCultures(); culture.Text = prefs.RecognitionCulture;
        var refreshVoice = new WrapPanel(); Children.Add(refreshVoice); Button(refreshVoice, "Обновить распознаватели", () => { RefreshCultures(); feedback.Text = "Список речевых компонентов обновлён"; return Task.CompletedTask; });
        speech.IsChecked = prefs.SpeakReplies; Children.Add(speech);
        wake.IsChecked=prefs.WakeEnabled;wake.Checked+=(_,_)=>speech.IsChecked=true;Children.Add(wake);
        Text("После активации дождитесь надписи «Слушаю» и произнесите команду отдельно. Она отправится после паузы. Ожидание приостанавливается во время ответа и озвучивания; опасные действия подтверждаются кнопкой. Скрытие приложения не выключает ожидание фразы. Нажмите индикатор микрофона на раскрытом острове или отключите эту настройку, чтобы остановить ожидание.");
        voices.ItemsSource=AssistantVoice.Voices();voices.SelectedItem=prefs.VoiceName;if(voices.SelectedIndex<0)voices.SelectedItem=AssistantVoice.Voices().FirstOrDefault(n=>n.Contains("Irina"))??AssistantVoice.Voices().FirstOrDefault();Children.Add(voices);
        Text("Скорость голоса · медленнее ← → быстрее");rate.Value=prefs.VoiceRate;Children.Add(rate);
        var preview=new WrapPanel();Children.Add(preview);
        Button(preview,"Проба голоса Windows",()=>{var speaker=new System.Speech.Synthesis.SpeechSynthesizer();if(voices.SelectedItem is string name)speaker.SelectVoice(name);speaker.Rate=(int)rate.Value;speaker.SpeakCompleted+=(_,_)=>speaker.Dispose();speaker.SetOutputToDefaultAudioDevice();speaker.SpeakAsync("Я здесь. Что у нас случилось?");return Task.CompletedTask;});
        Text("Обычная диктовка по кнопке остаётся ручной: текст можно проверить перед отправкой. Русские команды обрабатываются локально Vosk, модель включена в установщик. Это выбранный голос Windows, не копия актрисы Хошино.");
        neural.IsChecked=prefs.NeuralEnabled;Children.Add(neural);
        Text("Локальный Silero v5_5_ru. Нужен Python с torch и soundfile. Веса не входят в установщик и имеют отдельную некоммерческую лицензию. Без RVC этот режим даёт естественный русский голос; RVC добавляет тембр, но увеличивает задержку.");
        Text("Модель Silero v5_5_ru.pt · полный путь");neuralModel.Text=prefs.NeuralModel;Children.Add(neuralModel);
        neuralSpeaker.ItemsSource=new[]{"baya","kseniya","xenia","eugene","aidar"};neuralSpeaker.SelectedItem=prefs.NeuralSpeaker;Children.Add(neuralSpeaker);
        rvc.IsChecked=prefs.RvcEnabled;Children.Add(rvc);
        Text("Дополнительно: RVC меняет тембр русской озвучки. Нужен доверенный Applio с его Python и ваша модель. На CPU возможна задержка. Веса персонажа не включены в установщик; проверяйте права на использование и распространение. Выполняется выбранный локальный Python-движок.");
        Text("Python нейроголоса / Applio · полный путь к python.exe");rvcPython.Text=prefs.RvcPython;Children.Add(rvcPython);
        Folder("Папка движка Applio",rvcEngine,prefs.RvcEngine);
        Text("Модель .pth · полный путь");rvcModel.Text=prefs.RvcModel;Children.Add(rvcModel);
        Text("Индекс .index · полный путь");rvcIndex.Text=prefs.RvcIndex;Children.Add(rvcIndex);
        var neuralPreview=new WrapPanel();Children.Add(neuralPreview);
        Button(neuralPreview,"Проба сохранённого нейроголоса",()=>{if(AssistantRuntime.Busy)throw new InvalidOperationException("Дождитесь ответа Codex");if(!AssistantPreferences.Current.SpeakReplies)throw new InvalidOperationException("Включите озвучку и сохраните настройки");AssistantRuntime.Voice?.Speak("Ух... Ты уже вернулся? Ладно, показывай, что у нас случилось.");feedback.Text="Озвучивание запущено. RVC может занять время; остановка — «Без звука» в чате.";return Task.CompletedTask;});
        Label("Поведение помощника"); instructions.Text = prefs.Instructions; instructions.AcceptsReturn = true; instructions.TextWrapping = TextWrapping.Wrap; instructions.Height = 190; instructions.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; Children.Add(instructions);
        Label("Codex CLI"); executable.Text = prefs.CodexExecutable; Children.Add(executable); Text("Оставьте пустым для автоматического поиска. Либо полный путь к официальному codex.exe. На компьютере друга CLI устанавливается отдельно.");
        var save = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) }; Children.Add(save);
        Button(save, "Сохранить настройки", () =>
        {
            if (AssistantRuntime.Busy) throw new InvalidOperationException("Сначала остановите ответ");
            var settings = new AssistantPreferences { Model = model.SelectedValue?.ToString() ?? prefs.Model, AccessMode = access.SelectedValue?.ToString() ?? "workspace-write", WorkingDirectory = work.Text.Trim(), Vault = vault.Text.Trim(), CodexExecutable = executable.Text.Trim(), RecognitionCulture = culture.Text.Trim(), SpeakReplies = speech.IsChecked == true, WakeEnabled=wake.IsChecked==true, VoiceName=voices.SelectedItem?.ToString()??"",VoiceRate=(int)rate.Value, NeuralEnabled=neural.IsChecked==true,NeuralModel=neuralModel.Text.Trim(),NeuralSpeaker=neuralSpeaker.SelectedItem?.ToString()??"baya",RvcEnabled=rvc.IsChecked==true,RvcPython=rvcPython.Text.Trim(),RvcEngine=rvcEngine.Text.Trim(),RvcModel=rvcModel.Text.Trim(),RvcIndex=rvcIndex.Text.Trim(), Instructions = instructions.Text };
            settings.Save(); AssistantRuntime.Bridge.Restart(); feedback.Text = "Сохранено. Следующее сообщение начнёт новый диалог с этими параметрами."; return Task.CompletedTask;
        });
        Children.Add(feedback);
    }
    private void RefreshCultures() { try { var text = culture.Text; culture.ItemsSource = AssistantVoice.Cultures(); culture.Text = text; } catch (Exception e) { feedback.Text = e.Message; } }
    private async Task Refresh()
    {
        account.Text = await AssistantAccount.Refresh();
        if (AssistantRuntime.AccountKey.Length == 0) return;
        var result = await AssistantRuntime.Bridge.Call("model/list", new { }); var list = new List<Choice>();
        if (result["data"] is JsonArray data) foreach (var item in data) { var id = item?["id"]?.ToString(); if (id != null) list.Add(new(id, item?["displayName"]?.ToString() ?? id)); }
        model.ItemsSource = list; model.SelectedValue = AssistantPreferences.Current.Model; if (model.SelectedIndex < 0 && list.Count > 0) model.SelectedIndex = 0;
        feedback.Text = "Аккаунт и доступные модели обновлены";
    }
    private void Label(string text) => Children.Add(new TextBlock { Text = text, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 10) });
    private void Text(string text) => Children.Add(new TextBlock { Text = text, FontSize = 12, Opacity = .65, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
    private void Folder(string title, TextBox box, string value)
    {
        Text(title); var row = new DockPanel(); var choose = new Button { Content = "Выбрать…", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(8, 0, 0, 0) }; DockPanel.SetDock(choose, Dock.Right); row.Children.Add(choose); box.Text = value; row.Children.Add(box); Children.Add(row);
        choose.Click += (_, _) => { var dialog = new Microsoft.Win32.OpenFolderDialog(); if (dialog.ShowDialog() == true) box.Text = dialog.FolderName; };
    }
    private void Button(Panel parent, string title, Func<Task> action)
    {
        var button = new Button { Content = title, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 6) }; parent.Children.Add(button);
        button.Click += async (_, _) => { button.IsEnabled = false; try { await action(); } catch (Exception e) { feedback.Text = e.Message; } finally { button.IsEnabled = true; } };
    }
    public static void Open()
    {
        var window = new Window { Title = "Помощник — аккаунт, файлы и голос", Width = 740, Height = 760, MinWidth = 520, MinHeight = 400, Background = new SolidColorBrush(Color.FromRgb(22, 25, 34)), Foreground = Brushes.White, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        window.Content = new ScrollViewer { Content = new AssistantSettingsPanel { Margin = new Thickness(26, 4, 26, 26) }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; window.Show();
    }
}
