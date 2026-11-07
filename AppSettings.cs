using System;
using System.IO;
using System.Text.Json;

namespace LocketWidget;

public class AppSettings
{
    // a piece of the url of the request that returns the moments feed.
    // leave it empty on the first run: the app logs every fetch/xhr url
    // to requests.log so you can pick the right one.
    public string FeedUrlMarker { get; set; } = "";

    // a piece of the image url inside the feed json
    public string ImageUrlMarker { get; set; } = "locket-img";

    // false = newest image is the first match in the json, true = the last one
    public bool PickLastImage { get; set; } = false;

    // site that is opened for login and when the widget is clicked
    public string SiteUrl { get; set; } = "https://locket-dio.com";

    public int RefreshMinutes { get; set; } = 5;
    public int Size { get; set; } = 220;
    public bool AlwaysOnTop { get; set; } = false;

    // a locked widget can not be moved or resized (a plain click still opens the site)
    public bool Locked { get; set; } = false;

    // keep the widget visible when "Show desktop" (Win+D, taskbar corner) is used
    public bool PinOnShowDesktop { get; set; } = true;

    // set to true if a thin outline or haze still shows around the rounded widget:
    // the window is then cut to the rounded shape (edge is slightly pixelated)
    public bool ClipRoundedWindow { get; set; } = false;
    public double? Left { get; set; }
    public double? Top { get; set; }

    public static string Dir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LocketWidget");

    static string FilePath => Path.Combine(Dir, "settings.json");

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        Directory.CreateDirectory(Dir);

        if (File.Exists(FilePath))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (loaded != null)
                {
                    return loaded;
                }
            }
            catch
            {
                // broken file: fall through and write the defaults again
            }
        }

        var fresh = new AppSettings();
        fresh.Save();
        return fresh;
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
