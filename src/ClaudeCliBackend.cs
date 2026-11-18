// Original HoshiBar code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DIsland;

public sealed class ClaudeCliBackend : IAssistantProviderBackend
{
    private readonly object procLock = new();
    private Process? currentProcess;
    private volatile bool interrupted;
    private string? activeSessionId;

    public string ProviderId => AssistantProviders.Claude;
    public event Action<string>? Delta;
    public event Action<string>? Activity;
    public event Action<string, string?>? TurnCompleted;
    public event Action? Disconnected { add { } remove { } }
    public event Action? AccountUpdated;

    public static readonly IReadOnlyList<AssistantSettingsPanel.Choice> DefaultModelChoices = new[]
    {
        new AssistantSettingsPanel.Choice("", "Авто (модель Claude Code по умолчанию)"),
        new AssistantSettingsPanel.Choice("sonnet", "Claude Sonnet · баланс скорости и ума"),
        new AssistantSettingsPanel.Choice("opus", "Claude Opus · максимальная глубина"),
        new AssistantSettingsPanel.Choice("haiku", "Claude Haiku · мгновенные ответы"),
    };

    public static string ResolveExecutable(string? configured = null)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        var extra = new[]
        {
            Path.Combine(userProfile, ".local", "bin"),
            Path.Combine(userProfile, ".claude", "local"),
            Path.Combine(localAppData, "Programs", "claude-code"),
            Path.Combine(localAppData, "Programs", "Claude"),
            Path.Combine(appData, "npm"),
        };

        return CliProcessHelper.ResolveExecutable(
            configured ?? AssistantPreferences.Current.ClaudeExecutable,
            new[] { "claude.exe", "claude.cmd", "claude.bat" },
            extra,
            "Claude Code CLI не найден. Установите официальный @anthropic-ai/claude-code (или нативный установщик Claude Code) либо укажите путь к claude.exe / claude.cmd в настройках.");
    }

    public async Task<string> RefreshAccountAsync()
    {
        string exe;
        try
        {
            exe = ResolveExecutable();
        }
        catch (Exception e)
        {
            AssistantRuntime.SetAccount("");
            return e.Message;
        }

        try
        {
            var info = CliProcessHelper.CreateHeadlessStartInfo(
                exe,
                new[] { "auth", "status", "--json" },
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

            using var proc = Process.Start(info);
            if (proc is not null)
            {
                proc.StandardInput.Close();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var stdout = await proc.StandardOutput.ReadToEndAsync(cts.Token);
                await proc.WaitForExitAsync(cts.Token);

                var trimmed = stdout.Trim();
                if (trimmed.StartsWith('{') && JsonNode.Parse(trimmed) is { } json)
                {
                    var loggedIn = json["loggedIn"] is JsonValue lv && lv.TryGetValue<bool>(out var b) ? b
                        : json["authenticated"] is JsonValue av && av.TryGetValue<bool>(out var ab) ? ab
                        : proc.ExitCode == 0;

                    if (!loggedIn)
                    {
                        AssistantRuntime.SetAccount("");
                        return "Вход в Claude Code не выполнен — нажмите «Войти в Claude Code».";
                    }

                    var email = json["email"]?.ToString()
                        ?? json["account"]?["email"]?.ToString()
                        ?? json["authMethod"]?.ToString()
                        ?? "claude-user";
                    var plan = json["subscriptionType"]?.ToString() ?? json["plan"]?.ToString();

                    AssistantRuntime.SetAccount(AssistantProviders.ScopedAccountKey(AssistantProviders.Claude, email));
                    return string.IsNullOrWhiteSpace(plan)
                        ? $"Подключён Claude Code · {email}"
                        : $"Подключён Claude Code · {email} ({plan})";
                }
            }
        }
        catch (Exception) { }

        // Fallback check on ~/.claude.json / ~/.claude/.credentials.json or ANTHROPIC_API_KEY
        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var claudeConfig = Path.Combine(userHome, ".claude.json");
        var claudeCreds = Path.Combine(userHome, ".claude", ".credentials.json");
        var hasEnvKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));

        if (hasEnvKey || File.Exists(claudeCreds) || File.Exists(claudeConfig))
        {
            var identity = hasEnvKey ? "claude-api-key" : "claude-local-profile";
            AssistantRuntime.SetAccount(AssistantProviders.ScopedAccountKey(AssistantProviders.Claude, identity));
            return "Подключён профиль Claude Code";
        }

        AssistantRuntime.SetAccount("");
        return "Вход в Claude Code не выполнен — нажмите «Войти в Claude Code».";
    }

    public Task<string> LoginAsync()
    {
        if (AssistantRuntime.Busy) throw new InvalidOperationException("Сначала остановите активный ответ");
        var exe = ResolveExecutable();
        CliProcessHelper.LaunchInteractiveTerminal(
            "HoshiBar - Claude Code Login",
            exe,
            "auth login",
            AssistantPreferences.Current.WorkingDirectory);
        return Task.FromResult("Открыто окно входа Claude Code. Завершите авторизацию Anthropic в браузере, затем нажмите «Обновить».");
    }

    public async Task LogoutAsync()
    {
        if (AssistantRuntime.Busy) throw new InvalidOperationException("Сначала остановите активный ответ");
        try
        {
            var exe = ResolveExecutable();
            var info = CliProcessHelper.CreateHeadlessStartInfo(
                exe,
                new[] { "auth", "logout" },
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            using var proc = Process.Start(info);
            if (proc is not null)
            {
                proc.StandardInput.Close();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await proc.WaitForExitAsync(cts.Token);
            }
        }
        catch (Exception) { }

        AssistantRuntime.SetAccount("");
        AccountUpdated?.Invoke();
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

        var systemPromptText = prefs.Instructions + ObsidianSync.Context();
        var systemPromptFile = CliProcessHelper.WriteSystemPromptFile("claude-system.md", systemPromptText);

        var permissionMode = prefs.AccessMode switch
        {
            "read-only" => "plan",
            "danger-full-access" => "bypassPermissions",
            _ => "acceptEdits",
        };

        var isResumingRealSession = !string.IsNullOrWhiteSpace(sessionId)
            && !sessionId.StartsWith("new-", StringComparison.Ordinal);

        var baseArgs = new List<string>
        {
            "-p",
            "--output-format", "stream-json",
            "--verbose",
            "--include-partial-messages",
            "--permission-mode", permissionMode,
        };

        var model = prefs.GetModelFor(AssistantProviders.Claude);
        if (!string.IsNullOrWhiteSpace(model))
        {
            baseArgs.Add("--model");
            baseArgs.Add(model.Trim());
        }

        if (isResumingRealSession)
        {
            baseArgs.Add("--resume");
            baseArgs.Add(sessionId);
        }

        // Prefer universally supported --append-system-prompt when using direct Win32 ArgumentList
        // (claude.exe or resolved node.exe + cli.js); fall back to --append-system-prompt-file under cmd.exe.
        var directArgs = new List<string>(baseArgs);
        if (systemPromptText.Length < 18000)
        {
            directArgs.Add("--append-system-prompt");
            directArgs.Add(systemPromptText);
        }
        else
        {
            directArgs.Add("--append-system-prompt-file");
            directArgs.Add(systemPromptFile);
        }

        var info = CliProcessHelper.CreateHeadlessStartInfo(exe, directArgs, cwd);
        if (info.FileName.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase))
        {
            var cmdSafeArgs = new List<string>(baseArgs)
            {
                "--append-system-prompt-file",
                systemPromptFile,
            };
            info = CliProcessHelper.CreateHeadlessStartInfo(exe, cmdSafeArgs, cwd);
        }
        interrupted = false;

        var proc = Process.Start(info) ?? throw new IOException("Не удалось запустить процесс Claude Code CLI");
        lock (procLock) currentProcess = proc;

        AssistantRuntime.ActiveThread = sessionId;
        AssistantRuntime.ActiveTurn = "claude-turn";

        _ = RunStreamingTurnAsync(proc, userText, onSessionIdResolved);
        return Task.CompletedTask;
    }

    private async Task RunStreamingTurnAsync(
        Process proc,
        string userText,
        Action<string>? onSessionIdResolved)
    {
        var stderrBuilder = new StringBuilder();
        var hadStreamedDeltaInBlock = false;
        var hadAnyOutput = false;
        var turnFinished = false;

        try
        {
            await proc.StandardInput.WriteAsync(userText);
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
                if (trimmed.Length == 0 || !trimmed.StartsWith('{')) continue;

                JsonNode? node;
                try { node = JsonNode.Parse(trimmed); }
                catch (JsonException) { continue; }
                if (node is null) continue;

                if (node["session_id"]?.ToString() is { Length: > 0 } sid && sid != activeSessionId)
                {
                    activeSessionId = sid;
                    AssistantRuntime.ActiveThread = sid;
                    onSessionIdResolved?.Invoke(sid);
                }

                var type = node["type"]?.ToString() ?? "";
                switch (type)
                {
                    case "system":
                        Activity?.Invoke("Claude готовит ответ…");
                        break;

                    case "stream_event":
                    {
                        var ev = node["event"];
                        var evType = ev?["type"]?.ToString() ?? "";
                        if (evType == "content_block_start")
                        {
                            hadStreamedDeltaInBlock = false;
                            if (ev?["content_block"]?["type"]?.ToString() == "tool_use")
                            {
                                var tName = ev["content_block"]?["name"]?.ToString() ?? "инструмент";
                                Activity?.Invoke($"Claude вызывает {tName}…");
                            }
                        }
                        else if (evType == "content_block_delta")
                        {
                            var deltaType = ev?["delta"]?["type"]?.ToString();
                            if (deltaType == "text_delta")
                            {
                                var chunk = ev?["delta"]?["text"]?.ToString();
                                if (!string.IsNullOrEmpty(chunk))
                                {
                                    hadStreamedDeltaInBlock = true;
                                    hadAnyOutput = true;
                                    Delta?.Invoke(chunk);
                                }
                            }
                        }
                        break;
                    }

                    case "content_block_delta":
                    {
                        if (node["delta"]?["type"]?.ToString() == "text_delta"
                            && node["delta"]?["text"]?.ToString() is { Length: > 0 } chunk)
                        {
                            hadStreamedDeltaInBlock = true;
                            hadAnyOutput = true;
                            Delta?.Invoke(chunk);
                        }
                        break;
                    }

                    case "assistant":
                    {
                        if (node["message"]?["content"] is JsonArray contentArr)
                        {
                            foreach (var block in contentArr)
                            {
                                var bType = block?["type"]?.ToString();
                                if (bType == "tool_use")
                                {
                                    var tName = block?["name"]?.ToString() ?? "инструмент";
                                    Activity?.Invoke($"Claude выполняет {tName}…");
                                }
                                else if (bType == "text" && !hadStreamedDeltaInBlock)
                                {
                                    var text = block?["text"]?.ToString();
                                    if (!string.IsNullOrEmpty(text))
                                    {
                                        hadAnyOutput = true;
                                        Delta?.Invoke(text);
                                    }
                                }
                            }
                        }
                        hadStreamedDeltaInBlock = false;
                        break;
                    }

                    case "result":
                    {
                        var isError = node["is_error"] is JsonValue iv && iv.TryGetValue<bool>(out var errBool) && errBool;
                        var subtype = node["subtype"]?.ToString() ?? "";
                        var resultText = node["result"]?.ToString();

                        if (!hadAnyOutput && !isError && !string.IsNullOrEmpty(resultText))
                        {
                            hadAnyOutput = true;
                            Delta?.Invoke(resultText);
                        }

                        turnFinished = true;
                        if (interrupted)
                        {
                            TurnCompleted?.Invoke("interrupted", null);
                        }
                        else if (isError || subtype.StartsWith("error", StringComparison.OrdinalIgnoreCase))
                        {
                            var msg = !string.IsNullOrWhiteSpace(resultText)
                                ? resultText
                                : CleanErrorSummary(stderrBuilder.ToString(), "Claude Code сообщил об ошибке");
                            TurnCompleted?.Invoke("failed", msg);
                        }
                        else
                        {
                            TurnCompleted?.Invoke("completed", null);
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
                else if (proc.ExitCode != 0 && !hadAnyOutput)
                {
                    TurnCompleted?.Invoke("failed", CleanErrorSummary(stderrBuilder.ToString(), $"Claude Code завершился с кодом {proc.ExitCode}"));
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

    private static string CleanErrorSummary(string rawStderr, string fallback)
    {
        var lines = rawStderr
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("(node:", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (lines.Count == 0) return fallback;
        var joined = string.Join(" ", lines.TakeLast(3));
        if (joined.Contains("not logged in", StringComparison.OrdinalIgnoreCase)
            || joined.Contains("auth login", StringComparison.OrdinalIgnoreCase)
            || joined.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
        {
            return "Требуется вход в Claude Code (откройте Настройки → Войти в Claude Code): " + joined;
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
