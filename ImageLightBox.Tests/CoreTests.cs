using System.Text;
using ImageLightBox.Core;

namespace ImageLightBox.Tests;

public sealed class TempFolder : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("ImageLightBoxTests").FullName;

    public string Add(string relativePath, byte[]? content = null)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content ?? [1, 2, 3]);
        return path;
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}

public sealed class FileKindsTests
{
    [Theory]
    [InlineData("a.jpg", FileKind.Image)]
    [InlineData("a.JPEG", FileKind.Image)]
    [InlineData("a.HeIc", FileKind.Image)]
    [InlineData("a.PNG", FileKind.Image)]
    [InlineData("a.MOV", FileKind.Video)]
    [InlineData("a.mp4", FileKind.Video)]
    [InlineData("a.mp3", FileKind.Audio)]
    [InlineData("a.txt", FileKind.Other)]
    [InlineData("jpg", FileKind.Other)]
    public void Goes_by_the_extension_in_any_case(string name, FileKind kind) =>
        Assert.Equal(kind, FileKinds.Of(name));
}

public sealed class FileScannerTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private string[] Scan() =>
        FileScanner.Enumerate(_temp.Root).Select(p => Path.GetRelativePath(_temp.Root, p)).ToArray();

    [Fact]
    public void Finds_every_kind_of_file_at_any_depth()
    {
        _temp.Add("a.jpg");
        _temp.Add("b.MOV");
        _temp.Add("c.txt");
        _temp.Add(@"sub\d.JPEG");
        _temp.Add(@"sub\deep\e");

        Assert.Equal(["a.jpg", "b.MOV", "c.txt", @"sub\d.JPEG", @"sub\deep\e"], Scan());
    }

    [Fact]
    public void Orders_numerically_with_a_folders_own_images_before_its_subfolders()
    {
        _temp.Add(@"b\1.jpg");
        _temp.Add(@"a\1.jpg");
        _temp.Add("IMG_10.jpg");
        _temp.Add("IMG_9.jpg");

        Assert.Equal(["IMG_9.jpg", "IMG_10.jpg", @"a\1.jpg", @"b\1.jpg"], Scan());
    }

    [Fact]
    public void Skips_hidden_files_and_folders()
    {
        File.SetAttributes(_temp.Add("hidden.jpg"), FileAttributes.Hidden);
        _temp.Add(@"secret\a.jpg");
        File.SetAttributes(Path.Combine(_temp.Root, "secret"), FileAttributes.Hidden);
        _temp.Add("seen.jpg");

        Assert.Equal(["seen.jpg"], Scan());
    }

    [Fact]
    public void Stops_when_cancelled()
    {
        _temp.Add("a.jpg");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => FileScanner.Enumerate(_temp.Root, cancellation.Token).ToList());
    }
}

public sealed class FileActionsTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Move_never_overwrites()
    {
        var source = _temp.Add(@"a\photo.jpg", [1]);
        var existing = _temp.Add(@"target\photo.jpg", [2]);

        var moved = FileActions.Move(source, Path.GetDirectoryName(existing)!);

        Assert.Equal("photo (1).jpg", Path.GetFileName(moved));
        Assert.False(File.Exists(source));
        Assert.Equal([1], File.ReadAllBytes(moved));
        Assert.Equal([2], File.ReadAllBytes(existing));
    }

    [Fact]
    public void Copy_never_overwrites_and_keeps_the_source()
    {
        var source = _temp.Add(@"a\photo.jpg", [1]);
        var target = Path.GetDirectoryName(_temp.Add(@"target\photo.jpg", [2]))!;
        _temp.Add(@"target\photo (1).jpg", [3]);

        var copy = FileActions.Copy(source, target);

        Assert.Equal("photo (2).jpg", Path.GetFileName(copy));
        Assert.True(File.Exists(source));
        Assert.Equal([1], File.ReadAllBytes(copy));
    }

    [Fact]
    public void Moving_to_its_own_folder_leaves_the_file_alone()
    {
        var source = _temp.Add(@"a\photo.jpg");

        Assert.Equal(source, FileActions.Move(source, Path.GetDirectoryName(source)!.ToUpperInvariant() + @"\"));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(source)!));
    }
}

public sealed class GridLayoutTests
{
    [Theory]
    [InlineData(800, 400)]
    [InlineData(3000, 2000)]
    [InlineData(100, 100)]
    public void Standard_size_always_shows_four(double width, double height) =>
        Assert.Equal(4, GridLayout.Fit(width, height, GridLayout.Standard).Count);

    [Fact]
    public void Fixed_size_shows_as_many_as_fit()
    {
        // 240 + 14 = 254 wide, 240 + 38 = 278 high.
        Assert.Equal(new GridSize(3, 2), GridLayout.Fit(800, 600, 240));
        Assert.Equal(new GridSize(4, 2), GridLayout.Fit(254 * 4, 278 * 3 - 1, 240));
    }

    [Fact]
    public void Fixed_size_shows_at_least_one() =>
        Assert.Equal(new GridSize(1, 1), GridLayout.Fit(50, 50, 480));

    [Theory]
    [InlineData(0, 4, 1)]
    [InlineData(1, 4, 1)]
    [InlineData(4, 4, 1)]
    [InlineData(5, 4, 2)]
    public void Counts_pages(int total, int pageSize, int pages) =>
        Assert.Equal(pages, GridLayout.PageCount(total, pageSize));

    [Theory]
    [InlineData(100, 256)]
    [InlineData(256, 256)]
    [InlineData(257, 512)]
    [InlineData(1920, 2048)]
    [InlineData(100000, 8192)]
    public void Decode_size_covers_the_screen_size(double pixels, int expected) =>
        Assert.Equal(expected, GridLayout.DecodeSize(pixels));
}

public sealed class MapLocationTests
{
    [Theory]
    [InlineData("+48.8584+002.2945+035.000/", 48.8584, 2.2945)]
    [InlineData("-33.8568+151.2153/", -33.8568, 151.2153)]
    [InlineData("+40.7-074.0", 40.7, -74.0)]
    public void Parses_quicktime_positions(string text, double latitude, double longitude) =>
        Assert.Equal(new MapLocation(latitude, longitude), MapLocation.ParseIso6709(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nowhere")]
    [InlineData("+91.0+002.0/")]
    [InlineData("+00.0000+000.0000/")]
    public void Rejects_what_is_no_position(string? text) =>
        Assert.Null(MapLocation.ParseIso6709(text));

    [Fact]
    public void Urls_use_dots_whatever_the_culture()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("da-DK");
        try
        {
            var location = new MapLocation(48.8584, 2.2945);

            Assert.Equal("https://www.openstreetmap.org/?mlat=48.8584&mlon=2.2945#map=16/48.8584/2.2945", location.PageUrl);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }
}

public sealed class ExifReaderTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Reads_the_location_from_exif_gps()
    {
        var details = ExifReader.ReadDetails(_temp.Add("a.jpg", Jpeg(ExifWithGps())));

        Assert.NotNull(details.Location);
        Assert.Equal(48.8584, details.Location.Latitude, 4);
        Assert.Equal(-2.2945, details.Location.Longitude, 4);
    }

    [Fact]
    public void A_photo_without_gps_has_no_location() =>
        Assert.Null(ExifReader.ReadDetails(_temp.Add("a.jpg", Jpeg(Exif("Acme")))).Location);

    [Fact]
    public void A_file_with_no_metadata_has_no_location() =>
        Assert.Null(ExifReader.ReadDetails(_temp.Add("notes.txt", Encoding.ASCII.GetBytes("plain text"))).Location);

    [Fact]
    public void Reads_the_file_details_and_exif()
    {
        var entries = ExifReader.Read(_temp.Add("a.jpg", Jpeg(Exif("Acme"))));

        Assert.Contains(new ExifEntry("File", "Name", "a.jpg"), entries);
        Assert.Contains(new ExifEntry("Exif IFD0", "Make", "Acme"), entries);
    }

    [Fact]
    public void Exif_comes_right_after_the_file_details()
    {
        var entries = ExifReader.Read(_temp.Add("a.jpg", Jpeg(Exif("Acme"))));

        Assert.Equal(["File", "Exif IFD0"], entries.Select(e => e.Section).Distinct());
    }

    [Fact]
    public void Bad_xmp_keeps_the_exif()
    {
        var xmp = Segment(0xE1, [.. "http://ns.adobe.com/xap/1.0/\0"u8, .. "<x:xmpmeta not xml"u8]);

        var entries = ExifReader.Read(_temp.Add("a.jpg", Jpeg(xmp, Exif("Acme"))));

        Assert.Contains(new ExifEntry("Exif IFD0", "Make", "Acme"), entries);
    }

    [Fact]
    public void A_file_that_is_no_image_still_shows_its_details()
    {
        var entries = ExifReader.Read(_temp.Add("broken.jpg", Encoding.ASCII.GetBytes("not an image at all")));

        Assert.Contains(new ExifEntry("File", "Name", "broken.jpg"), entries);
        Assert.Contains(entries, e => e.Name == "Metadata" && e.Value.StartsWith("Cannot be read"));
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("clip.mov")]
    public void A_file_that_claims_no_image_format_shows_its_details_without_an_error(string name)
    {
        var entries = ExifReader.Read(_temp.Add(name, Encoding.ASCII.GetBytes("just some bytes, nothing more")));

        Assert.Contains(new ExifEntry("File", "Name", name), entries);
        Assert.DoesNotContain(entries, e => e.Name == "Metadata");
    }

    [Fact]
    public void A_movie_with_an_ftyp_box_is_not_taken_for_a_heic()
    {
        // size, "ftyp", brand "qt  ", minor version, compatible brand "qt  "
        byte[] mov = [0, 0, 0, 20, .. "ftyp"u8, .. "qt  "u8, 0, 0, 0, 0, .. "qt  "u8];

        var entries = ExifReader.Read(_temp.Add("clip.mov", mov));

        Assert.Contains(new ExifEntry("File", "Name", "clip.mov"), entries);
        Assert.DoesNotContain(entries, e => e.Name == "Metadata");
    }

    private static byte[] Jpeg(params byte[][] segments) =>
        [0xFF, 0xD8, .. segments.SelectMany(s => s), 0xFF, 0xD9];

    private static byte[] Segment(byte marker, byte[] body)
    {
        var length = body.Length + 2;
        return [0xFF, marker, (byte)(length >> 8), (byte)length, .. body];
    }

    /// <summary>A little-endian TIFF block with IFD0 pointing at a GPS IFD: 48.8584 N, 2.2945 W, in decimal degrees.</summary>
    private static byte[] ExifWithGps()
    {
        const uint gpsIfd = 26;          // 8 + 2 + 12 + 4
        const uint values = gpsIfd + 2 + 4 * 12 + 4;
        using var body = new MemoryStream();
        using var writer = new BinaryWriter(body);

        void Entry(ushort tag, ushort type, uint count, uint valueOrOffset)
        {
            writer.Write(tag);
            writer.Write(type);
            writer.Write(count);
            writer.Write(valueOrOffset);
        }

        void Degrees(double degrees)
        {
            writer.Write((uint)Math.Round(degrees * 10000));
            writer.Write(10000u);
            writer.Write(0u);
            writer.Write(1u);
            writer.Write(0u);
            writer.Write(1u);
        }

        writer.Write("Exif\0\0"u8);
        writer.Write("II"u8);
        writer.Write((ushort)42);
        writer.Write(8u);
        writer.Write((ushort)1);
        Entry(0x8825, 4, 1, gpsIfd);     // GPS IFD pointer
        writer.Write(0u);
        writer.Write((ushort)4);
        Entry(1, 2, 2, 'N');             // GPSLatitudeRef, stored in the entry
        Entry(2, 5, 3, values);          // GPSLatitude
        Entry(3, 2, 2, 'W');             // GPSLongitudeRef, stored in the entry
        Entry(4, 5, 3, values + 24);     // GPSLongitude
        writer.Write(0u);
        Degrees(48.8584);
        Degrees(2.2945);
        return Segment(0xE1, body.ToArray());
    }

    /// <summary>A little-endian TIFF block with one tag in IFD0: Make, stored after the IFD.</summary>
    private static byte[] Exif(string make)
    {
        var value = Encoding.ASCII.GetBytes(make + "\0");
        using var body = new MemoryStream();
        using var writer = new BinaryWriter(body);
        writer.Write("Exif\0\0"u8);
        writer.Write("II"u8);
        writer.Write((ushort)42);
        writer.Write(8u);         // IFD0 offset
        writer.Write((ushort)1);  // one entry
        writer.Write((ushort)0x010F); // Make
        writer.Write((ushort)2);  // ASCII
        writer.Write((uint)value.Length);
        writer.Write(26u);        // value offset: 8 + 2 + 12 + 4
        writer.Write(0u);         // no next IFD
        writer.Write(value);
        return Segment(0xE1, body.ToArray());
    }
}
