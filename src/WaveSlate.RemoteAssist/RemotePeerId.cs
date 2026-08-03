namespace WaveSlate.RemoteAssist;

public sealed record RemotePeerId
{
    public const int MinimumLength = 3;
    public const int MaximumLength = 64;

    private RemotePeerId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static bool TryCreate(
        string? candidate,
        out RemotePeerId? peerId,
        out string error)
    {
        peerId = null;
        string value = candidate?.Trim() ?? string.Empty;
        if (value.Length is < MinimumLength or > MaximumLength)
        {
            error = $"Peer IDs must contain {MinimumLength} to {MaximumLength} characters.";
            return false;
        }

        bool hasLetterOrDigit = false;
        foreach (char character in value)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                hasLetterOrDigit = true;
                continue;
            }

            if (character is not '-' and not '_')
            {
                error = "Peer IDs may contain only ASCII letters, numbers, hyphens, and underscores.";
                return false;
            }
        }

        if (!hasLetterOrDigit)
        {
            error = "Peer IDs must contain at least one letter or number.";
            return false;
        }

        peerId = new RemotePeerId(value);
        error = string.Empty;
        return true;
    }
}
