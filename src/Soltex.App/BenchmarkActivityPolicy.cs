using System.Windows;

namespace Soltex.App;

internal static class BenchmarkActivityPolicy
{
    internal static bool ShouldContinue(
        bool isLoaded,
        bool isVisible,
        bool isClosing,
        WindowState windowState,
        bool monitoringVisible,
        bool benchmarkVisible) =>
        isLoaded &&
        isVisible &&
        !isClosing &&
        windowState != WindowState.Minimized &&
        monitoringVisible &&
        benchmarkVisible;
}
