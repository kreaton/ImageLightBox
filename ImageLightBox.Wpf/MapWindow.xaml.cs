using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using ImageLightBox.Core;
using Microsoft.Web.WebView2.Core;

namespace ImageLightBox.Wpf;

/// <summary>
/// OpenStreetMap with a marker on one location, drawn by a local page (Map\map.html). It follows
/// the selection without reloading, and opens without taking the keyboard so that the arrow keys
/// keep working in the main window. Closing it only hides it, so that it opens again at once.
/// </summary>
public partial class MapWindow : Window
{
    // A real-looking host, as OpenStreetMap's tile servers want a Referer that a file:// page has none of.
    private const string Host = "imagelightbox.map";
    private const long MaxLogBytes = 64 * 1024;

    private static readonly string AppFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImageLightBox");
    private static readonly string UserDataFolder = Path.Combine(AppFolder, "WebView2");
    private static readonly string LogFile = Path.Combine(AppFolder, "map.log");

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _stallTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private MapLocation? _wanted;
    private MapLocation? _shown;
    private bool _pageReady;
    private bool _closing;

    public MapWindow()
    {
        InitializeComponent();
        ShowActivated = false;
        _stallTimer.Tick += OnStalled;
        _stallTimer.Start();
        Log("window created");
    }

    /// <summary>Shows a place, or with no location says that this file has none.</summary>
    public void ShowLocation(MapLocation? location, string fileName)
    {
        _wanted = location;
        Title = location is null ? $"Map  ·  {fileName}" : $"Map  ·  {fileName}  ·  {location}";
        BrowserButton.Visibility = Visibility.Collapsed;

        if (location is null)
        {
            MessageText.Text = "This file has no location.";
            Web.Visibility = Visibility.Hidden;
            return;
        }

        MessageText.Text = "Loading the map...";
        _clock.Restart();
        MoveMap();
    }

    /// <summary>Really closes it, when the app ends.</summary>
    public void CloseForGood()
    {
        _closing = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closing)
        {
            _stallTimer.Stop();
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void MoveMap()
    {
        if (!_pageReady || _wanted is not { } location)
            return;

        Web.Visibility = Visibility.Visible;
        if (location == _shown)
            return;

        _shown = location;
        Log("location sent to the page");
        _ = Web.CoreWebView2.ExecuteScriptAsync(string.Create(CultureInfo.InvariantCulture, $"showLocation({location.Latitude},{location.Longitude})"));
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Not the default, which is a folder next to the program.
            Log("creating the browser engine");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: UserDataFolder);
            Log($"engine created, version {environment.BrowserVersionString}");
            await Web.EnsureCoreWebView2Async(environment);
            Log("browser view ready");

            var web = Web.CoreWebView2;
            web.SetVirtualHostNameToFolderMapping(Host, Path.Combine(AppContext.BaseDirectory, "Map"), CoreWebView2HostResourceAccessKind.Deny);
            web.NavigationStarting += OnNavigationStarting;
            web.NewWindowRequested += OnNewWindowRequested;
            web.ProcessFailed += OnProcessFailed;
            web.WebMessageReceived += (_, m) => Log(m.TryGetWebMessageAsString());
            web.NavigationCompleted += (_, n) =>
            {
                Log($"page loaded: ok={n.IsSuccess}, status={n.HttpStatusCode}, error={n.WebErrorStatus}");
                if (!n.IsSuccess)
                    return; // The stall timer offers the browser.

                _pageReady = true;
                _stallTimer.Stop();
                MoveMap();
            };
            web.Navigate($"https://{Host}/map.html");
            Log("page requested");
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or InvalidOperationException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            // Windows 11 includes the runtime, but it can be removed. The browser still works.
            Log($"failed: {ex.GetType().Name}: {ex.Message}");
            if (_wanted is { } location)
                OpenInBrowser(location.PageUrl);
            CloseForGood();
        }
    }

    private void OnStalled(object? sender, EventArgs e)
    {
        _stallTimer.Stop();
        Log("stalled: the page has not loaded after 15 s");
        MessageText.Text = "The map is not loading. It can still open in your browser.";
        BrowserButton.Visibility = Visibility.Visible;
        Web.Visibility = Visibility.Hidden; // It would cover the message.
    }

    private void OnOpenInBrowser(object sender, RoutedEventArgs e)
    {
        if (_wanted is { } location)
            OpenInBrowser(location.PageUrl);
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        Log($"browser process failed: {e.ProcessFailedKind}, {e.Reason}");
        if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
        {
            // A page that has gone is loaded again, and NavigationCompleted puts the marker back.
            _pageReady = false;
            _shown = null;
            Web.CoreWebView2.Reload();
        }
    }

    // The copyright link goes to the browser, not into this little window.
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (new Uri(e.Uri).Host == Host)
            return;
        e.Cancel = true;
        OpenInBrowser(e.Uri);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        OpenInBrowser(e.Uri);
    }

    private static void OpenInBrowser(string url)
    {
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            FileActions.OpenWithDefaultApp(url);
    }

    /// <summary>Loading stages with their timing, for when the map doesn't come up. Never holds a location.</summary>
    private void Log(string stage)
    {
        try
        {
            if (File.Exists(LogFile) && new FileInfo(LogFile).Length > MaxLogBytes)
                File.Delete(LogFile);
            File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  +{_clock.ElapsedMilliseconds,6} ms  {stage}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The log must never be the reason the map doesn't open.
        }
    }
}
