// Original Hoshino Island code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.IO;
using System.Text.Json;
namespace DIsland;

/// <summary>Shuffle bag: every authored line once per cycle, with no repeated cycle boundary.</summary>
public sealed class PhraseDeck
{
    private readonly string[] phrases;
    private int[] order=Array.Empty<int>();
    private int cursor;
    private string? previous;
    public int Count=>phrases.Length;
    public PhraseDeck()
    {
        var path=Path.Combine(AppContext.BaseDirectory,"Assets","Hoshino","phrases.json");
        phrases=(JsonSerializer.Deserialize<string[]>(File.ReadAllText(path))??Array.Empty<string>()).Where(p=>!string.IsNullOrWhiteSpace(p)).Distinct().ToArray();
        if(phrases.Length==0)phrases=new[]{"Я всегда тебе помогу"};
    }
    public string Next()
    {
        if(cursor>=order.Length){order=Enumerable.Range(0,phrases.Length).ToArray();Random.Shared.Shuffle(order);cursor=0;if(order.Length>1&&phrases[order[0]]==previous)(order[0],order[1])=(order[1],order[0]);}
        return previous=phrases[order[cursor++]];
    }
}
