using System.Collections.ObjectModel;

namespace Soltex.Whisper;

/// <summary>
/// One Scratchpad tab: bounded text with a bounded undo/redo stack.
/// </summary>
/// <remarks>
/// The undo history is capped rather than unlimited. An always-on dictation target
/// that keeps every intermediate revision of everything the user has ever said is a
/// retention problem, not a convenience.
/// </remarks>
public sealed class WhisperScratchpadTab
{
    public const int MaximumTitleCharacters = 48;
    public const int MaximumContentCharacters = 100_000;
    public const int MaximumUndoDepth = 50;

    private readonly List<string> _undo = [];
    private readonly List<string> _redo = [];
    private string _content = string.Empty;

    public WhisperScratchpadTab(string title)
    {
        Title = NormalizeTitle(title);
    }

    public string Title { get; private set; }

    public string Content => _content;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public void Rename(string title) => Title = NormalizeTitle(title);

    /// <summary>Replaces the whole document, pushing the previous value onto the undo stack.</summary>
    public void Replace(string content) => Commit(ValidateContent(content));

    /// <summary>Appends dictated text, separating it from existing content with a space.</summary>
    public void Append(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return;
        }

        string separator = _content.Length > 0 && !char.IsWhiteSpace(_content[^1])
            ? " "
            : string.Empty;
        Commit(ValidateContent(_content + separator + text));
    }

    public bool Undo()
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        _redo.Add(_content);
        _content = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        _undo.Add(_content);
        _content = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        return true;
    }

    public void Clear()
    {
        Commit(string.Empty);
        _redo.Clear();
    }

    private void Commit(string next)
    {
        if (string.Equals(next, _content, StringComparison.Ordinal))
        {
            return;
        }

        _undo.Add(_content);
        while (_undo.Count > MaximumUndoDepth)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        _content = next;
    }

    private static string ValidateContent(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.Length > MaximumContentCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(content),
                $"A Scratchpad tab cannot hold more than {MaximumContentCharacters} characters.");
        }

        return content;
    }

    private static string NormalizeTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        string trimmed = title.Trim();
        if (trimmed.Length > MaximumTitleCharacters || trimmed.Any(char.IsControl))
        {
            throw new ArgumentOutOfRangeException(
                nameof(title),
                $"Scratchpad titles must contain 1 to {MaximumTitleCharacters} printable characters.");
        }

        return trimmed;
    }
}

/// <summary>
/// The Scratchpad: at most five tabs, one of them active.
/// </summary>
public sealed class WhisperScratchpad
{
    public const int MaximumTabs = 5;

    private readonly List<WhisperScratchpadTab> _tabs = [];

    public WhisperScratchpad()
    {
        _tabs.Add(new WhisperScratchpadTab("Note 1"));
        ActiveIndex = 0;
    }

    public ReadOnlyCollection<WhisperScratchpadTab> Tabs => Array.AsReadOnly(_tabs.ToArray());

    public int ActiveIndex { get; private set; }

    public WhisperScratchpadTab Active => _tabs[ActiveIndex];

    public bool CanAddTab => _tabs.Count < MaximumTabs;

    public WhisperScratchpadTab AddTab(string? title = null)
    {
        if (!CanAddTab)
        {
            throw new InvalidOperationException(
                $"The Scratchpad holds at most {MaximumTabs} tabs.");
        }

        WhisperScratchpadTab tab = new(title ?? $"Note {_tabs.Count + 1}");
        _tabs.Add(tab);
        ActiveIndex = _tabs.Count - 1;
        return tab;
    }

    public void Activate(int index)
    {
        if (index < 0 || index >= _tabs.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                "The requested Scratchpad tab does not exist.");
        }

        ActiveIndex = index;
    }

    /// <summary>
    /// Closes a tab. The last remaining tab is cleared rather than removed, so the
    /// Scratchpad always has somewhere to dictate into.
    /// </summary>
    public void CloseTab(int index)
    {
        if (index < 0 || index >= _tabs.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                "The requested Scratchpad tab does not exist.");
        }

        if (_tabs.Count == 1)
        {
            _tabs[0].Clear();
            ActiveIndex = 0;
            return;
        }

        _tabs.RemoveAt(index);
        ActiveIndex = Math.Min(ActiveIndex, _tabs.Count - 1);
    }
}
