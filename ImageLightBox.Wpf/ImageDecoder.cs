using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageMagick;

namespace ImageLightBox.Wpf;

/// <summary>
/// Turns a file into a frozen bitmap, the right way up and no larger than asked for. Safe on any
/// thread, and leaves no file open so that images on screen can be deleted or moved.
/// </summary>
internal static class ImageDecoder
{
    public static BitmapSource Decode(string path, int maxSide)
    {
        if (Path.GetExtension(path).ToLowerInvariant() is not (".heic" or ".heif"))
        {
            try
            {
                return DecodeWithWic(path, maxSide);
            }
            catch (Exception)
            {
                // Magick still gets a chance with a file WIC rejects, such as a HEIC named .jpg.
            }
        }
        return DecodeWithMagick(path, maxSide);
    }

    private static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private static BitmapSource DecodeWithWic(string path, int maxSide)
    {
        using var stream = OpenShared(path);

        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var (width, height) = (frame.PixelWidth, frame.PixelHeight);
        var orientation = ReadOrientation(frame);
        stream.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        if (Math.Max(width, height) > maxSide)
        {
            // Setting one side keeps the aspect ratio. Never scales up.
            if (width >= height)
                image.DecodePixelWidth = maxSide;
            else
                image.DecodePixelHeight = maxSide;
        }
        image.EndInit();
        image.Freeze();

        return Orient(image, orientation);
    }

    /// <summary>The tag is where JPEG keeps it. Other formats have none here, which the query reports by throwing.</summary>
    private static int ReadOrientation(BitmapFrame frame)
    {
        try
        {
            return frame.Metadata is BitmapMetadata metadata && metadata.GetQuery("/app1/ifd/{ushort=274}") is ushort value ? value : 1;
        }
        catch (Exception)
        {
            return 1; // Damaged metadata shouldn't cost the picture.
        }
    }

    /// <summary>Applies the EXIF orientation, for which WPF does nothing by itself.</summary>
    private static BitmapSource Orient(BitmapSource image, int orientation)
    {
        var (mirror, degrees) = orientation switch
        {
            2 => (true, 0),
            3 => (false, 180),
            4 => (true, 180),
            5 => (true, 270),
            6 => (false, 90),
            7 => (true, 90),
            8 => (false, 270),
            _ => (false, 0),
        };
        if (!mirror && degrees == 0)
            return image;

        var transform = new TransformGroup();
        if (mirror)
            transform.Children.Add(new ScaleTransform(-1, 1));
        if (degrees != 0)
            transform.Children.Add(new RotateTransform(degrees));

        // Cached so the work happens here, not on the UI thread at every render.
        var result = new CachedBitmap(new TransformedBitmap(image, transform), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        result.Freeze();
        return result;
    }

    private static BitmapSource DecodeWithMagick(string path, int maxSide)
    {
        using var stream = OpenShared(path);
        using var image = new MagickImage(stream);

        image.AutoOrient();

        // iPhones shoot in Display P3, which looks dull if shown as if it were sRGB. Before
        // Thumbnail, which drops the profile.
        if (image.GetColorProfile() is not null)
            image.TransformColorSpace(ColorProfiles.SRGB);

        if (Math.Max(image.Width, image.Height) > maxSide)
            image.Thumbnail(new MagickGeometry((uint)maxSide, (uint)maxSide));

        using var pixels = image.GetPixels();
        var bytes = pixels.ToByteArray(PixelMapping.BGRA)
            ?? throw new InvalidOperationException("No pixels in " + path);

        var (width, height) = ((int)image.Width, (int)image.Height);
        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bytes, width * 4);
        result.Freeze();
        return result;
    }
}
