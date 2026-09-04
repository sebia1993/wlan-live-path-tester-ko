using System.Runtime.CompilerServices;
using System.Windows;

namespace WlanLivePathTester.App;

internal static class BrowserObservationSessionReportBootstrap
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded));
    }

    private static void OnMainWindowLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is MainWindow window)
        {
            window.EnsureBrowserObservationSessionReportTab();
        }
    }
}
