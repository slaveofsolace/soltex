using System.Text;

namespace Soltex.Update;

internal static class UpdateText
{
    internal const int MaximumDetailLength = 512;

    internal static string Sanitize(string? value, string fallback = "No additional detail.")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        StringBuilder builder = new(Math.Min(value.Length, MaximumDetailLength));
        bool previousWhitespace = false;
        foreach (char character in value)
        {
            char output = char.IsControl(character) ? ' ' : character;
            bool whitespace = char.IsWhiteSpace(output);
            if (whitespace && previousWhitespace)
            {
                continue;
            }

            builder.Append(whitespace ? ' ' : output);
            previousWhitespace = whitespace;
            if (builder.Length >= MaximumDetailLength)
            {
                break;
            }
        }

        string sanitized = builder.ToString().Trim();
        return sanitized.Length == 0 ? fallback : sanitized;
    }

    internal static string BoundIdentifier(string value, string parameterName, int maximum = 128)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string normalized = value.Trim();
        if (normalized.Length > maximum || normalized.Any(char.IsControl))
        {
            throw new ArgumentException("The identifier is outside the accepted bounds.", parameterName);
        }

        return normalized;
    }

    internal static bool IsSha256(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length == 64 &&
        value.All(character =>
            character is >= '0' and <= '9' or
            >= 'A' and <= 'F' or
            >= 'a' and <= 'f');
}
