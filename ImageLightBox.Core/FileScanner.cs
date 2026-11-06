using System.Globalization;

namespace ImageLightBox.Core;

/// <summary>Finds the files under a folder, one folder at a time so results can be shown as they arrive.</summary>
public static class FileScanner
{
    // Like Explorer: IMG_9 comes before IMG_10.
    private static readonly StringComparer NameOrder =
        StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.NumericOrdering | CompareOptions.IgnoreCase);

    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
    };

    /// <summary>
    /// Depth first in name order, with a folder's own files before its subfolders', so the
    /// order never changes while the scan is running. Unreadable folders are skipped.
    /// </summary>
    public static IEnumerable<string> Enumerate(string root, CancellationToken cancellation = default)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.TryPop(out var folder))
        {
            cancellation.ThrowIfCancellationRequested();

            string[] files;
            string[] subfolders;
            try
            {
                files = Directory.EnumerateFiles(folder, "*", Options).ToArray();
                subfolders = Directory.EnumerateDirectories(folder, "*", Options).Where(d => !IsLink(d)).ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue; // Vanished or locked while scanning.
            }

            Array.Sort(files, NameOrder);
            foreach (var file in files)
                yield return file;

            // Reversed because the stack pops the last one first.
            Array.Sort(subfolders, NameOrder);
            for (var i = subfolders.Length - 1; i >= 0; i--)
                pending.Push(subfolders[i]);
        }
    }

    /// <summary>Symlinks and junctions can loop. OneDrive folders are reparse points too, but not links.</summary>
    private static bool IsLink(string folder)
    {
        try
        {
            return new DirectoryInfo(folder).LinkTarget is not null;
        }
        catch (IOException)
        {
            return true;
        }
    }
}
