using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.FileType;
using MetadataExtractor.Formats.Heif;
using MetadataExtractor.Formats.Icc;
using MetadataExtractor.Formats.Jfif;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.QuickTime;
using MetadataExtractor.IO;
using MxDirectory = MetadataExtractor.Directory;

namespace ImageLightBox.Core;

public sealed record ExifEntry(string Section, string Name, string Value);

/// <summary>What the side pane lists, and where the file was taken if it says so.</summary>
public sealed record FileDetails(IReadOnlyList<ExifEntry> Entries, MapLocation? Location);

/// <summary>Reads what the side pane shows: the file's own details, then its metadata.</summary>
public static class ExifReader
{
    private const string FileSection = "File";
    private const int MaxValueLength = 300;

    public static IReadOnlyList<ExifEntry> Read(string path) => ReadDetails(path).Entries;

    public static FileDetails ReadDetails(string path)
    {
        var entries = new List<ExifEntry>();
        MapLocation? location = null;
        try
        {
            var info = new FileInfo(path);
            entries.Add(new(FileSection, "Name", info.Name));
            entries.Add(new(FileSection, "Folder", info.DirectoryName ?? ""));
            entries.Add(new(FileSection, "Size", FormatSize(info.Length)));
            entries.Add(new(FileSection, "Modified", info.LastWriteTime.ToString("g")));

            // Other programs may delete or rename the file while we read it.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var directories = ReadDirectories(stream, path);
            location = ReadLocation(directories);

            if (ReadDimensions(directories) is { } dimensions)
                entries.Insert(3, new(FileSection, "Dimensions", dimensions));

            foreach (var directory in directories.Where(IsShown).OrderBy(SectionRank))
                foreach (var tag in directory.Tags.Where(t => t.HasName && IsShown(directory, t)))
                    if (Clean(tag.Description) is { } value)
                        entries.Add(new(directory.Name, tag.Name, value));
        }
        catch (Exception ex)
        {
            // The parser can throw more than its documented exceptions on a damaged file.
            entries.Add(new(FileSection, "Metadata", $"Cannot be read: {ex.Message}"));
        }
        return new FileDetails(entries, location);
    }

    /// <summary>Photos keep it in EXIF, and iPhone movies in QuickTime metadata.</summary>
    private static MapLocation? ReadLocation(List<MxDirectory> directories)
    {
        foreach (var gps in directories.OfType<GpsDirectory>())
            if (gps.TryGetGeoLocation(out var position) && MapLocation.Create(position.Latitude, position.Longitude) is { } location)
                return location;

        return directories.OfType<QuickTimeMetadataHeaderDirectory>()
            .Select(d => MapLocation.ParseIso6709(d.GetString(QuickTimeMetadataHeaderDirectory.TagGpsLocation)))
            .FirstOrDefault(l => l is not null);
    }

    /// <summary>
    /// Told apart by content as an extension can be wrong. JPEG readers run one at a time
    /// because the library lets one reader's exception discard what the others found.
    /// </summary>
    private static List<MxDirectory> ReadDirectories(Stream stream, string path)
    {
        Span<byte> head = stackalloc byte[12];
        var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        stream.Position = 0;

        if (read == head.Length && IsHeif(head))
            return HeifMetadataReader.ReadMetadata(stream).ToList();
        if (read < 2 || head[0] != 0xFF || head[1] != 0xD8)
            return ReadOtherFormat(stream, path);

        var readers = JpegMetadataReader.AllReaders;
        var segments = JpegSegmentReader.ReadSegments(
            new SequentialStreamReader(stream),
            readers.SelectMany(r => r.SegmentTypes).ToHashSet()).ToList();

        var directories = new List<MxDirectory>();
        foreach (var reader in readers)
        {
            try
            {
                directories.AddRange(JpegMetadataReader.ProcessJpegSegments([reader], segments));
            }
            catch (Exception)
            {
                // Bad XMP, say, shouldn't cost the EXIF.
            }
        }
        return directories;
    }

    /// <summary>PNG, MOV, MP4 and so on. A file with no metadata format is no error unless it claims to be an image.</summary>
    private static List<MxDirectory> ReadOtherFormat(Stream stream, string path)
    {
        try
        {
            return ImageMetadataReader.ReadMetadata(stream).ToList();
        }
        catch (ImageProcessingException) when (!FileKinds.IsImage(path))
        {
            return [];
        }
    }

    /// <summary>HEIF files start with an ftyp box, and so do MOV and MP4 files, with another brand in it.</summary>
    private static bool IsHeif(ReadOnlySpan<byte> head)
    {
        if (!head[4..8].SequenceEqual("ftyp"u8))
            return false;
        var brand = System.Text.Encoding.ASCII.GetString(head[8..12]);
        return brand is "heic" or "heix" or "hevc" or "hevx" or "heim" or "heis" or "hevm" or "hevs" or "mif1" or "msf1";
    }

    private static string? ReadDimensions(List<MxDirectory> directories)
    {
        if (directories.OfType<JpegDirectory>().FirstOrDefault() is { } jpeg
            && jpeg.TryGetInt32(JpegDirectory.TagImageWidth, out var width)
            && jpeg.TryGetInt32(JpegDirectory.TagImageHeight, out var height))
            return $"{width} × {height}";

        // A HEIC holds many tiles and a thumbnail, and the largest is the whole image.
        var heif = directories.OfType<HeicImagePropertiesDirectory>()
            .Where(d => d.ContainsTag(HeicImagePropertiesDirectory.TagImageWidth) && d.ContainsTag(HeicImagePropertiesDirectory.TagImageHeight))
            .MaxBy(d => d.TryGetInt32(HeicImagePropertiesDirectory.TagImageWidth, out var w) ? w : 0);
        return heif is null
            ? null
            : $"{heif.GetString(HeicImagePropertiesDirectory.TagImageWidth)} × {heif.GetString(HeicImagePropertiesDirectory.TagImageHeight)}";
    }

    /// <summary>Leaves out how the file is encoded, which says nothing about the photo.</summary>
    private static bool IsShown(MxDirectory directory) => directory
        is not (JpegDirectory or JfifDirectory or HuffmanTablesDirectory or ExifThumbnailDirectory
            or IccDirectory or FileTypeDirectory or HeicImagePropertiesDirectory or HeicThumbnailDirectory
            or QuickTimeFileTypeDirectory);

    /// <summary>A movie has dozens of technical rows, with a track for each stream. Keeps what describes the movie.</summary>
    private static bool IsShown(MxDirectory directory, Tag tag) => directory switch
    {
        QuickTimeMovieHeaderDirectory => tag.Name is "Created" or "Duration",
        QuickTimeTrackHeaderDirectory => directory.GetString(QuickTimeTrackHeaderDirectory.TagWidth) is not (null or "0")
            && tag.Name is "Width" or "Height" or "Rotation",
        _ => true,
    };

    private static int SectionRank(MxDirectory directory) => directory switch
    {
        ExifIfd0Directory => 0,
        ExifSubIfdDirectory => 1,
        GpsDirectory => 2,
        _ => 3,
    };

    private static string? Clean(string? value)
    {
        if (value is null)
            return null;
        value = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (value.Length == 0)
            return null;
        return value.Length > MaxValueLength ? value[..MaxValueLength] + "…" : value;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.0} MB",
        >= 1024 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} bytes",
    };
}
