// Original HoshiBar code: Copyright (c) 2026 Takayom1
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
    private readonly TextBlock feedback = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly ComboBox provider = new() { MinHeight = 32, DisplayMemberPath = "Label", SelectedValuePath = "Id" };
    private readonly ComboBox model = new() { MinHeight = 32, DisplayMemberPath = "Label", SelectedValuePath = "Id" };
    private readonly ComboBox access = new() { MinHeight = 32, DisplayMemberPath = "Label", SelectedValuePath = "Id" };
    private readonly ComboBox culture = new() { MinHeight = 32, IsEditable = true };
    private readonly TextBox work = new(), vault = new(), codexExe = new(), geminiExe = new(), claudeExe = new(), instructions = new();
    private readonly Dictionary<string, string> stagedModels = new(StringComparer.OrdinalIgnoreCase);
    private string currentProviderId = AssistantProviders.Codex;
    private Button? loginButton;
    private TextBlock? privacyText;
    private readonly CheckBox neural=new(){Content="Нейросетевая русская речь вместо голоса Windows"};
    private readonly TextBox neuralModel=new();
    private readonly ComboBox neuralSpeaker=new();
    private readonly CheckBox rvc=new(){Content="Преобразовывать речь через локальный RVC"};
    private readonly TextBox rvcPython=new(),rvcEngine=new(),rvcModel=new(),rvcIndex=new();
    private readonly CheckBox wake=new(){Content="Ждать выбранную фразу — микрофон включён постоянно"};
    private readonly ComboBox voices=new(){MinHeight=32};
    private readonly Slider rate=new(){Minimum=-5,Maximum=5,TickFrequency=1,IsSnapToTickEnabled=true};
    private readonly CheckBox speech = new() { Content = "Озвучивать готовый ответ голосом Windows" };
    public record Choice(string Id, string Label) { public override string ToString() => Label; }
    public AssistantSettingsPanel()
    {
        AssistantTheme.Apply(this);
        feedback.SetResourceReference(TextBlock.ForegroundProperty, "HoshinoAccent");
        speech.SetResourceReference(Control.ForegroundProperty, "HoshinoText");

        var prefs = AssistantPreferences.Current;
        currentProviderId = AssistantProviders.Normalize(prefs.Provider);
        stagedModels[AssistantProviders.Codex] = prefs.Model;
        stagedModels[AssistantProviders.Gemini] = prefs.GeminiModel;
        stagedModels[AssistantProviders.Claude] = prefs.ClaudeModel;

        Label("Провайдер AI, аккаунт и модель");
        Text("Выберите официальный CLI-движок помощника (все три работают с голосом Хошино, фразой «Хей Хошино» и памятью Obsidian):");
        provider.ItemsSource = AssistantProviders.All;
        provider.SelectedValue = currentProviderId;
        Children.Add(provider);

        account.Margin = new Thickness(0, 10, 0, 0);
        Children.Add(account);

        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 12) };
        Children.Add(actions);
        loginButton = CreateActionButton(actions, GetLoginButtonTitle(currentProviderId), async () =>
        {
            SaveSelectedProviderImmediate();
            feedback.Text = await AssistantRuntime.Router.Get(currentProviderId).LoginAsync();
        });
        Button(actions, "Обновить", Refresh);
        Button(actions, "Выйти / сменить аккаунт", async () =>
        {
            SaveSelectedProviderImmediate();
            await AssistantRuntime.Router.Get(currentProviderId).LogoutAsync();
            account.Text = "Аккаунт отключён. Теперь можно выполнить новый вход.";
            if (currentProviderId == AssistantProviders.Codex) model.ItemsSource = null;
        });

        Text("Модель — нажмите «Обновить» для загрузки списка или выберите пресет (для Gemini и Claude можно также вписать ID модели вручную).");
        Children.Add(model);

        privacyText = new TextBlock { FontSize = 12, Opacity = .65, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
        Children.Add(privacyText);

        ApplyProviderUiState(currentProviderId);
        provider.SelectionChanged += async (_, _) =>
        {
            CaptureCurrentModelSelection();
            currentProviderId = AssistantProviders.Normalize(provider.SelectedValue?.ToString());
            ApplyProviderUiState(currentProviderId);
            try
            {
                SaveSelectedProviderImmediate();
                account.Text = await AssistantRuntime.Router.Get(currentProviderId).RefreshAccountAsync();
            }
            catch (Exception e) { account.Text = e.Message; }
        };

        Label("Файлы и доступ к компьютеру");
        access.ItemsSource = new[] { new Choice("read-only", "Только чтение и план"), new Choice("workspace-write", "Работа с файлами в выбранной папке"), new Choice("danger-full-access", "Весь ПК: права текущего пользователя") }; access.SelectedValue = prefs.AccessMode; Children.Add(access);
        Text("Режим доступа передаётся выбранному CLI (Codex sandbox / Gemini --approval-mode / Claude --permission-mode). Полный доступ не даёт права администратора и ослабляет изоляцию; перед опасными действиями помощнику предписано запросить согласие.");
        Folder("Рабочая папка", work, prefs.WorkingDirectory);
        vault.Text=prefs.Vault;
        Text("Синхронизация Obsidian, память и ежедневник настраиваются в отдельном разделе Obsidian основного окна настроек. Подключённый там vault становится рабочей папкой помощника.");
        Label("Голос"); culture.Text = prefs.RecognitionCulture; Children.Add(culture); RefreshCultures(); culture.Text = prefs.RecognitionCulture;
        Text("Распознавание речи");
        var recognition=new ComboBox{ItemsSource=new[]{new Choice("whisper","Whisper · точнее, локально"),new Choice("vosk","Vosk · лёгкий, локально"),new Choice("windows","Windows · установленный распознаватель")},DisplayMemberPath="Label",SelectedValuePath="Id",SelectedValue=prefs.RecognitionEngine};Children.Add(recognition);
        var whisperFolder=new TextBox();Folder("Папка модели faster-whisper",whisperFolder,prefs.WhisperModel);
        Text("Whisper использует выбранный ниже Python с faster-whisper. Аудио остаётся на ПК. Сохрани настройки после выбора движка и модели.");
        Text("Пауза окончания фразы · секунды");var speechPause=new Slider{Minimum=.8,Maximum=3,TickFrequency=.1,IsSnapToTickEnabled=true,Value=prefs.SpeechPauseSeconds};Children.Add(speechPause);
        var refreshVoice = new WrapPanel(); Children.Add(refreshVoice); Button(refreshVoice, "Обновить распознаватели", () => { RefreshCultures(); feedback.Text = "Список речевых компонентов обновлён"; return Task.CompletedTask; });
        speech.IsChecked = prefs.SpeakReplies; Children.Add(speech);
        var volumeLabel=new TextBlock();Children.Add(volumeLabel);var voiceVolume=new Slider{Minimum=0,Maximum=100,TickFrequency=1,IsSnapToTickEnabled=true,Value=Math.Clamp(prefs.VoiceVolume,0,1)*100};Children.Add(voiceVolume);
        var volumeSave=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};
        void SaveVolume(){try{var p=AssistantPreferences.Current;var value=voiceVolume.Value/100;if(Math.Abs(p.VoiceVolume-value)>.0001){p.VoiceVolume=value;p.Save(false);AssistantRuntime.Voice?.VolumeChanged();}}catch(Exception e){feedback.Text=e.Message;}}
        void VolumeLabel()=>volumeLabel.Text=$"Громкость Хошино · {voiceVolume.Value:0}%";
        VolumeLabel();voiceVolume.ValueChanged+=(_,_)=>{VolumeLabel();volumeSave.Stop();volumeSave.Start();};volumeSave.Tick+=(_,_)=>{volumeSave.Stop();SaveVolume();};Unloaded+=(_,_)=>{volumeSave.Stop();SaveVolume();};
        Text("Меняет только голос Хошино. Для нейроголоса новая громкость применяется со следующей реплики.");

        var packCard=new Border{CornerRadius=new CornerRadius(12),Padding=new Thickness(14,12,14,12),Margin=new Thickness(0,8,0,10),BorderThickness=new Thickness(1),Background=new SolidColorBrush(Color.FromArgb(40,234,176,207)),BorderBrush=new SolidColorBrush(Color.FromArgb(95,234,176,207))};
        var packStack=new StackPanel();packCard.Child=packStack;
        packStack.Children.Add(new TextBlock{Text="Нейроголос Таканаши Хошино · Офлайн RU (Silero xenia + HoshinoOv2)",FontSize=13.5,FontWeight=FontWeights.SemiBold});
        var packStatus=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12,Opacity=.85,Margin=new Thickness(0,4,0,8)};
        packStack.Children.Add(packStatus);
        var packBar=new ProgressBar{Height=6,Minimum=0,Maximum=100,Margin=new Thickness(0,0,0,8),Visibility=HoshinoVoicePack.IsInstalling?Visibility.Visible:Visibility.Collapsed,Value=HoshinoVoicePack.LastProgress};
        packStack.Children.Add(packBar);
        var cudaToggle=new CheckBox{Content="Ускорение на видеокарте NVIDIA (CUDA · скачивает ~2.3 ГБ вместо ~360 МБ)",Margin=new Thickness(0,0,0,8)};
        packStack.Children.Add(cudaToggle);
        var packButtons=new WrapPanel();packStack.Children.Add(packButtons);
        void RefreshPackUi()
        {
            if(HoshinoVoicePack.IsInstalling){packBar.Visibility=Visibility.Visible;packBar.Value=HoshinoVoicePack.LastProgress;packStatus.Text=string.IsNullOrEmpty(HoshinoVoicePack.LastStatus)?"Установка пакета голоса…":HoshinoVoicePack.LastStatus;return;}
            packBar.Visibility=Visibility.Collapsed;
            var installed=HoshinoVoicePack.IsInstalled(out _,out _);
            var active=AssistantPreferences.Current.NeuralEnabled&&AssistantPreferences.Current.RvcEnabled;
            packStatus.Text=installed?(active?"Пакет установлен и активен · работает полностью локально без интернета.":"Пакет уже скачан на ПК · нажмите кнопку ниже, чтобы активировать его в 1 клик."):"Скачивает изолированный Python 3.11, Applio, Silero v5_5_ru и тембр HoshinoOv2 в папку данных пользователя (не требует ручной настройки Python и не стирается при обновлениях).";
        }
        RefreshPackUi();
        Action<string,double> onPackProgress=(msg,pct)=>Dispatcher.InvokeAsync(()=>{packBar.Visibility=HoshinoVoicePack.IsInstalling?Visibility.Visible:Visibility.Collapsed;packBar.Value=pct;packStatus.Text=$"{msg} ({pct:0}%)";});
        HoshinoVoicePack.ProgressChanged+=onPackProgress;
        Unloaded+=(_,_)=>HoshinoVoicePack.ProgressChanged-=onPackProgress;
        Button(packButtons,HoshinoVoicePack.IsInstalled(out _,out _)?"Подключить / Проверить пакет Хошино":"Установить голос Хошино в 1 клик",async()=>{
            if(HoshinoVoicePack.IsInstalled(out var existingPth,out var existingIdx)&&cudaToggle.IsChecked!=true)
            {
                var cur=AssistantPreferences.Current;HoshinoVoicePack.ApplyPresetTo(cur,existingPth,existingIdx,enableSpeech:true);cur.Save();
            }
            else
            {
                await HoshinoVoicePack.InstallAsync(cudaToggle.IsChecked==true,CancellationToken.None);
            }
            var updated=AssistantPreferences.Current;
            speech.IsChecked=updated.SpeakReplies;neural.IsChecked=updated.NeuralEnabled;neuralModel.Text=updated.NeuralModel;neuralSpeaker.SelectedItem=updated.NeuralSpeaker;
            rvc.IsChecked=updated.RvcEnabled;rvcPython.Text=updated.RvcPython;rvcEngine.Text=updated.RvcEngine;rvcModel.Text=updated.RvcModel;rvcIndex.Text=updated.RvcIndex;
            AssistantRuntime.Voice?.Configure();RefreshPackUi();feedback.Text="Голос Таканаши Хошино готов к работе.";
        });
        Button(packButtons,"Прослушать голос Хошино",async()=>{
            if(AssistantRuntime.Busy)throw new InvalidOperationException("Дождитесь ответа Codex");
            var cur=AssistantPreferences.Current;
            if(!cur.SpeakReplies){cur.SpeakReplies=true;speech.IsChecked=true;cur.Save(false);}
            if(AssistantRuntime.Voice is { } voice)await voice.Preview("Привет. Не спеши так, давай спокойно разберём твои дела по порядку.");
        });
        Children.Add(packCard);
        var cues=new CheckBox{Content="Свои звуки: слушаю, работаю, ответ готов",IsChecked=prefs.AssistantSounds};Children.Add(cues);
        var cueVolume=new Slider{Minimum=0,Maximum=40,TickFrequency=1,IsSnapToTickEnabled=true,Value=prefs.AssistantSoundVolume*100};Children.Add(cueVolume);
        void SaveCues(){try{var p=AssistantPreferences.Current;p.AssistantSounds=cues.IsChecked==true;p.AssistantSoundVolume=cueVolume.Value/100;p.Save(false);}catch(Exception e){feedback.Text=e.Message;}}
        cues.Checked+=(_,_)=>SaveCues();cues.Unchecked+=(_,_)=>SaveCues();cueVolume.ValueChanged+=(_,_)=>{volumeSave.Stop();volumeSave.Start();};volumeSave.Tick+=(_,_)=>SaveCues();Unloaded+=(_,_)=>SaveCues();
        var cuePreview=new WrapPanel();Children.Add(cuePreview);Button(cuePreview,"Проверить звук уведомления",()=>{SaveCues();AssistantCues.Play(AssistantCue.Listen);return Task.CompletedTask;});

        var wakePhrase=new ComboBox{ItemsSource=WakeProfile.Phrases,SelectedItem=WakeProfile.SelectedPhrase};Children.Add(wakePhrase);
        wakePhrase.SelectionChanged+=(_,_)=>{if(wakePhrase.SelectedItem is not string phrase)return;try{AssistantPreferences.Current.WakePhrase=phrase;AssistantPreferences.Current.Save(false);AssistantRuntime.Voice?.Configure();}catch(Exception e){feedback.Text=e.Message;}};
        Text("Выбери, на какую фразу реагировать. У каждой свой профиль калибровки; без него используется распознаватель Windows. Для выбранного микрофона лучше повторить новую фразу три раза.");
        wake.IsChecked=prefs.WakeEnabled;Children.Add(wake);
        var wakeState=new TextBlock{TextWrapping=TextWrapping.Wrap,Opacity=.7,Margin=new Thickness(0,6,0,8)};Children.Add(wakeState);
        void RefreshWake(){wakeState.Text=AssistantRuntime.Voice?.CurrentStatus ?? (AssistantPreferences.Current.WakeEnabled?"Ожидание включено":"Ожидание выключено");}
        void ApplyWake(bool on){try{var current=AssistantPreferences.Current;if(current.WakeEnabled!=on){current.WakeEnabled=on;if(on){speech.IsChecked=true;current.SpeakReplies=true;}AssistantRuntime.Voice?.Configure();current.Save(false);}RefreshWake();}catch(Exception e){wakeState.Text=e.Message;}}
        wake.Checked+=(_,_)=>ApplyWake(true);wake.Unchecked+=(_,_)=>ApplyWake(false);RefreshWake();
        var liveVoice=AssistantRuntime.Voice;Action<string> updateStatus=text=>wakeState.Text=text;if(liveVoice!=null){liveVoice.Status+=updateStatus;Unloaded+=(_,_)=>liveVoice.Status-=updateStatus;}
        var calibrationButtons=new WrapPanel();Children.Add(calibrationButtons);
        Button(calibrationButtons,"Калибровка · три повторения",()=>{var voice=AssistantRuntime.Voice??throw new InvalidOperationException("Остров не запущен");var window=new WakeCalibrationWindow(voice,()=>{wake.IsChecked=AssistantPreferences.Current.WakeEnabled;speech.IsChecked=AssistantPreferences.Current.SpeakReplies;RefreshWake();}){Owner=Window.GetWindow(this)};window.ShowDialog();RefreshWake();return Task.CompletedTask;});
        Button(calibrationButtons,"Сбросить калибровку",()=>{if(MessageBox.Show("Удалить шаблоны выбранной фразы?", "Калибровка",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return Task.CompletedTask;System.IO.File.Delete(WakeProfile.PathFor(WakeProfile.SelectedPhrase));AssistantRuntime.Voice?.Configure();RefreshWake();return Task.CompletedTask;});
        Text("Переключатель ожидания применяется сразу, отдельное сохранение внизу для него не нужно. Калибровка учит локальные шаблоны фразы, а не большую речевую модель. Индикатор уровня поможет выбрать правильный микрофон.");
        Text("После активации дождитесь сигнала и появления Хошино, затем произнесите команду отдельно. Она отправится после паузы. Ожидание приостанавливается во время ответа и озвучивания; опасные действия подтверждаются кнопкой. Скрытие приложения не выключает ожидание фразы. Нажмите индикатор микрофона на раскрытом острове или отключите эту настройку, чтобы остановить ожидание.");
        voices.ItemsSource=AssistantVoice.Voices();voices.SelectedItem=prefs.VoiceName;if(voices.SelectedIndex<0)voices.SelectedItem=AssistantVoice.Voices().FirstOrDefault(n=>n.Contains("Irina"))??AssistantVoice.Voices().FirstOrDefault();Children.Add(voices);
        Text("Скорость голоса · медленнее ← → быстрее");rate.Value=prefs.VoiceRate;Children.Add(rate);
        var preview=new WrapPanel();Children.Add(preview);
        Button(preview,"Проба голоса Windows",()=>{var speaker=new System.Speech.Synthesis.SpeechSynthesizer();if(voices.SelectedItem is string name)speaker.SelectVoice(name);speaker.Rate=(int)rate.Value;speaker.Volume=(int)Math.Round(voiceVolume.Value);speaker.SpeakCompleted+=(_,_)=>speaker.Dispose();speaker.SetOutputToDefaultAudioDevice();speaker.SpeakAsync("Я здесь. Что у нас случилось?");return Task.CompletedTask;});
        Text("Обычная диктовка по кнопке остаётся ручной: текст можно проверить перед отправкой. Команды обрабатываются выбранным движком: Whisper или Vosk работают локально. Vosk включён в текущую сборку, модель Whisper выбирается отдельно. Это выбранный голос Windows, не копия актрисы Хошино.");
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
        Button(neuralPreview,"Проба сохранённого нейроголоса",async()=>{if(AssistantRuntime.Busy)throw new InvalidOperationException("Дождитесь завершения ответа помощника");if(!AssistantPreferences.Current.SpeakReplies)throw new InvalidOperationException("Включите озвучку и сохраните настройки");if(AssistantRuntime.Voice is { } voice)await voice.Preview("Я здесь. Слушаю тебя.");});
        Button(neuralPreview,"Остановить голос",()=>{AssistantRuntime.Voice?.Silence();return Task.CompletedTask;});
        var neuralState=new TextBlock{Text=liveVoice?.CurrentStatus??"Голос не запущен",TextWrapping=TextWrapping.Wrap};neuralState.SetResourceReference(TextBlock.ForegroundProperty,"HoshinoAccent");Children.Add(neuralState);
        Action<string> neuralStatus=text=>neuralState.Text=text;if(liveVoice!=null){liveVoice.Status+=neuralStatus;Unloaded+=(_,_)=>liveVoice.Status-=neuralStatus;}
        Text("После включения озвучки модели прогреваются один раз и остаются в памяти до отключения озвучки или закрытия острова. Повторные реплики используют тот же процесс. Совместимый Python с CUDA включает GPU для RVC автоматически.");
        Label("Поведение помощника"); instructions.Text = prefs.Instructions; instructions.AcceptsReturn = true; instructions.TextWrapping = TextWrapping.Wrap; instructions.Height = 190; instructions.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; Children.Add(instructions);

        Label("Пути к CLI-нейросетям (необязательно)");
        Text("Оставьте поля пустыми для автоматического поиска в PATH и глобальных папках npm / .local/bin, либо укажите точный путь к исполняемому файлу:");
        ExeFileRow("Codex CLI (@openai/codex · codex.exe)", codexExe, prefs.CodexExecutable);
        ExeFileRow("Gemini CLI (@google/gemini-cli · gemini.cmd / gemini.exe)", geminiExe, prefs.GeminiExecutable);
        ExeFileRow("Claude Code CLI (@anthropic-ai/claude-code · claude.exe / claude.cmd)", claudeExe, prefs.ClaudeExecutable);

        var save = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) }; Children.Add(save);
        Button(save, "Сохранить настройки", () =>
        {
            if (AssistantRuntime.Busy) throw new InvalidOperationException("Сначала остановите ответ");
            CaptureCurrentModelSelection();
            var cur = AssistantPreferences.Current;
            var settings = new AssistantPreferences
            {
                Provider = AssistantProviders.Normalize(provider.SelectedValue?.ToString() ?? currentProviderId),
                Model = stagedModels.GetValueOrDefault(AssistantProviders.Codex, cur.Model),
                GeminiModel = stagedModels.GetValueOrDefault(AssistantProviders.Gemini, cur.GeminiModel),
                ClaudeModel = stagedModels.GetValueOrDefault(AssistantProviders.Claude, cur.ClaudeModel),
                AccessMode = access.SelectedValue?.ToString() ?? "workspace-write",
                WorkingDirectory = work.Text.Trim(),
                Vault = vault.Text.Trim(),
                CodexExecutable = codexExe.Text.Trim(),
                GeminiExecutable = geminiExe.Text.Trim(),
                ClaudeExecutable = claudeExe.Text.Trim(),
                RecognitionCulture = culture.Text.Trim(),
                RecognitionEngine = recognition.SelectedValue?.ToString() ?? "vosk",
                WhisperModel = whisperFolder.Text.Trim(),
                SpeechPauseSeconds = speechPause.Value,
                VoiceVolume = voiceVolume.Value / 100,
                AssistantSounds = cur.AssistantSounds,
                AssistantSoundVolume = cur.AssistantSoundVolume,
                SpeakReplies = speech.IsChecked == true,
                WakeEnabled = wake.IsChecked == true,
                WakePhrase = cur.WakePhrase,
                MicrophoneDevice = cur.MicrophoneDevice,
                VoiceName = voices.SelectedItem?.ToString() ?? "",
                VoiceRate = (int)rate.Value,
                NeuralEnabled = neural.IsChecked == true,
                NeuralModel = neuralModel.Text.Trim(),
                NeuralSpeaker = neuralSpeaker.SelectedItem?.ToString() ?? "xenia",
                RvcEnabled = rvc.IsChecked == true,
                RvcPython = rvcPython.Text.Trim(),
                RvcEngine = rvcEngine.Text.Trim(),
                RvcModel = rvcModel.Text.Trim(),
                RvcIndex = rvcIndex.Text.Trim(),
                RvcPitch = cur.RvcPitch,
                RvcIndexRate = cur.RvcIndexRate,
                RvcProtect = cur.RvcProtect,
                Instructions = instructions.Text,
            };
            settings.Save();
            AssistantRuntime.Router.RestartAll();
            feedback.Text = $"Сохранено ({AssistantProviders.DisplayName(settings.Provider)}). Следующее сообщение начнёт новый диалог.";
            return Task.CompletedTask;
        });
        Children.Add(feedback);
    }

    private static string GetLoginButtonTitle(string providerId) => AssistantProviders.Normalize(providerId) switch
    {
        AssistantProviders.Gemini => "Войти в Gemini CLI",
        AssistantProviders.Claude => "Войти в Claude Code",
        _ => "Войти через ChatGPT",
    };

    private void SaveSelectedProviderImmediate()
    {
        if (AssistantRuntime.Busy) return;
        var cur = AssistantPreferences.Current;
        cur.Provider = currentProviderId;
        cur.CodexExecutable = codexExe.Text.Trim();
        cur.GeminiExecutable = geminiExe.Text.Trim();
        cur.ClaudeExecutable = claudeExe.Text.Trim();
        cur.Save(false);
    }

    private void CaptureCurrentModelSelection()
    {
        if (currentProviderId == AssistantProviders.Codex)
        {
            if (model.SelectedValue is string sel) stagedModels[AssistantProviders.Codex] = sel;
        }
        else
        {
            if (model.SelectedItem is Choice ch)
                stagedModels[currentProviderId] = ch.Id;
            else
                stagedModels[currentProviderId] = (model.Text ?? "").Trim();
        }
    }

    private void ApplyProviderUiState(string providerId)
    {
        var norm = AssistantProviders.Normalize(providerId);
        if (loginButton != null) loginButton.Content = GetLoginButtonTitle(norm);

        var vendor = AssistantProviders.VendorName(norm);
        if (privacyText != null)
        {
            privacyText.Text = $"Пользователь подключает собственный локальный CLI и свой аккаунт {vendor}. Запросы, память и прочитанное помощником содержимое файлов могут передаваться {vendor}. Локальная история сохраняется в %LOCALAPPDATA%\\GlassChat.";
        }

        var savedModel = stagedModels.GetValueOrDefault(norm, "");
        if (norm == AssistantProviders.Gemini)
        {
            model.IsEditable = true;
            model.ItemsSource = GeminiCliBackend.DefaultModelChoices;
            SelectOrDisplayModel(GeminiCliBackend.DefaultModelChoices, savedModel);
        }
        else if (norm == AssistantProviders.Claude)
        {
            model.IsEditable = true;
            model.ItemsSource = ClaudeCliBackend.DefaultModelChoices;
            SelectOrDisplayModel(ClaudeCliBackend.DefaultModelChoices, savedModel);
        }
        else
        {
            model.IsEditable = false;
            model.ItemsSource = string.IsNullOrWhiteSpace(savedModel) ? null : new[] { new Choice(savedModel, savedModel) };
            model.SelectedValue = savedModel;
        }
    }

    private void SelectOrDisplayModel(IReadOnlyList<Choice> presets, string savedId)
    {
        var match = presets.FirstOrDefault(c => string.Equals(c.Id, savedId, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            model.SelectedItem = match;
        }
        else
        {
            model.SelectedIndex = -1;
            model.Text = savedId;
        }
    }

    private void RefreshCultures() { try { var text = culture.Text; culture.ItemsSource = AssistantVoice.Cultures(); culture.Text = text; } catch (Exception e) { feedback.Text = e.Message; } }
    private async Task Refresh()
    {
        SaveSelectedProviderImmediate();
        var backend = AssistantRuntime.Router.Get(currentProviderId);
        account.Text = await backend.RefreshAccountAsync();
        if (AssistantRuntime.AccountKey.Length == 0 && currentProviderId == AssistantProviders.Codex) return;

        var list = await backend.ListModelsAsync();
        model.ItemsSource = list;
        var targetModel = stagedModels.GetValueOrDefault(currentProviderId, AssistantPreferences.Current.GetModelFor(currentProviderId));
        if (currentProviderId == AssistantProviders.Codex)
        {
            model.SelectedValue = targetModel;
            if (model.SelectedIndex < 0 && list.Count > 0) model.SelectedIndex = 0;
        }
        else
        {
            SelectOrDisplayModel(list, targetModel);
        }
        feedback.Text = $"Статус {AssistantProviders.ShortName(currentProviderId)} и список моделей обновлены";
    }
    private void Label(string text) => Children.Add(new TextBlock { Text = text, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 10) });
    private void Text(string text) => Children.Add(new TextBlock { Text = text, FontSize = 12, Opacity = .65, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
    private void Folder(string title, TextBox box, string value)
    {
        Text(title); var row = new DockPanel(); var choose = new Button { Content = "Выбрать…", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(8, 0, 0, 0) }; DockPanel.SetDock(choose, Dock.Right); row.Children.Add(choose); box.Text = value; row.Children.Add(box); Children.Add(row);
        choose.Click += (_, _) => { var dialog = new Microsoft.Win32.OpenFolderDialog(); if (dialog.ShowDialog() == true) box.Text = dialog.FolderName; };
    }
    private void ExeFileRow(string title, TextBox box, string value)
    {
        Text(title);
        var row = new DockPanel();
        var choose = new Button { Content = "Обзор…", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(choose, Dock.Right);
        row.Children.Add(choose);
        box.Text = value;
        row.Children.Add(box);
        Children.Add(row);
        choose.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "CLI Исполняемые файлы (*.exe;*.cmd;*.bat)|*.exe;*.cmd;*.bat|Все файлы (*.*)|*.*",
            };
            if (dialog.ShowDialog() == true) box.Text = dialog.FileName;
        };
    }
    private Button CreateActionButton(Panel parent, string title, Func<Task> action)
    {
        var button = new Button { Content = title, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 6) };
        parent.Children.Add(button);
        button.Click += async (_, _) => { button.IsEnabled = false; try { await action(); } catch (Exception e) { feedback.Text = e.Message; } finally { button.IsEnabled = true; } };
        return button;
    }
    private void Button(Panel parent, string title, Func<Task> action) => CreateActionButton(parent, title, action);
    public static void Open()
    {
        var window = new Window { Title = "Помощник — CLI-нейросети, файлы и голос", Width = 740, Height = 780, MinWidth = 520, MinHeight = 400, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        window.SetResourceReference(Control.BackgroundProperty, "HoshinoBackground");
        window.SetResourceReference(Control.ForegroundProperty, "HoshinoText");
        AssistantTheme.Apply(window);
        window.Content = new ScrollViewer { Content = new AssistantSettingsPanel { Margin = new Thickness(26, 4, 26, 26) }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        window.Show();
    }
}
