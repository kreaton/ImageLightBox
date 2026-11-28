using System.Text;
using ImageLightBox.Core;

namespace ImageLightBox.Tests;

/// <summary>Builds the smallest JPEG that has an EXIF DateTimeOriginal, and optionally its time zone.</summary>
internal static class TestJpeg
{
    public static byte[] WithDateTaken(string taken, string? offset = null)
    {
        var entries = new List<(ushort Tag, byte[] Text)> { (0x9003, Ascii(taken)) };
        if (offset is not null)
            entries.Add((0x9011, Ascii(offset)));

        var tiff = new List<byte>();
        tiff.AddRange("II"u8.ToArray());
        tiff.AddRange(U16(42));
        tiff.AddRange(U32(8));

        // IFD0, pointing to the EXIF sub-IFD right after it.
        const int subIfd = 8 + 2 + 12 + 4;
        tiff.AddRange(U16(1));
        tiff.AddRange(U16(0x8769));
        tiff.AddRange(U16(4));
        tiff.AddRange(U32(1));
        tiff.AddRange(U32(subIfd));
        tiff.AddRange(U32(0));

        var data = subIfd + 2 + entries.Count * 12 + 4;
        tiff.AddRange(U16((ushort)entries.Count));
        foreach (var (tag, text) in entries)
        {
            tiff.AddRange(U16(tag));
            tiff.AddRange(U16(2));
            tiff.AddRange(U32((uint)text.Length));
            tiff.AddRange(U32((uint)data));
            data += text.Length;
        }
        tiff.AddRange(U32(0));
        foreach (var (_, text) in entries)
            tiff.AddRange(text);

        var app1 = "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();
        var jpeg = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE1 };
        jpeg.Add((byte)((app1.Length + 2) >> 8));
        jpeg.Add((byte)(app1.Length + 2));
        jpeg.AddRange(app1);
        jpeg.AddRange([0xFF, 0xD9]);
        return jpeg.ToArray();
    }

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text + "\0");

    private static byte[] U16(ushort value) => BitConverter.GetBytes(value);

    private static byte[] U32(uint value) => BitConverter.GetBytes(value);
}

public sealed class ContentTypeTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16];

    private static byte[] Ftyp(string brand) => [0, 0, 0, 24, .. "ftyp"u8.ToArray(), .. Encoding.ASCII.GetBytes(brand)];

    [Fact]
    public void Tells_formats_by_their_first_bytes()
    {
        Assert.Equal(ContentType.Jpeg, ContentType.Detect(Jpeg));
        Assert.Equal(ContentType.Png, ContentType.Detect(Png));
        Assert.Equal(ContentType.Gif, ContentType.Detect("GIF89a\0\0"u8));
        Assert.Equal(ContentType.WebP, ContentType.Detect("RIFF\0\0\0\0WEBPVP8 "u8));
        Assert.Equal(ContentType.Heif, ContentType.Detect(Ftyp("heic")));
        Assert.Equal(ContentType.Mpeg4Video, ContentType.Detect(Ftyp("qt  ")));
        Assert.Null(ContentType.Detect("hello world"u8));
        Assert.Null(ContentType.Detect([]));
    }

    [Theory]
    [InlineData("a.png", true)]
    [InlineData("a.JPG", false)]
    [InlineData("a.jpg", false)]
    [InlineData("a.jpeg", false)]
    [InlineData("a", true)]
    [InlineData("a.txt", false)]
    public void Judges_only_media_extensions_and_none(string name, bool jpegIsWrong) =>
        Assert.Equal(jpegIsWrong, ContentType.Jpeg.IsWrongExtensionFor(name));

    [Fact]
    public void Lets_mov_and_mp4_stand_for_each_other() =>
        Assert.False(ContentType.Detect(Ftyp("qt  "))!.IsWrongExtensionFor("clip.mp4"));

    [Fact]
    public void Doesnt_mind_a_raw_photo_being_tiff_inside() =>
        Assert.False(ContentType.Tiff.IsWrongExtensionFor("IMG_1.dng"));
}

public sealed class FileCheckTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Finds_a_png_named_jpg_and_names_the_fix()
    {
        var path = _temp.Add("photo.jpg", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0]);

        var check = FileChecker.Check(path);

        Assert.Equal(ContentType.Png, check.WrongExtension);
        Assert.Equal("photo.png", check.FixedName);
    }

    [Fact]
    public void Reads_the_date_taken_and_sees_the_file_date_differ()
    {
        var path = _temp.Add("a.jpg", TestJpeg.WithDateTaken("2020:05:17 14:30:00"));
        File.SetLastWriteTime(path, new DateTime(2024, 1, 1, 9, 0, 0));

        var check = FileChecker.Check(path);

        Assert.Null(check.WrongExtension);
        Assert.Equal(new DateTime(2020, 5, 17, 14, 30, 0), check.Taken!.Value.LocalDateTime);
        Assert.True(check.DateDiffers);
    }

    [Fact]
    public void Brings_the_file_date_in_sync()
    {
        var path = _temp.Add("a.jpg", TestJpeg.WithDateTaken("2020:05:17 14:30:00"));
        var taken = FileChecker.Check(path).Taken!.Value;

        FileChecker.SyncDate(path, taken);

        var after = FileChecker.Check(path);
        Assert.False(after.DateDiffers);
        Assert.Equal(new DateTime(2020, 5, 17, 14, 30, 0), File.GetLastWriteTime(path));
        Assert.Equal(new DateTime(2020, 5, 17, 14, 30, 0), File.GetCreationTime(path));
    }

    [Fact]
    public void Uses_the_time_zone_the_camera_wrote()
    {
        var path = _temp.Add("a.jpg", TestJpeg.WithDateTaken("2020:05:17 14:30:00", "+09:00"));

        Assert.Equal(new DateTime(2020, 5, 17, 5, 30, 0, DateTimeKind.Utc), FileChecker.Check(path).Taken!.Value.UtcDateTime);
    }

    [Fact]
    public void Allows_a_few_seconds_between_shot_and_file()
    {
        var path = _temp.Add("a.jpg", TestJpeg.WithDateTaken("2020:05:17 14:30:00"));
        File.SetLastWriteTime(path, new DateTime(2020, 5, 17, 14, 30, 5));

        Assert.False(FileChecker.Check(path).DateDiffers);
    }

    [Fact]
    public void Has_no_date_for_a_file_without_exif()
    {
        var path = _temp.Add("a.jpg", [0xFF, 0xD8, 0xFF, 0xD9]);

        var check = FileChecker.Check(path);

        Assert.Null(check.Taken);
        Assert.False(check.DateDiffers);
    }
}

public sealed class DuplicateTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private static List<List<string>> Find(params string[] paths) =>
        DuplicateFinder.Find(paths.Select(p => (p, new FileInfo(p).Length)));

    [Fact]
    public void Finds_identical_files_whatever_their_names()
    {
        var a = _temp.Add("a.jpg", [1, 2, 3, 4]);
        var b = _temp.Add(@"sub\a.jpg", [1, 2, 3, 4]);
        var c = _temp.Add("other name.jpg", [1, 2, 3, 4]);
        var sameSizeOtherContent = _temp.Add("d.jpg", [1, 2, 3, 5]);
        var otherSize = _temp.Add("e.jpg", [1, 2, 3]);

        var groups = Find(a, b, c, sameSizeOtherContent, otherSize);

        Assert.Equal([a, b, c], Assert.Single(groups));
    }

    [Fact]
    public void Leaves_out_empty_files()
    {
        Assert.Empty(Find(_temp.Add("a.jpg", []), _temp.Add("b.jpg", [])));
    }

    [Fact]
    public void Index_drops_the_last_twin_once_the_other_is_gone()
    {
        var index = new DuplicateIndex();
        index.Add([@"C:\a.jpg", @"C:\b.jpg"]);

        Assert.True(index.IsDuplicate(@"C:\A.JPG"));
        Assert.Equal([@"C:\b.jpg"], index.OthersOf(@"C:\a.jpg"));

        index.Remove(@"C:\a.jpg");

        Assert.False(index.IsDuplicate(@"C:\b.jpg"));
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void Index_follows_a_rename()
    {
        var index = new DuplicateIndex();
        index.Add([@"C:\a.jpg", @"C:\b.jpg", @"C:\c.jpg"]);

        index.Rename(@"C:\a.jpg", @"C:\z.jpg");

        Assert.False(index.IsDuplicate(@"C:\a.jpg"));
        Assert.Equal([@"C:\b.jpg", @"C:\c.jpg"], index.OthersOf(@"C:\z.jpg"));
        Assert.Equal([@"C:\z.jpg", @"C:\c.jpg"], index.OthersOf(@"C:\b.jpg"));
    }
}
