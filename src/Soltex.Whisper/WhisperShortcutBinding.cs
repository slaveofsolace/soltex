using System.Collections.ObjectModel;

namespace Soltex.Whisper;

public sealed class WhisperShortcutBinding : IEquatable<WhisperShortcutBinding>
{
    private readonly ReadOnlyCollection<string> _keys;

    private WhisperShortcutBinding(
        WhisperShortcutAction action,
        IEnumerable<string> keys)
    {
        Action = action;
        _keys = Array.AsReadOnly(keys.ToArray());
    }

    public WhisperShortcutAction Action { get; }

    public ReadOnlyCollection<string> Keys => _keys;

    public string DisplayText => string.Join("+", _keys);

    public static WhisperShortcutBinding Create(
        WhisperShortcutAction action,
        params string[] keys)
    {
        if (!TryCreate(action, keys, out WhisperShortcutBinding? binding, out string? error))
        {
            throw new ArgumentException(error, nameof(keys));
        }

        return binding!;
    }

    public static bool TryCreate(
        WhisperShortcutAction action,
        IEnumerable<string> keys,
        out WhisperShortcutBinding? binding,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(keys);

        string[] normalizedKeys;
        try
        {
            normalizedKeys = keys.Select(NormalizeKey).ToArray();
        }
        catch (ArgumentException exception)
        {
            binding = null;
            error = exception.Message;
            return false;
        }

        if (normalizedKeys.Length is < 1 or > WhisperLimits.MaximumShortcutKeys)
        {
            binding = null;
            error = $"A shortcut must contain 1 to {WhisperLimits.MaximumShortcutKeys} keys.";
            return false;
        }

        normalizedKeys = normalizedKeys
            .OrderBy(GetKeySortOrder)
            .ThenBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalizedKeys.Length)
        {
            binding = null;
            error = "A shortcut cannot contain the same key more than once.";
            return false;
        }

        binding = new WhisperShortcutBinding(action, normalizedKeys);
        error = null;
        return true;
    }

    public bool Equals(WhisperShortcutBinding? other)
    {
        return other is not null &&
            Action == other.Action &&
            _keys.SequenceEqual(other._keys, StringComparer.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => Equals(obj as WhisperShortcutBinding);

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Action);
        foreach (string key in _keys)
        {
            hash.Add(key, StringComparer.OrdinalIgnoreCase);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => $"{Action}: {DisplayText}";

    private static string NormalizeKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        string normalized = key.Trim();
        if (normalized.Length > 24 ||
            normalized.Any(character =>
                !(char.IsLetterOrDigit(character) ||
                  character is ' ' or '-' or '_')))
        {
            throw new ArgumentException(
                "Shortcut keys must contain 1 to 24 letters, numbers, spaces, hyphens, or underscores.",
                nameof(key));
        }

        return normalized.ToUpperInvariant() switch
        {
            "CONTROL" or "CTRL" => "Ctrl",
            "WINDOWS" or "WIN" => "Win",
            "OPTION" or "ALT" => "Alt",
            "SHIFT" => "Shift",
            "ESCAPE" or "ESC" => "Esc",
            "RETURN" or "ENTER" => "Enter",
            "SPACEBAR" or "SPACE" => "Space",
            "MOUSE4" or "MOUSE 4" or "MOUSE BUTTON 4" => "Mouse 4",
            "MOUSE5" or "MOUSE 5" or "MOUSE BUTTON 5" => "Mouse 5",
            _ => normalized.ToUpperInvariant()
        };
    }

    private static int GetKeySortOrder(string key)
    {
        return key switch
        {
            "Ctrl" => 0,
            "Win" => 1,
            "Shift" => 2,
            "Alt" => 3,
            _ => 10
        };
    }
}

public sealed class WhisperShortcutSet
{
    private readonly ReadOnlyCollection<WhisperShortcutBinding> _bindings;

    public WhisperShortcutSet(IEnumerable<WhisperShortcutBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        WhisperShortcutBinding[] materialized = bindings.ToArray();
        if (materialized.Length == 0)
        {
            throw new ArgumentException("At least one shortcut binding is required.", nameof(bindings));
        }

        foreach (IGrouping<WhisperShortcutAction, WhisperShortcutBinding> group in
                 materialized.GroupBy(binding => binding.Action))
        {
            if (group.Count() > WhisperLimits.MaximumShortcutsPerAction)
            {
                throw new ArgumentException(
                    $"Action {group.Key} has more than {WhisperLimits.MaximumShortcutsPerAction} bindings.",
                    nameof(bindings));
            }
        }

        IGrouping<string, WhisperShortcutBinding>? conflict = materialized
            .GroupBy(binding => binding.DisplayText, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Select(binding => binding.Action).Distinct().Count() > 1);
        if (conflict is not null)
        {
            throw new ArgumentException(
                $"Shortcut {conflict.Key} is assigned to more than one action.",
                nameof(bindings));
        }

        if (materialized.Distinct().Count() != materialized.Length)
        {
            throw new ArgumentException("Duplicate shortcut bindings are not allowed.", nameof(bindings));
        }

        _bindings = Array.AsReadOnly(materialized);
    }

    public ReadOnlyCollection<WhisperShortcutBinding> Bindings => _bindings;

    public ReadOnlyCollection<WhisperShortcutBinding> ForAction(WhisperShortcutAction action)
    {
        return Array.AsReadOnly(_bindings.Where(binding => binding.Action == action).ToArray());
    }

    public static WhisperShortcutSet CreateDefault()
    {
        return new WhisperShortcutSet(
        [
            WhisperShortcutBinding.Create(WhisperShortcutAction.PushToTalk, "Ctrl", "Alt", "Space"),
            WhisperShortcutBinding.Create(WhisperShortcutAction.HandsFree, "Ctrl", "Alt", "H"),
            WhisperShortcutBinding.Create(WhisperShortcutAction.CommandMode, "Ctrl", "Alt", "C"),
            WhisperShortcutBinding.Create(WhisperShortcutAction.PasteLastTranscript, "Shift", "Alt", "Z"),
            WhisperShortcutBinding.Create(WhisperShortcutAction.CopyLastTranscript, "Shift", "Alt", "X"),
            WhisperShortcutBinding.Create(WhisperShortcutAction.Cancel, "Esc"),
            WhisperShortcutBinding.Create(WhisperShortcutAction.OpenScratchpad, "Ctrl", "Alt", "S")
        ]);
    }
}
