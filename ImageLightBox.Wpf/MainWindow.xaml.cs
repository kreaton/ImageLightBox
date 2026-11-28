using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageLightBox.Core;
using Microsoft.Win32;
using Velopack;
using Velopack.Sources;

namespace ImageLightBox.Wpf;

public partial class MainWindow : Window
{
    private const int WheelNotch = 120;

    // Public GitHub repo whose releases BuildRelease.ps1 uploads to
    private const string RepoUrl = "https://github.com/kreaton/ImageLightBox";

    private readonly UpdateManager _updateManager = new(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly ThumbnailLoader _loader;
    private readonly List<string> _files = [];
    private readonly ObservableCollection<Tile> _tiles = [];

    // What the background checks found, and the files that left the list while they ran.
    private readonly Dictionary<string, FileCheck> _checks = new(StringComparer.OrdinalIgnoreCase);
    private readonly DuplicateIndex _duplicates = new();
    private readonly HashSet<string> _gone = new(StringComparer.OrdinalIgnoreCase);
    private bool _checksDone;

    private UniformGrid? _panel;
    private FullScreenWindow? _fullScreen;
    private MapWindow? _map;
    private MapLocation? _location;
    private CancellationTokenSource? _scan;
    private GridSize _grid = new(2, 2);
    private int _decodeSize = 512;
    private int _page;
    private int _selected = -1;
    private int _exifRequest;
    private int _wheelDelta;
    private bool _acting;

    public MainWindow()
    {
        InitializeComponent();

        _loader = new ThumbnailLoader(Dispatcher);
        _loader.Loaded += OnImageLoaded;
        ThumbList.ItemsSource = _tiles;

        ConfirmDeleteBox.IsChecked = _settings.ConfirmDelete;
        ConfirmMoveBox.IsChecked = _settings.ConfirmMove;
        ImagesOnlyBox.IsChecked = _settings.ImagesOnly;
        SourceBox.Text = _settings.SourceFolder ?? "";
        SizeBox.SelectedItem = SizeBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => TagOf(i) == _settings.ThumbnailSize) ?? SizeBox.Items[0];

    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(_settings.SourceFolder))
            _ = ScanAsync();
        else
            SourceBox.Focus();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _scan?.Cancel();
        _settings.Save();
        _map?.CloseForGood();
    }

    private static int TagOf(ComboBoxItem item) => int.Parse((string)item.Tag);

    // ---- Scanning ----

    private async Task ScanAsync()
    {
        var root = SourceBox.Text.Trim().Trim('"');
        if (!Directory.Exists(root))
        {
            SetStatus($"Folder not found: {root}");
            return;
        }

        _scan?.Cancel();
        var scan = _scan = new CancellationTokenSource();

        _settings.SourceFolder = root;
        _files.Clear();
        _checks.Clear();
        _duplicates.Clear();
        _gone.Clear();
        _checksDone = false;
        UpdateCheckSummary("");
        _page = 0;
        Select(-1);
        ShowPage();
        ThumbHost.Focus();
        SetStatus("Scanning...");
        var imagesOnly = _settings.ImagesOnly;
        var noun = imagesOnly ? "images" : "files";

        try
        {
            await Task.Run(() =>
            {
                var batch = new List<string>();
                var sinceFlush = Stopwatch.StartNew();
                foreach (var file in FileScanner.Enumerate(root, scan.Token))
                {
                    if (imagesOnly && !FileKinds.IsImage(file))
                        continue;
                    batch.Add(file);
                    if (batch.Count >= 500 || sinceFlush.ElapsedMilliseconds >= 100)
                    {
                        AddBatch(batch, scan, noun);
                        sinceFlush.Restart();
                    }
                }
                AddBatch(batch, scan, noun);
            }, scan.Token);
        }
        catch (OperationCanceledException)
        {
            return; // A newer scan owns the list now.
        }

        SetStatus(_files.Count == 0 ? $"No {noun} in {root}" : $"Found {_files.Count:N0} {noun} in {root}");
        RequestImages();
        await CheckFilesAsync(scan);
    }

    // ---- Checks: duplicates, wrong extensions, file dates ----

    /// <summary>
    /// Looks inside every file once the scan is done: its real format and its EXIF date, then
    /// which files are identical. Tags appear on the thumbnails as results come in.
    /// </summary>
    private async Task CheckFilesAsync(CancellationTokenSource scan)
    {
        var files = _files.ToArray();
        if (files.Length == 0)
            return;

        var found = new System.Collections.Concurrent.ConcurrentQueue<FileCheck>();
        var done = 0;
        void Flush()
        {
            if (scan != _scan)
                return;
            var touchedSelected = false;
            while (found.TryDequeue(out var check))
            {
                if (_gone.Contains(check.Path))
                    continue;
                _checks[check.Path] = check;
                touchedSelected |= _selected >= 0 && _files[_selected] == check.Path;
            }
            RefreshTileChecks();
            UpdateCheckSummary($"Checking files... {done:N0} of {files.Length:N0}");
            if (touchedSelected)
                _ = LoadExifAsync(_selected);
        }

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => Flush();
        timer.Start();
        try
        {
            // Two at a time, so the thumbnails still get the disk.
            await Task.Run(() => Parallel.ForEach(files,
                new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = scan.Token },
                path =>
                {
                    try
                    {
                        found.Enqueue(FileChecker.Check(path));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Gone or locked: nothing to say about it.
                    }
                    Interlocked.Increment(ref done);
                }), scan.Token);
            timer.Stop();
            Flush();

            UpdateCheckSummary("Looking for duplicates...");
            var sizes = _checks.Values.Select(c => (c.Path, c.Length)).ToList();
            var groups = await Task.Run(() => DuplicateFinder.Find(sizes, scan.Token), scan.Token);
            if (scan != _scan)
                return;

            foreach (var group in groups)
                _duplicates.Add(group.Where(p => !_gone.Contains(p)));
            _checksDone = true;
            RefreshTileChecks();
            UpdateCheckSummary();
            if (_selected >= 0 && _duplicates.IsDuplicate(_files[_selected]))
                _ = LoadExifAsync(_selected);
        }
        catch (OperationCanceledException)
        {
            // A newer scan owns the list now.
        }
        finally
        {
            timer.Stop();
        }
    }

    private void RefreshTileChecks()
    {
        foreach (var tile in _tiles)
            ApplyChecks(tile);
    }

    private void ApplyChecks(Tile tile)
    {
        var others = _duplicates.OthersOf(tile.Path);
        tile.DuplicateTip = others.Count == 0
            ? null
            : "Duplicate: exactly the same content as\n" + string.Join("\n", others.Take(8)) + (others.Count > 8 ? $"\n...and {others.Count - 8} more" : "");

        _checks.TryGetValue(tile.Path, out var check);
        if (check?.WrongExtension is { } actual)
        {
            tile.ExtensionTip = $"Wrong extension: this is really a {actual.Name} file.\nIts name should end in {actual.PreferredExtension}.";
            tile.FixExtensionLabel = $"Rename to {check.FixedName}";
        }
        else
        {
            tile.ExtensionTip = null;
            tile.FixExtensionLabel = "Fix the extension";
        }

        tile.DateTip = check is { DateDiffers: true, Taken: { } taken }
            ? $"The file date differs from the date taken.\nTaken: {taken.LocalDateTime:g}\nFile modified: {check.ModifiedUtc.ToLocalTime():g}"
            : null;
    }

    /// <summary>The selected file's problems, shown first in the side pane.</summary>
    private IEnumerable<ExifEntry> ProblemEntries(string path)
    {
        const string section = "Problems";
        foreach (var other in _duplicates.OthersOf(path))
            yield return new(section, "Duplicate of", other);
        if (!_checks.TryGetValue(path, out var check))
            yield break;
        if (check.WrongExtension is { } actual)
            yield return new(section, "Wrong extension", $"Really a {actual.Name} file, so it should be named {check.FixedName}");
        if (check is { DateDiffers: true, Taken: { } taken })
            yield return new(section, "File date differs from date taken",
                $"Taken {taken.LocalDateTime:g}, file modified {check.ModifiedUtc.ToLocalTime():g}");
    }

    /// <summary>Progress while checking, the totals after.</summary>
    private void UpdateCheckSummary(string? progress = null)
    {
        var dates = _checks.Values.Count(c => c.DateDiffers);
        var extensions = _checks.Values.Count(c => c.WrongExtension is not null);
        SyncDatesButton.IsEnabled = dates > 0;
        SyncDatesText.Text = dates > 0 ? $"Update all dates ({dates:N0})" : "Update all dates";
        FixExtensionsButton.IsEnabled = extensions > 0;
        FixExtensionsText.Text = extensions > 0 ? $"Update all extensions ({extensions:N0})" : "Update all extensions";

        if (progress is not null || !_checksDone)
        {
            CheckText.Text = progress ?? CheckText.Text;
            return;
        }

        var parts = new List<string>();
        if (_duplicates.Count > 0)
            parts.Add(_duplicates.Count == 1 ? "1 duplicate (D)" : $"{_duplicates.Count:N0} duplicates (D)");
        if (extensions > 0)
            parts.Add(extensions == 1 ? "1 wrong extension (E)" : $"{extensions:N0} wrong extensions (E)");
        if (dates > 0)
            parts.Add(dates == 1 ? "1 date differs (T)" : $"{dates:N0} dates differ (T)");
        CheckText.Text = parts.Count == 0 ? "No duplicates or other problems" : string.Join("  ·  ", parts);
    }

    private void OnSyncDate(object sender, RoutedEventArgs e)
    {
        if (_selected >= 0 && _checks.TryGetValue(_files[_selected], out var check) && check.DateDiffers)
            _ = SyncDatesAsync([check.Path]);
    }

    private void OnSyncAllDates(object sender, RoutedEventArgs e)
    {
        var paths = _checks.Values.Where(c => c.DateDiffers).Select(c => c.Path).ToList();
        if (paths.Count == 0)
            return;
        var files = paths.Count == 1 ? "1 file" : $"{paths.Count:N0} files";
        if (Confirm($"Set the created and modified dates of {files} to the date taken in their EXIF?\n\nThe old dates aren't kept anywhere.", "Update all dates"))
            _ = SyncDatesAsync(paths);
    }

    private void OnFixAllExtensions(object sender, RoutedEventArgs e) => _ = FixAllExtensionsAsync();

    /// <summary>
    /// Renames every file tagged E in place, as X does for one. A name already taken is left
    /// alone and reported, as nothing is ever overwritten.
    /// </summary>
    private async Task FixAllExtensionsAsync()
    {
        if (_acting)
            return;
        var work = _checks.Values.Where(c => c.FixedName is not null).Select(c => (c.Path, Name: c.FixedName!)).ToList();
        if (work.Count == 0)
            return;

        var list = string.Join("\n", work.Take(15).Select(w => $"{Path.GetFileName(w.Path)}  →  {w.Name}"))
            + (work.Count > 15 ? $"\n...and {work.Count - 15} more" : "");
        var files = work.Count == 1 ? "1 file" : $"{work.Count:N0} files";
        if (!Confirm($"Rename {files} to the extension that fits their content?\n\n{list}", "Update all extensions"))
            return;

        _acting = true;
        try
        {
            var (renamed, failures) = await Task.Run(() =>
            {
                var renamed = new List<(string From, string To)>();
                var failures = new List<string>();
                foreach (var (path, name) in work)
                {
                    try
                    {
                        renamed.Add((path, FileActions.Rename(path, name)));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        failures.Add($"{Path.GetFileName(path)}: {ex.Message}");
                    }
                }
                return (renamed, failures);
            });

            // Renamed in place, so every file keeps its position and the selection stays put.
            foreach (var (from, to) in renamed)
            {
                if (_files.IndexOf(from) is var at && at >= 0)
                    _files[at] = to;
                _loader.Forget(from);
                if (_checks.Remove(from, out var check))
                    _checks[to] = check with { Path = to };
                _duplicates.Rename(from, to);
                _gone.Add(from);
            }
            UpdateCheckSummary();
            ShowPage();
            if (_selected >= 0)
                Select(_selected);

            SetStatus($"Fixed the extension of {renamed.Count:N0} file{(renamed.Count == 1 ? "" : "s")}"
                + (failures.Count > 0 ? $", {failures.Count:N0} failed" : ""));
            if (failures.Count > 0)
                MessageBox.Show(ActiveOwner, string.Join("\n", failures.Take(15)), "Some files couldn't be renamed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _acting = false;
        }
    }

    /// <summary>Sets each file's dates to when it was taken, then looks at it again so the tag goes.</summary>
    private async Task SyncDatesAsync(IReadOnlyList<string> paths)
    {
        if (_acting)
            return;

        var work = paths
            .Select(p => _checks.TryGetValue(p, out var c) && c.Taken is { } taken ? (Path: p, Taken: taken) : default)
            .Where(w => w.Path is not null)
            .ToList();
        if (work.Count == 0)
            return;

        _acting = true;
        try
        {
            var (updated, failures) = await Task.Run(() =>
            {
                var updated = new List<FileCheck>();
                var failures = new List<string>();
                foreach (var (path, taken) in work)
                {
                    try
                    {
                        FileChecker.SyncDate(path, taken);
                        updated.Add(FileChecker.Check(path));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        failures.Add($"{Path.GetFileName(path)}: {ex.Message}");
                    }
                }
                return (updated, failures);
            });

            foreach (var check in updated.Where(c => !_gone.Contains(c.Path)))
                _checks[check.Path] = check;
            RefreshTileChecks();
            UpdateCheckSummary();
            if (_selected >= 0)
                _ = LoadExifAsync(_selected);

            SetStatus(work.Count == 1 && failures.Count == 0
                ? $"Set the date of {Path.GetFileName(work[0].Path)} to {work[0].Taken.LocalDateTime:g}, when it was taken"
                : $"Synced the dates of {updated.Count:N0} files" + (failures.Count > 0 ? $", {failures.Count:N0} failed" : ""));
            if (failures.Count > 0)
                MessageBox.Show(ActiveOwner, string.Join("\n", failures.Take(15)), "Some dates couldn't be set", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _acting = false;
        }
    }

    private void OnKeepThis(object sender, RoutedEventArgs e) => _ = KeepThisAsync();

    /// <summary>
    /// Sends every other copy of the selected file to the Recycle Bin, wherever they are, and
    /// keeps this one selected. Always asks, as the copies are mostly not on screen.
    /// </summary>
    private async Task KeepThisAsync()
    {
        if (_selected < 0 || _acting)
            return;

        var keep = _files[_selected];
        var others = _duplicates.OthersOf(keep).ToList();
        if (others.Count == 0)
            return;

        var list = string.Join("\n", others.Take(15)) + (others.Count > 15 ? $"\n...and {others.Count - 15} more" : "");
        var copies = others.Count == 1 ? "its duplicate" : $"its {others.Count} duplicates";
        if (!Confirm($"Keep \"{Path.GetFileName(keep)}\" and move {copies} to the Recycle Bin?\n\n{list}", "Delete duplicates"))
            return;

        _acting = true;
        try
        {
            var (deleted, failures) = await Task.Run(() =>
            {
                var deleted = new List<string>();
                var failures = new List<string>();
                foreach (var path in others)
                {
                    try
                    {
                        FileActions.Recycle(path);
                        deleted.Add(path);
                    }
                    catch (FileNotFoundException)
                    {
                        deleted.Add(path); // Already gone, which is what was wanted.
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
                    {
                        failures.Add($"{path}: {ex.Message}");
                    }
                }
                return (deleted, failures);
            });

            foreach (var path in deleted)
                ForgetFile(path);
            UpdateCheckSummary();

            // The kept file has moved up if copies before it went.
            _selected = _files.IndexOf(keep);
            if (_selected >= 0)
                _page = GridLayout.PageOf(_selected, _grid.Count);
            ShowPage();
            Select(_selected);

            SetStatus($"Kept {Path.GetFileName(keep)} and deleted {deleted.Count:N0} duplicate{(deleted.Count == 1 ? "" : "s")} (they're in the Recycle Bin)"
                + (failures.Count > 0 ? $", {failures.Count:N0} failed" : ""));
            if (failures.Count > 0)
                MessageBox.Show(ActiveOwner, string.Join("\n", failures.Take(15)), "Some duplicates couldn't be deleted", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _acting = false;
        }
    }

    private void OnFixExtension(object sender, RoutedEventArgs e)
    {
        if (_selected >= 0 && _checks.TryGetValue(_files[_selected], out var check) && check.FixedName is { } name)
            _ = RenameToAsync(_selected, name);
    }

    private void AddBatch(List<string> batch, CancellationTokenSource scan, string noun)
    {
        var files = batch.ToArray();
        batch.Clear();
        Dispatcher.Invoke(() =>
        {
            if (scan != _scan)
                return;

            _files.AddRange(files);
            if (_tiles.Count < _grid.Count)
                ShowPage();
            else
                UpdatePager();
            if (_files.Count > 0)
                SetStatus($"Scanning... {_files.Count:N0} {noun} so far");
        });
    }

    // ---- The page of thumbnails ----

    private void OnThumbPanelLoaded(object sender, RoutedEventArgs e)
    {
        _panel = (UniformGrid)sender;
        _panel.Columns = _grid.Columns;
        _panel.Rows = _grid.Rows;
    }

    private void OnThumbHostSizeChanged(object sender, SizeChangedEventArgs e) => UpdateGrid();

    private void OnSizeSelected(object sender, SelectionChangedEventArgs e)
    {
        if (SizeBox.SelectedItem is not ComboBoxItem item)
            return;
        _settings.ThumbnailSize = TagOf(item);
        UpdateGrid();
        if (IsLoaded)
            ThumbHost.Focus();
    }

    /// <summary>Works out what fits, and keeps the selected or else the first file on screen if that changed.</summary>
    private void UpdateGrid()
    {
        var (width, height) = (ThumbHost.ActualWidth, ThumbHost.ActualHeight);
        if (width <= 0 || height <= 0)
            return;

        var grid = GridLayout.Fit(width, height, _settings.ThumbnailSize);
        var decodeSize = GridLayout.DecodeSize(
            Math.Max(width / grid.Columns, height / grid.Rows) * VisualTreeHelper.GetDpi(this).DpiScaleX);
        if (grid == _grid && decodeSize == _decodeSize)
            return;

        var anchor = _selected >= 0 ? _selected : _page * _grid.Count;
        _grid = grid;
        _decodeSize = decodeSize;
        _page = GridLayout.PageOf(anchor, grid.Count);
        if (_panel is not null)
        {
            _panel.Columns = grid.Columns;
            _panel.Rows = grid.Rows;
        }
        ShowPage();
    }

    private void ShowPage()
    {
        var pageSize = _grid.Count;
        _page = Math.Clamp(_page, 0, GridLayout.PageCount(_files.Count, pageSize) - 1);

        _tiles.Clear();
        var start = _page * pageSize;
        for (var i = start; i < Math.Min(start + pageSize, _files.Count); i++)
        {
            var image = _loader.GetBest(_files[i], _decodeSize, out var failed);
            var tile = new Tile(i, _files[i]) { Image = image, Failed = failed, IsSelected = i == _selected, HasLocation = i == _selected && _location is not null };
            ApplyChecks(tile);
            _tiles.Add(tile);
        }

        UpdatePager();
        RequestImages();
    }

    private void UpdatePager()
    {
        var pageSize = _grid.Count;
        var pages = GridLayout.PageCount(_files.Count, pageSize);
        var first = _page * pageSize + 1;
        var last = Math.Min(first + pageSize - 1, _files.Count);

        PageText.Text = _files.Count == 0
            ? ""
            : $"{first:N0}–{last:N0} of {_files.Count:N0}  ·  Page {_page + 1:N0} of {pages:N0}";
        PageUpButton.IsEnabled = _page > 0;
        PageDownButton.IsEnabled = _page < pages - 1;

        EmptyHint.Visibility = _files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_files.Count == 0 && _settings.SourceFolder is not null && _scan is not null)
            EmptyHint.Text = "No files to show.";
    }

    /// <summary>What's on screen first, then the neighbours so that the next flip is instant.</summary>
    private void RequestImages()
    {
        if (_fullScreen is { } fullScreen)
        {
            var requests = new[] { _selected, _selected + 1, _selected - 1, _selected + 2 }
                .Where(i => i >= 0 && i < _files.Count && FileKinds.IsImage(_files[i]))
                .Select(i => new ImageRequest(_files[i], fullScreen.DecodeSize));
            _loader.Request(requests);
            return;
        }

        var pageSize = _grid.Count;
        var pages = new[] { _page, _page + 1, _page - 1, _page + 2 };
        _loader.Request(pages
            .SelectMany(page => Enumerable.Range(page * pageSize, pageSize))
            .Where(i => i >= 0 && i < _files.Count && FileKinds.IsImage(_files[i]))
            .Select(i => new ImageRequest(_files[i], _decodeSize)));
    }

    private void OnImageLoaded(ImageRequest request, BitmapSource? image)
    {
        if (_fullScreen is { } fullScreen && fullScreen.ImagePath == request.Path
            && (request.Size == fullScreen.DecodeSize || !fullScreen.HasImage))
            fullScreen.SetImage(image, failed: image is null);

        foreach (var tile in _tiles)
        {
            if (tile.Path != request.Path)
                continue;
            if (request.Size == _decodeSize || tile.Image is null)
            {
                tile.Image = image ?? tile.Image;
                tile.Failed = image is null && request.Size == _decodeSize;
            }
            break;
        }
    }

    // ---- Paging and selecting ----

    /// <summary>Paging away deselects, so that a key never acts on a file that isn't on screen.</summary>
    private void GoToPage(int page)
    {
        page = Math.Clamp(page, 0, GridLayout.PageCount(_files.Count, _grid.Count) - 1);
        if (page == _page)
            return;
        _page = page;
        Select(-1);
        ShowPage();
    }

    private void Select(int index)
    {
        _selected = index;

        if (index >= 0 && GridLayout.PageOf(index, _grid.Count) != _page)
        {
            _page = GridLayout.PageOf(index, _grid.Count);
            ShowPage();
        }
        else
        {
            foreach (var tile in _tiles)
                tile.IsSelected = tile.Index == index;
        }

        _ = LoadExifAsync(index);

        if (_fullScreen is not null && index >= 0)
            ShowInFullScreen();
    }

    private async Task LoadExifAsync(int index)
    {
        var request = ++_exifRequest;
        SetLocation(null);
        if (index < 0)
        {
            ExifList.ItemsSource = null;
            ExifHint.Visibility = Visibility.Visible;
            return;
        }

        var path = _files[index];
        var details = await Task.Run(() => ExifReader.ReadDetails(path));
        if (request != _exifRequest)
            return; // The selection moved on while reading.

        var view = new ListCollectionView(ProblemEntries(path).Concat(details.Entries).ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ExifEntry.Section)));
        ExifList.ItemsSource = view;
        ExifHint.Visibility = Visibility.Collapsed;
        ExifScroll.ScrollToTop();

        SetLocation(details.Location);

        // A closed map is only hidden. It must not keep sending locations.
        if (_map is { IsVisible: true })
            _map.ShowLocation(details.Location, Path.GetFileName(path));
    }

    private void SetLocation(MapLocation? location)
    {
        _location = location;
        foreach (var tile in _tiles)
            tile.HasLocation = tile.Index == _selected && location is not null;
    }

    private void OnTileLeftDown(object sender, MouseButtonEventArgs e)
    {
        ThumbHost.Focus();
        Select(((Tile)((FrameworkElement)sender).DataContext).Index);
        if (e.ClickCount == 2)
            OpenFullScreen();
    }

    // Before the context menu opens, whose commands then apply to this image.
    private void OnTileRightDown(object sender, MouseButtonEventArgs e)
    {
        ThumbHost.Focus();
        Select(((Tile)((FrameworkElement)sender).DataContext).Index);
    }

    private void OnThumbWheel(object sender, MouseWheelEventArgs e)
    {
        // Precision touchpads send many small deltas for what is one notch on a mouse.
        _wheelDelta += e.Delta;
        if (Math.Abs(_wheelDelta) >= WheelNotch)
        {
            GoToPage(_page + (_wheelDelta > 0 ? -1 : 1));
            _wheelDelta = 0;
        }
        e.Handled = true;
    }

    private void OnPageUp(object sender, RoutedEventArgs e) => GoToPage(_page - 1);

    private void OnPageDown(object sender, RoutedEventArgs e) => GoToPage(_page + 1);

    // ---- Keyboard ----

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // No shortcuts underneath the About card. F1 toggles it.
        if (AboutOverlay.Visibility == Visibility.Visible)
        {
            if (e.Key is Key.Escape or Key.F1)
            {
                CloseAbout();
                e.Handled = true;
            }
            return;
        }
        if (e.Key == Key.F1 && Keyboard.Modifiers == ModifierKeys.None)
        {
            OnAbout(this, e);
            e.Handled = true;
            return;
        }

        // Typing in the source box or choosing a size keeps its keys.
        if (Keyboard.FocusedElement is TextBox or ComboBox or ComboBoxItem)
            return;
        e.Handled = HandleKey(e.Key, e.IsRepeat);
    }

    private bool HandleKey(Key key, bool isRepeat)
    {
        if (Keyboard.Modifiers != ModifierKeys.None)
            return false;

        // Holding a key down must not run through a whole folder of files.
        if (key is Key.Delete or Key.M or Key.C or Key.L or Key.F2 or Key.Enter or Key.F11
            or Key.K or Key.X or Key.T or Key.O or Key.E)
        {
            if (!isRepeat)
                _ = RunKeyCommand(key);
            return true;
        }

        if (_fullScreen is not null)
        {
            switch (key)
            {
                case Key.Left or Key.Up or Key.PageUp: Step(-1); return true;
                case Key.Right or Key.Down or Key.PageDown: Step(1); return true;
                case Key.Home: SelectIfAny(0); return true;
                case Key.End: SelectIfAny(_files.Count - 1); return true;
                default: return false;
            }
        }

        switch (key)
        {
            case Key.PageUp: GoToPage(_page - 1); return true;
            case Key.PageDown: GoToPage(_page + 1); return true;
            case Key.Home: GoToPage(0); return true;
            case Key.End: GoToPage(int.MaxValue); return true;
            case Key.Left: Step(-1); return true;
            case Key.Right: Step(1); return true;
            case Key.Up: Step(-_grid.Columns); return true;
            case Key.Down: Step(_grid.Columns); return true;
            case Key.Escape: Select(-1); return true;
            default: return false;
        }
    }

    private async Task RunKeyCommand(Key key)
    {
        switch (key)
        {
            case Key.Delete: await DeleteAsync(); break;
            case Key.M: await MoveAsync(); break;
            case Key.C: await CopyAsync(); break;
            case Key.L: ShowMap(); break;
            case Key.F2: await RenameAsync(); break;
            case Key.K: await KeepThisAsync(); break;
            case Key.X: OnFixExtension(this, new RoutedEventArgs()); break;
            case Key.T: OnSyncDate(this, new RoutedEventArgs()); break;
            case Key.O: OnOpenWithDefault(this, new RoutedEventArgs()); break;
            case Key.E: OnShowInExplorer(this, new RoutedEventArgs()); break;
            default: OpenFullScreen(); break;
        }
    }

    private void Step(int delta)
    {
        if (_files.Count == 0)
            return;
        Select(_selected < 0
            ? _page * _grid.Count
            : Math.Clamp(_selected + delta, 0, _files.Count - 1));
    }

    private void SelectIfAny(int index)
    {
        if (_files.Count > 0)
            Select(index);
    }

    // ---- Source folder ----

    private void OnOpen(object sender, RoutedEventArgs e) => _ = ScanAsync();

    private void OnSourceKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            _ = ScanAsync();
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose the source folder" };
        if (Directory.Exists(SourceBox.Text))
            dialog.InitialDirectory = SourceBox.Text;
        if (dialog.ShowDialog(this) == true)
        {
            SourceBox.Text = dialog.FolderName;
            _ = ScanAsync();
        }
    }

    // ---- Drag and drop a folder ----

    // A dropped folder becomes the source; a dropped file opens the folder it is in.
    private static string? DroppedFolder(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths)
            return null;
        var path = paths[0];
        if (Directory.Exists(path))
            return path;
        return File.Exists(path) ? Path.GetDirectoryName(path) : null;
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedFolder(e) is not null ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnPreviewDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DroppedFolder(e) is not { } folder)
            return;
        SourceBox.Text = folder;
        Activate();
        _ = ScanAsync();
    }

    // ---- Actions on the selected image ----

    private void OnConfirmChanged(object sender, RoutedEventArgs e)
    {
        _settings.ConfirmDelete = ConfirmDeleteBox.IsChecked == true;
        _settings.ConfirmMove = ConfirmMoveBox.IsChecked == true;
    }

    private void OnImagesOnlyChanged(object sender, RoutedEventArgs e)
    {
        _settings.ImagesOnly = ImagesOnlyBox.IsChecked == true;
        if (_settings.SourceFolder is not null && Directory.Exists(SourceBox.Text.Trim().Trim('"')))
            _ = ScanAsync();
    }

    private void OnDelete(object sender, RoutedEventArgs e) => _ = DeleteAsync();

    private void OnMove(object sender, RoutedEventArgs e) => _ = MoveAsync();

    private void OnCopy(object sender, RoutedEventArgs e) => _ = CopyAsync();

    private void OnRename(object sender, RoutedEventArgs e) => _ = RenameAsync();

    private void OnFullScreen(object sender, RoutedEventArgs e) => OpenFullScreen();

    private void OnMap(object sender, RoutedEventArgs e) => ShowMap();

    private void OnOpenWithDefault(object sender, RoutedEventArgs e)
    {
        if (_selected >= 0)
            OpenExternally(_files[_selected], FileActions.OpenWithDefaultApp, "in its default program");
    }

    private void OnShowInExplorer(object sender, RoutedEventArgs e)
    {
        if (_selected >= 0)
            OpenExternally(_files[_selected], FileActions.ShowInExplorer, "in File Explorer");
    }

    private Window ActiveOwner => _fullScreen ?? (Window)this;

    private async Task DeleteAsync()
    {
        if (_selected < 0 || _acting)
            return;

        var path = _files[_selected];
        var name = Path.GetFileName(path);
        if (_settings.ConfirmDelete && !Confirm($"Move \"{name}\" to the Recycle Bin?", "Delete file"))
            return;

        await ActAsync(path, () =>
        {
            FileActions.Recycle(path);
            return $"Deleted {name} (it's in the Recycle Bin)";
        }, removesFile: true);
    }

    private async Task MoveAsync()
    {
        if (_selected < 0 || _acting)
            return;

        var path = _files[_selected];
        var name = Path.GetFileName(path);
        if (PickFolder($"Move {name} to") is not { } folder)
            return;
        if (FileActions.IsInFolder(path, folder))
        {
            SetStatus($"{name} already is in {folder}");
            return;
        }
        if (_settings.ConfirmMove && !Confirm($"Move \"{name}\" to {folder}?", "Move file"))
            return;

        await ActAsync(path, () => $"Moved {name} to {FileActions.Move(path, folder)}", removesFile: true);
    }

    private async Task CopyAsync()
    {
        if (_selected < 0 || _acting)
            return;

        var path = _files[_selected];
        var name = Path.GetFileName(path);
        if (PickFolder($"Copy {name} to") is not { } folder)
            return;

        await ActAsync(path, () => $"Copied {name} to {FileActions.Copy(path, folder)}", removesFile: false);
    }

    /// <summary>Renames in place: the file keeps its position in the list and stays selected.</summary>
    private async Task RenameAsync()
    {
        if (_selected < 0 || _acting)
            return;

        var index = _selected;
        var path = _files[index];
        var dialog = new RenameWindow(ActiveOwner, path);
        if (dialog.ShowDialog() != true)
            return;
        await RenameToAsync(index, dialog.NewName);
    }

    private async Task RenameToAsync(int index, string newName)
    {
        var path = _files[index];
        if (newName == Path.GetFileName(path) || _acting)
            return;

        _acting = true;
        try
        {
            var renamed = await Task.Run(() => FileActions.Rename(path, newName));
            if (_files.IndexOf(path) is var at && at >= 0)
                _files[at] = renamed;
            _loader.Forget(path);
            if (_checks.Remove(path, out var check))
                _checks[renamed] = check with { Path = renamed };
            _duplicates.Rename(path, renamed);
            _gone.Add(path); // A check still running on the old name has nothing to say any more.
            UpdateCheckSummary();
            ShowPage();
            if (_selected == index)
                Select(index);
            SetStatus($"Renamed {Path.GetFileName(path)} to {newName}");
        }
        catch (FileNotFoundException)
        {
            RemoveFromList(path);
            SetStatus($"{Path.GetFileName(path)} is gone: something else moved or deleted it");
        }
        catch (Exception ex)
        {
            SetStatus($"Failed: {ex.Message}");
            MessageBox.Show(ActiveOwner, ex.Message, Path.GetFileName(path), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _acting = false;
        }
    }

    private bool Confirm(string message, string title) =>
        MessageBox.Show(ActiveOwner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes) == MessageBoxResult.Yes;

    private string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        var initial = _settings.LastTargetFolder ?? _settings.SourceFolder;
        if (Directory.Exists(initial))
            dialog.InitialDirectory = initial;
        if (dialog.ShowDialog(ActiveOwner) != true)
            return null;

        _settings.LastTargetFolder = dialog.FolderName;
        return dialog.FolderName;
    }

    private async Task ActAsync(string path, Func<string> action, bool removesFile)
    {
        _acting = true;
        try
        {
            var status = await Task.Run(action);
            if (removesFile)
                RemoveFromList(path);
            SetStatus(status);
        }
        catch (FileNotFoundException)
        {
            RemoveFromList(path);
            SetStatus($"{Path.GetFileName(path)} is gone: something else moved or deleted it");
        }
        catch (Exception ex)
        {
            SetStatus($"Failed: {ex.Message}");
            MessageBox.Show(ActiveOwner, ex.Message, Path.GetFileName(path), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _acting = false;
        }
    }

    /// <summary>The next file moves into its place and gets selected, ready for the next decision.</summary>
    private void RemoveFromList(string path)
    {
        var index = ForgetFile(path);
        if (index < 0)
            return;
        UpdateCheckSummary();

        var next = _files.Count == 0 ? -1 : Math.Min(index, _files.Count - 1);
        _selected = next;
        if (next >= 0)
            _page = GridLayout.PageOf(next, _grid.Count);
        ShowPage();

        if (next < 0)
            _fullScreen?.Close();
        Select(next);
    }

    /// <summary>Takes a file out of the list and everything known about it. Returns where it was, or -1.</summary>
    private int ForgetFile(string path)
    {
        var index = _files.IndexOf(path);
        if (index >= 0)
            _files.RemoveAt(index);
        _loader.Forget(path);
        _checks.Remove(path);
        _duplicates.Remove(path);
        _gone.Add(path);
        return index;
    }

    private void PlayVideo(string path) => OpenExternally(path, FileActions.OpenWithDefaultApp, "in its default player");

    private void OpenExternally(string path, Action<string> open, string where)
    {
        var name = Path.GetFileName(path);
        try
        {
            open(path);
            SetStatus($"Opened {name} {where}");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            SetStatus($"Can't open {name}: {ex.Message}");
        }
    }

    // ---- About ----

    private void OnAbout(object sender, RoutedEventArgs e)
    {
        // Set by hand, as a plain Image would take the first, 16 px frame and blur it.
        AboutIcon.Source ??= System.Windows.Media.Imaging.BitmapDecoder
            .Create(new Uri("pack://application:,,,/ImageLightBox;component/app.ico"), System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad)
            .Frames.MaxBy(f => f.PixelWidth);
        AboutVersion.Text = $"Version {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}";
        MainLayer.IsEnabled = false;
        AboutOverlay.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => AboutCloseButton.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CloseAbout()
    {
        AboutOverlay.Visibility = Visibility.Collapsed;
        MainLayer.IsEnabled = true;
        ThumbHost.Focus();
    }

    private void OnAboutClose(object sender, RoutedEventArgs e) => CloseAbout();

    private async void OnCheckForUpdates(object sender, RoutedEventArgs e)
    {
        if (!_updateManager.IsInstalled)
        {
            MessageBox.Show(this, "Updates are only available in the installed app.", "Check for updates", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        AboutUpdateButton.IsEnabled = false;
        try
        {
            var newVersion = await _updateManager.CheckForUpdatesAsync();
            if (newVersion is null)
            {
                MessageBox.Show(this, "You have the latest version.", "Check for updates", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(this,
                $"Version {newVersion.TargetFullRelease.Version} is available. Download and install it now?",
                "Check for updates", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
                return;

            await _updateManager.DownloadUpdatesAsync(newVersion);

            // The restart ends the process without raising Closing, which is where settings are saved.
            _settings.Save();
            _updateManager.ApplyUpdatesAndRestart(newVersion);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Update failed: {ex.Message}", "Check for updates", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            AboutUpdateButton.IsEnabled = true;
        }
    }

    private void OnAboutOverlayClick(object sender, MouseButtonEventArgs e) => CloseAbout();

    // A click on the card itself must not reach the overlay behind it.
    private void OnAboutCardClick(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void OnCreditLink(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            FileActions.OpenWithDefaultApp(e.Uri.AbsoluteUri);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            SetStatus($"Can't open {e.Uri}: {ex.Message}");
        }
        e.Handled = true;
    }

    // ---- Map ----

    /// <summary>Nothing leaves this PC until here: the map window sends the location to OpenStreetMap.</summary>
    private void ShowMap()
    {
        if (_selected < 0 || _location is null)
            return;

        // Owned by this window even from full screen, as closing its owner would end it.
        if (_map is null)
        {
            _map = new MapWindow { Owner = this };
            _map.Closed += (_, _) => _map = null;
        }

        // Full screen is a sibling, and would cover it after a click.
        _map.Topmost = _fullScreen is not null;
        _map.Show();
        _map.ShowLocation(_location, Path.GetFileName(_files[_selected]));
    }

    // ---- Full screen ----

    private void OpenFullScreen()
    {
        if (_selected >= 0 && FileIcon.IsVideo(_files[_selected]))
        {
            PlayVideo(_files[_selected]);
            return;
        }
        if (_selected < 0 || _fullScreen is not null)
            return;

        _fullScreen = new FullScreenWindow(this, HandleKey, ShowInFullScreen);
        _fullScreen.Closed += (_, _) =>
        {
            _fullScreen = null;
            if (_map is not null)
                _map.Topmost = false;
            Activate();
            ThumbHost.Focus();
            RequestImages();
        };
        ShowInFullScreen();
        _fullScreen.Show();
    }

    private void ShowInFullScreen()
    {
        if (_fullScreen is not { } fullScreen || _selected < 0)
            return;

        var path = _files[_selected];
        var image = _loader.GetBest(path, fullScreen.DecodeSize, out var failed);
        fullScreen.ShowImage(path, image, failed, $"{Path.GetFileName(path)}  ·  {_selected + 1:N0} of {_files.Count:N0}");
        RequestImages();
    }

    private void SetStatus(string text) => StatusText.Text = text;
}
