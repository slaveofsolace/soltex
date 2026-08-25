using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Soltex.App;

internal enum ResolvedAppearance
{
    Dark,
    Light,
    HighContrast
}

internal static class AppearanceThemeManager
{
    private enum HighContrastRole
    {
        Surface,
        Text,
        SecondaryText,
        Accent,
        AccentText,
        Border,
        Transparent
    }

    private sealed record BrushBinding(
        string BrushKey,
        string DarkColorKey,
        string LightColorKey,
        HighContrastRole HighContrastRole);

    private static readonly BrushBinding[] BrushBindings =
    [
        new("CanvasBrush", "CanvasColor", "LightCanvasColor", HighContrastRole.Surface),
        new("ShellBackdropBrush", "ShellBackdropColor", "LightShellBackdropColor", HighContrastRole.Surface),
        new("ShellTitleBarBrush", "ShellTitleBarColor", "LightShellTitleBarColor", HighContrastRole.Surface),
        new("ShellNavigationBrush", "ShellNavigationColor", "LightShellNavigationColor", HighContrastRole.Surface),
        new("SidebarBrush", "SidebarColor", "LightSidebarColor", HighContrastRole.Surface),
        new("FieldBrush", "FieldColor", "LightFieldColor", HighContrastRole.Surface),
        new("InsetBrush", "InsetColor", "LightInsetColor", HighContrastRole.Surface),
        new("QuietBrush", "QuietColor", "LightQuietColor", HighContrastRole.Surface),
        new("SurfaceBrush", "SurfaceColor", "LightSurfaceColor", HighContrastRole.Surface),
        new("RaisedBrush", "RaisedColor", "LightRaisedColor", HighContrastRole.Surface),
        new("ElevatedBrush", "ElevatedColor", "LightElevatedColor", HighContrastRole.Surface),
        new("TrackBrush", "TrackColor", "LightTrackColor", HighContrastRole.Border),
        new("ScrollTrackBrush", "ScrollTrackColor", "LightScrollTrackColor", HighContrastRole.Surface),
        new("ScrollThumbBrush", "ScrollThumbColor", "LightScrollThumbColor", HighContrastRole.SecondaryText),
        new("HeroSurfaceBrush", "SurfaceColor", "LightSurfaceColor", HighContrastRole.Surface),
        new("HeroPanelBrush", "HeroBaseColor", "LightHeroBaseColor", HighContrastRole.Surface),
        new("BorderBrush", "BorderColor", "LightBorderColor", HighContrastRole.Border),
        new("StrongBorderBrush", "BorderStrongColor", "LightBorderStrongColor", HighContrastRole.Border),
        new("HeroBorderBrush", "HeroBorderColor", "LightHeroBorderColor", HighContrastRole.Border),
        new("TextBrightBrush", "TextBrightColor", "LightTextBrightColor", HighContrastRole.Text),
        new("TextBrush", "TextColor", "LightTextColor", HighContrastRole.Text),
        new("TextSecondaryBrush", "TextSecondaryColor", "LightTextSecondaryColor", HighContrastRole.SecondaryText),
        new("MutedBrush", "MutedColor", "LightMutedColor", HighContrastRole.SecondaryText),
        new("QuietTextBrush", "QuietTextColor", "LightQuietTextColor", HighContrastRole.SecondaryText),
        new("AccentBrush", "AccentColor", "LightAccentColor", HighContrastRole.Accent),
        new("AccentFocusBrush", "AccentFocusColor", "LightAccentFocusColor", HighContrastRole.Accent),
        new("AccentDimBrush", "AccentDimColor", "LightAccentDimColor", HighContrastRole.Accent),
        new("AccentQuietBrush", "AccentQuietColor", "LightAccentQuietColor", HighContrastRole.Surface),
        new("OnAccentBrush", "OnAccentColor", "LightOnAccentColor", HighContrastRole.AccentText),
        new("SignalBrush", "SignalColor", "LightSignalColor", HighContrastRole.Text),
        new("SignalTextBrush", "SignalTextColor", "LightSignalTextColor", HighContrastRole.Text),
        new("SignalSurfaceBrush", "SignalSurfaceColor", "LightSignalSurfaceColor", HighContrastRole.Surface),
        new("SignalQuietBrush", "SignalQuietColor", "LightSignalQuietColor", HighContrastRole.Surface),
        new("SignalBorderBrush", "SignalBorderColor", "LightSignalBorderColor", HighContrastRole.Border),
        new("WarningBrush", "WarningColor", "LightWarningColor", HighContrastRole.Text),
        new("WarningSurfaceBrush", "WarningSurfaceColor", "LightWarningSurfaceColor", HighContrastRole.Surface),
        new("WarningBorderBrush", "WarningBorderColor", "LightWarningBorderColor", HighContrastRole.Border),
        new("DangerBrush", "DangerColor", "LightDangerColor", HighContrastRole.Text),
        new("DangerTextBrush", "DangerTextColor", "LightDangerTextColor", HighContrastRole.Text),
        new("DangerSurfaceBrush", "DangerSurfaceColor", "LightDangerSurfaceColor", HighContrastRole.Surface),
        new("DangerBorderBrush", "DangerBorderColor", "LightDangerBorderColor", HighContrastRole.Border),
        new("SelectedNavBrush", "NavSelectedColor", "LightNavSelectedColor", HighContrastRole.Surface),
        new("NavRestBrush", "TransparentColor", "TransparentColor", HighContrastRole.Transparent),
        new("OverlayBrush", "OverlayColor", "LightOverlayColor", HighContrastRole.Surface),
        new("OverlayBorderBrush", "OverlayBorderColor", "LightOverlayBorderColor", HighContrastRole.Border),
        new("MeterTrackBrush", "MeterTrackColor", "LightMeterTrackColor", HighContrastRole.Border),
        new("MeterQuietBrush", "MeterQuietColor", "LightMeterQuietColor", HighContrastRole.Border),
        new("MeterFillBrush", "AccentColor", "LightAccentColor", HighContrastRole.Accent),
        new("MeterPeakBrush", "WarningColor", "LightWarningColor", HighContrastRole.Text),
        new("KeycapBrush", "KeycapColor", "LightKeycapColor", HighContrastRole.Surface),
        new("KeycapBorderBrush", "KeycapBorderColor", "LightKeycapBorderColor", HighContrastRole.Border),
        new("AccentAreaBrush", "AccentAreaColor", "LightAccentAreaColor", HighContrastRole.Accent),
        new("SignalAreaBrush", "SignalAreaColor", "LightSignalAreaColor", HighContrastRole.Accent),
        new("SelectionBrush", "SelectionColor", "LightSelectionColor", HighContrastRole.Accent),
        new("ModalScrimBrush", "ModalScrimColor", "LightModalScrimColor", HighContrastRole.Surface)
    ];

    internal static ResolvedAppearance Resolve(
        AppearancePreference preference,
        bool highContrast,
        bool appsUseLightTheme)
    {
        if (highContrast)
        {
            return ResolvedAppearance.HighContrast;
        }

        return preference switch
        {
            AppearancePreference.Light => ResolvedAppearance.Light,
            AppearancePreference.Dark => ResolvedAppearance.Dark,
            _ => appsUseLightTheme ? ResolvedAppearance.Light : ResolvedAppearance.Dark
        };
    }

    internal static ResolvedAppearance ApplyPreference(
        ResourceDictionary resources,
        AppearancePreference preference)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ResolvedAppearance resolved = Resolve(
            preference,
            SystemParameters.HighContrast,
            ReadWindowsAppsUseLightTheme());
        ApplyResolved(resources, resolved);
        return resolved;
    }

    internal static ResolvedAppearance ApplyProfile(
        ResourceDictionary resources,
        ThemeProfile profile,
        Color? windowsAccent = null)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ThemeProfile normalized = profile.Normalize();
        AppearancePreference preference = normalized.Mode switch
        {
            ThemeMode.Light => AppearancePreference.Light,
            ThemeMode.Dark => AppearancePreference.Dark,
            _ => AppearancePreference.System
        };
        ResolvedAppearance resolved = Resolve(
            preference,
            SystemParameters.HighContrast,
            ReadWindowsAppsUseLightTheme());
        ApplyProfileResolved(resources, normalized, resolved, windowsAccent);
        return resolved;
    }

    internal static void ApplyProfileResolved(
        ResourceDictionary resources,
        ThemeProfile profile,
        ResolvedAppearance resolved,
        Color? windowsAccent = null)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ThemeProfile normalized = profile.Normalize();
        ApplyResolved(resources, resolved);
        ApplyAccent(resources, resolved, normalized.Accent, windowsAccent);
        ApplyDensity(resources, normalized.Density);
        resources["ResolvedThemeAccent"] = normalized.Accent.ToString();
        resources["ResolvedInterfaceDensity"] = normalized.Density.ToString();
    }

    internal static void ApplyResolved(
        ResourceDictionary resources,
        ResolvedAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(resources);
        foreach (BrushBinding binding in BrushBindings)
        {
            ResourceDictionary owner = FindResourceOwner(resources, binding.BrushKey);
            if (owner[binding.BrushKey] is not SolidColorBrush)
            {
                throw new InvalidOperationException(
                    $"Theme resource '{binding.BrushKey}' is not a solid-colour brush.");
            }

            Color color = appearance switch
            {
                ResolvedAppearance.Light =>
                    FindResource<Color>(resources, binding.LightColorKey),
                ResolvedAppearance.HighContrast =>
                    ResolveHighContrastColor(binding.HighContrastRole),
                _ => FindResource<Color>(resources, binding.DarkColorKey)
            };
            // Publish the active semantic brush at the application dictionary.
            // Replacing an entry only inside a merged token dictionary does not
            // invalidate every DynamicResource consumer that already resolved
            // through Application.Resources (notably stateful controls).
            resources[binding.BrushKey] = new SolidColorBrush(color);
        }

        resources["ResolvedAppearance"] = appearance.ToString();
    }

    internal static bool ReadWindowsAppsUseLightTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                writable: false);
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch (Exception exception) when (exception is IOException or
                                           UnauthorizedAccessException or
                                           SecurityException)
        {
            return false;
        }
    }

    private static void ApplyAccent(
        ResourceDictionary resources,
        ResolvedAppearance appearance,
        ThemeAccent accentPreference,
        Color? windowsAccent)
    {
        if (appearance == ResolvedAppearance.HighContrast ||
            accentPreference != ThemeAccent.Windows)
        {
            return;
        }

        Color canvas = ((SolidColorBrush)resources["CanvasBrush"]).Color;
        Color accent = windowsAccent ?? SystemParameters.WindowGlassColor;
        accent = Color.FromRgb(accent.R, accent.G, accent.B);
        accent = EnsureContrast(accent, canvas, 4.5);
        Color toward = RelativeLuminance(canvas) < 0.5 ? Colors.White : Colors.Black;
        Color focus = EnsureContrast(Mix(accent, toward, 0.28), canvas, 4.5);
        Color dim = Mix(accent, canvas, 0.34);
        Color quiet = Mix(canvas, accent, 0.14);
        Color onAccent = ContrastRatio(Colors.Black, accent) >=
                         ContrastRatio(Colors.White, accent)
            ? Colors.Black
            : Colors.White;

        PublishBrush(resources, "AccentBrush", accent);
        PublishBrush(resources, "AccentFocusBrush", focus);
        PublishBrush(resources, "AccentDimBrush", dim);
        PublishBrush(resources, "AccentQuietBrush", quiet);
        PublishBrush(resources, "OnAccentBrush", onAccent);
        PublishBrush(resources, "SelectedNavBrush", quiet);
        PublishBrush(resources, "MeterFillBrush", accent);
        PublishBrush(resources, "AccentAreaBrush", Color.FromArgb(36, accent.R, accent.G, accent.B));
        PublishBrush(resources, "SelectionBrush", Color.FromArgb(96, accent.R, accent.G, accent.B));
    }

    private static void ApplyDensity(
        ResourceDictionary resources,
        InterfaceDensity density)
    {
        bool compact = density == InterfaceDensity.Compact;
        resources["PadButtonActive"] = compact
            ? new Thickness(10, 6, 10, 6)
            : new Thickness(12, 8, 12, 8);
        resources["PadNavActive"] = compact
            ? new Thickness(10, 6, 10, 6)
            : new Thickness(11, 9, 11, 9);
        resources["PadTabActive"] = compact
            ? new Thickness(9, 6, 9, 6)
            : new Thickness(10, 8, 10, 8);
        resources["PadFieldActive"] = compact
            ? new Thickness(9, 6, 9, 6)
            : new Thickness(10, 8, 10, 8);
        resources["PadComboActive"] = compact
            ? new Thickness(9, 6, 9, 6)
            : new Thickness(10, 7, 10, 7);
    }

    private static void PublishBrush(
        ResourceDictionary resources,
        string key,
        Color color) => resources[key] = new SolidColorBrush(color);

    private static Color EnsureContrast(Color foreground, Color background, double minimum)
    {
        if (ContrastRatio(foreground, background) >= minimum)
        {
            return foreground;
        }

        Color target = RelativeLuminance(background) < 0.5 ? Colors.White : Colors.Black;
        for (int step = 1; step <= 10; step++)
        {
            Color candidate = Mix(foreground, target, step / 10d);
            if (ContrastRatio(candidate, background) >= minimum)
            {
                return candidate;
            }
        }

        return target;
    }

    private static Color Mix(Color first, Color second, double secondWeight)
    {
        double weight = Math.Clamp(secondWeight, 0d, 1d);
        return Color.FromRgb(
            (byte)Math.Round((first.R * (1d - weight)) + (second.R * weight)),
            (byte)Math.Round((first.G * (1d - weight)) + (second.G * weight)),
            (byte)Math.Round((first.B * (1d - weight)) + (second.B * weight)));
    }

    private static double ContrastRatio(Color first, Color second)
    {
        double firstLuminance = RelativeLuminance(first);
        double secondLuminance = RelativeLuminance(second);
        double lighter = Math.Max(firstLuminance, secondLuminance);
        double darker = Math.Min(firstLuminance, secondLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Color color) =>
        (0.2126 * Linearize(color.R)) +
        (0.7152 * Linearize(color.G)) +
        (0.0722 * Linearize(color.B));

    private static double Linearize(byte channel)
    {
        double value = channel / 255d;
        return value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static Color ResolveHighContrastColor(HighContrastRole role) =>
        role switch
        {
            HighContrastRole.Text => SystemColors.WindowTextColor,
            HighContrastRole.SecondaryText => SystemColors.GrayTextColor,
            HighContrastRole.Accent => SystemColors.HighlightColor,
            HighContrastRole.AccentText => SystemColors.HighlightTextColor,
            HighContrastRole.Border => SystemColors.WindowTextColor,
            HighContrastRole.Transparent => Colors.Transparent,
            _ => SystemColors.WindowColor
        };

    private static T FindResource<T>(ResourceDictionary resources, string key)
    {
        if (resources.Contains(key) && resources[key] is T value)
        {
            return value;
        }

        for (int index = resources.MergedDictionaries.Count - 1; index >= 0; index--)
        {
            try
            {
                return FindResource<T>(resources.MergedDictionaries[index], key);
            }
            catch (KeyNotFoundException)
            {
                // Continue through the remaining merged dictionaries.
            }
        }

        throw new KeyNotFoundException(
            $"The required theme resource '{key}' was not found.");
    }

    private static ResourceDictionary FindResourceOwner(
        ResourceDictionary resources,
        string key)
    {
        if (resources.Contains(key))
        {
            return resources;
        }

        for (int index = resources.MergedDictionaries.Count - 1; index >= 0; index--)
        {
            try
            {
                return FindResourceOwner(resources.MergedDictionaries[index], key);
            }
            catch (KeyNotFoundException)
            {
                // Continue through the remaining merged dictionaries.
            }
        }

        throw new KeyNotFoundException(
            $"The required theme resource '{key}' was not found.");
    }
}
