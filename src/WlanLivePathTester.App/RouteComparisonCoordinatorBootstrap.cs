using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

namespace WlanLivePathTester.App;

internal static class RouteComparisonCoordinatorBootstrap
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
        if (sender is not MainWindow window)
        {
            return;
        }

        window.EnsureRouteComparisonCoordinatorTabV1();
        _ = window.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            () => window.EnsureRouteComparisonCoordinatorTabV1());
    }
}
