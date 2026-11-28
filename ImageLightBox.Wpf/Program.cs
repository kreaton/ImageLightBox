using Velopack;

namespace ImageLightBox.Wpf;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        // Must be the first thing to run: handles Velopack's install/update/uninstall hooks
        // and may exit the process. Don't swallow exceptions here.
        VelopackApp.Build()
            .OnAfterInstallFastCallback(_ => RefreshShellIcons())
            .OnAfterUpdateFastCallback(_ => RefreshShellIcons())
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    private const int ShcneAssocChanged = 0x08000000;
    private const int ShcnfIdList = 0x0000;

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

    /// <summary>
    /// An update swaps the folder the shortcuts point into. If Explorer looks while the exe is
    /// gone, it caches a blank page icon and keeps it through later updates. This makes it
    /// read the icons again.
    /// </summary>
    private static void RefreshShellIcons() => SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);
}
