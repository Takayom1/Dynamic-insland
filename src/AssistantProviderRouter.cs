// Original HoshiBar code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DIsland;

public interface IAssistantProviderBackend : IDisposable
{
    string ProviderId { get; }
    event Action<string>? Delta;
    event Action<string>? Activity;
    event Action<string, string?>? TurnCompleted;
    event Action? Disconnected;
    event Action? AccountUpdated;

    Task<string> RefreshAccountAsync();
    Task<string> LoginAsync();
    Task LogoutAsync();
    Task<IReadOnlyList<AssistantSettingsPanel.Choice>> ListModelsAsync();
    Task<string> StartSessionAsync();
    Task ResumeSessionAsync(string threadId);
    Task StartTurnAsync(string sessionId, string userText, Action<string>? onSessionIdResolved = null);
    Task InterruptAsync();
    void Restart();
}

public static class AssistantProviders
{
    public const string Codex = "codex";
    public const string Gemini = "gemini";
    public const string Claude = "claude";

    public static readonly IReadOnlyList<AssistantSettingsPanel.Choice> All = new[]
    {
        new AssistantSettingsPanel.Choice(Codex, "Codex CLI · ChatGPT (OpenAI)"),
        new AssistantSettingsPanel.Choice(Gemini, "Gemini CLI · Google"),
        new AssistantSettingsPanel.Choice(Claude, "Claude Code · Anthropic"),
    };

    public static string Normalize(string? id) => (id ?? "").Trim().ToLowerInvariant() switch
    {
        Gemini => Gemini,
        Claude => Claude,
        _ => Codex,
    };

    public static string ShortName(string? id) => Normalize(id) switch
    {
        Gemini => "Gemini",
        Claude => "Claude",
        _ => "Codex",
    };

    public static string DisplayName(string? id) => Normalize(id) switch
    {
        Gemini => "Gemini CLI · Google",
        Claude => "Claude Code · Anthropic",
        _ => "Codex CLI · ChatGPT",
    };

    public static string VendorName(string? id) => Normalize(id) switch
    {
        Gemini => "Google",
        Claude => "Anthropic",
        _ => "OpenAI",
    };

    public static string ScopedAccountKey(string provider, string rawIdentity)
    {
        if (string.IsNullOrWhiteSpace(rawIdentity)) return "";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawIdentity.Trim())));
        return $"{Normalize(provider)}:{hash}";
    }

    public static bool MatchesConversation(ChatConversation conversation, string activeAccountKey, string activeProvider)
    {
        if (string.IsNullOrEmpty(activeAccountKey)) return false;
        var normProvider = Normalize(activeProvider);
        var convProvider = string.IsNullOrWhiteSpace(conversation.Provider)
            ? (conversation.AccountKey.Contains(':') ? Normalize(conversation.AccountKey.Split(':', 2)[0]) : Codex)
            : Normalize(conversation.Provider);

        if (convProvider != normProvider) return false;
        if (string.Equals(conversation.AccountKey, activeAccountKey, StringComparison.OrdinalIgnoreCase)) return true;

        // Backward compatibility with legacy pre-T06 Codex conversations stored with an unprefixed SHA-256 key.
        if (normProvider == Codex && !conversation.AccountKey.Contains(':')
            && string.Equals("codex:" + conversation.AccountKey, activeAccountKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}

internal static class CliProcessHelper
{
    public static string ResolveExecutable(
        string? configuredPath,
        string[] candidateNames,
        string[] extraDirectories,
        string notFoundMessage)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var trimmed = configuredPath.Trim().Trim('"');
            if (File.Exists(trimmed)) return Path.GetFullPath(trimmed);
            if (Directory.Exists(trimmed))
            {
                foreach (var name in candidateNames)
                {
                    var inside = Path.Combine(trimmed, name);
                    if (File.Exists(inside)) return Path.GetFullPath(inside);
                }
            }
            throw new IOException($"Указанный CLI не найден: {trimmed}");
        }

        var searchDirs = new List<string>();
        foreach (var part in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var dir = part.Trim().Trim('"');
            if (dir.Length > 0) searchDirs.Add(dir);
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        searchDirs.Add(Path.Combine(userProfile, ".local", "bin"));
        searchDirs.Add(Path.Combine(appData, "npm"));
        searchDirs.Add(Path.Combine(localAppData, "pnpm"));
        searchDirs.Add(Path.Combine(userProfile, "scoop", "shims"));
        searchDirs.Add(Path.Combine(programFiles, "nodejs"));
        searchDirs.AddRange(extraDirectories.Where(d => !string.IsNullOrWhiteSpace(d)));

        foreach (var dir in searchDirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var name in candidateNames)
                {
                    var candidate = Path.Combine(dir, name);
                    if (File.Exists(candidate)) return Path.GetFullPath(candidate);
                }
            }
            catch (Exception) { }
        }

        throw new IOException(notFoundMessage);
    }

    public static ProcessStartInfo CreateHeadlessStartInfo(string resolvedBinary, IReadOnlyList<string> args, string workingDirectory)
    {
        var utf8 = new UTF8Encoding(false);
        ProcessStartInfo info;

        if ((resolvedBinary.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            || resolvedBinary.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            && TryResolveNodeShim(resolvedBinary, out var nodeExe, out var scriptPath))
        {
            info = new ProcessStartInfo(nodeExe)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardInputEncoding = utf8,
                StandardOutputEncoding = utf8,
                StandardErrorEncoding = utf8,
            };
            info.ArgumentList.Add("--no-warnings");
            info.ArgumentList.Add(scriptPath);
            foreach (var arg in args) info.ArgumentList.Add(arg);
        }
        else if (resolvedBinary.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            || resolvedBinary.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            var comSpec = Environment.GetEnvironmentVariable("ComSpec");
            if (string.IsNullOrWhiteSpace(comSpec) || !File.Exists(comSpec)) comSpec = "cmd.exe";
            var cmdLine = BuildCmdCommandLine(resolvedBinary, args);
            info = new ProcessStartInfo(comSpec, $"/d /s /c \"{cmdLine}\"")
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardInputEncoding = utf8,
                StandardOutputEncoding = utf8,
                StandardErrorEncoding = utf8,
            };
        }
        else
        {
            info = new ProcessStartInfo(resolvedBinary)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardInputEncoding = utf8,
                StandardOutputEncoding = utf8,
                StandardErrorEncoding = utf8,
            };
            foreach (var arg in args) info.ArgumentList.Add(arg);
        }

        EnrichPath(info);
        info.Environment["NO_COLOR"] = "1";
        info.Environment["FORCE_COLOR"] = "0";
        info.Environment["CI"] = "true";
        return info;
    }

    private static bool TryResolveNodeShim(string cmdPath, out string nodeExe, out string scriptPath)
    {
        nodeExe = "";
        scriptPath = "";
        try
        {
            var cmdDir = Path.GetDirectoryName(cmdPath);
            if (string.IsNullOrEmpty(cmdDir)) return false;
            var content = File.ReadAllText(cmdPath);
            var match = Regex.Match(content, @"%[dp0~]+%\\([^\r\n""]+?\.(?:c?m?js))""", RegexOptions.IgnoreCase);
            if (!match.Success) return false;

            var candidateScript = Path.GetFullPath(Path.Combine(cmdDir, match.Groups[1].Value));
            if (!File.Exists(candidateScript)) return false;

            var foundNode = FindNodeExecutable(cmdDir);
            if (foundNode is null) return false;

            nodeExe = foundNode;
            scriptPath = candidateScript;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string? FindNodeExecutable(string preferredDir)
    {
        var candidates = new List<string> { Path.Combine(preferredDir, "node.exe") };
        foreach (var part in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var dir = part.Trim().Trim('"');
            if (dir.Length > 0) candidates.Add(Path.Combine(dir, "node.exe"));
        }
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "nodejs", "node.exe"));

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string BuildCmdCommandLine(string binary, IReadOnlyList<string> args)
    {
        var sb = new StringBuilder();
        sb.Append('"').Append(binary.Replace("\"", "")).Append('"');
        foreach (var arg in args)
        {
            var clean = arg.Replace("\r", " ").Replace("\n", " ").Replace("\"", "\\\"");
            sb.Append(' ').Append('"').Append(clean).Append('"');
        }
        return sb.ToString();
    }

    public static void EnrichPath(ProcessStartInfo info)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        var current = info.Environment.TryGetValue("PATH", out var p) && p is not null
            ? p
            : Environment.GetEnvironmentVariable("PATH") ?? "";

        var extras = new[]
        {
            Path.Combine(userProfile, ".local", "bin"),
            Path.Combine(appData, "npm"),
            Path.Combine(programFiles, "nodejs"),
            Path.Combine(localAppData, "Programs", "nodejs"),
            Path.Combine(localAppData, "pnpm"),
        };

        var parts = current.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
        foreach (var extra in extras)
        {
            if (Directory.Exists(extra) && !parts.Contains(extra, StringComparer.OrdinalIgnoreCase))
                parts.Add(extra);
        }
        info.Environment["PATH"] = string.Join(Path.PathSeparator, parts);
    }

    public static string WriteSystemPromptFile(string fileName, string content)
    {
        var dir = Path.Combine(AssistantPreferences.DataDirectory, "system-prompts");
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, fileName);
        var tmp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tmp, content, new UTF8Encoding(false));
        File.Move(tmp, target, overwrite: true);
        return target;
    }

    public static void LaunchInteractiveTerminal(string windowTitle, string resolvedBinary, string arguments, string workingDirectory)
    {
        var safeDir = Directory.Exists(workingDirectory)
            ? workingDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var comSpec = Environment.GetEnvironmentVariable("ComSpec");
        if (string.IsNullOrWhiteSpace(comSpec) || !File.Exists(comSpec)) comSpec = "cmd.exe";

        var inner = string.IsNullOrWhiteSpace(arguments)
            ? $"title {windowTitle} & \"{resolvedBinary}\""
            : $"title {windowTitle} & \"{resolvedBinary}\" {arguments}";

        Process.Start(new ProcessStartInfo(comSpec, $"/k \"{inner}\"")
        {
            WorkingDirectory = safeDir,
            UseShellExecute = true,
        });
    }
}

public sealed class CodexProviderBackend : IAssistantProviderBackend
{
    private readonly CodexBridge bridge;
    private string? activeThread;
    private string? activeTurn;

    public string ProviderId => AssistantProviders.Codex;
    public event Action<string>? Delta;
    public event Action<string>? Activity;
    public event Action<string, string?>? TurnCompleted;
    public event Action? Disconnected;
    public event Action? AccountUpdated;

    public CodexProviderBackend(CodexBridge bridge)
    {
        this.bridge = bridge;
        bridge.Event += OnBridgeEvent;
        bridge.Request += ApprovalDialog.Handle;
    }

    private void OnBridgeEvent(string method, JsonNode? p)
    {
        if (AssistantProviders.Normalize(AssistantPreferences.Current.Provider) != AssistantProviders.Codex) return;

        if (method is "account/updated" or "account/login/completed")
        {
            AccountUpdated?.Invoke();
            return;
        }
        if (method == "glass/disconnected")
        {
            activeThread = null;
            activeTurn = null;
            Disconnected?.Invoke();
            return;
        }
        if (p?["threadId"]?.ToString() != activeThread) return;

        if (method == "turn/started")
        {
            activeTurn = p?["turn"]?["id"]?.ToString();
            AssistantRuntime.ActiveTurn = activeTurn;
        }
        else if (method == "item/agentMessage/delta")
        {
            var chunk = p?["delta"]?.ToString();
            if (!string.IsNullOrEmpty(chunk)) Delta?.Invoke(chunk);
        }
        else if (method == "item/started")
        {
            ApprovalDialog.TrackItem(p?["item"]);
            var type = p?["item"]?["type"]?.ToString();
            if (type == "commandExecution") Activity?.Invoke("Выполняется команда…");
            else if (type == "fileChange") Activity?.Invoke("Изменяются файлы…");
        }
        else if (method == "turn/completed")
        {
            activeTurn = null;
            var turnStatus = p?["turn"]?["status"]?.ToString() ?? "completed";
            var err = p?["turn"]?["error"]?["message"]?.ToString();
            TurnCompleted?.Invoke(turnStatus, err);
        }
    }

    public Task<string> RefreshAccountAsync() => AssistantAccount.RefreshCodex();

    public async Task<string> LoginAsync()
    {
        await AssistantAccount.LoginCodex();
        return "Завершите вход ChatGPT в браузере, затем нажмите «Обновить».";
    }

    public Task LogoutAsync() => AssistantAccount.LogoutCodex();

    public async Task<IReadOnlyList<AssistantSettingsPanel.Choice>> ListModelsAsync()
    {
        await bridge.Start();
        var result = await bridge.Call("model/list", new { });
        var list = new List<AssistantSettingsPanel.Choice>();
        if (result["data"] is JsonArray data)
        {
            foreach (var item in data)
            {
                var id = item?["id"]?.ToString();
                if (!string.IsNullOrWhiteSpace(id))
                    list.Add(new(id, item?["displayName"]?.ToString() ?? id));
            }
        }
        return list;
    }

    public async Task<string> StartSessionAsync()
    {
        await bridge.Start();
        var prefs = AssistantPreferences.Current;
        var model = prefs.GetModelFor(AssistantProviders.Codex);
        var arguments = new
        {
            sandbox = prefs.AccessMode,
            approvalPolicy = "untrusted",
            approvalsReviewer = "user",
            cwd = ObsidianSync.EffectiveDirectory,
            model = string.IsNullOrWhiteSpace(model) ? null : model,
            developerInstructions = prefs.Instructions + ObsidianSync.Context(),
        };
        var r = await bridge.Call("thread/start", arguments);
        activeThread = r["thread"]!["id"]!.ToString();
        return activeThread;
    }

    public async Task ResumeSessionAsync(string threadId)
    {
        await bridge.Start();
        var prefs = AssistantPreferences.Current;
        await bridge.Call("thread/resume", new
        {
            threadId,
            excludeTurns = true,
            sandbox = prefs.AccessMode,
            approvalPolicy = "untrusted",
            approvalsReviewer = "user",
            cwd = ObsidianSync.EffectiveDirectory,
            developerInstructions = prefs.Instructions + ObsidianSync.Context(),
        });
        activeThread = threadId;
    }

    public async Task StartTurnAsync(string sessionId, string userText, Action<string>? onSessionIdResolved = null)
    {
        activeThread = sessionId;
        AssistantRuntime.ActiveThread = sessionId;
        var result = await bridge.Call("turn/start", new
        {
            threadId = sessionId,
            input = new[] { new { type = "text", text = userText } },
        });
        activeTurn = result["turn"]?["id"]?.ToString();
        AssistantRuntime.ActiveTurn = activeTurn;
    }

    public async Task InterruptAsync()
    {
        if (activeThread == null || activeTurn == null) return;
        await bridge.Call("turn/interrupt", new { threadId = activeThread, turnId = activeTurn });
    }

    public void Restart()
    {
        activeThread = null;
        activeTurn = null;
        bridge.Restart();
    }

    public void Dispose()
    {
        bridge.Event -= OnBridgeEvent;
        bridge.Request -= ApprovalDialog.Handle;
        bridge.Dispose();
    }
}

public sealed class AssistantProviderRouter : IDisposable
{
    private readonly CodexProviderBackend codex;
    private readonly GeminiCliBackend gemini;
    private readonly ClaudeCliBackend claude;

    public event Action<string>? Delta;
    public event Action<string>? Activity;
    public event Action<string, string?>? TurnCompleted;
    public event Action? Disconnected;
    public event Action? AccountUpdated;

    public AssistantProviderRouter(CodexBridge codexBridge)
    {
        codex = new CodexProviderBackend(codexBridge);
        gemini = new GeminiCliBackend();
        claude = new ClaudeCliBackend();

        foreach (var b in new IAssistantProviderBackend[] { codex, gemini, claude })
        {
            var id = b.ProviderId;
            b.Delta += text => { if (ActiveId == id) Delta?.Invoke(text); };
            b.Activity += status => { if (ActiveId == id) Activity?.Invoke(status); };
            b.TurnCompleted += (st, err) => { if (ActiveId == id) TurnCompleted?.Invoke(st, err); };
            b.Disconnected += () => { if (ActiveId == id) Disconnected?.Invoke(); };
            b.AccountUpdated += () => { if (ActiveId == id) AccountUpdated?.Invoke(); };
        }
    }

    public string ActiveId => AssistantProviders.Normalize(AssistantPreferences.Current.Provider);

    public IAssistantProviderBackend Active => Get(ActiveId);

    public IAssistantProviderBackend Get(string? providerId) => AssistantProviders.Normalize(providerId) switch
    {
        AssistantProviders.Gemini => gemini,
        AssistantProviders.Claude => claude,
        _ => codex,
    };

    public void RestartAll()
    {
        if (AssistantRuntime.Busy) throw new IOException("Сначала остановите активный ответ");
        codex.Restart();
        gemini.Restart();
        claude.Restart();
    }

    public void Dispose()
    {
        codex.Dispose();
        gemini.Dispose();
        claude.Dispose();
    }
}
