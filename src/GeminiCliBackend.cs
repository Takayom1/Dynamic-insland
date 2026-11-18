// Original HoshiBar code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DIsland;

public sealed class GeminiCliBackend : IAssistantProviderBackend
{
    private readonly object procLock = new();
    private Process? currentProcess;
    private volatile bool interrupted;
    private string? activeSessionId;

    public string ProviderId => AssistantProviders.Gemini;
    public event Action<string>? Delta;
    public event Action<string>? Activity;
    public event Action<string, string?>? TurnCompleted;
    public event Action? Disconnected { add { } remove { } }
    public event Action? AccountUpdated;

    public static readonly IReadOnlyList<AssistantSettingsPanel.Choice> DefaultModelChoices = new[]
    {
        new AssistantSettingsPanel.Choice("", "Авто (модель Gemini CLI по умолчанию)"),
        new AssistantSettingsPanel.Choice("gemini-2.5-pro", "Gemini 2.5 Pro · глубокий анализ"),
        new AssistantSettingsPanel.Choice("gemini-2.5-flash", "Gemini 2.5 Flash · быстрый ответ"),
        new AssistantSettingsPanel.Choice("gemini-3-pro-preview", "Gemini 3 Pro Preview"),
        new AssistantSettingsPanel.Choice("gemini-3-flash-preview", "Gemini 3 Flash Preview"),
    };

    public static string ResolveExecutable(string? configured = null)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var extra = new[]
        {
            Path.Combine(appData, "npm"),
            Path.Combine(appData, "npm", "node_modules", "@google", "gemini-cli", "bin"),
        };
        return CliProcessHelper.ResolveExecutable(
            configured ?? AssistantPreferences.Current.GeminiExecutable,
            new[] { "gemini.exe", "gemini.cmd", "gemini.bat" },
            extra,
            "Gemini CLI не найден. Установите официальный пакет (npm install -g @google/gemini-cli) или укажите путь к gemini.cmd в настройках.");
    }

    public Task<string> RefreshAccountAsync()
    {
        try
        {
            _ = ResolveExecutable();
        }
        catch (Exception e)
        {
            AssistantRuntime.SetAccount("");
            return Task.FromResult(e.Message);
        }

        var home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini");
        string? email = null;
        var hasAuthHint = false;

        try
        {
            var accountsFile = Path.Combine(home, "google_accounts.json");
            if (File.Exists(accountsFile))
            {
                var node = JsonNode.Parse(File.ReadAllText(accountsFile));
                email = node?["active"]?.ToString();
                if (!string.IsNullOrWhiteSpace(email)) hasAuthHint = true;
            }
        }
        catch (Exception) { }

        try
        {
            var oauthFile = Path.Combine(home, "oauth_creds.json");
            if (File.Exists(oauthFile) && new FileInfo(oauthFile).Length > 16)
                hasAuthHint = true;

            var settingsFile = Path.Combine(home, "settings.json");
            if (File.Exists(settingsFile))
            {
                var raw = File.ReadAllText(settingsFile);
                if (raw.Contains("oauth", StringComparison.OrdinalIgnoreCase)
                    || raw.Contains("selectedAuthType", StringComparison.OrdinalIgnoreCase)
                    || raw.Contains("selectedType", StringComparison.OrdinalIgnoreCase)
                    || raw.Contains("api-key", StringComparison.OrdinalIgnoreCase)
                    || raw.Contains("vertex", StringComparison.OrdinalIgnoreCase))
                {
                    hasAuthHint = true;
                }
            }
        }
        catch (Exception) { }

        var hasEnvKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GOOGLE_API_KEY"));
        if (hasEnvKey) hasAuthHint = true;

        if (!hasAuthHint && !Directory.Exists(home))
        {
            AssistantRuntime.SetAccount("");
            return Task.FromResult("Gemini CLI установлен, но вход ещё не выполнен — нажмите «Войти в Gemini CLI».");
        }

        var identity = !string.IsNullOrWhiteSpace(email)
            ? email!
            : hasEnvKey ? "gemini-env-key" : "gemini-local-profile";

        AssistantRuntime.SetAccount(AssistantProviders.ScopedAccountKey(AssistantProviders.Gemini, identity));
        var label = !string.IsNullOrWhiteSpace(email)
            ? $"Подключён Gemini CLI · {email}"
            : hasAuthHint
                ? "Подключён профиль Gemini CLI"
                : "Gemini CLI найден (при первом запросе проверьте вход)";
        return Task.FromResult(label);
    }

    public Task<string> LoginAsync()
    {
        if (AssistantRuntime.Busy) throw new InvalidOperationException("Сначала остановите активный ответ");
        var exe = ResolveExecutable();
        CliProcessHelper.LaunchInteractiveTerminal(
            "HoshiBar - Gemini CLI Login",
            exe,
            "",
            AssistantPreferences.Current.WorkingDirectory);
        return Task.FromResult("Открыто окно Gemini CLI. Выполните вход Google в браузере (или команду /auth), затем закройте консоль и нажмите «Обновить».");
    }

    public Task LogoutAsync()
    {
        if (AssistantRuntime.Busy) throw new InvalidOperationException("Сначала остановите активный ответ");
        var oauthFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "oauth_creds.json");
        try { if (File.Exists(oauthFile)) File.Delete(oauthFile); } catch (Exception) { }
        AssistantRuntime.SetAccount("");
        AccountUpdated?.Invoke();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AssistantSettingsPanel.Choice>> ListModelsAsync() =>
        Task.FromResult(DefaultModelChoices);

    public Task<string> StartSessionAsync()
    {
        _ = ResolveExecutable();
        activeSessionId = "new-" + Guid.NewGuid().ToString("N");
        return Task.FromResult(activeSessionId);
    }

    public Task ResumeSessionAsync(string threadId)
    {
        _ = ResolveExecutable();
        activeSessionId = threadId;
        return Task.CompletedTask;
    }

    public Task StartTurnAsync(string sessionId, string userText, Action<string>? onSessionIdResolved = null)
    {
        var exe = ResolveExecutable();
        var prefs = AssistantPreferences.Current;
        var cwd = ObsidianSync.EffectiveDirectory;
        var systemPromptPath = CliProcessHelper.WriteSystemPromptFile(
            "gemini-system.md",
            prefs.Instructions + ObsidianSync.Context());

        var approvalMode = prefs.AccessMode switch
        {
            "read-only" => "plan",
            "danger-full-access" => "yolo",
            _ => "auto_edit",
        };

        var isResumingRealSession = !string.IsNullOrWhiteSpace(sessionId)
            && !sessionId.StartsWith("new-", StringComparison.Ordinal);

        var args = new List<string>
        {
            "--output-format", "stream-json",
            "--approval-mode", approvalMode,
        };

        var model = prefs.GetModelFor(AssistantProviders.Gemini);
        if (!string.IsNullOrWhiteSpace(model))
        {
            args.Add("-m");
            args.Add(model.Trim());
        }

        if (isResumingRealSession)
        {
            args.Add("--resume");
            args.Add(sessionId);
        }

        var useStdinForPrompt = false;
        var info = BuildGeminiStartInfo(exe, args, userText, cwd, ref useStdinForPrompt);
        info.Environment["GEMINI_SYSTEM_MD"] = systemPromptPath;

        interrupted = false;
        var proc = Process.Start(info) ?? throw new IOException("Не удалось запустить процесс Gemini CLI");
        lock (procLock) currentProcess = proc;

        AssistantRuntime.ActiveThread = sessionId;
        AssistantRuntime.ActiveTurn = "gemini-turn";

        _ = RunStreamingTurnAsync(proc, userText, useStdinForPrompt, onSessionIdResolved);
        return Task.CompletedTask;
    }

    private static ProcessStartInfo BuildGeminiStartInfo(
        string exe,
        List<string> baseArgs,
        string userText,
        string cwd,
        ref bool useStdinForPrompt)
    {
        // When launched via ArgumentList (direct .exe or resolved node.exe + index.js), passing -p <userText>
        // works cleanly with --resume and avoids cmd.exe quoting limits up to ~24 KB.
        var canPassDirectArg = userText.Length < 20000;
        var fullArgs = new List<string>(baseArgs);
        if (canPassDirectArg)
        {
            fullArgs.Add("-p");
            fullArgs.Add(userText);
        }

        var info = CliProcessHelper.CreateHeadlessStartInfo(exe, fullArgs, cwd);

        // If CreateHeadlessStartInfo fell back to cmd.exe AND userText has newlines or cmd metacharacters,
        // rebuild without userText on the command line and pipe it cleanly via UTF-8 stdin instead.
        if (canPassDirectArg
            && info.FileName.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase)
            && userText.IndexOfAny(new[] { '\r', '\n', '"', '&', '|', '<', '>', '^', '%' }) >= 0)
        {
            info = CliProcessHelper.CreateHeadlessStartInfo(exe, baseArgs, cwd);
            useStdinForPrompt = true;
        }
        else if (!canPassDirectArg)
        {
            useStdinForPrompt = true;
        }

        return info;
    }

    private async Task RunStreamingTurnAsync(
        Process proc,
        string userText,
        bool writePromptToStdin,
        Action<string>? onSessionIdResolved)
    {
        var stderrBuilder = new StringBuilder();
        var hadStreamedDelta = false;
        var turnFinished = false;

        try
        {
            if (writePromptToStdin)
            {
                await proc.StandardInput.WriteAsync(userText);
            }
            proc.StandardInput.Close();

            var errTask = Task.Run(async () =>
            {
                try
                {
                    while (await proc.StandardError.ReadLineAsync() is { } errLine)
                    {
                        if (stderrBuilder.Length < 4000) stderrBuilder.AppendLine(errLine);
                    }
                }
                catch (Exception) { }
            });

            while (await proc.StandardOutput.ReadLineAsync() is { } line)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                if (!trimmed.StartsWith('{'))
                {
                    // Plain diagnostic output fallback if CLI emitted non-JSON warning
                    continue;
                }

                JsonNode? node;
                try { node = JsonNode.Parse(trimmed); }
                catch (JsonException) { continue; }
                if (node is null) continue;

                var type = node["type"]?.ToString() ?? "";
                if (node["session_id"]?.ToString() is { Length: > 0 } sid && sid != activeSessionId)
                {
                    activeSessionId = sid;
                    AssistantRuntime.ActiveThread = sid;
                    onSessionIdResolved?.Invoke(sid);
                }

                switch (type)
                {
                    case "init":
                        Activity?.Invoke("Gemini анализирует запрос…");
                        break;

                    case "message":
                    {
                        var role = node["role"]?.ToString() ?? "";
                        if (role is "assistant" or "model")
                        {
                            var content = ExtractTextContent(node["content"]);
                            var isDelta = node["delta"] is JsonValue dv && dv.TryGetValue<bool>(out var b) && b;
                            if (!string.IsNullOrEmpty(content))
                            {
                                if (isDelta)
                                {
                                    hadStreamedDelta = true;
                                    Delta?.Invoke(content);
                                }
                                else if (!hadStreamedDelta)
                                {
                                    hadStreamedDelta = true;
                                    Delta?.Invoke(content);
                                }
                            }
                        }
                        break;
                    }

                    case "tool_use":
                    {
                        var toolName = node["tool_name"]?.ToString() ?? node["name"]?.ToString() ?? "инструмент";
                        Activity?.Invoke($"Gemini выполняет {toolName}…");
                        break;
                    }

                    case "error":
                    {
                        var errMsg = node["message"]?.ToString() ?? node["error"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(errMsg) && stderrBuilder.Length < 4000)
                            stderrBuilder.AppendLine(errMsg);
                        break;
                    }

                    case "result":
                    {
                        var status = node["status"]?.ToString() ?? "success";
                        if (!hadStreamedDelta && node["response"]?.ToString() is { Length: > 0 } respText)
                        {
                            hadStreamedDelta = true;
                            Delta?.Invoke(respText);
                        }

                        if (status is "error" or "failed")
                        {
                            var err = node["error"]?["message"]?.ToString()
                                ?? node["error"]?.ToString()
                                ?? CleanErrorSummary(stderrBuilder.ToString(), "Gemini CLI сообщил об ошибке");
                            turnFinished = true;
                            TurnCompleted?.Invoke("failed", err);
                        }
                        else
                        {
                            turnFinished = true;
                            TurnCompleted?.Invoke(interrupted ? "interrupted" : "completed", null);
                        }
                        break;
                    }
                }
            }

            await errTask;
            await proc.WaitForExitAsync();

            if (!turnFinished)
            {
                if (interrupted)
                {
                    TurnCompleted?.Invoke("interrupted", null);
                }
                else if (proc.ExitCode != 0 && !hadStreamedDelta)
                {
                    TurnCompleted?.Invoke("failed", CleanErrorSummary(stderrBuilder.ToString(), $"Gemini CLI завершился с кодом {proc.ExitCode}"));
                }
                else
                {
                    TurnCompleted?.Invoke("completed", null);
                }
            }
        }
        catch (Exception e)
        {
            if (!turnFinished)
            {
                TurnCompleted?.Invoke(interrupted ? "interrupted" : "failed", interrupted ? null : e.Message);
            }
        }
        finally
        {
            lock (procLock)
            {
                if (ReferenceEquals(currentProcess, proc)) currentProcess = null;
            }
            try { proc.Dispose(); } catch (Exception) { }
        }
    }

    private static string ExtractTextContent(JsonNode? contentNode)
    {
        if (contentNode is null) return "";
        if (contentNode is JsonValue val && val.TryGetValue<string>(out var s)) return s;
        if (contentNode is JsonArray arr)
        {
            var sb = new StringBuilder();
            foreach (var item in arr)
            {
                if (item is JsonValue iv && iv.TryGetValue<string>(out var part)) sb.Append(part);
                else if (item?["text"]?.ToString() is { } t) sb.Append(t);
            }
            return sb.ToString();
        }
        return contentNode["text"]?.ToString() ?? "";
    }

    private static string CleanErrorSummary(string rawStderr, string fallback)
    {
        var lines = rawStderr
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0
                && !l.StartsWith("DeprecationWarning", StringComparison.OrdinalIgnoreCase)
                && !l.StartsWith("(node:", StringComparison.OrdinalIgnoreCase)
                && !l.StartsWith("Loaded cached credentials", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (lines.Count == 0) return fallback;
        var joined = string.Join(" ", lines.TakeLast(3));
        if (joined.Contains("auth", StringComparison.OrdinalIgnoreCase)
            || joined.Contains("login", StringComparison.OrdinalIgnoreCase)
            || joined.Contains("credential", StringComparison.OrdinalIgnoreCase))
        {
            return "Требуется вход в Gemini CLI (откройте Настройки → Войти в Gemini CLI): " + joined;
        }
        return joined.Length > 320 ? joined[..320] + "…" : joined;
    }

    public Task InterruptAsync()
    {
        interrupted = true;
        KillActiveProcess();
        return Task.CompletedTask;
    }

    public void Restart()
    {
        interrupted = true;
        activeSessionId = null;
        KillActiveProcess();
    }

    private void KillActiveProcess()
    {
        Process? proc;
        lock (procLock)
        {
            proc = currentProcess;
            currentProcess = null;
        }
        if (proc is null) return;
        try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch (Exception) { }
        try { proc.Dispose(); } catch (Exception) { }
    }

    public void Dispose() => Restart();
}
