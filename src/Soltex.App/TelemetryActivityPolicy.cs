using System.Windows;

namespace Soltex.App;

public static class TelemetryActivityPolicy
{
    public static bool ShouldRun(
        bool isLoaded,
        bool isClosing,
        WindowState windowState,
        bool homeVisible,
        bool monitoringVisible) =>
        isLoaded &&
        !isClosing &&
        windowState != WindowState.Minimized &&
        (homeVisible || monitoringVisible);
}
