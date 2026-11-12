// Original Hoshino Island code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
using System.IO;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Text.Json;
using System.Windows.Threading;
using NAudio.Wave;
namespace DIsland;
public sealed class AssistantVoice : IDisposable
{
    private readonly Dispatcher ui=Dispatcher.CurrentDispatcher;
    private readonly object audioLock=new();
    private SpeechRecognitionEngine? engine;
    private SpeechSynthesizer? speaker;
    private WaveInEvent? capture;
    private Vosk.Model? model;
    private Vosk.VoskRecognizer? decoder;
    private readonly DispatcherTimer limit=new(){Interval=TimeSpan.FromSeconds(30)};
    private readonly DispatcherTimer rearm=new(){Interval=TimeSpan.FromMilliseconds(700)};
    private int generation;
    private bool disposed,busy,speaking;
    private CancellationTokenSource? conversion;
    private System.Media.SoundPlayer? player;
    private int speechVersion;
    private bool sessionPaused;
    public bool Listening {get;private set;}
    public bool Waiting {get;private set;}
    public bool MicrophoneActive=>Listening||Waiting;
    public event Action<string>? Recognized;
    public event Action<string>? CommandRecognized;
    public event Action? Activated;
    public event Action<string>? Status;
    public event Action? StateChanged;
    public static string ModelPath=>Path.Combine(AppContext.BaseDirectory,"Assets","Speech","vosk-model-small-ru-0.22");
    public static bool HasRussianModel=>File.Exists(Path.Combine(ModelPath,"am","final.mdl"));
    public static string[] Cultures()=>SpeechRecognitionEngine.InstalledRecognizers().Select(r=>r.Culture.Name).Concat(HasRussianModel?new[]{"ru-RU"}:Array.Empty<string>()).Distinct().ToArray();
    public static string[] Voices(){using var s=new SpeechSynthesizer();return s.GetInstalledVoices().Where(v=>v.Enabled).Select(v=>v.VoiceInfo.Name).ToArray();}
    private void SessionChanged(object sender,Microsoft.Win32.SessionSwitchEventArgs e)=>ui.BeginInvoke(()=>{if(e.Reason==Microsoft.Win32.SessionSwitchReason.SessionLock){sessionPaused=true;Silence();StopInput();}else if(e.Reason==Microsoft.Win32.SessionSwitchReason.SessionUnlock){sessionPaused=false;ArmLater();}});
    private void PowerChanged(object sender,Microsoft.Win32.PowerModeChangedEventArgs e)=>ui.BeginInvoke(()=>{if(e.Mode==Microsoft.Win32.PowerModes.Suspend){sessionPaused=true;Silence();StopInput();}else if(e.Mode==Microsoft.Win32.PowerModes.Resume){sessionPaused=false;ArmLater();}});
    public AssistantVoice()
    {
        Microsoft.Win32.SystemEvents.SessionSwitch+=SessionChanged;Microsoft.Win32.SystemEvents.PowerModeChanged+=PowerChanged;
        limit.Tick+=(_,_)=>{limit.Stop();StopInput();Status?.Invoke("Команда не услышана. Повторите «Хей Хошино».");ArmLater();};
        rearm.Tick+=(_,_)=>{rearm.Stop();TryWake();};
    }
    public void Configure(){StopInput();Silence();ArmLater();}
    public void PauseForBusy(bool value){busy=value;if(value){Silence();StopInput();}else ArmLater();}
    private void ArmLater(){if(disposed)return;rearm.Stop();if(AssistantPreferences.Current.WakeEnabled&&!busy&&!speaking)rearm.Start();}
    private void TryWake()
    {
        if(disposed||sessionPaused||busy||speaking||Listening||!AssistantPreferences.Current.WakeEnabled)return;
        try {
            if(!HasRussianModel&&!SpeechRecognitionEngine.InstalledRecognizers().Any(r=>r.Culture.Name=="ru-RU"))throw new InvalidOperationException("Нужна локальная русская модель из установщика или распознаватель ru-RU");
            if(HasRussianModel&&model==null){Vosk.Vosk.SetLogLevel(-1);model=new Vosk.Model(ModelPath);}
            var recognizers=SpeechRecognitionEngine.InstalledRecognizers();
            var info=recognizers.FirstOrDefault(r=>r.Culture.Name=="ru-RU")??recognizers.FirstOrDefault(r=>r.Culture.Name=="en-US")??throw new InvalidOperationException("Распознаватель фразы активации Windows не найден");
            StopInput();var token=generation;
            engine=new SpeechRecognitionEngine(info);
            var words=info.Culture.Name=="ru-RU"?new[]{"хей хошино","хэй хошино"}:new[]{"hey hoshino","hey ho she no"};
            var grammar=new GrammarBuilder{Culture=info.Culture};grammar.Append(new Choices(words));engine.LoadGrammar(new Grammar(grammar));
            engine.SpeechRecognized+=(_,e)=>{if(e.Result.Confidence<.65)return;ui.BeginInvoke(()=>{if(token!=generation||!Waiting||busy)return;StopInput();System.Media.SystemSounds.Asterisk.Play();Activated?.Invoke();Status?.Invoke("Слушаю команду…");StartCommand(true);});};
            engine.RecognizeCompleted+=(_,e)=>{if(e.Error!=null)ui.BeginInvoke(()=>{if(token!=generation)return;StopInput();Status?.Invoke("Микрофон: "+e.Error.Message);});};
            engine.SetInputToDefaultAudioDevice();Waiting=true;engine.RecognizeAsync(RecognizeMode.Multiple);Status?.Invoke("Микрофон · жду «Хей Хошино»");StateChanged?.Invoke();
        }catch(Exception e){StopInput();Status?.Invoke(e.Message);}
    }
    public void Listen(){if(Listening){Stop();return;}Silence();StopInput();StartCommand(false);}
    private void StartCommand(bool fromWake)
    {
        try {
            StopInput();var token=generation;var culture=AssistantPreferences.Current.RecognitionCulture;
            if(culture.StartsWith("ru",StringComparison.OrdinalIgnoreCase)&&HasRussianModel) {
                Vosk.Vosk.SetLogLevel(-1);model??=new Vosk.Model(ModelPath);decoder=new Vosk.VoskRecognizer(model,16000f);
                capture=new WaveInEvent{DeviceNumber=-1,WaveFormat=new WaveFormat(16000,16,1),BufferMilliseconds=100};
                capture.DataAvailable+=(_,e)=>{string? text=null;lock(audioLock){if(token!=generation||decoder==null)return;if(decoder.AcceptWaveform(e.Buffer,e.BytesRecorded)){using var json=JsonDocument.Parse(decoder.Result());text=json.RootElement.GetProperty("text").GetString();}}if(!string.IsNullOrWhiteSpace(text))ui.BeginInvoke(()=>Finish(text!,fromWake,token));};
                capture.RecordingStopped+=(_,e)=>{if(e.Exception!=null)ui.BeginInvoke(()=>{if(token!=generation)return;StopInput();Status?.Invoke(e.Exception.Message);ArmLater();});};
                Listening=true;capture.StartRecording();
            }else {
                var info=SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault(r=>r.Culture.Name.Equals(culture,StringComparison.OrdinalIgnoreCase))??throw new InvalidOperationException("Распознаватель "+culture+" не найден");
                engine=new SpeechRecognitionEngine(info);engine.LoadGrammar(new DictationGrammar());engine.InitialSilenceTimeout=TimeSpan.FromSeconds(10);engine.EndSilenceTimeout=TimeSpan.FromMilliseconds(900);engine.SetInputToDefaultAudioDevice();
                engine.SpeechRecognized+=(_,e)=>{if(e.Result.Confidence>=.45)ui.BeginInvoke(()=>Finish(e.Result.Text,fromWake,token));};
                engine.RecognizeCompleted+=(_,e)=>ui.BeginInvoke(()=>{if(token==generation){StopInput();Status?.Invoke(e.Error?.Message??"Микрофон выключен");ArmLater();}});
                Listening=true;engine.RecognizeAsync(RecognizeMode.Single);
            }
            limit.Start();Status?.Invoke(fromWake?"Слушаю… После паузы команда отправится в чат":"Диктовка… Текст появится для проверки");StateChanged?.Invoke();
        }catch(Exception e){StopInput();Status?.Invoke(e.Message);ArmLater();}
    }
    private void Finish(string text,bool fromWake,int token){if(token!=generation||!Listening)return;StopInput();if(fromWake)CommandRecognized?.Invoke(text);else Recognized?.Invoke(text);ArmLater();}
    private void StopInput()
    {
        generation++;rearm.Stop();limit.Stop();Listening=false;Waiting=false;
        var old=engine;engine=null;if(old!=null){try{old.RecognizeAsyncCancel();}catch(InvalidOperationException){}old.Dispose();}
        var wave=capture;capture=null;if(wave!=null){wave.StopRecording();wave.Dispose();}
        lock(audioLock){decoder?.Dispose();decoder=null;}StateChanged?.Invoke();
    }
    public void Stop(){StopInput();ArmLater();}
    public void Suspend(){StopInput();}
    public void Speak(string text)
    {
        if(!AssistantPreferences.Current.SpeakReplies||string.IsNullOrWhiteSpace(text))return;
        if(AssistantPreferences.Current.RvcEnabled||AssistantPreferences.Current.NeuralEnabled){_=SpeakConverted(text);return;}
        StopInput();speaker??=CreateSpeaker();speaker.SpeakAsyncCancelAll();speaking=true;
        var prefs=AssistantPreferences.Current;var installed=speaker.GetInstalledVoices().Where(v=>v.Enabled).ToList();
        var selected=installed.FirstOrDefault(v=>v.VoiceInfo.Name==prefs.VoiceName)??installed.FirstOrDefault(v=>v.VoiceInfo.Culture.Name.StartsWith("ru"));
        if(selected==null){speaking=false;ArmLater();throw new InvalidOperationException("Русский голос Windows не установлен");}
        speaker.SelectVoice(selected.VoiceInfo.Name);speaker.Rate=Math.Clamp(prefs.VoiceRate,-5,5);speaker.SetOutputToDefaultAudioDevice();
        speaker.SpeakAsync(text.Length>6000?text[..6000]:text);
    }
    private async Task SpeakConverted(string text)
    {
        Silence();StopInput();speaking=true;var version=speechVersion;using var current=new CancellationTokenSource(TimeSpan.FromMinutes(3));conversion=current;var cancel=current.Token;System.Media.SoundPlayer? playback=null;
        var dir=Path.Combine(AssistantPreferences.DataDirectory,"speech-temp",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);var input=Path.Combine(dir,"source.wav");var output=Path.Combine(dir,"hoshino.wav");var textFile=Path.Combine(dir,"text.txt");
        try {
            Status?.Invoke("Готовлю голос Хошино локально…");
            if(AssistantPreferences.Current.NeuralEnabled){await File.WriteAllTextAsync(textFile,text.Length>1500?text[..1500]:text,cancel);await RvcVoice.Synthesize(textFile,input,cancel);}
            else using(var tts=new SpeechSynthesizer()){var prefs=AssistantPreferences.Current;var name=Voices().FirstOrDefault(v=>v==prefs.VoiceName)??Voices().FirstOrDefault(v=>v.Contains("Irina"))??throw new IOException("Нужен русский голос Windows");tts.SelectVoice(name);tts.Rate=Math.Clamp(prefs.VoiceRate,-5,5);tts.SetOutputToWaveFile(input);var complete=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);tts.SpeakCompleted+=(_,e)=>{if(e.Error!=null)complete.TrySetException(e.Error);else complete.TrySetResult();};using var registration=cancel.Register(()=>tts.SpeakAsyncCancelAll());tts.SpeakAsync(text.Length>1500?text[..1500]:text);await complete.Task;cancel.ThrowIfCancellationRequested();}
            if(AssistantPreferences.Current.RvcEnabled)await RvcVoice.Convert(input,output,cancel);else File.Copy(input,output);cancel.ThrowIfCancellationRequested();playback=new System.Media.SoundPlayer(output);player=playback;Status?.Invoke("Хошино говорит…");await Task.Run(()=>playback.PlaySync(),cancel);
        } catch(OperationCanceledException){}catch(Exception e){Status?.Invoke("Голос RVC недоступен: "+e.Message);}
        finally {playback?.Dispose();if(ReferenceEquals(player,playback))player=null;try{File.Delete(input);File.Delete(output);File.Delete(textFile);Directory.Delete(dir);}catch(IOException){}if(ReferenceEquals(conversion,current))conversion=null;if(version==speechVersion){speaking=false;ArmLater();}}
    }
    private SpeechSynthesizer CreateSpeaker(){var s=new SpeechSynthesizer();s.SpeakCompleted+=(_,_)=>ui.BeginInvoke(()=>{if(s.State==SynthesizerState.Ready){speaking=false;ArmLater();}});return s;}
    public void Silence(){speechVersion++;conversion?.Cancel();player?.Stop();speaker?.SpeakAsyncCancelAll();speaking=false;ArmLater();}
    public void Dispose(){Microsoft.Win32.SystemEvents.SessionSwitch-=SessionChanged;Microsoft.Win32.SystemEvents.PowerModeChanged-=PowerChanged;disposed=true;Silence();StopInput();speaker?.Dispose();model?.Dispose();}
}
