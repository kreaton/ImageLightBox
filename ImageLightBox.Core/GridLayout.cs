namespace ImageLightBox.Core;

public readonly record struct GridSize(int Columns, int Rows)
{
    public int Count => Columns * Rows;
}

/// <summary>How many thumbnails fit, how they page, and what size to decode them at.</summary>
public static class GridLayout
{
    /// <summary>Thumbnail size meaning "whatever makes four images fit the window".</summary>
    public const int Standard = 0;

    /// <summary>Space each thumbnail needs around its image: margin and border, plus the file name below it.</summary>
    public const double ChromeWidth = 14;
    public const double ChromeHeight = 38;

    private static readonly int[] DecodeSizes = [256, 512, 1024, 2048, 4096, 8192];

    /// <summary>
    /// The standard size follows the window and always shows two by two. A fixed size shows as
    /// many as fit, and always at least one.
    /// </summary>
    public static GridSize Fit(double width, double height, int thumbnailSize)
    {
        if (thumbnailSize == Standard)
            return new GridSize(2, 2);

        var columns = (int)Math.Floor(width / (thumbnailSize + ChromeWidth));
        var rows = (int)Math.Floor(height / (thumbnailSize + ChromeHeight));
        return new GridSize(Math.Max(columns, 1), Math.Max(rows, 1));
    }

    public static int PageCount(int total, int pageSize) => total == 0 ? 1 : (total + pageSize - 1) / pageSize;

    public static int PageOf(int index, int pageSize) => index / pageSize;

    /// <summary>
    /// Sizes come in steps so that dragging the window edge doesn't decode everything again:
    /// the smallest step at least as long as the longest side on screen, in device pixels.
    /// </summary>
    public static int DecodeSize(double longestSidePixels)
    {
        foreach (var size in DecodeSizes)
            if (size >= longestSidePixels)
                return size;
        return DecodeSizes[^1];
    }
}
