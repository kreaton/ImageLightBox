using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using ImageLightBox.Core;

namespace ImageLightBox.Wpf;

/// <summary>What stands in for a file with no picture: a Segoe Fluent Icons glyph and a short label.</summary>
internal static class FileIcon
{
    public static string Glyph(string path) => FileKinds.Of(path) switch
    {
        FileKind.Image => "",
        FileKind.Video => "",
        FileKind.Audio => "",
        _ => "",
    };

    public static bool IsVideo(string path) => FileKinds.Of(path) == FileKind.Video;

    /// <summary>The main action on a file: videos go to their player, and the rest full screen.</summary>
    public static string OpenLabel(string path) => IsVideo(path) ? "Play" : "Full screen";

    public static string OpenGlyph(string path) => IsVideo(path) ? "" : "";

    public static string Label(string path)
    {
        if (FileKinds.IsImage(path))
            return "Can't be shown";
        var extension = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        return extension.Length == 0 ? "File" : extension;
    }
}

/// <summary>One file on the current page.</summary>
internal sealed class Tile(int index, string path) : INotifyPropertyChanged
{
    public int Index { get; } = index;
    public string Path { get; } = path;
    public string FileName { get; } = System.IO.Path.GetFileName(path);
    public string IconGlyph { get; } = FileIcon.Glyph(path);
    public string IconLabel { get; } = FileIcon.Label(path);
    public string OpenLabel { get; } = FileIcon.OpenLabel(path);
    public string OpenGlyph { get; } = FileIcon.OpenGlyph(path);

    /// <summary>Instead of a picture: for a file that isn't an image, or an image that can't be decoded.</summary>
    public bool ShowIcon => !FileKinds.IsImage(Path) || Failed;

    public BitmapSource? Image
    {
        get;
        set { field = value; Changed(); }
    }

    public bool IsSelected
    {
        get;
        set { field = value; Changed(); }
    }

    public bool HasLocation
    {
        get;
        set { field = value; Changed(); }
    }

    public bool Failed
    {
        get;
        set { field = value; Changed(); Changed(nameof(ShowIcon)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
