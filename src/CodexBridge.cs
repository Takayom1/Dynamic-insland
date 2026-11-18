// Original HoshiBar code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace DIsland;
public sealed class CodexBridge : IDisposable
{
    private Process? process;
    private int seq;
    private Task? starting;
    private readonly object startLock = new();
    private readonly SemaphoreSlim writeLock = new(1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode>> pending = new();
    public event Action<string, JsonNode?>? Event;
    public event Func<string, JsonNode?, Task<JsonNode?>>? Request;
    public Task Start() { lock (startLock) { if (starting?.IsFaulted == true || starting?.IsCanceled == true) starting = null; return starting ??= StartCore(); } }
    private async Task StartCore()
    {
        try {
            var executable = AssistantPreferences.Current.CodexExecutable;
            if (string.IsNullOrWhiteSpace(executable)) executable = ResolveCodex();
            var utf8 = new UTF8Encoding(false);
            var info = new ProcessStartInfo(executable, "app-server") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardInputEncoding = utf8, StandardOutputEncoding = utf8, StandardErrorEncoding = utf8 };
            var home = Path.Combine(AssistantPreferences.DataDirectory, "codex"); Directory.CreateDirectory(home); info.Environment["CODEX_HOME"] = home;
            foreach (var key in new[] { "OPENAI_API_KEY", "CODEX_API_KEY", "CODEX_ACCESS_TOKEN", "CHATGPT_ACCESS_TOKEN" }) info.Environment.Remove(key);
            process = Process.Start(info) ?? throw new IOException("Не удалось запустить Codex"); var current = process;
            _ = Task.Run(async () => { while (await current.StandardError.ReadLineAsync() is { }) { } });
            _ = Read(current);
            await Call("initialize", new { clientInfo = new { name = "glass_chat", version = "0.8.0" } });
            await Write(new { method = "initialized", @params = new { } });
        }
        catch { lock (startLock) starting = null; DisposeProcess(); throw; }
    }
    private static string ResolveCodex()
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        { var path = Path.Combine(dir.Trim('"'), "codex.exe"); if (File.Exists(path)) return path; }
        var npm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@openai", "codex");
        if (Directory.Exists(npm)) { var found = Directory.EnumerateFiles(npm, "codex.exe", SearchOption.AllDirectories).FirstOrDefault(); if (found != null) return found; }
        throw new IOException("Codex CLI не найден. Установите официальный @openai/codex или укажите codex.exe в настройках.");
    }
    private async Task Read(Process current)
    {
        try {
            while (await current.StandardOutput.ReadLineAsync() is { } line) {
                var n = JsonNode.Parse(line)!;
                if (n["method"] is { } method) {
                    if (n["id"] is { } id) _ = Answer(current, id.DeepClone(), method.ToString(), n["params"]?.DeepClone());
                    else Event?.Invoke(method.ToString(), n["params"]);
                } else if (n["id"] is { } rid && int.TryParse(rid.ToString(), out var value) && pending.TryRemove(value, out var t)) {
                    if (n["error"] is { } error) t.TrySetException(new IOException(error["message"]?.ToString() ?? "Ошибка Codex")); else t.TrySetResult(n["result"] ?? new JsonObject());
                }
            }
        } catch (Exception e) { if (ReferenceEquals(process, current)) foreach (var t in pending.Values) t.TrySetException(e); }
        finally {
            if (ReferenceEquals(process, current)) {
                foreach (var t in pending.Values) t.TrySetException(new IOException("Соединение Codex закрыто"));
                lock (startLock) starting = null;
                Event?.Invoke("glass/disconnected", null);
            }
        }
    }
    private async Task Answer(Process current, JsonNode id, string method, JsonNode? args)
    {
        try {
            var result = Request == null ? null : await Request(method, args);
            if (!ReferenceEquals(current, process)) return;
            var reply = new JsonObject { ["id"] = id };
            if (result != null) reply["result"] = result;
            else reply["error"] = new JsonObject { ["code"] = -32601, ["message"] = "Unsupported or declined request" };
            await Write(reply);
        } catch (Exception) { }
    }
    private async Task Write(object message)
    {
        await writeLock.WaitAsync();
        try { await process!.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message)); }
        finally { writeLock.Release(); }
    }
    public async Task<JsonNode> Call(string method, object args)
    {
        var id = Interlocked.Increment(ref seq); var t = new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously); pending[id] = t;
        try { await Write(new { id, method, @params = args }); return await t.Task.WaitAsync(TimeSpan.FromSeconds(90)); }
        finally { pending.TryRemove(id, out _); }
    }
    public void Restart() { if (AssistantRuntime.Busy) throw new IOException("Сначала остановите ответ"); DisposeProcess(); lock (startLock) starting = null; }
    private void DisposeProcess() {
        var previous = process; process = null;
        foreach (var pair in pending.ToArray()) if (pending.TryRemove(pair.Key, out var task)) task.TrySetException(new IOException("Соединение перезапущено"));
        if (previous is { HasExited: false }) previous.Kill(true); previous?.Dispose();
    }
    public void Dispose() => DisposeProcess();
}
