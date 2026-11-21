// SPDX-License-Identifier: LGPL-3.0-only
// Protocol based on src/edge_tts/communicate.py from the edge-tts project (https://github.com/rany2/edge-tts).
using System.Globalization;
using System.Net;
using System.Net.WebSockets;
using System.Security;
using System.Text;
using System.Text.Json;

namespace SnapVox.EdgeTts;

/// <summary>A spoken word: when it starts, how long it lasts and its text.</summary>
public sealed record EdgeWordBoundary(TimeSpan Start, TimeSpan Duration, string Text);

/// <summary>Client for the Microsoft Edge voice service ("Read aloud"). Returns MP3 + the timing of each word.</summary>
public static class EdgeTtsClient
{
    private const string ChromiumFullVersion = "143.0.3650.75";
    private const string SecMsGecVersion = "2-" + ChromiumFullVersion;
    private const string Endpoint = "wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1";

    /// <summary>Generates the audio (MP3 24 kHz 48 kbit/s) for a text of up to ~3000 characters.</summary>
    public static async Task<(byte[] Mp3, IReadOnlyList<EdgeWordBoundary> Words)> SynthesizeAsync(
        string text, string voice, CancellationToken ct)
    {
        try
        {
            return await RunAsync(text, voice, ct).ConfigureAwait(false);
        }
        catch (WebSocketException ex) when (ex.Message.Contains("403"))
        {
            // The PC clock may be off: sync with the server time and try once more.
            await AdjustClockSkewAsync(ct).ConfigureAwait(false);
            return await RunAsync(text, voice, ct).ConfigureAwait(false);
        }
    }

    private static async Task<(byte[], IReadOnlyList<EdgeWordBoundary>)> RunAsync(string text, string voice, CancellationToken ct)
    {
        string major = ChromiumFullVersion.Split('.')[0];
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Pragma", "no-cache");
        ws.Options.SetRequestHeader("Cache-Control", "no-cache");
        ws.Options.SetRequestHeader("Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
        ws.Options.SetRequestHeader("User-Agent",
            $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{major}.0.0.0 Safari/537.36 Edg/{major}.0.0.0");
        ws.Options.SetRequestHeader("Accept-Language", "en-US,en;q=0.9");
        ws.Options.SetRequestHeader("Cookie", $"muid={EdgeDrm.GenerateMuid()};");

        var uri = new Uri($"{Endpoint}?TrustedClientToken={EdgeDrm.TrustedClientToken}&ConnectionId={Guid.NewGuid():N}" +
                          $"&Sec-MS-GEC={EdgeDrm.GenerateSecMsGec()}&Sec-MS-GEC-Version={SecMsGecVersion}");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        await ws.ConnectAsync(uri, timeout.Token).ConfigureAwait(false);

        string ts = DateTime.UtcNow.ToString("ddd MMM dd yyyy HH:mm:ss 'GMT+0000 (Coordinated Universal Time)'", CultureInfo.InvariantCulture);
        await Send(ws,
            $"X-Timestamp:{ts}\r\nContent-Type:application/json; charset=utf-8\r\nPath:speech.config\r\n\r\n" +
            "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":{\"sentenceBoundaryEnabled\":\"false\",\"wordBoundaryEnabled\":\"true\"}," +
            "\"outputFormat\":\"audio-24khz-48kbitrate-mono-mp3\"}}}}\r\n", timeout.Token).ConfigureAwait(false);

        await Send(ws,
            $"X-RequestId:{Guid.NewGuid():N}\r\nContent-Type:application/ssml+xml\r\nX-Timestamp:{ts}Z\r\nPath:ssml\r\n\r\n" +
            "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'>" +
            $"<voice name='{SecurityElement.Escape(voice)}'><prosody pitch='+0Hz' rate='+0%' volume='+0%'>{SecurityElement.Escape(text)}</prosody></voice></speak>",
            timeout.Token).ConfigureAwait(false);

        var audio = new MemoryStream();
        var words = new List<EdgeWordBoundary>();
        var buffer = new byte[64 * 1024];
        var message = new MemoryStream();

        while (true)
        {
            message.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(buffer, timeout.Token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new IOException("The voice service closed the connection.");
                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            var data = message.ToArray();
            if (result.MessageType == WebSocketMessageType.Text)
            {
                string msg = Encoding.UTF8.GetString(data);
                if (msg.Contains("Path:turn.end"))
                    break;
                if (msg.Contains("Path:audio.metadata"))
                    ParseMetadata(msg[(msg.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)..], words);
            }
            else if (data.Length >= 2)
            {
                // Binary: 2 bytes (big-endian) with the header size, the header, then the MP3.
                int headerLength = (data[0] << 8) | data[1];
                if (2 + headerLength < data.Length)
                    audio.Write(data, 2 + headerLength, data.Length - 2 - headerLength);
            }
        }

        if (audio.Length == 0)
            throw new IOException("The voice service returned no audio.");
        return (audio.ToArray(), words);
    }

    private static void ParseMetadata(string json, List<EdgeWordBoundary> into)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var item in doc.RootElement.GetProperty("Metadata").EnumerateArray())
        {
            if (item.GetProperty("Type").GetString() != "WordBoundary")
                continue;
            var d = item.GetProperty("Data");
            into.Add(new EdgeWordBoundary(
                TimeSpan.FromTicks(d.GetProperty("Offset").GetInt64()),   // 100 ns units
                TimeSpan.FromTicks(d.GetProperty("Duration").GetInt64()),
                d.GetProperty("text").GetProperty("Text").GetString() ?? ""));
        }
    }

    /// <summary>Reads the server time and adjusts the difference for the next token.</summary>
    private static async Task AdjustClockSkewAsync(CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var resp = await http.GetAsync("https://speech.platform.bing.com/", ct).ConfigureAwait(false);
            if (resp.Headers.Date is { } server)
                EdgeDrm.ClockSkewSeconds += (server - DateTimeOffset.UtcNow).TotalSeconds;
        }
        catch (HttpRequestException)
        {
            // no internet: the second attempt will fail too and the app uses another voice
        }
    }

    private static Task Send(ClientWebSocket ws, string text, CancellationToken ct) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, ct);
}
