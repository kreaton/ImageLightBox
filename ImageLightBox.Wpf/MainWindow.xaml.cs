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

namespace ImageLightBox.Wpf;

public partial class MainWindow : Window
{
    private const int WheelNotch = 120;

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly ThumbnailLoader _loader;
    private readonly List<string> _files = [];
    private readonly ObservableCollection<Tile> _tiles = [];

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
        SourceBox.Text = _settings.SourceFolder ?? "";
        SizeBox.SelectedItem = SizeBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => TagOf(i) == _settings.ThumbnailSize) ?? SizeBox.Items[0];

        UpdateCommands();
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
        _page = 0;
        Select(-1);
        ShowPage();
        ThumbHost.Focus();
        SetStatus("Scanning...");

        try
        {
            await Task.Run(() =>
            {
                var batch = new List<string>();
                var sinceFlush = Stopwatch.StartNew();
                foreach (var file in FileScanner.Enumerate(root, scan.Token))
                {
                    batch.Add(file);
                    if (batch.Count >= 500 || sinceFlush.ElapsedMilliseconds >= 100)
                    {
                        AddBatch(batch, scan);
                        sinceFlush.Restart();
                    }
                }
                AddBatch(batch, scan);
            }, scan.Token);
        }
        catch (OperationCanceledException)
        {
            return; // A newer scan owns the list now.
        }

        SetStatus(_files.Count == 0 ? $"No files in {root}" : $"Found {_files.Count:N0} files in {root}");
        RequestImages();
    }

    private void AddBatch(List<string> batch, CancellationTokenSource scan)
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
                SetStatus($"Scanning... {_files.Count:N0} files so far");
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
            _tiles.Add(new Tile(i, _files[i]) { Image = image, Failed = failed, IsSelected = i == _selected, HasLocation = i == _selected && _location is not null });
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

        UpdateCommands();
        _ = LoadExifAsync(index);

        if (_fullScreen is not null && index >= 0)
            ShowInFullScreen();
    }

    private void UpdateCommands()
    {
        var any = _selected >= 0;
        DeleteButton.IsEnabled = any;
        MoveButton.IsEnabled = any;
        CopyButton.IsEnabled = any;
        FullScreenButton.IsEnabled = any;
        MapButton.IsEnabled = any && _location is not null;

        // Same width either way would be nicer, but the label must say what the button does.
        var path = any ? _files[_selected] : "";
        OpenIcon.Text = FileIcon.OpenGlyph(path);
        OpenText.Text = FileIcon.OpenLabel(path);
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

        var view = new ListCollectionView(details.Entries.ToList());
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
        UpdateCommands();
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
        // No shortcuts underneath the About card.
        if (AboutOverlay.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape)
            {
                CloseAbout();
                e.Handled = true;
            }
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
        if (key is Key.Delete or Key.M or Key.C or Key.L or Key.Enter or Key.F11)
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

    // ---- Actions on the selected image ----

    private void OnConfirmChanged(object sender, RoutedEventArgs e)
    {
        _settings.ConfirmDelete = ConfirmDeleteBox.IsChecked == true;
        _settings.ConfirmMove = ConfirmMoveBox.IsChecked == true;
    }

    private void OnDelete(object sender, RoutedEventArgs e) => _ = DeleteAsync();

    private void OnMove(object sender, RoutedEventArgs e) => _ = MoveAsync();

    private void OnCopy(object sender, RoutedEventArgs e) => _ = CopyAsync();

    private void OnFullScreen(object sender, RoutedEventArgs e) => OpenFullScreen();

    private void OnMap(object sender, RoutedEventArgs e) => ShowMap();

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
        var index = _files.IndexOf(path);
        if (index < 0)
            return;

        _files.RemoveAt(index);
        _loader.Forget(path);

        var next = _files.Count == 0 ? -1 : Math.Min(index, _files.Count - 1);
        _selected = next;
        if (next >= 0)
            _page = GridLayout.PageOf(next, _grid.Count);
        ShowPage();

        if (next < 0)
            _fullScreen?.Close();
        Select(next);
    }

    private void PlayVideo(string path)
    {
        var name = Path.GetFileName(path);
        try
        {
            FileActions.OpenWithDefaultApp(path);
            SetStatus($"Opened {name} in its default player");
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
