// Original HoshiBar code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace DIsland;

/// <summary>
/// One-click & first-launch provisioner for Hoshino's offline Russian neural voice
/// (Silero v5_5_ru 'xenia' + HoshinoOv2 RVC v2 via standalone Python 3.11 + Applio).
/// Everything is stored outside the application folder under %LOCALAPPDATA%\GlassChat\VoicePack
/// so app updates never overwrite or redistribute third-party model weights.
/// </summary>
public static class HoshinoVoicePack
{
    public const string SileroSha256 = "50081637b602126ee06cb3bc8a744d25651d2da149ee8864b9a379bfdd934437";
    public const string DefaultSpeaker = "xenia";
    public const int DefaultPitch = 1;
    public const double DefaultIndexRate = 0.30;
    public const double DefaultProtect = 0.42;

    private const string UvZipUrl = "https://github.com/astral-sh/uv/releases/latest/download/uv-x86_64-pc-windows-msvc.zip";
    private const string ApplioZipUrl = "https://github.com/IAHispano/Applio/archive/refs/heads/main.zip";
    private const string ApplioResBase = "https://huggingface.co/IAHispano/Applio/resolve/main/Resources";
    private const string SileroUrl = "https://models.silero.ai/models/tts/ru/v5_5_ru.pt";
    private const string HoshinoOv2Url = "https://huggingface.co/C0ttontheBunny/HoshinoOv2/resolve/main/HoshinoOv2.zip";

    private static readonly SemaphoreSlim InstallGate = new(1, 1);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    public static string RootDirectory => Path.Combine(AssistantPreferences.DataDirectory, "VoicePack");
    public static string AutoInstallFlagPath => Path.Combine(AssistantPreferences.DataDirectory, "auto-install-voice.flag");
    public static string PythonExePath => Path.Combine(RootDirectory, "venv", "Scripts", "python.exe");
    public static string ApplioDirectory => Path.Combine(RootDirectory, "Applio");
    public static string SileroModelPath => Path.Combine(RootDirectory, "models", "v5_5_ru.pt");
    public static string HoshinoModelDir => Path.Combine(RootDirectory, "models", "HoshinoOv2");

    public static bool IsInstalling { get; private set; }
    public static string LastStatus { get; private set; } = "";
    public static double LastProgress { get; private set; }

    public static event Action<string, double>? ProgressChanged;

    public static bool IsInstalled(out string pthPath, out string indexPath)
    {
        pthPath = "";
        indexPath = "";
        if (!File.Exists(PythonExePath)) return false;
        if (!File.Exists(Path.Combine(ApplioDirectory, "rvc", "infer", "infer.py"))) return false;
        if (!File.Exists(Path.Combine(ApplioDirectory, "rvc", "models", "predictors", "rmvpe.pt"))) return false;
        if (!File.Exists(Path.Combine(ApplioDirectory, "rvc", "models", "embedders", "contentvec", "pytorch_model.bin"))) return false;
        if (!File.Exists(SileroModelPath)) return false;
        if (!Directory.Exists(HoshinoModelDir)) return false;

        var pth = Directory.GetFiles(HoshinoModelDir, "*.pth", SearchOption.AllDirectories).FirstOrDefault();
        var idx = Directory.GetFiles(HoshinoModelDir, "*.index", SearchOption.AllDirectories).FirstOrDefault();
        if (pth is null || idx is null) return false;

        pthPath = pth;
        indexPath = idx;
        return true;
    }

    /// <summary>
    /// Automatically wires AssistantPreferences to the installed VoicePack if no custom paths are configured yet.
    /// </summary>
    public static bool TryBindIfUnconfigured(AssistantPreferences prefs)
    {
        if (!IsInstalled(out var pth, out var idx)) return false;
        if (!string.IsNullOrWhiteSpace(prefs.RvcPython) && File.Exists(prefs.RvcPython)
            && !string.IsNullOrWhiteSpace(prefs.RvcModel) && File.Exists(prefs.RvcModel))
        {
            return false;
        }

        ApplyPresetTo(prefs, pth, idx, enableSpeech: true);
        return true;
    }

    public static void ApplyPresetTo(AssistantPreferences prefs, string pthPath, string indexPath, bool enableSpeech = true)
    {
        prefs.NeuralEnabled = true;
        prefs.NeuralModel = SileroModelPath;
        prefs.NeuralSpeaker = DefaultSpeaker;
        prefs.RvcEnabled = true;
        prefs.RvcPython = PythonExePath;
        prefs.RvcEngine = ApplioDirectory;
        prefs.RvcModel = pthPath;
        prefs.RvcIndex = indexPath;
        prefs.RvcPitch = DefaultPitch;
        prefs.RvcIndexRate = DefaultIndexRate;
        prefs.RvcProtect = DefaultProtect;
        if (enableSpeech) prefs.SpeakReplies = true;
    }

    /// <summary>
    /// Triggered on app startup when the installer left auto-install-voice.flag for a new user.
    /// </summary>
    public static void StartAutoProvisionIfPending()
    {
        if (!File.Exists(AutoInstallFlagPath)) return;
        if (IsInstalled(out var pth, out var idx))
        {
            try { File.Delete(AutoInstallFlagPath); } catch (IOException) { }
            var p = AssistantPreferences.Current;
            if (TryBindIfUnconfigured(p))
            {
                try { p.Save(false); AssistantRuntime.Voice?.Configure(); } catch (IOException) { }
            }
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await InstallAsync(preferCuda: false, CancellationToken.None);
                try { File.Delete(AutoInstallFlagPath); } catch (IOException) { }
            }
            catch (Exception ex)
            {
                Report($"Автоустановка голоса отложена: {ex.Message}", LastProgress);
            }
        });
    }

    public static async Task InstallAsync(bool preferCuda, CancellationToken cancel)
    {
        await InstallGate.WaitAsync(cancel);
        IsInstalling = true;
        var staging = Path.Combine(RootDirectory, "staging");
        var uvCache = Path.Combine(RootDirectory, "uv-cache");
        try
        {
            Directory.CreateDirectory(RootDirectory);
            Directory.CreateDirectory(staging);

            // 1. Standalone uv + Python 3.11 environment
            var uvExe = Path.Combine(RootDirectory, "uv", "uv.exe");
            if (!File.Exists(uvExe))
            {
                Report("Скачиваю менеджер окружения Python (uv)…", 5);
                var uvZip = Path.Combine(staging, "uv.zip");
                await DownloadFileAsync(UvZipUrl, uvZip, 5, 10, cancel);
                Directory.CreateDirectory(Path.GetDirectoryName(uvExe)!);
                ZipFile.ExtractToDirectory(uvZip, Path.GetDirectoryName(uvExe)!, overwriteFiles: true);
                if (!File.Exists(uvExe))
                {
                    var nested = Directory.GetFiles(Path.GetDirectoryName(uvExe)!, "uv.exe", SearchOption.AllDirectories).FirstOrDefault()
                                 ?? throw new IOException("Не удалось распаковать uv.exe");
                    if (!string.Equals(nested, uvExe, StringComparison.OrdinalIgnoreCase))
                        File.Copy(nested, uvExe, overwrite: true);
                }
            }

            var uvPythonDir = Path.Combine(RootDirectory, "python-runtime");
            Directory.CreateDirectory(uvPythonDir);
            Directory.CreateDirectory(uvCache);

            if (!File.Exists(PythonExePath))
            {
                Report("Устанавливаю изолированный Python 3.11…", 14);
                await RunToolAsync(uvExe, new[] { "venv", Path.Combine(RootDirectory, "venv"), "--python", "3.11" }, uvPythonDir, uvCache, cancel);
            }

            // 2. Install PyTorch + RVC + Whisper wheels
            Report(preferCuda
                ? "Устанавливаю PyTorch CUDA для видеокарты NVIDIA (~2.3 ГБ)…"
                : "Устанавливаю движок нейросети PyTorch (~160 МБ)…", 24);

            var torchArgs = preferCuda
                ? new[] { "pip", "install", "--python", PythonExePath, "torch==2.4.1+cu121", "torchaudio==2.4.1+cu121", "--index-url", "https://download.pytorch.org/whl/cu121" }
                : new[] { "pip", "install", "--python", PythonExePath, "torch", "torchaudio", "--index-url", "https://download.pytorch.org/whl/cpu" };
            await RunToolAsync(uvExe, torchArgs, uvPythonDir, uvCache, cancel);

            Report("Устанавливаю аудио-библиотеки RVC и Whisper…", 48);
            await RunToolAsync(uvExe, new[]
            {
                "pip", "install", "--python", PythonExePath, "--only-binary", ":all:",
                "numpy", "scipy", "soundfile", "soxr", "faiss-cpu", "librosa", "stftpitchshift",
                "einops", "transformers", "huggingface_hub", "onnxruntime", "pedalboard",
                "noisereduce", "omegaconf", "requests", "tqdm", "faster-whisper"
            }, uvPythonDir, uvCache, cancel);

            await RunToolAsync(uvExe, new[]
            {
                "pip", "install", "--python", PythonExePath, "--no-deps", "torchcrepe", "torchfcpe"
            }, uvPythonDir, uvCache, cancel);

            // 3. Applio inference core + RMVPE + ContentVec
            if (!File.Exists(Path.Combine(ApplioDirectory, "rvc", "infer", "infer.py")))
            {
                Report("Скачиваю локальный движок тембра Applio…", 60);
                var applioZip = Path.Combine(staging, "applio.zip");
                await DownloadFileAsync(ApplioZipUrl, applioZip, 60, 67, cancel);
                var extractTmp = Path.Combine(staging, "applio-extract");
                if (Directory.Exists(extractTmp)) Directory.Delete(extractTmp, true);
                ZipFile.ExtractToDirectory(applioZip, extractTmp, overwriteFiles: true);
                var innerRoot = Directory.GetDirectories(extractTmp).FirstOrDefault()
                                ?? throw new IOException("Архив Applio пуст");
                if (Directory.Exists(ApplioDirectory)) Directory.Delete(ApplioDirectory, true);
                Directory.Move(innerRoot, ApplioDirectory);
            }

            var predictorsDir = Path.Combine(ApplioDirectory, "rvc", "models", "predictors");
            var contentVecDir = Path.Combine(ApplioDirectory, "rvc", "models", "embedders", "contentvec");
            Directory.CreateDirectory(predictorsDir);
            Directory.CreateDirectory(contentVecDir);

            var rmvpePath = Path.Combine(predictorsDir, "rmvpe.pt");
            if (!File.Exists(rmvpePath))
            {
                Report("Скачиваю детектор интонации RMVPE…", 70);
                await DownloadFileAsync($"{ApplioResBase}/predictors/rmvpe.pt", rmvpePath, 70, 74, cancel);
            }

            var cvBin = Path.Combine(contentVecDir, "pytorch_model.bin");
            var cvCfg = Path.Combine(contentVecDir, "config.json");
            if (!File.Exists(cvBin) || !File.Exists(cvCfg))
            {
                Report("Скачиваю голосовой эмбеддер ContentVec…", 75);
                await DownloadFileAsync($"{ApplioResBase}/embedders/contentvec/pytorch_model.bin", cvBin, 75, 81, cancel);
                await DownloadFileAsync($"{ApplioResBase}/embedders/contentvec/config.json", cvCfg, 81, 82, cancel);
            }

            var ffmpegExe = Path.Combine(ApplioDirectory, "ffmpeg.exe");
            if (!File.Exists(ffmpegExe))
            {
                Report("Скачиваю аудио-кодек ffmpeg…", 83);
                await DownloadFileAsync($"{ApplioResBase}/ffmpeg.exe", ffmpegExe, 83, 86, cancel);
            }

            // 4. Official Silero v5_5_ru.pt (SHA-256 verified)
            Directory.CreateDirectory(Path.GetDirectoryName(SileroModelPath)!);
            if (!File.Exists(SileroModelPath) || !VerifySha256(SileroModelPath, SileroSha256))
            {
                Report("Скачиваю русскую нейромодель Silero v5_5_ru…", 87);
                await DownloadFileAsync(SileroUrl, SileroModelPath, 87, 92, cancel);
                if (!VerifySha256(SileroModelPath, SileroSha256))
                {
                    File.Delete(SileroModelPath);
                    throw new IOException("Контрольная сумма модели Silero v5_5_ru не совпала");
                }
            }

            // 5. Takanashi Hoshino RVC v2 (HoshinoOv2)
            Directory.CreateDirectory(HoshinoModelDir);
            if (!IsInstalled(out var pthPath, out var indexPath))
            {
                Report("Скачиваю тембр Таканаши Хошино (HoshinoOv2)…", 93);
                var ov2Zip = Path.Combine(staging, "HoshinoOv2.zip");
                await DownloadFileAsync(HoshinoOv2Url, ov2Zip, 93, 98, cancel);
                ZipFile.ExtractToDirectory(ov2Zip, HoshinoModelDir, overwriteFiles: true);
            }

            if (!IsInstalled(out pthPath, out indexPath))
                throw new IOException("Файлы модели HoshinoOv2 не найдены после распаковки");

            // Activate Variant #4 preset in AssistantPreferences
            var prefs = AssistantPreferences.Current;
            ApplyPresetTo(prefs, pthPath, indexPath, enableSpeech: true);
            prefs.Save();
            AssistantRuntime.Voice?.Configure();

            Report("Готово! Нейроголос Таканаши Хошино подключён и прогревается.", 100);
        }
        finally
        {
            IsInstalling = false;
            TryDeleteDirectory(staging);
            TryDeleteDirectory(uvCache);
            InstallGate.Release();
        }
    }

    private static async Task DownloadFileAsync(string url, string destination, double pStart, double pEnd, CancellationToken cancel)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var tmp = destination + ".part";
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1L;

        await using (var src = await resp.Content.ReadAsStreamAsync(cancel))
        await using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
        {
            var buf = new byte[65536];
            long readTotal = 0;
            int n;
            while ((n = await src.ReadAsync(buf, cancel)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), cancel);
                readTotal += n;
                if (total > 0)
                {
                    var pct = pStart + (pEnd - pStart) * Math.Clamp((double)readTotal / total, 0, 1);
                    Report(LastStatus, pct);
                }
            }
        }
        File.Move(tmp, destination, overwrite: true);
    }

    private static async Task RunToolAsync(string exe, string[] args, string uvPythonDir, string uvCacheDir, CancellationToken cancel)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.Environment["UV_PYTHON_INSTALL_DIR"] = uvPythonDir;
        psi.Environment["UV_CACHE_DIR"] = uvCacheDir;
        psi.Environment["PYTHONUTF8"] = "1";
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi) ?? throw new IOException($"Не удалось запустить {Path.GetFileName(exe)}");
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        try
        {
            await proc.WaitForExitAsync(cancel);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }

        await stdoutTask;
        var err = await stderrTask;
        if (proc.ExitCode != 0)
            throw new IOException(err.Length > 600 ? err[^600..] : err);
    }

    private static bool VerifySha256(string filePath, string expectedHex)
    {
        try
        {
            using var fs = File.OpenRead(filePath);
            var hex = Convert.ToHexString(SHA256.HashData(fs));
            return string.Equals(hex, expectedHex, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception) { }
    }

    private static void Report(string status, double progress)
    {
        if (!string.IsNullOrEmpty(status)) LastStatus = status;
        LastProgress = Math.Clamp(progress, 0, 100);
        ProgressChanged?.Invoke(LastStatus, LastProgress);
    }
}
