using Microsoft.VisualBasic.FileIO;

namespace ImageLightBox.Core;

/// <summary>Delete, move and copy. None of them ever overwrite an existing file.</summary>
public static class FileActions
{
    /// <summary>Sends the file to the Recycle Bin, so a delete with confirmation turned off can still be undone.</summary>
    public static void Recycle(string path) =>
        FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);

    /// <summary>Opens the file in the program Windows has for its type, without waiting for it.</summary>
    public static void OpenWithDefaultApp(string path) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    /// <summary>Opens File Explorer on the file's folder with the file selected.</summary>
    public static void ShowInExplorer(string path) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { ArgumentList = { $"/select,{path}" } })?.Dispose();

    /// <summary>Returns where the file ended up, which is the same path if it already is in that folder.</summary>
    public static string Move(string path, string folder)
    {
        if (IsInFolder(path, folder))
            return path;

        var target = UniqueTarget(path, folder);
        File.Move(path, target, overwrite: false);
        return target;
    }

    /// <summary>Returns the path of the copy.</summary>
    public static string Copy(string path, string folder)
    {
        var target = UniqueTarget(path, folder);
        File.Copy(path, target, overwrite: false);
        return target;
    }

    public static bool IsInFolder(string path, string folder) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(Path.GetFullPath(path))!),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The scan is recursive, so images from different subfolders often share a name when they
    /// land in one folder: the later ones become "name (1).jpg", like in Explorer.
    /// </summary>
    public static string UniqueTarget(string path, string folder)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        var target = Path.Combine(folder, name + extension);
        for (var i = 1; File.Exists(target); i++)
            target = Path.Combine(folder, $"{name} ({i}){extension}");
        return target;
    }
}
