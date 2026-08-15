using Soltex.Whisper;

namespace Soltex.Whisper.Windows;

internal sealed class WhisperShortcutMatcher
{
    private readonly CompiledBinding[] _bindings;
    private readonly HashSet<string> _relevantKeys;
    private readonly HashSet<int> _physicalInputsDown = [];
    private readonly Dictionary<string, int> _logicalKeyCounts =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _activeBindings = [];

    internal WhisperShortcutMatcher(WhisperShortcutSet shortcutSet)
    {
        ArgumentNullException.ThrowIfNull(shortcutSet);
        _bindings = shortcutSet.Bindings
            .Select((binding, index) => new CompiledBinding(
                index,
                binding.Action,
                new HashSet<string>(binding.Keys, StringComparer.OrdinalIgnoreCase)))
            .ToArray();
        _relevantKeys = new HashSet<string>(
            _bindings.SelectMany(binding => binding.Keys),
            StringComparer.OrdinalIgnoreCase);
    }

    internal IReadOnlyList<WhisperShortcutSignal> Observe(
        int physicalInputId,
        string logicalKey,
        bool isDown,
        TimeSpan monotonicTime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalKey);
        ArgumentOutOfRangeException.ThrowIfLessThan(monotonicTime, TimeSpan.Zero);

        if (!_relevantKeys.Contains(logicalKey))
        {
            return [];
        }

        bool changed = isDown
            ? _physicalInputsDown.Add(physicalInputId)
            : _physicalInputsDown.Remove(physicalInputId);
        if (!changed)
        {
            return [];
        }

        if (isDown)
        {
            _logicalKeyCounts.TryGetValue(logicalKey, out int count);
            _logicalKeyCounts[logicalKey] = count + 1;
        }
        else if (_logicalKeyCounts.TryGetValue(logicalKey, out int count))
        {
            if (count <= 1)
            {
                _logicalKeyCounts.Remove(logicalKey);
            }
            else
            {
                _logicalKeyCounts[logicalKey] = count - 1;
            }
        }

        List<WhisperShortcutSignal> signals = [];
        foreach (int bindingIndex in _activeBindings.ToArray())
        {
            CompiledBinding binding = _bindings[bindingIndex];
            if (!IsExactMatch(binding))
            {
                _activeBindings.Remove(bindingIndex);
                signals.Add(new WhisperShortcutSignal(
                    binding.Action,
                    WhisperShortcutTransition.Released,
                    monotonicTime));
            }
        }

        if (!isDown)
        {
            return signals;
        }

        foreach (CompiledBinding binding in _bindings)
        {
            if (!_activeBindings.Contains(binding.Index) && IsExactMatch(binding))
            {
                _activeBindings.Add(binding.Index);
                signals.Add(new WhisperShortcutSignal(
                    binding.Action,
                    WhisperShortcutTransition.Pressed,
                    monotonicTime));
            }
        }

        return signals;
    }

    internal bool IsRelevant(string logicalKey) => _relevantKeys.Contains(logicalKey);

    private bool IsExactMatch(CompiledBinding binding)
    {
        return _logicalKeyCounts.Count == binding.Keys.Count &&
            binding.Keys.All(_logicalKeyCounts.ContainsKey);
    }

    private sealed record CompiledBinding(
        int Index,
        WhisperShortcutAction Action,
        HashSet<string> Keys);
}
