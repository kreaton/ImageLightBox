namespace ImageLightBox.Core;

public enum FileKind
{
    Image,
    Video,
    Audio,
    Other,
}

/// <summary>What a file is, by its extension. Only images are ever decoded, as a video can be gigabytes.</summary>
public static class FileKinds
{
    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".heic", ".heif", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp" };

    private static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase)
        { ".mov", ".mp4", ".m4v", ".avi", ".mkv", ".wmv", ".webm", ".mpg", ".mpeg", ".mts", ".m2ts", ".3gp" };

    private static readonly HashSet<string> Audio = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".m4a", ".wav", ".flac", ".aac", ".ogg", ".wma" };

    public static FileKind Of(string path)
    {
        var extension = Path.GetExtension(path);
        if (Images.Contains(extension))
            return FileKind.Image;
        if (Videos.Contains(extension))
            return FileKind.Video;
        return Audio.Contains(extension) ? FileKind.Audio : FileKind.Other;
    }

    public static bool IsImage(string path) => Of(path) == FileKind.Image;
}
