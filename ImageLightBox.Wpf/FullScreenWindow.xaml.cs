using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageLightBox.Core;

namespace ImageLightBox.Wpf;

/// <summary>Shows one file over the whole monitor the main window is on. The main window decides which.</summary>
public partial class FullScreenWindow : Window
{
    private readonly Func<Key, bool, bool> _handleKey;
    private readonly Action _sizeChanged;

    internal string? ImagePath { get; private set; }
    internal int DecodeSize { get; private set; }

    /// <param name="handleKey">Handles a key the main window's way. Gets the key and whether it is a repeat.</param>
    /// <param name="sizeChanged">The right decode size is known once the window has been maximized.</param>
    internal FullScreenWindow(Window owner, Func<Key, bool, bool> handleKey, Action sizeChanged)
    {
        InitializeComponent();
        Owner = owner;
        _handleKey = handleKey;
        _sizeChanged = sizeChanged;
        DecodeSize = GridLayout.DecodeSize(Math.Max(SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight)
            * VisualTreeHelper.GetDpi(owner).DpiScaleX);
    }

    internal void ShowImage(string path, BitmapSource? image, bool failed, string caption)
    {
        ImagePath = path;
        CaptionText.Text = caption;
        IconGlyph.Text = FileIcon.Glyph(path);
        IconLabel.Text = FileIcon.Label(path);
        IconName.Text = Path.GetFileName(path);
        IconHint.Text = FileIcon.IsVideo(path) ? "Press Enter or double-click to play" : "";
        SetImage(image, failed || !FileKinds.IsImage(path));
    }

    internal void SetImage(BitmapSource? image, bool failed)
    {
        Picture.Source = image;
        IconPanel.Visibility = failed && image is null ? Visibility.Visible : Visibility.Collapsed;
    }

    internal bool HasImage => Picture.Source is not null;

    // Maximized only now: this way it maximizes on the owner's monitor.
    private void OnLoaded(object sender, RoutedEventArgs e) => WindowState = WindowState.Maximized;

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var size = GridLayout.DecodeSize(Math.Max(ActualWidth, ActualHeight) * VisualTreeHelper.GetDpi(this).DpiScaleX);
        if (WindowState == WindowState.Maximized && size != DecodeSize)
        {
            DecodeSize = size;
            _sizeChanged();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // On a video, Enter plays it, which the main window does.
        if (e.Key == Key.Escape || (e.Key is Key.Enter or Key.F11 && !IsOnVideo))
        {
            if (!e.IsRepeat)
                Close();
            e.Handled = true;
            return;
        }
        e.Handled = _handleKey(e.Key, e.IsRepeat);
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IsOnVideo)
            _handleKey(Key.Enter, false);
        else
            Close();
    }

    private bool IsOnVideo => ImagePath is not null && FileIcon.IsVideo(ImagePath);
}
