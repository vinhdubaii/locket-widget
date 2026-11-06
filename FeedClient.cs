using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LocketWidget;

public class SessionExpiredException : Exception
{
}

// replays the captured feed request with a plain HttpClient (a few mb of ram)
public class FeedClient
{
    static readonly HashSet<string> SkipHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host",
        "Content-Length",
        "Accept-Encoding",
        "Connection",
    };

    readonly AppSettings _settings;

    readonly HttpClient _http = new(new HttpClientHandler
    {
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.All,
    });

    public FeedClient(AppSettings settings)
    {
        _settings = settings;
    }

    public async Task<string?> GetLatestImageUrlAsync(CapturedRequest captured)
    {
        using var message = new HttpRequestMessage(new HttpMethod(captured.Method), captured.Url);

        if (captured.Body != null)
        {
            message.Content = new StringContent(captured.Body, Encoding.UTF8);
        }

        foreach (var pair in captured.Headers)
        {
            if (SkipHeaders.Contains(pair.Key))
            {
                continue;
            }

            if (pair.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Content == null)
                {
                    continue;
                }

                message.Content.Headers.Remove(pair.Key);
                message.Content.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
            }
            else
            {
                message.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
            }
        }

        using var response = await _http.SendAsync(message);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new SessionExpiredException();
        }

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var found = new List<string>();
        Collect(doc.RootElement, _settings.ImageUrlMarker, found);

        if (found.Count == 0)
        {
            return null;
        }

        return _settings.PickLastImage ? found[^1] : found[0];
    }

    public async Task<byte[]> DownloadImageAsync(string url)
    {
        var bytes = await _http.GetByteArrayAsync(url);

        // keep a copy so the widget has something to show at the next start
        Directory.CreateDirectory(AppSettings.Dir);
        await File.WriteAllBytesAsync(Path.Combine(AppSettings.Dir, "latest.img"), bytes);

        return bytes;
    }

    // walks the whole json and collects every string that looks like an image url
    static void Collect(JsonElement element, string marker, List<string> found)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString();
                if (text != null
                    && text.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    && text.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(text);
                }
                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Collect(property.Value, marker, found);
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, marker, found);
                }
                break;
        }
    }
}
