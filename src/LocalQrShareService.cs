// Original HoshiBar code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DIsland.Services;

namespace DIsland;

/// <summary>
/// Ephemeral local Wi-Fi bridge (Shelf ⇄ Phone) using TcpListener on a random free port (no admin rights, no cloud).
/// Streams files directly from/to disk without buffering whole files in memory and auto-expires after 5 minutes.
/// </summary>
public sealed class LocalQrShareService : IDisposable
{
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SingleDownloadGrace = TimeSpan.FromSeconds(20);

    private static readonly string InboxDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DIsland", "PhoneInbox");

    private readonly ShelfService shelf;
    private readonly Dispatcher ui = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer countdown = new() { Interval = TimeSpan.FromSeconds(1) };

    private TcpListener? listener;
    private CancellationTokenSource? sessionCancel;
    private DateTime expiresUtc;
    private DateTime? singleDownloadGraceUtc;
    private string? lastDownloadedFileName;
    private int activeDownloads;
    private byte[] tokenBytes = Array.Empty<byte>();
    private string token = "";
    private string[] sharedFiles = Array.Empty<string>();
    private bool autoCloseOnSingleDownload;
    private bool disposed;

    public LocalQrShareService(ShelfService shelf)
    {
        this.shelf = shelf;
        countdown.Tick += (_, _) => OnCountdownTick();
    }

    public bool IsRunning => listener is not null;
    public string? ShareUrl { get; private set; }
    public string StatusText { get; private set; } = "";
    public string ScopeTitle { get; private set; } = "";
    public TimeSpan Remaining => IsRunning ? Max(TimeSpan.Zero, expiresUtc - DateTime.UtcNow) : TimeSpan.Zero;

    public event Action? StateChanged;

    public bool Start(IEnumerable<string>? candidatePaths, bool singleItemSelection = false)
    {
        Stop(silent: true);
        if (disposed) return false;

        var ip = FindPrivateLanIPv4();
        if (ip is null)
        {
            StatusText = "Частная сеть Wi-Fi / LAN не найдена. Проверьте подключение ПК и телефона к одному роутеру.";
            StateChanged?.Invoke();
            return false;
        }

        var files = (candidatePaths ?? shelf.Items)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        sharedFiles = files;
        autoCloseOnSingleDownload = files.Length == 1 && (singleItemSelection || shelf.Items.Count(File.Exists) == 1);
        singleDownloadGraceUtc = null;
        lastDownloadedFileName = null;
        Interlocked.Exchange(ref activeDownloads, 0);

        token = CreateToken(16);
        tokenBytes = Encoding.ASCII.GetBytes(token);

        var tcp = new TcpListener(IPAddress.Any, 0);
        tcp.Start();
        listener = tcp;
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;

        ShareUrl = $"http://{ip}:{port}/s/{token}";
        ScopeTitle = files.Length switch
        {
            0 => "Приём файлов с телефона на Полку",
            1 => $"Файл: {Path.GetFileName(files[0])}",
            _ => $"Файлов для телефона: {files.Length}",
        };
        StatusText = "Наведите камеру телефона на QR-код (одна домашняя сеть Wi-Fi)";
        expiresUtc = DateTime.UtcNow + SessionLifetime;

        var cts = new CancellationTokenSource();
        sessionCancel = cts;
        countdown.Start();
        _ = AcceptLoopAsync(tcp, token, tokenBytes, files, cts.Token);
        StateChanged?.Invoke();
        return true;
    }

    public void Stop(string? reason = null, bool silent = false)
    {
        countdown.Stop();
        singleDownloadGraceUtc = null;
        var cts = sessionCancel;
        sessionCancel = null;
        cts?.Cancel();
        cts?.Dispose();

        var tcp = listener;
        listener = null;
        if (tcp is not null)
        {
            try { tcp.Stop(); } catch (SocketException) { }
        }

        ShareUrl = null;
        sharedFiles = Array.Empty<string>();
        if (!string.IsNullOrEmpty(reason)) StatusText = reason;
        if (!silent) StateChanged?.Invoke();
    }

    private void OnCountdownTick()
    {
        if (!IsRunning) { countdown.Stop(); return; }

        // Never cut off an actively streaming file transfer mid-flight
        if (Volatile.Read(ref activeDownloads) > 0)
        {
            if (expiresUtc - DateTime.UtcNow < TimeSpan.FromSeconds(30))
                expiresUtc = DateTime.UtcNow.AddSeconds(30);
            StateChanged?.Invoke();
            return;
        }

        if (autoCloseOnSingleDownload && singleDownloadGraceUtc is { } graceUtc && DateTime.UtcNow >= graceUtc)
        {
            var name = lastDownloadedFileName ?? "файл";
            Stop($"Файл «{name}» передан на телефон · доступ закрыт");
            return;
        }

        if (Remaining <= TimeSpan.Zero)
        {
            Stop("Доступ по QR автоматически закрыт (5 мин)");
            return;
        }
        StateChanged?.Invoke();
    }

    private async Task AcceptLoopAsync(TcpListener tcp, string sessionToken, byte[] sessionTokenBytes, string[] filesSnapshot, CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await tcp.AcceptTcpClientAsync(cancel);
            }
            catch (Exception)
            {
                break;
            }
            _ = Task.Run(() => HandleClientAsync(client, sessionToken, sessionTokenBytes, filesSnapshot, cancel), CancellationToken.None);
        }
    }

    private async Task HandleClientAsync(TcpClient client, string sessionToken, byte[] sessionTokenBytes, string[] filesSnapshot, CancellationToken cancel)
    {
        try
        {
            using (client)
            {
                client.NoDelay = true;
                client.SendBufferSize = 131072;
                client.ReceiveTimeout = 30000;
                client.SendTimeout = 120000;

                if (client.Client.RemoteEndPoint is not IPEndPoint remote || !IsPrivateOrLoopback(remote.Address))
                    return;

                using var stream = client.GetStream();
                var headerInfo = await ReadHttpHeadersAsync(stream, cancel);
                if (headerInfo is null) return;

                var (method, rawTarget, headers, leftover) = headerInfo.Value;
                var targetPath = ExtractRequestPath(rawTarget);
                var prefix = $"/s/{sessionToken}";

                if (!VerifyTokenPath(targetPath, sessionTokenBytes, out var subPath))
                {
                    await WriteSimpleResponseAsync(stream, 404, "text/plain; charset=utf-8", "Доступ закрыт или ссылка устарела.", cancel);
                    await GracefulCloseAsync(client, stream);
                    return;
                }

                if ((method == "GET" || method == "HEAD") && (subPath == "" || subPath == "/"))
                {
                    var html = BuildMobilePageHtml(prefix, filesSnapshot);
                    await WriteSimpleResponseAsync(stream, 200, "text/html; charset=utf-8", method == "HEAD" ? "" : html, cancel, Encoding.UTF8.GetByteCount(html));
                    await GracefulCloseAsync(client, stream);
                    _ = ui.InvokeAsync(() =>
                    {
                        if (IsRunning && singleDownloadGraceUtc is null)
                        {
                            StatusText = "Телефон подключён · выберите действие на экране смартфона";
                            StateChanged?.Invoke();
                        }
                    });
                    return;
                }

                if ((method == "GET" || method == "HEAD") && subPath.StartsWith("/dl/", StringComparison.Ordinal))
                {
                    var dlRest = subPath.Substring(4);
                    var nextSlash = dlRest.IndexOf('/');
                    var idxText = nextSlash < 0 ? dlRest : dlRest.Substring(0, nextSlash);
                    if (!int.TryParse(idxText, NumberStyles.None, CultureInfo.InvariantCulture, out var idx)
                        || idx < 0 || idx >= filesSnapshot.Length || !File.Exists(filesSnapshot[idx]))
                    {
                        await WriteSimpleResponseAsync(stream, 404, "text/plain; charset=utf-8", "Файл не найден.", cancel);
                        await GracefulCloseAsync(client, stream);
                        return;
                    }

                    Interlocked.Increment(ref activeDownloads);
                    _ = ui.InvokeAsync(() => { singleDownloadGraceUtc = null; });
                    var completedWhole = false;
                    var downloadedName = Path.GetFileName(filesSnapshot[idx]);
                    try
                    {
                        completedWhole = await StreamFileDownloadAsync(stream, filesSnapshot[idx], method == "HEAD", headers, cancel);
                        await GracefulCloseAsync(client, stream);
                    }
                    finally
                    {
                        var remActive = Interlocked.Decrement(ref activeDownloads);
                        if (completedWhole)
                        {
                            _ = ui.InvokeAsync(() =>
                            {
                                if (!IsRunning) return;
                                lastDownloadedFileName = downloadedName;
                                if (autoCloseOnSingleDownload)
                                {
                                    singleDownloadGraceUtc = DateTime.UtcNow + SingleDownloadGrace;
                                    StatusText = $"Передано на телефон: {downloadedName}";
                                }
                                else
                                {
                                    StatusText = $"Передано на телефон: {downloadedName}";
                                }
                                StateChanged?.Invoke();
                            });
                        }
                        else if (remActive == 0 && lastDownloadedFileName is not null && autoCloseOnSingleDownload)
                        {
                            _ = ui.InvokeAsync(() =>
                            {
                                if (IsRunning && singleDownloadGraceUtc is null)
                                    singleDownloadGraceUtc = DateTime.UtcNow + SingleDownloadGrace;
                            });
                        }
                    }
                    return;
                }

                if (method == "POST" && subPath == "/upload")
                {
                    await HandleMultipartUploadAsync(stream, headers, leftover, cancel);
                    await GracefulCloseAsync(client, stream);
                    return;
                }

                await WriteSimpleResponseAsync(stream, 404, "text/plain; charset=utf-8", "Not found", cancel);
                await GracefulCloseAsync(client, stream);
            }
        }
        catch (Exception)
        {
            // Disconnected client or cancelled session — handled per connection.
        }
    }

    private static string ExtractRequestPath(string rawTarget)
    {
        var noQuery = rawTarget.Split('?', 2)[0];
        if ((noQuery.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
             || noQuery.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            && Uri.TryCreate(noQuery, UriKind.Absolute, out var abs))
        {
            return abs.AbsolutePath;
        }
        return noQuery;
    }

    private static bool VerifyTokenPath(string path, byte[] expectedTokenBytes, out string subPath)
    {
        subPath = "";
        if (!path.StartsWith("/s/", StringComparison.Ordinal)) return false;
        var rest = path.Substring(3);
        var slash = rest.IndexOf('/');
        var candidate = slash < 0 ? rest : rest.Substring(0, slash);
        subPath = slash < 0 ? "" : rest.Substring(slash);

        var candidateBytes = Encoding.ASCII.GetBytes(candidate);
        return candidateBytes.Length == expectedTokenBytes.Length
               && CryptographicOperations.FixedTimeEquals(candidateBytes, expectedTokenBytes);
    }

    private static async Task<bool> StreamFileDownloadAsync(
        NetworkStream stream,
        string filePath,
        bool headOnly,
        Dictionary<string, string> reqHeaders,
        CancellationToken cancel)
    {
        var fileName = Path.GetFileName(filePath);
        FileStream fs;
        try
        {
            fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, useAsync: true);
        }
        catch (IOException)
        {
            await WriteSimpleResponseAsync(stream, 500, "text/plain; charset=utf-8", "Не удалось открыть файл на ПК.", cancel);
            return false;
        }

        using (fs)
        {
            var totalLen = fs.Length;
            var lastWriteUtc = File.GetLastWriteTimeUtc(filePath);
            var etag = $"\"{totalLen:x}-{lastWriteUtc.Ticks:x}\"";
            var lastModified = lastWriteUtc.ToString("R", CultureInfo.InvariantCulture);
            var mime = GuessMimeType(fileName);
            var asciiFallback = SanitizeAsciiFileName(fileName);
            var encodedUtf8 = Uri.EscapeDataString(fileName);

            long start = 0, end = Math.Max(0, totalLen - 1);
            var partial = false;
            if (totalLen > 0 && reqHeaders.TryGetValue("range", out var rangeHeader))
            {
                var rangeStatus = TryParseByteRange(rangeHeader, totalLen, out var rs, out var re);
                if (rangeStatus == RangeParseResult.Satisfiable)
                {
                    start = rs;
                    end = re;
                    partial = true;
                }
                else if (rangeStatus == RangeParseResult.Unsatisfiable)
                {
                    var hdr416 = string.Create(CultureInfo.InvariantCulture,
                        $"HTTP/1.1 416 Range Not Satisfiable\r\nContent-Range: bytes */{totalLen}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(hdr416), cancel);
                    return false;
                }
            }

            var sendLen = totalLen == 0 ? 0 : end - start + 1;
            var sb = new StringBuilder(512);
            sb.Append(partial ? "HTTP/1.1 206 Partial Content\r\n" : "HTTP/1.1 200 OK\r\n");
            sb.Append("Content-Type: ").Append(mime).Append("\r\n");
            sb.Append("Accept-Ranges: bytes\r\n");
            sb.Append("ETag: ").Append(etag).Append("\r\n");
            sb.Append("Last-Modified: ").Append(lastModified).Append("\r\n");
            if (partial) sb.Append(CultureInfo.InvariantCulture, $"Content-Range: bytes {start}-{end}/{totalLen}\r\n");
            sb.Append(CultureInfo.InvariantCulture, $"Content-Length: {sendLen}\r\n");
            sb.Append($"Content-Disposition: attachment; filename=\"{asciiFallback}\"; filename*=UTF-8''{encodedUtf8}\r\n");
            sb.Append("Cache-Control: private, max-age=300\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n");

            var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
            await stream.WriteAsync(headerBytes, cancel);
            if (headOnly) return false;
            if (sendLen <= 0) return totalLen == 0;

            if (start > 0) fs.Seek(start, SeekOrigin.Begin);
            var buf = new byte[65536];
            long remaining = sendLen;
            long sent = 0;
            while (remaining > 0)
            {
                var read = await fs.ReadAsync(buf.AsMemory(0, (int)Math.Min(buf.Length, remaining)), cancel);
                if (read <= 0) break;
                await stream.WriteAsync(buf.AsMemory(0, read), cancel);
                remaining -= read;
                sent += read;
            }
            await stream.FlushAsync(cancel);

            return start == 0 && sent >= totalLen;
        }
    }

    private static async Task GracefulCloseAsync(TcpClient client, NetworkStream stream)
    {
        try
        {
            await stream.FlushAsync();
            client.Client.Shutdown(SocketShutdown.Send);
            using var drainCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var sink = new byte[1024];
            while (await stream.ReadAsync(sink, drainCts.Token) > 0) { }
        }
        catch (Exception)
        {
            // Peer closed or drain window elapsed after FIN.
        }
    }

    private async Task HandleMultipartUploadAsync(NetworkStream stream, Dictionary<string, string> headers, byte[] initialBody, CancellationToken cancel)
    {
        if (!headers.TryGetValue("content-type", out var ct) || !TryExtractBoundary(ct, out var boundary))
        {
            await WriteSimpleResponseAsync(stream, 400, "application/json; charset=utf-8", "{\"ok\":false,\"error\":\"Неверный формат загрузки\"}", cancel);
            return;
        }
        if (!headers.TryGetValue("content-length", out var clStr) || !long.TryParse(clStr, NumberStyles.None, CultureInfo.InvariantCulture, out var contentLength)
            || contentLength <= 0 || contentLength > 512L * 1024 * 1024)
        {
            await WriteSimpleResponseAsync(stream, 413, "application/json; charset=utf-8", "{\"ok\":false,\"error\":\"Размер превышает лимит 512 МБ\"}", cancel);
            return;
        }

        Directory.CreateDirectory(InboxDirectory);
        var savedFiles = new List<string>();
        try
        {
            await StreamMultipartFilesToDiskAsync(stream, initialBody, contentLength, boundary, savedFiles, cancel);
        }
        catch (Exception)
        {
            await WriteSimpleResponseAsync(stream, 500, "application/json; charset=utf-8", "{\"ok\":false,\"error\":\"Сбой передачи файла\"}", cancel);
            return;
        }

        if (savedFiles.Count > 0)
        {
            _ = ui.InvokeAsync(() =>
            {
                shelf.Add(savedFiles);
                if (IsRunning)
                {
                    var label = savedFiles.Count == 1 ? Path.GetFileName(savedFiles[0]) : $"{savedFiles.Count} файл(ов)";
                    StatusText = $"Принято на Полку: {label}";
                    StateChanged?.Invoke();
                }
            });
        }

        await WriteSimpleResponseAsync(stream, 200, "application/json; charset=utf-8", $"{{\"ok\":true,\"count\":{savedFiles.Count}}}", cancel);
    }

    private static async Task StreamMultipartFilesToDiskAsync(
        NetworkStream network,
        byte[] initial,
        long contentLength,
        string boundary,
        List<string> savedPaths,
        CancellationToken cancel)
    {
        var dashBoundary = Encoding.ASCII.GetBytes("--" + boundary);
        var crlfBoundary = Encoding.ASCII.GetBytes("\r\n--" + boundary);
        var headerEndSeq = "\r\n\r\n"u8.ToArray();

        var window = new byte[131072];
        var winLen = Math.Min(initial.Length, window.Length);
        Array.Copy(initial, 0, window, 0, winLen);
        long bytesRemainingFromSocket = contentLength - winLen;

        async Task EnsureBufferedAsync(int minBytes)
        {
            while (winLen < minBytes && bytesRemainingFromSocket > 0)
            {
                var toRead = (int)Math.Min(window.Length - winLen, bytesRemainingFromSocket);
                if (toRead <= 0) break;
                var n = await network.ReadAsync(window.AsMemory(winLen, toRead), cancel);
                if (n <= 0) { bytesRemainingFromSocket = 0; break; }
                winLen += n;
                bytesRemainingFromSocket -= n;
            }
        }

        void Consume(int count)
        {
            if (count >= winLen) { winLen = 0; return; }
            Buffer.BlockCopy(window, count, window, 0, winLen - count);
            winLen -= count;
        }

        await EnsureBufferedAsync(dashBoundary.Length + 4);
        var firstIdx = IndexOf(window, winLen, dashBoundary);
        if (firstIdx < 0) return;
        Consume(firstIdx + dashBoundary.Length);

        while (true)
        {
            await EnsureBufferedAsync(4);
            if (winLen >= 2 && window[0] == '-' && window[1] == '-') break; // final --boundary--
            if (winLen >= 2 && window[0] == '\r' && window[1] == '\n') Consume(2);

            // Read part headers
            await EnsureBufferedAsync(4096);
            var hdrEnd = IndexOf(window, winLen, headerEndSeq);
            if (hdrEnd < 0) break;

            var partHeaders = Encoding.UTF8.GetString(window, 0, hdrEnd);
            Consume(hdrEnd + 4);

            var rawFileName = ExtractMultipartFileName(partHeaders);
            FileStream? outStream = null;
            string? tempFile = null;
            string? finalFile = null;

            if (!string.IsNullOrWhiteSpace(rawFileName))
            {
                finalFile = ReserveUniqueInboxPath(rawFileName);
                tempFile = finalFile + ".part";
                outStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true);
            }

            try
            {
                while (true)
                {
                    await EnsureBufferedAsync(crlfBoundary.Length + 32768);
                    var bIdx = IndexOf(window, winLen, crlfBoundary);
                    if (bIdx >= 0)
                    {
                        if (outStream is not null && bIdx > 0)
                            await outStream.WriteAsync(window.AsMemory(0, bIdx), cancel);
                        Consume(bIdx + crlfBoundary.Length);
                        break;
                    }

                    var safeFlush = winLen - crlfBoundary.Length;
                    if (safeFlush <= 0)
                    {
                        if (bytesRemainingFromSocket <= 0) break;
                        continue;
                    }
                    if (outStream is not null)
                        await outStream.WriteAsync(window.AsMemory(0, safeFlush), cancel);
                    Consume(safeFlush);
                }

                if (outStream is not null)
                {
                    await outStream.FlushAsync(cancel);
                    outStream.Dispose();
                    outStream = null;
                    File.Move(tempFile!, finalFile!, overwrite: true);
                    savedPaths.Add(finalFile!);
                }
            }
            finally
            {
                outStream?.Dispose();
                if (tempFile is not null && File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch (IOException) { }
                }
            }
        }
    }

    private static async Task<(string Method, string Target, Dictionary<string, string> Headers, byte[] Leftover)?> ReadHttpHeadersAsync(
        NetworkStream stream, CancellationToken cancel)
    {
        var buffer = new byte[16384];
        var total = 0;
        var marker = "\r\n\r\n"u8.ToArray();

        while (total < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancel);
            if (n <= 0) return null;
            total += n;
            var idx = IndexOf(buffer, total, marker);
            if (idx >= 0)
            {
                var headerText = Encoding.ASCII.GetString(buffer, 0, idx);
                var lines = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length == 0) return null;
                var reqParts = lines[0].Split(' ', 3);
                if (reqParts.Length < 2) return null;

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 1; i < lines.Length; i++)
                {
                    var colon = lines[i].IndexOf(':');
                    if (colon > 0) headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
                }

                var bodyStart = idx + 4;
                var leftover = new byte[total - bodyStart];
                if (leftover.Length > 0) Buffer.BlockCopy(buffer, bodyStart, leftover, 0, leftover.Length);
                return (reqParts[0].ToUpperInvariant(), reqParts[1], headers, leftover);
            }
        }
        return null;
    }

    private static async Task WriteSimpleResponseAsync(NetworkStream stream, int code, string contentType, string body, CancellationToken cancel, int? explicitLength = null)
    {
        var payload = Encoding.UTF8.GetBytes(body);
        var len = explicitLength ?? payload.Length;
        var reason = code switch { 200 => "OK", 400 => "Bad Request", 413 => "Payload Too Large", 500 => "Internal Server Error", _ => "Not Found" };
        var header = $"HTTP/1.1 {code} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {len}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), cancel);
        if (payload.Length > 0) await stream.WriteAsync(payload, cancel);
        await stream.FlushAsync(cancel);
    }

    private static string BuildMobilePageHtml(string prefix, IReadOnlyList<string> files)
    {
        var sb = new StringBuilder();
        sb.Append("""
<!DOCTYPE html>
<html lang="ru">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1,maximum-scale=1">
<title>HoshiBar · Полка ⇄ Телефон</title>
<style>
*{box-sizing:border-box}
body{margin:0;padding:18px;background:#18141F;color:#F4EDF6;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;line-height:1.45}
.wrap{max-width:440px;margin:0 auto}
.hero{background:#25202D;border:1px solid #4A3C55;border-radius:20px;padding:18px;margin-bottom:14px}
.halo{display:inline-block;width:36px;height:8px;border:2px solid #EAB0CF;border-radius:50%;box-shadow:0 0 0 1px #83CFF3 inset;margin-bottom:6px}
h1{margin:0;font-size:20px;font-weight:600}
.sub{color:#A59CAC;font-size:13px;margin-top:4px}
.card{background:#25202D;border:1px solid #4A3C55;border-radius:18px;padding:16px;margin-bottom:14px}
.sec{font-size:13px;font-weight:600;color:#83CFF3;margin:0 0 10px 0;text-transform:uppercase;letter-spacing:.04em}
.file{display:flex;align-items:center;justify-content:space-between;gap:10px;background:#332B3D;border-radius:12px;padding:10px 12px;margin-bottom:8px}
.fname{font-size:14px;font-weight:500;word-break:break-word}
.fsize{font-size:12px;color:#A59CAC;margin-top:2px}
.btn{display:inline-flex;align-items:center;justify-content:center;width:100%;padding:12px 16px;border-radius:12px;border:none;font-size:15px;font-weight:600;cursor:pointer;text-decoration:none;transition:opacity .15s}
.btn-pink{background:#EAB0CF;color:#18141F}
.btn-sky{background:#83CFF3;color:#18141F}
.btn-sm{width:auto;padding:8px 14px;font-size:13px;flex-shrink:0}
input[type=file]{display:block;width:100%;margin-bottom:10px;padding:10px;background:#332B3D;border:1px dashed #4A3C55;border-radius:12px;color:#F4EDF6;font-size:13px}
.bar{height:6px;background:#332B3D;border-radius:3px;overflow:hidden;margin-top:10px;display:none}
.fill{height:100%;width:0%;background:#83CFF3;transition:width .1s}
.msg{font-size:13px;color:#EAB0CF;margin-top:8px;min-height:18px}
.empty{font-size:13px;color:#A59CAC}
</style>
</head>
<body>
<div class="wrap">
  <div class="hero">
    <div class="halo"></div>
    <h1>HoshiBar</h1>
    <div class="sub">Локальный мост Полка ⇄ Телефон · без облаков</div>
  </div>
  <div class="card">
    <div class="sec">1 · Скачать с ПК</div>
""");

        if (files.Count == 0)
        {
            sb.Append("<div class=\"empty\">На полке ПК сейчас нет выбранных файлов для скачивания.</div>");
        }
        else if (files.Count == 1)
        {
            var info = new FileInfo(files[0]);
            var htmlName = WebUtility.HtmlEncode(info.Name);
            var urlName = Uri.EscapeDataString(info.Name);
            sb.Append($"<div class=\"file\"><div><div class=\"fname\">{htmlName}</div><div class=\"fsize\">{FormatSize(info.Length)}</div></div></div>");
            sb.Append($"<a class=\"btn btn-pink\" href=\"{prefix}/dl/0/{urlName}\" download=\"{htmlName}\">Скачать файл с ПК</a>");
        }
        else
        {
            for (var i = 0; i < files.Count; i++)
            {
                var info = new FileInfo(files[i]);
                var htmlName = WebUtility.HtmlEncode(info.Name);
                var urlName = Uri.EscapeDataString(info.Name);
                sb.Append($"<div class=\"file\"><div><div class=\"fname\">{htmlName}</div><div class=\"fsize\">{FormatSize(info.Length)}</div></div>");
                sb.Append($"<a class=\"btn btn-pink btn-sm\" href=\"{prefix}/dl/{i}/{urlName}\" download=\"{htmlName}\">Скачать</a></div>");
            }
        }

        sb.Append($$"""
  </div>
  <div class="card">
    <div class="sec">2 · Отправить на Полку ПК</div>
    <input id="pick" type="file" multiple>
    <button id="sendBtn" class="btn btn-sky" type="button">Отправить фото / файл на ПК</button>
    <div id="bar" class="bar"><div id="fill" class="fill"></div></div>
    <div id="msg" class="msg"></div>
  </div>
</div>
<script>
const pick=document.getElementById('pick'),btn=document.getElementById('sendBtn'),bar=document.getElementById('bar'),fill=document.getElementById('fill'),msg=document.getElementById('msg');
btn.onclick=()=>{
  if(!pick.files||!pick.files.length){msg.textContent='Сначала выберите фото или файл.';return;}
  const fd=new FormData();
  for(const f of pick.files)fd.append('file',f,f.name);
  btn.disabled=true;bar.style.display='block';fill.style.width='0%';msg.textContent='Передача по Wi-Fi…';
  const xhr=new XMLHttpRequest();
  xhr.open('POST','{{prefix}}/upload');
  xhr.upload.onprogress=e=>{if(e.lengthComputable)fill.style.width=Math.round(e.loaded*100/e.total)+'%';};
  xhr.onload=()=>{
    btn.disabled=false;
    if(xhr.status===200){fill.style.width='100%';msg.textContent='Готово! Добавлено на Полку HoshiBar.';pick.value='';}
    else{msg.textContent='Не удалось отправить ('+xhr.status+').';}
  };
  xhr.onerror=()=>{btn.disabled=false;msg.textContent='Соединение прервано.';};
  xhr.send(fd);
};
</script>
</body>
</html>
""");
        return sb.ToString();
    }

    private static IPAddress? FindPrivateLanIPv4()
    {
        var candidates = new List<(IPAddress Ip, int Score)>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

            var desc = (nic.Name + " " + nic.Description).ToLowerInvariant();
            var isVirtual = desc.Contains("virtual") || desc.Contains("vethernet") || desc.Contains("wsl")
                            || desc.Contains("hyper-v") || desc.Contains("vmware") || desc.Contains("vbox");

            var props = nic.GetIPProperties();
            var hasGateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.Any.Equals(g.Address));

            foreach (var uni in props.UnicastAddresses)
            {
                if (uni.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (!IsPrivateIPv4(uni.Address)) continue;

                var score = 10;
                if (hasGateway) score += 40;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) score += 25;
                else if (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet) score += 15;
                if (isVirtual) score -= 35;
                candidates.Add((uni.Address, score));
            }
        }
        return candidates.OrderByDescending(c => c.Score).Select(c => c.Ip).FirstOrDefault();
    }

    private static bool IsPrivateOrLoopback(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return address.AddressFamily == AddressFamily.InterNetwork && IsPrivateIPv4(address);
    }

    private static bool IsPrivateIPv4(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return b[0] == 10
               || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
               || (b[0] == 192 && b[1] == 168);
    }

    private static string CreateToken(int length)
    {
        const string alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = RandomNumberGenerator.GetBytes(length);
        var chars = new char[length];
        for (var i = 0; i < length; i++) chars[i] = alphabet[bytes[i] % alphabet.Length];
        return new string(chars);
    }

    private static bool TryExtractBoundary(string contentType, out string boundary)
    {
        boundary = "";
        foreach (var part in contentType.Split(';'))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("boundary=", StringComparison.OrdinalIgnoreCase))
            {
                boundary = trimmed.Substring(9).Trim('"');
                return boundary.Length is > 0 and < 120;
            }
        }
        return false;
    }

    private static string? ExtractMultipartFileName(string partHeaders)
    {
        const string key = "filename=\"";
        var idx = partHeaders.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        idx += key.Length;
        var end = partHeaders.IndexOf('"', idx);
        return end > idx ? partHeaders[idx..end] : null;
    }

    private static string ReserveUniqueInboxPath(string rawName)
    {
        var clean = Path.GetFileName(rawName.Replace('/', '\\')).Trim();
        foreach (var bad in Path.GetInvalidFileNameChars()) clean = clean.Replace(bad, '_');
        if (string.IsNullOrWhiteSpace(clean) || clean is "." or "..") clean = $"phone-{DateTime.Now:yyyyMMdd-HHmmss}.bin";
        if (clean.Length > 140) clean = clean[^140..];

        var stem = Path.GetFileNameWithoutExtension(clean);
        var ext = Path.GetExtension(clean);
        var candidate = Path.Combine(InboxDirectory, clean);
        var suffix = 2;
        while (File.Exists(candidate) || File.Exists(candidate + ".part"))
        {
            candidate = Path.Combine(InboxDirectory, $"{stem} ({suffix++}){ext}");
        }
        return candidate;
    }

    private static string SanitizeAsciiFileName(string name)
    {
        var ext = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);
        var cleanExt = new StringBuilder(ext.Length);
        foreach (var ch in ext)
        {
            if (ch == '.' || ch is >= '0' and <= '9' || ch is >= 'A' and <= 'Z' || ch is >= 'a' and <= 'z' || ch is '-' or '_')
                cleanExt.Append(ch);
        }

        var cleanStem = new StringBuilder(stem.Length);
        var hasAlnum = false;
        foreach (var ch in stem)
        {
            if (ch is >= ' ' and <= '~' && ch is not '"' and not '\\' and not '/' and not ';')
            {
                cleanStem.Append(ch);
                if (char.IsLetterOrDigit(ch)) hasAlnum = true;
            }
            else
            {
                cleanStem.Append('_');
            }
        }

        var finalStem = hasAlnum ? cleanStem.ToString().Trim() : "download";
        if (string.IsNullOrEmpty(finalStem)) finalStem = "download";
        return finalStem + cleanExt;
    }

    private static string GuessMimeType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".bmp" => "image/bmp",
        ".ico" => "image/x-icon",
        ".pdf" => "application/pdf",
        ".txt" or ".md" or ".log" or ".csv" or ".ini" or ".cfg" => "text/plain; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".xml" => "application/xml; charset=utf-8",
        ".zip" => "application/zip",
        ".7z" => "application/x-7z-compressed",
        ".rar" => "application/vnd.rar",
        ".gz" or ".tgz" => "application/gzip",
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".ogg" => "audio/ogg",
        ".flac" => "audio/flac",
        ".m4a" => "audio/mp4",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".mkv" => "video/x-matroska",
        ".mov" => "video/quicktime",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xls" => "application/vnd.ms-excel",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".ppt" => "application/vnd.ms-powerpoint",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ".apk" => "application/vnd.android.package-archive",
        _ => "application/octet-stream",
    };

    private enum RangeParseResult { InvalidSyntax, Unsatisfiable, Satisfiable }

    private static RangeParseResult TryParseByteRange(string header, long totalLen, out long start, out long end)
    {
        start = 0; end = totalLen - 1;
        if (!header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return RangeParseResult.InvalidSyntax;
        var spec = header.Substring(6).Split(',')[0].Trim();
        var dash = spec.IndexOf('-');
        if (dash < 0) return RangeParseResult.InvalidSyntax;
        var left = spec[..dash].Trim();
        var right = spec[(dash + 1)..].Trim();
        if (left.Length == 0)
        {
            if (!long.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var tail) || tail <= 0)
                return RangeParseResult.InvalidSyntax;
            start = Math.Max(0, totalLen - tail);
            return RangeParseResult.Satisfiable;
        }
        if (!long.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out start) || start < 0)
            return RangeParseResult.InvalidSyntax;
        if (start >= totalLen)
            return RangeParseResult.Unsatisfiable;
        if (right.Length > 0)
        {
            if (!long.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out end) || end < start)
                return RangeParseResult.InvalidSyntax;
            end = Math.Min(end, totalLen - 1);
        }
        return RangeParseResult.Satisfiable;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} Б",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} КБ",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):0.#} МБ",
        _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):0.##} ГБ",
    };

    private static int IndexOf(byte[] haystack, int haystackLen, byte[] needle)
    {
        var limit = haystackLen - needle.Length;
        for (var i = 0; i <= limit; i++)
        {
            if (haystack[i] != needle[0]) continue;
            var match = true;
            for (var j = 1; j < needle.Length; j++)
                if (haystack[i + j] != needle[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    public void Dispose()
    {
        disposed = true;
        Stop(silent: true);
    }
}

/// <summary>In-island QR card on the Shelf page using dynamic HoshinoPalette resources.</summary>
public sealed class ShelfQrPanel : Border
{
    private readonly LocalQrShareService service;
    private readonly Border qrSlot = new() { Width = 128, Height = 128, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock scopeLabel = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock timerLabel = new() { FontSize = 12, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock urlLabel = new() { FontSize = 10.5, Opacity = 0.55, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) };
    private readonly TextBlock statusLabel = new() { FontSize = 11.5, Opacity = 0.85, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8) };
    private string? renderedUrl;
    private bool lightPad;

    public event Action? LayoutChanged;

    public bool IsOpen => Visibility == Visibility.Visible;

    public ShelfQrPanel(LocalQrShareService service)
    {
        this.service = service;
        Visibility = Visibility.Collapsed;
        CornerRadius = new CornerRadius(14);
        Padding = new Thickness(12);
        Margin = new Thickness(0, 0, 0, 8);
        BorderThickness = new Thickness(1);
        SetResourceReference(BackgroundProperty, "HoshinoSurface");
        SetResourceReference(BorderBrushProperty, "HoshinoBorder");
        MouseLeftButtonUp += (_, e) => e.Handled = true;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        Child = grid;

        grid.Children.Add(qrSlot);

        var right = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(right, 1);

        var topRow = new DockPanel();
        var timerBadge = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(7, 2, 7, 2),
            Margin = new Thickness(8, 0, 0, 0),
        };
        timerBadge.SetResourceReference(BackgroundProperty, "HoshinoControl");
        Typography.SetNumeralAlignment(timerLabel, FontNumeralAlignment.Tabular);
        timerLabel.SetResourceReference(TextBlock.ForegroundProperty, "HoshinoAccent");
        timerBadge.Child = timerLabel;
        DockPanel.SetDock(timerBadge, Dock.Right);
        topRow.Children.Add(timerBadge);
        topRow.Children.Add(scopeLabel);
        right.Children.Add(topRow);

        right.Children.Add(urlLabel);
        right.Children.Add(statusLabel);

        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        var closeBtn = new Button
        {
            Style = (Style)Application.Current.FindResource("ChipButton"),
            Content = "Закрыть доступ",
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 6, 0),
        };
        closeBtn.Click += (_, _) =>
        {
            if (service.IsRunning) service.Stop("Доступ по QR закрыт");
            Visibility = Visibility.Collapsed;
            renderedUrl = null;
            LayoutChanged?.Invoke();
        };

        var contrastBtn = new Button
        {
            Style = (Style)Application.Current.FindResource("ChipButton"),
            Content = "Фон QR",
            ToolTip = "Переключить тёмный / светлый фон QR-кода",
            Cursor = Cursors.Hand,
        };
        contrastBtn.Click += (_, _) =>
        {
            lightPad = !lightPad;
            renderedUrl = null;
            Refresh();
        };

        actions.Children.Add(closeBtn);
        actions.Children.Add(contrastBtn);
        right.Children.Add(actions);

        grid.Children.Add(right);
        service.StateChanged += Refresh;
    }

    public void Open(IEnumerable<string>? paths = null, bool singleItem = false)
    {
        Visibility = Visibility.Visible;
        service.Start(paths, singleItem);
        Refresh();
        LayoutChanged?.Invoke();
    }

    private void Refresh()
    {
        if (service.IsRunning && service.ShareUrl is { } url)
        {
            Visibility = Visibility.Visible;
            if (renderedUrl != url)
            {
                renderedUrl = url;
                qrSlot.Child = QrCodeVector.CreateVisual(url, 128, lightPad);
            }
            qrSlot.Visibility = Visibility.Visible;
            scopeLabel.Text = service.ScopeTitle;
            var rem = service.Remaining;
            timerLabel.Text = $"{(int)rem.TotalMinutes:00}:{rem.Seconds:00}";
            urlLabel.Text = url;
            statusLabel.Text = service.StatusText;
        }
        else if (Visibility == Visibility.Visible)
        {
            // Keep brief summary visible if just auto-closed or failed, with option to dismiss
            qrSlot.Visibility = Visibility.Collapsed;
            scopeLabel.Text = "Полка ⇄ Телефон";
            timerLabel.Text = "00:00";
            urlLabel.Text = "";
            statusLabel.Text = string.IsNullOrEmpty(service.StatusText) ? "Доступ закрыт" : service.StatusText;
            renderedUrl = null;
        }
        LayoutChanged?.Invoke();
    }
}
