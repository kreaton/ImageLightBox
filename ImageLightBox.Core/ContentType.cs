namespace ImageLightBox.Core;

/// <summary>What a file really is, told by its first bytes, and the extensions that fit it. The first is the usual one.</summary>
public sealed record ContentType(string Name, params string[] Extensions)
{
    public static readonly ContentType Jpeg = new("JPEG", ".jpg", ".jpeg", ".jpe", ".jfif");
    public static readonly ContentType Png = new("PNG", ".png");
    public static readonly ContentType Gif = new("GIF", ".gif");
    public static readonly ContentType Bmp = new("BMP", ".bmp", ".dib");
    public static readonly ContentType Tiff = new("TIFF", ".tif", ".tiff");
    public static readonly ContentType WebP = new("WebP", ".webp");
    public static readonly ContentType Heif = new("HEIC", ".heic", ".heif", ".hif");
    public static readonly ContentType Avif = new("AVIF", ".avif");

    // MOV and MP4 are the same container, and players don't mind which name it has. Many
    // M4A files carry a video brand, so that name is no sign of a mistake either.
    public static readonly ContentType Mpeg4Video = new("MP4/MOV video", ".mp4", ".mov", ".m4v", ".3gp", ".3g2", ".m4a");
    public static readonly ContentType Mpeg4Audio = new("M4A audio", ".m4a", ".m4b", ".mp4");
    public static readonly ContentType Avi = new("AVI video", ".avi");
    public static readonly ContentType Matroska = new("MKV/WebM video", ".mkv", ".webm", ".mka");
    public static readonly ContentType MpegVideo = new("MPEG video", ".mpg", ".mpeg", ".vob");
    public static readonly ContentType Asf = new("WMV/WMA", ".wmv", ".wma", ".asf");
    public static readonly ContentType Mp3 = new("MP3 audio", ".mp3");
    public static readonly ContentType Aac = new("AAC audio", ".aac");
    public static readonly ContentType Wav = new("WAV audio", ".wav");
    public static readonly ContentType Flac = new("FLAC audio", ".flac");
    public static readonly ContentType Ogg = new("Ogg audio", ".ogg", ".oga", ".opus");

    /// <summary>Bytes to read from the start of a file for <see cref="Detect"/>.</summary>
    public const int HeadLength = 32;

    public string PreferredExtension => Extensions[0];

    public bool Fits(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Null when the bytes aren't any of the formats known here, which is no proof of anything.</summary>
    public static ContentType? Detect(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
            return Jpeg;
        if (head.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
            return Png;
        if (head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8))
            return Gif;
        if (head.StartsWith("II*\0"u8) || head.StartsWith("MM\0*"u8))
            return Tiff;
        if (head.Length >= 18 && head.StartsWith("BM"u8) && BitConverter.ToInt32(head[14..18]) is 12 or 40 or 52 or 56 or 64 or 108 or 124)
            return Bmp;
        if (head.Length >= 12 && head.StartsWith("RIFF"u8))
        {
            var form = head[8..12];
            if (form.SequenceEqual("WEBP"u8))
                return WebP;
            if (form.SequenceEqual("AVI "u8))
                return Avi;
            if (form.SequenceEqual("WAVE"u8))
                return Wav;
            return null;
        }
        if (head.Length >= 12 && head[4..8].SequenceEqual("ftyp"u8))
            return FromBrand(System.Text.Encoding.ASCII.GetString(head[8..12]));
        if (head.StartsWith((ReadOnlySpan<byte>)[0x1A, 0x45, 0xDF, 0xA3]))
            return Matroska;
        if (head.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0x01, 0xBA]))
            return MpegVideo;
        if (head.StartsWith((ReadOnlySpan<byte>)[0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11]))
            return Asf;
        if (head.StartsWith("fLaC"u8))
            return Flac;
        if (head.StartsWith("OggS"u8))
            return Ogg;
        if (head.StartsWith("ID3"u8))
            return Mp3;
        if (head.Length >= 2 && head[0] == 0xFF)
        {
            // MPEG audio frame sync. Layer III is MP3, and layer bits 00 is AAC in ADTS.
            if (head[1] is 0xFB or 0xFA or 0xF3 or 0xF2 or 0xE3 or 0xE2)
                return Mp3;
            if (head[1] is 0xF1 or 0xF9)
                return Aac;
        }
        return null;
    }

    private static ContentType FromBrand(string brand) => brand switch
    {
        "heic" or "heix" or "hevc" or "hevx" or "heim" or "heis" or "hevm" or "hevs" or "mif1" or "msf1" => Heif,
        "avif" or "avis" => Avif,
        "M4A " or "M4B " or "M4P " => Mpeg4Audio,
        _ => Mpeg4Video,
    };

    /// <summary>
    /// Whether a file with this name and this content has the wrong extension. Only media
    /// extensions, or none, are judged: raw photos such as .dng are TIFF inside, and that's right.
    /// </summary>
    public bool IsWrongExtensionFor(string path) =>
        (FileKinds.Of(path) != FileKind.Other || Path.GetExtension(path).Length == 0) && !Fits(path);
}
