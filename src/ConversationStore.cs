// Original HoshiBar code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.IO;
using System.Text;
using System.Text.Json;
namespace DIsland;
public sealed class ChatMessage { public string Role { get; set; } = ""; public string Text { get; set; } = ""; }
public sealed class ChatConversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Provider { get; set; } = AssistantProviders.Codex;
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
        try {
            var list=JsonSerializer.Deserialize<List<ChatConversation>>(File.ReadAllText(PathName)) ?? new();
            foreach(var message in list.SelectMany(c=>c.Messages).Where(m=>m.Role=="assistant"))message.Text=RepairLegacyText(message.Text);
            return list;
        }
        catch (Exception e) when (e is IOException or JsonException) { return new(); }
    }
    // Early Windows builds decoded UTF-8 stdout as CP1252. Only repair a lossless,
    // unmistakable Cyrillic case; viewing history never rewrites the stored file.
    public static string RepairLegacyText(string text)
    {
        if(text.Count(c=>c is '\u00d0' or '\u00d1')<4||text.Any(c=>c is >= '\u0400' and <= '\u04ff'))return text;
        try {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var western=Encoding.GetEncoding(1252,EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback);
            var utf8=new UTF8Encoding(false,true);var repaired=utf8.GetString(western.GetBytes(text));
            return repaired.Count(c=>c is >= '\u0400' and <= '\u04ff')>=4&&western.GetString(utf8.GetBytes(repaired))==text?repaired:text;
        }catch(Exception e) when(e is EncoderFallbackException or DecoderFallbackException){return text;}
    }
    public static void Save(ChatConversation conversation)
    {
        Directory.CreateDirectory(AssistantPreferences.DataDirectory); var list = Read(); list.RemoveAll(c => c.Id == conversation.Id); conversation.Updated = DateTime.UtcNow; list.Insert(0, conversation);
        File.WriteAllText(PathName + ".tmp", JsonSerializer.Serialize(list)); File.Move(PathName + ".tmp", PathName, true);
    }
}
