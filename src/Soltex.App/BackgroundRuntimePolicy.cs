namespace Soltex.App;

internal static class BackgroundRuntimePolicy
{
    internal static bool ShouldHideOnClose(
        bool renderSmokeMode,
        bool explicitExitRequested,
        CloseBehavior closeBehavior,
        bool notificationAreaAvailable) =>
        !renderSmokeMode &&
        !explicitExitRequested &&
        closeBehavior == CloseBehavior.NotificationArea &&
        notificationAreaAvailable;

    internal static bool ShouldShowNotificationArea(
        bool renderSmokeMode,
        CloseBehavior closeBehavior,
        bool notificationAreaAvailable) =>
        !renderSmokeMode &&
        closeBehavior == CloseBehavior.NotificationArea &&
        notificationAreaAvailable;
}
