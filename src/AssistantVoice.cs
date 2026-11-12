// Original Hoshino Island code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Speech.Recognition;
using System.Speech.Synthesis;
namespace DIsland;
public sealed class AssistantVoice : IDisposable
{
    private SpeechRecognitionEngine? engine;
    private SpeechSynthesizer? speaker;
    private System.Threading.Timer? timeout;
    public bool Listening { get; private set; }
    public event Action<string>? Recognized;
    public event Action<string>? Status;
    public static string[] Cultures() => SpeechRecognitionEngine.InstalledRecognizers().Select(r => r.Culture.Name).Distinct().ToArray();
    public void Listen()
    {
        if (Listening) { Stop(); return; }
        speaker?.SpeakAsyncCancelAll();
        var culture = AssistantPreferences.Current.RecognitionCulture;
        var recognizer = SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault(r => r.Culture.Name.Equals(culture, StringComparison.OrdinalIgnoreCase));
        if (recognizer == null) throw new InvalidOperationException("Распознаватель " + culture + " не найден. Установите речевой пакет Windows и обновите список в настройках помощника.");
        engine?.Dispose(); engine = new SpeechRecognitionEngine(recognizer); engine.LoadGrammar(new DictationGrammar()); engine.SetInputToDefaultAudioDevice();
        engine.InitialSilenceTimeout = TimeSpan.FromSeconds(10); engine.BabbleTimeout = TimeSpan.FromSeconds(10);
        engine.SpeechRecognized += (sender, e) => { if (ReferenceEquals(sender, engine) && Listening && e.Result.Confidence >= .45) Recognized?.Invoke(e.Result.Text); };
        engine.RecognizeCompleted += (sender, e) => { if (!ReferenceEquals(sender, engine)) return; Listening = false; timeout?.Dispose(); Status?.Invoke(e.Error == null ? "Микрофон выключен" : "Ошибка распознавания: " + e.Error.Message); };
        Listening = true; Status?.Invoke("Слушаю… Нажмите микрофон ещё раз, чтобы остановить");
        engine.RecognizeAsync(RecognizeMode.Single); timeout = new System.Threading.Timer(_ => Stop(), null, TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
    }
    public void Stop() { Listening = false; timeout?.Dispose(); try { engine?.RecognizeAsyncCancel(); } catch (InvalidOperationException) { } }
    public void Speak(string text)
    {
        if (!AssistantPreferences.Current.SpeakReplies || string.IsNullOrWhiteSpace(text)) return;
        speaker ??= new SpeechSynthesizer(); speaker.SpeakAsyncCancelAll();
        var russian = speaker.GetInstalledVoices().FirstOrDefault(v => v.Enabled && v.VoiceInfo.Culture.Name.StartsWith("ru"));
        if (russian != null) speaker.SelectVoice(russian.VoiceInfo.Name);
        else if (text.Any(c => c >= 'А' && c <= 'я')) throw new InvalidOperationException("Русский голос Windows не установлен");
        speaker.SpeakAsync(text.Length > 6000 ? text[..6000] : text);
    }
    public void Silence() => speaker?.SpeakAsyncCancelAll();
    public void Dispose() { Stop(); engine?.Dispose(); speaker?.Dispose(); }
}
