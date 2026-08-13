using System.Windows;

namespace Soltex.App;

public static class TelemetryActivityPolicy
{
    public static bool ShouldRun(
        bool isLoaded,
        bool isVisible,
        bool isClosing,
        WindowState windowState,
        bool homeVisible,
        bool monitoringVisible,
        bool benchmarkVisible = false) =>
        isLoaded &&
        isVisible &&
        !isClosing &&
        windowState != WindowState.Minimized &&
        (homeVisible || (monitoringVisible && !benchmarkVisible));
}
