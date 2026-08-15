using Soltex.Whisper;

namespace Soltex.Whisper.Windows;

internal static class WindowsShortcutKeyMap
{
    internal const int Mouse4InputId = 0x1001;
    internal const int Mouse5InputId = 0x1002;

    internal static IReadOnlyDictionary<string, int> Compile(WhisperShortcutSet shortcutSet)
    {
        ArgumentNullException.ThrowIfNull(shortcutSet);
        Dictionary<string, int> keys = new(StringComparer.OrdinalIgnoreCase);
        foreach (string key in shortcutSet.Bindings.SelectMany(binding => binding.Keys).Distinct(
                     StringComparer.OrdinalIgnoreCase))
        {
            if (!TryGetInputId(key, out int inputId))
            {
                throw new WhisperShortcutRegistrationException(
                    $"{key} is not a supported Windows shortcut key.");
            }

            keys.Add(key, inputId);
        }

        return keys;
    }

    internal static bool TryMapKeyboard(uint virtualKey, out string key)
    {
        switch (virtualKey)
        {
            case 0x10:
            case 0xA0:
            case 0xA1:
                key = "Shift";
                return true;
            case 0x11:
            case 0xA2:
            case 0xA3:
                key = "Ctrl";
                return true;
            case 0x12:
            case 0xA4:
            case 0xA5:
                key = "Alt";
                return true;
            case 0x1B:
                key = "Esc";
                return true;
            case 0x0D:
                key = "Enter";
                return true;
            case 0x20:
                key = "Space";
                return true;
            case 0x09:
                key = "Tab";
                return true;
            case 0x2E:
                key = "DELETE";
                return true;
            case 0x5B:
            case 0x5C:
                key = "Win";
                return true;
        }

        if (virtualKey is >= 0x30 and <= 0x39 || virtualKey is >= 0x41 and <= 0x5A)
        {
            key = ((char)virtualKey).ToString();
            return true;
        }

        if (virtualKey is >= 0x70 and <= 0x87)
        {
            key = $"F{virtualKey - 0x6F}";
            return true;
        }

        key = string.Empty;
        return false;
    }

    internal static bool TryGetInputId(string key, out int inputId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (string.Equals(key, "Mouse 4", StringComparison.OrdinalIgnoreCase))
        {
            inputId = Mouse4InputId;
            return true;
        }

        if (string.Equals(key, "Mouse 5", StringComparison.OrdinalIgnoreCase))
        {
            inputId = Mouse5InputId;
            return true;
        }

        for (uint virtualKey = 0; virtualKey <= 0xFF; virtualKey++)
        {
            if (TryMapKeyboard(virtualKey, out string candidate) &&
                string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase))
            {
                inputId = checked((int)virtualKey);
                return true;
            }
        }

        inputId = 0;
        return false;
    }
}
