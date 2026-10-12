using System.Text.Json;

namespace PhotoExplorer.Core;

public sealed class UserSettings
{
    public string Theme { get; set; } = "Dark";
    public string? LastFolder { get; set; }
    public int ThumbnailSize { get; set; } = 220;
    public bool Recursive { get; set; }
    public int SearchDepth { get; set; }
    public double LeftPanelWidth { get; set; } = 218;
    public double RightPanelWidth { get; set; } = 278;
    public PhotoSort Sort { get; set; }
    public List<string> Favorites { get; set; } = [];
    public int SlideshowSeconds { get; set; } = 4;
}

public sealed class SettingsStore(string path)
{
    public string? LoadWarning { get; private set; }
    public UserSettings Load()
    {
        try
        {
            if (!File.Exists(path)) return new();
            var settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path)) ?? new();
            settings.Theme = settings.Theme == "Light" ? "Light" : "Dark";
            settings.ThumbnailSize = Math.Clamp(settings.ThumbnailSize, 160, 320);
            settings.SlideshowSeconds = Math.Clamp(settings.SlideshowSeconds, 1, 30);
            settings.SearchDepth = Math.Max(0, settings.SearchDepth);
            settings.LeftPanelWidth = double.IsFinite(settings.LeftPanelWidth) ? Math.Clamp(settings.LeftPanelWidth, 170, 480) : 218;
            settings.RightPanelWidth = double.IsFinite(settings.RightPanelWidth) ? Math.Clamp(settings.RightPanelWidth, 220, 560) : 278;
            settings.Favorites ??= [];
            if (!Enum.IsDefined(settings.Sort)) settings.Sort = PhotoSort.Name;
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            LoadWarning = "Не удалось прочитать настройки. Используются значения по умолчанию.";
            return new();
        }
    }

    public void Save(UserSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
