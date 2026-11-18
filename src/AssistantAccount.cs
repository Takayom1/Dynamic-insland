// Original HoshiBar code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
namespace DIsland;
public static class AssistantAccount
{
    public static Task<string> Refresh() => AssistantRuntime.Router.Active.RefreshAccountAsync();

    public static Task<string> Login() => AssistantRuntime.Router.Active.LoginAsync();

    public static Task Logout() => AssistantRuntime.Router.Active.LogoutAsync();

    public static async Task<string> RefreshCodex()
    {
        await AssistantRuntime.Bridge.Start();
        var result = await AssistantRuntime.Bridge.Call("account/read", new { refreshToken = false });
        var account = result["account"];
        if (account == null) { AssistantRuntime.SetAccount(""); return "Аккаунт ChatGPT не подключён"; }
        var identity = account["email"]?.ToString() ?? account["type"]?.ToString() ?? "local-account";
        AssistantRuntime.SetAccount(AssistantProviders.ScopedAccountKey(AssistantProviders.Codex, identity));
        return account["email"]?.ToString() is { Length: > 0 } mail
            ? $"Подключён Codex · {mail}"
            : "Подключён аккаунт Codex";
    }

    public static async Task LoginCodex()
    {
        if (AssistantRuntime.Busy) throw new InvalidOperationException("Сначала остановите ответ");
        await AssistantRuntime.Bridge.Start();
        var result = await AssistantRuntime.Bridge.Call("account/login/start", new { type = "chatgpt" });
        var url = result["authUrl"]?.ToString();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !(uri.Host == "chatgpt.com" || uri.Host.EndsWith(".openai.com", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Сервер не вернул официальный адрес входа ChatGPT");
        Process.Start(new ProcessStartInfo(url!) { UseShellExecute = true });
    }

    public static async Task LogoutCodex()
    {
        if (AssistantRuntime.Busy) throw new InvalidOperationException("Сначала остановите ответ");
        await AssistantRuntime.Bridge.Start(); await AssistantRuntime.Bridge.Call("account/logout", new { }); AssistantRuntime.SetAccount("");
        AssistantRuntime.Bridge.Restart();
    }
}
