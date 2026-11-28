using System.Globalization;
using System.Security.Cryptography;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Heif;
using MetadataExtractor.Formats.Jpeg;
using MxDirectory = MetadataExtractor.Directory;

namespace ImageLightBox.Core;

/// <summary>What one look at a file found: its size, its real format, and when the photo was taken.</summary>
public sealed record FileCheck(string Path, long Length, DateTime ModifiedUtc, ContentType? Content, DateTimeOffset? Taken)
{
    /// <summary>
    /// A camera may finish writing the file a little after the shot, and FAT cards only keep
    /// even seconds, so a difference this small is no mismatch.
    /// </summary>
    public static readonly TimeSpan DateTolerance = TimeSpan.FromMinutes(1);

    /// <summary>What the file really is, when its extension says something else.</summary>
    public ContentType? WrongExtension => Content is { } content && content.IsWrongExtensionFor(Path) ? content : null;

    public bool DateDiffers => Taken is { } taken && (ModifiedUtc - taken.UtcDateTime).Duration() > DateTolerance;

    /// <summary>The name the file should have: the same, with the extension that fits its content.</summary>
    public string? FixedName => WrongExtension is { } content
        ? System.IO.Path.GetFileNameWithoutExtension(Path) + content.PreferredExtension
        : null;
}

/// <summary>Looks inside files for what their names and dates get wrong. Safe on any thread.</summary>
public static class FileChecker
{
    // EXIF 2.31 keeps the time zone of DateTimeOriginal here, as in "+02:00".
    private const int TagOffsetTimeOriginal = 0x9011;

    public static FileCheck Check(string path)
    {
        var info = new FileInfo(path);
        var (length, modified) = (info.Length, info.LastWriteTimeUtc);

        using var stream = OpenShared(path);
        Span<byte> head = stackalloc byte[ContentType.HeadLength];
        var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        var content = ContentType.Detect(head[..read]);
        stream.Position = 0;

        return new FileCheck(path, length, modified, content, ReadTaken(stream, content));
    }

    /// <summary>Sets the file's created and modified dates to when the photo was taken.</summary>
    public static void SyncDate(string path, DateTimeOffset taken)
    {
        File.SetLastWriteTimeUtc(path, taken.UtcDateTime);
        File.SetCreationTimeUtc(path, taken.UtcDateTime);
    }

    /// <summary>SHA-256 of the whole file, which tells apart any two files that aren't identical.</summary>
    public static string Hash(string path)
    {
        using var stream = OpenShared(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    /// <summary>
    /// DateTimeOriginal, or else DateTimeDigitized. Not the plain DateTime, which is when a program
    /// last changed the file. Null for a file without one, or with metadata that can't be read.
    /// </summary>
    private static DateTimeOffset? ReadTaken(Stream stream, ContentType? content)
    {
        try
        {
            IEnumerable<MxDirectory>? directories =
                content == ContentType.Jpeg ? JpegMetadataReader.ReadMetadata(stream, [new MetadataExtractor.Formats.Exif.ExifReader()])
                : content == ContentType.Heif ? HeifMetadataReader.ReadMetadata(stream)
                : content == ContentType.Tiff || content == ContentType.Png || content == ContentType.WebP ? ImageMetadataReader.ReadMetadata(stream)
                : null;
            if (directories is null)
                return null;

            foreach (var exif in directories.OfType<ExifSubIfdDirectory>())
            {
                if (exif.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var taken)
                    || exif.TryGetDateTime(ExifDirectoryBase.TagDateTimeDigitized, out taken))
                    return WithOffset(taken, exif.GetString(TagOffsetTimeOriginal));
            }
        }
        catch (Exception)
        {
            // The parser can throw more than its documented exceptions on a damaged file.
        }
        return null;
    }

    /// <summary>EXIF times are the camera's clock. Without a stated zone, it's taken to be this PC's.</summary>
    public static DateTimeOffset WithOffset(DateTime taken, string? offset)
    {
        taken = DateTime.SpecifyKind(taken, DateTimeKind.Unspecified);
        if (offset is { Length: 6 } && offset[0] is '+' or '-'
            && TimeSpan.TryParseExact(offset[1..], @"hh\:mm", CultureInfo.InvariantCulture, out var span))
            return new DateTimeOffset(taken, offset[0] == '-' ? -span : span);
        return new DateTimeOffset(taken, TimeZoneInfo.Local.GetUtcOffset(taken));
    }
}

/// <summary>Finds files with exactly the same content, whatever they're called.</summary>
public static class DuplicateFinder
{
    /// <summary>
    /// Only files of the same size can be the same, so only those are read. Empty files are left
    /// out, as they're all alike. A file that can't be read is left out of its group.
    /// </summary>
    public static List<List<string>> Find(IEnumerable<(string Path, long Length)> files, CancellationToken cancellation = default)
    {
        var groups = new List<List<string>>();
        foreach (var sameSize in files.Where(f => f.Length > 0).GroupBy(f => f.Length).Where(g => g.Count() > 1))
        {
            var byHash = new Dictionary<string, List<string>>();
            foreach (var (path, _) in sameSize)
            {
                cancellation.ThrowIfCancellationRequested();
                string hash;
                try
                {
                    hash = FileChecker.Hash(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                if (!byHash.TryGetValue(hash, out var group))
                    byHash[hash] = group = [];
                group.Add(path);
            }
            groups.AddRange(byHash.Values.Where(g => g.Count > 1));
        }
        return groups;
    }
}

/// <summary>Which files have a twin, kept up to date as files are deleted, moved and renamed.</summary>
public sealed class DuplicateIndex
{
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
    private readonly Dictionary<string, List<string>> _groupOf = new(PathComparer);

    /// <summary>How many files have at least one twin.</summary>
    public int Count => _groupOf.Count;

    public void Add(IEnumerable<string> group)
    {
        var members = group.Distinct(PathComparer).ToList();
        if (members.Count < 2)
            return;
        foreach (var path in members)
            _groupOf[path] = members;
    }

    public bool IsDuplicate(string path) => _groupOf.ContainsKey(path);

    public IReadOnlyList<string> OthersOf(string path) =>
        _groupOf.TryGetValue(path, out var group) ? group.Where(p => !PathComparer.Equals(p, path)).ToList() : [];

    /// <summary>The last of a group is no duplicate any more once its twins are gone.</summary>
    public void Remove(string path)
    {
        if (!_groupOf.Remove(path, out var group))
            return;
        group.RemoveAll(p => PathComparer.Equals(p, path));
        if (group.Count == 1)
            _groupOf.Remove(group[0]);
    }

    public void Rename(string oldPath, string newPath)
    {
        if (!_groupOf.Remove(oldPath, out var group))
            return;
        group[group.FindIndex(p => PathComparer.Equals(p, oldPath))] = newPath;
        _groupOf[newPath] = group;
    }

    public void Clear() => _groupOf.Clear();
}
