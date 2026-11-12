// Original Hoshino Island code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DIsland.Services;

public sealed record LyricLine(double Seconds, string Text);
public sealed record LyricsResult(IReadOnlyList<LyricLine> Lines, string? Plain, string Status);

public static class LrcParser
{
    public static IReadOnlyList<LyricLine> Parse(string? lrc)
    {
        var lines = new List<LyricLine>();
        if (string.IsNullOrWhiteSpace(lrc)) return lines;
        var offsetMatch = Regex.Match(lrc, @"\[offset:([+-]?\d+)\]", RegexOptions.IgnoreCase);
        var offset = offsetMatch.Success && double.TryParse(offsetMatch.Groups[1].Value, CultureInfo.InvariantCulture, out var ms) ? ms / 1000 : 0;
        foreach (var raw in lrc.Split('\n'))
        {
            var stamps = Regex.Matches(raw, @"\[(\d{1,3}):([0-5]\d)(?:[.:](\d{1,3}))?\]");
            if (stamps.Count == 0) continue;
            var text = raw[(stamps[^1].Index + stamps[^1].Length)..].Trim();
            foreach (Match stamp in stamps)
            {
                var fraction = stamp.Groups[3].Value;
                var time = int.Parse(stamp.Groups[1].Value) * 60 + int.Parse(stamp.Groups[2].Value) + (fraction.Length == 0 ? 0 : double.Parse("0." + fraction, CultureInfo.InvariantCulture));
                lines.Add(new LyricLine(Math.Max(0, time + offset), text));
            }
        }
        return lines.OrderBy(l => l.Seconds).ToArray();
    }

    public static int Current(IReadOnlyList<LyricLine> lines, double seconds)
    {
        int lo = 0, hi = lines.Count - 1, found = -1;
        while (lo <= hi) { var mid = lo + (hi - lo) / 2; if (lines[mid].Seconds <= seconds) { found = mid; lo = mid + 1; } else hi = mid - 1; }
        return found;
    }
}

public sealed class LyricsService : IDisposable
{
    private readonly HttpClient http;
    private readonly Dictionary<string, LyricsResult> cache = new();
    private readonly Dictionary<string, DateTime> expiry = new();
    public event Action<string>? Searching;
    public LyricsService(HttpClient? client = null)
    {
        http = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GlassChatPrototype/0.5 (+https://github.com/H7gamer/d-island; unofficial-local-modification)");
    }
    public async Task<LyricsResult> Get(string title, string artist, string album, double duration, CancellationToken cancel)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist)) return new([], null, "Плеер не передал название или исполнителя");
        var key = string.Join("\n", title, artist, album, Math.Round(duration));
        if (cache.TryGetValue(key, out var cached) && (!expiry.TryGetValue(key, out var until) || until > DateTime.UtcNow)) return cached;
        LyricsResult? plain = null; int failures = 0;
        var attempts = new Func<Task<LyricsResult>>[] {
            () => Lrclib(title, artist, album, duration, cancel),
            () => LyricsPlus(title, artist, duration, cancel),
            () => Red(title, artist, duration, cancel),
            () => Better(title, artist, album, duration, false, cancel),
            () => Better(title, artist, album, duration, true, cancel)
        };
        var names = new[] { "LRCLIB", "LyricsPlus", "lrc.red", "Better Lyrics", "Kugou" };
        for (var index = 0; index < attempts.Length; index++)
        {
            cancel.ThrowIfCancellationRequested();
            Searching?.Invoke("Ищем синхру · " + names[index] + " · " + (index + 1) + "/5");
            try {
                var result = await attempts[index]();
                if (result.Lines.Count > 0) return Store(key, result);
                if (!string.IsNullOrWhiteSpace(result.Plain)) plain ??= result;
            }
            catch (OperationCanceledException) when (!cancel.IsCancellationRequested) { failures++; }
            catch (Exception e) when (e is HttpRequestException or JsonException or System.Xml.XmlException or FormatException) { failures++; }
        }
        return Store(key, plain ?? new([], null, failures > 0 ? "Синхра не найдена; часть источников недоступна" : "Синхронный текст не найден в 5 источниках"));
    }
    private async Task<LyricsResult> Lrclib(string title, string artist, string album, double duration, CancellationToken cancel)
    {
        var url = "https://lrclib.net/api/get?track_name=" + Uri.EscapeDataString(title) + "&artist_name=" + Uri.EscapeDataString(artist);
        if (!string.IsNullOrWhiteSpace(album)) url += "&album_name=" + Uri.EscapeDataString(album);
        if (duration >= 1 && duration <= 3600) url += "&duration=" + Math.Round(duration).ToString(CultureInfo.InvariantCulture);
        using var response = await http.GetAsync(url, cancel);
        if (response.StatusCode == HttpStatusCode.NotFound) return new([], null, "Текст не найден в LRCLIB");
        if ((int)response.StatusCode == 429) return new([], null, "Лимит LRCLIB. Попробуйте позже");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel)); var root = json.RootElement;
        string? Value(string field) => root.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        if (root.TryGetProperty("instrumental", out var instrumental) && instrumental.ValueKind == JsonValueKind.True) return new([], null, "Инструментальный трек — слов нет");
        var lines = LrcParser.Parse(Value("syncedLyrics")); var plain = Value("plainLyrics");
        return new(lines, plain, lines.Count > 0 ? "Синхронный текст · LRCLIB" : string.IsNullOrWhiteSpace(plain) ? "Текст не найден" : "Текст без таймингов · LRCLIB");
    }

    private static string Q(string s) => Uri.EscapeDataString(s);
    private async Task<JsonDocument> Json(string url, CancellationToken cancel)
    {
        using var response = await http.GetAsync(url, cancel); response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
    }
    private async Task<LyricsResult> LyricsPlus(string title, string artist, double duration, CancellationToken cancel)
    {
        using var json = await Json("https://lyricsplus.prjktla.my.id/v2/lyrics/get?title=" + Q(title) + "&artist=" + Q(artist) + "&duration=" + duration.ToString(CultureInfo.InvariantCulture), cancel);
        var root = json.RootElement;
        if (!root.TryGetProperty("type", out var type) || !new[]{"Word","Line","syllable"}.Contains(type.GetString(), StringComparer.OrdinalIgnoreCase)) return new([], null, "LyricsPlus: без таймингов");
        var lines = new List<LyricLine>();
        if (root.TryGetProperty("lyrics", out var lyrics) && lyrics.ValueKind == JsonValueKind.Array)
            foreach (var line in lyrics.EnumerateArray())
                if (line.TryGetProperty("time", out var time) && time.TryGetDouble(out var ms) && double.IsFinite(ms) && ms >= 0 && line.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    lines.Add(new(ms / 1000, text.GetString()!));
        return new(lines.OrderBy(l=>l.Seconds).ToArray(), null, "Синхронный текст · LyricsPlus");
    }
    private static string Normalize(string value) => Regex.Replace(value.ToLowerInvariant(), @"[^\p{L}\p{N}]", "");
    private async Task<LyricsResult> Red(string title, string artist, double duration, CancellationToken cancel)
    {
        using var json = await Json("https://lrc.red/search.json?q=" + Q(title + " " + artist), cancel);
        if (json.RootElement.TryGetProperty("hits", out var hits)) foreach (var hit in hits.EnumerateArray())
        {
            if (!hit.TryGetProperty("title", out var t) || Normalize(t.GetString() ?? "") != Normalize(title) || !hit.TryGetProperty("artist", out var a) || Normalize(a.GetString() ?? "") != Normalize(artist)) continue;
            if (duration > 0 && hit.TryGetProperty("duration", out var d) && d.TryGetDouble(out var seconds) && Math.Abs(seconds-duration)>3) continue;
            var isrc = hit.GetProperty("isrc").GetString() ?? "";
            if (!Regex.IsMatch(isrc, "^[A-Za-z0-9]{12}$")) continue;
            var xml = await http.GetStringAsync("https://lrc.red/s/" + isrc + ".ttml", cancel);
            return new(Ttml(xml), null, "Синхронный текст · lrc.red");
        }
        return new([], null, "lrc.red: не найдено");
    }
    private async Task<LyricsResult> Better(string title, string artist, string album, double duration, bool kugou, CancellationToken cancel)
    {
        using var json = await Json("https://api.betterlyrics.org/" + (kugou ? "kugou/" : "") + "getLyrics?s=" + Q(title) + "&a=" + Q(artist) + "&al=" + Q(album) + "&d=" + Math.Round(duration).ToString(CultureInfo.InvariantCulture), cancel);
        var field = kugou ? "lyrics" : "ttml";
        var raw = json.RootElement.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return new(kugou ? LrcParser.Parse(raw) : Ttml(raw), null, "Синхронный текст · " + (kugou ? "Kugou через Better Lyrics" : "Better Lyrics"));
    }
    public static IReadOnlyList<LyricLine> Ttml(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return [];
        using var reader = System.Xml.XmlReader.Create(new System.IO.StringReader(xml), new System.Xml.XmlReaderSettings { DtdProcessing=System.Xml.DtdProcessing.Prohibit, XmlResolver=null, MaxCharactersInDocument=2_000_000 });
        var doc = System.Xml.Linq.XDocument.Load(reader); var lines = new List<LyricLine>();
        foreach (var p in doc.Descendants().Where(e=>e.Name.LocalName=="p"))
        {
            var begin = p.Attribute("begin")?.Value;
            double time;
            if (begin != null && begin.EndsWith("s") && double.TryParse(begin[..^1], CultureInfo.InvariantCulture, out var secs)) time=secs;
            else if (TimeSpan.TryParse(begin, CultureInfo.InvariantCulture, out var span)) time=span.TotalSeconds;
            else continue;
            if (!double.IsFinite(time) || time<0) continue;
            lines.Add(new(time,p.Value.Trim()));
        }
        return lines.OrderBy(l=>l.Seconds).ToArray();
    }
    private LyricsResult Store(string key, LyricsResult result) { if (cache.Count >= 50) { cache.Clear(); expiry.Clear(); } cache[key] = result; expiry[key]=DateTime.UtcNow.AddMinutes(result.Lines.Count>0 ? 60 : 2); return result; }
    public void Dispose() => http.Dispose();
}
