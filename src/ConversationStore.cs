// Original Hoshino Island code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.IO;
using System.Text.Json;
namespace DIsland;
public sealed class ChatMessage { public string Role { get; set; } = ""; public string Text { get; set; } = ""; }
public sealed class ChatConversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AccountKey { get; set; } = "";
    public string? ThreadId { get; set; }
    public string Title { get; set; } = "Новый диалог";
    public string Model { get; set; } = "";
    public List<ChatMessage> Messages { get; set; } = new();
    public DateTime Updated { get; set; } = DateTime.UtcNow;
}
public static class ConversationStore
{
    private static string PathName => Path.Combine(AssistantPreferences.DataDirectory, "conversations.json");
    public static List<ChatConversation> Read()
    {
        try { return JsonSerializer.Deserialize<List<ChatConversation>>(File.ReadAllText(PathName)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException) { return new(); }
    }
    public static void Save(ChatConversation conversation)
    {
        Directory.CreateDirectory(AssistantPreferences.DataDirectory); var list = Read(); list.RemoveAll(c => c.Id == conversation.Id); conversation.Updated = DateTime.UtcNow; list.Insert(0, conversation);
        File.WriteAllText(PathName + ".tmp", JsonSerializer.Serialize(list)); File.Move(PathName + ".tmp", PathName, true);
    }
}
