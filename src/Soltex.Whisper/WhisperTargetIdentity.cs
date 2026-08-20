namespace Soltex.Whisper;

/// <summary>
/// Stable runtime identity of the control that owned focus when a session began.
/// Recorded once at session start, then compared again immediately before insertion
/// and once more before any submission. Identity comparison is what turns
/// "the policy said yes" into "the policy still says yes about the same control".
/// </summary>
public sealed class WhisperTargetIdentity : IEquatable<WhisperTargetIdentity>
{
    public const int MaximumRuntimeIdCharacters = 160;

    public WhisperTargetIdentity(
        int processId,
        string processName,
        string elementRuntimeId)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(processId),
                "A target process identifier must be greater than zero.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(elementRuntimeId);

        string normalizedRuntimeId = elementRuntimeId.Trim();
        if (normalizedRuntimeId.Length > MaximumRuntimeIdCharacters ||
            normalizedRuntimeId.Any(char.IsControl))
        {
            throw new ArgumentOutOfRangeException(
                nameof(elementRuntimeId),
                $"Element runtime identifiers must contain 1 to {MaximumRuntimeIdCharacters} printable characters.");
        }

        ProcessId = processId;
        ProcessName = WhisperAppProfile.NormalizeProcessName(processName);
        ElementRuntimeId = normalizedRuntimeId;
    }

    public int ProcessId { get; }

    public string ProcessName { get; }

    /// <summary>
    /// An opaque provider-supplied element key. It is never a caption, a value, or
    /// surrounding text, so it can be compared and logged without exposing content.
    /// </summary>
    public string ElementRuntimeId { get; }

    public bool Equals(WhisperTargetIdentity? other)
    {
        return other is not null &&
            ProcessId == other.ProcessId &&
            string.Equals(ProcessName, other.ProcessName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(ElementRuntimeId, other.ElementRuntimeId, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj) => Equals(obj as WhisperTargetIdentity);

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(ProcessId);
        hash.Add(ProcessName, StringComparer.OrdinalIgnoreCase);
        hash.Add(ElementRuntimeId, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    public override string ToString() => $"{ProcessName}#{ProcessId}/{ElementRuntimeId}";
}

/// <summary>
/// Why a captured target is no longer the target Whisper is about to act on.
/// Every value other than <see cref="None"/> must stop submission.
/// </summary>
public enum WhisperTargetDrift
{
    None,
    TargetUnknown,
    ProcessChanged,
    ElementChanged,
    EditabilityLost,
    ProtectedFieldAppeared,
    ElevationChanged,
    CategoryChanged
}

/// <summary>
/// A captured target: its stable identity plus the inspected capabilities that the
/// delivery policy reasoned about.
/// </summary>
public sealed class WhisperTargetSnapshot
{
    public WhisperTargetSnapshot(
        WhisperTargetIdentity identity,
        WhisperTargetContext context)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(context);

        if (!string.Equals(identity.ProcessName, context.ProcessName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "A target snapshot must describe one process; the identity and context disagree.",
                nameof(context));
        }

        Identity = identity;
        Context = context;
    }

    public WhisperTargetIdentity Identity { get; }

    public WhisperTargetContext Context { get; }

    /// <summary>
    /// Compares the target captured at session start with the target observed now.
    /// Fails closed: an unknown current target is drift, never a match.
    /// </summary>
    public WhisperTargetDrift CompareWith(WhisperTargetSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (!current.Context.IsKnown)
        {
            return WhisperTargetDrift.TargetUnknown;
        }

        if (current.Identity.ProcessId != Identity.ProcessId ||
            !string.Equals(
                current.Identity.ProcessName,
                Identity.ProcessName,
                StringComparison.OrdinalIgnoreCase))
        {
            return WhisperTargetDrift.ProcessChanged;
        }

        if (!string.Equals(
                current.Identity.ElementRuntimeId,
                Identity.ElementRuntimeId,
                StringComparison.Ordinal))
        {
            return WhisperTargetDrift.ElementChanged;
        }

        if (current.Context.IsPassword)
        {
            return WhisperTargetDrift.ProtectedFieldAppeared;
        }

        if (!current.Context.IsEditable || current.Context.IsReadOnly)
        {
            return WhisperTargetDrift.EditabilityLost;
        }

        if (current.Context.IsElevated != Context.IsElevated)
        {
            return WhisperTargetDrift.ElevationChanged;
        }

        return current.Context.Kind != Context.Kind
            ? WhisperTargetDrift.CategoryChanged
            : WhisperTargetDrift.None;
    }
}
