using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace LocketWidget;

// the feed request exactly as the website sent it (url, headers incl. token, body)
public class CapturedRequest
{
    public string Url { get; set; } = "";
    public string Method { get; set; } = "GET";
    public Dictionary<string, string> Headers { get; set; } = new();
    public string? Body { get; set; }
}

// keeps the captured request on disk, encrypted with the current windows user (DPAPI)
public static class SecureStore
{
    static string FilePath => Path.Combine(AppSettings.Dir, "session.bin");

    public static void Save(CapturedRequest request)
    {
        Directory.CreateDirectory(AppSettings.Dir);

        var json = JsonSerializer.SerializeToUtf8Bytes(request);
        var encrypted = ProtectedData.Protect(json, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(FilePath, encrypted);
    }

    public static CapturedRequest? Load()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            var encrypted = File.ReadAllBytes(FilePath);
            var json = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<CapturedRequest>(json);
        }
        catch
        {
            return null;
        }
    }

    public static void Clear()
    {
        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }
    }
}
