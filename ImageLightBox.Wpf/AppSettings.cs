using System.IO;
using System.Text.Json;
using ImageLightBox.Core;

namespace ImageLightBox.Wpf;

/// <summary>Remembered between runs in %LOCALAPPDATA%\ImageLightBox. Losing them is harmless, so failures are ignored.</summary>
internal sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImageLightBox", "settings.json");

    public string? SourceFolder { get; set; }
    public string? LastTargetFolder { get; set; }
    public int ThumbnailSize { get; set; } = GridLayout.Standard;
    public bool ConfirmDelete { get; set; } = true;
    public bool ConfirmMove { get; set; } = true;

    public static AppSettings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
