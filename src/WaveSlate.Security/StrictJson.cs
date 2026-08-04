using System.Text.Json;
using System.Text.Json.Serialization;

namespace WaveSlate.Security;

public static class StrictJson
{
    private const int DefaultMaximumDepth = 64;

    public static JsonSerializerOptions CreateSerializerOptions(bool writeIndented = false) =>
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = writeIndented,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = DefaultMaximumDepth
        };

    public static T Deserialize<T>(
        ReadOnlySpan<byte> utf8Json,
        JsonSerializerOptions? options = null)
    {
        ValidateNoDuplicateProperties(utf8Json);
        return JsonSerializer.Deserialize<T>(utf8Json, options ?? CreateSerializerOptions())
            ?? throw new InvalidDataException("The JSON document was empty.");
    }

    public static void ValidateNoDuplicateProperties(
        ReadOnlySpan<byte> utf8Json,
        int maximumDepth = DefaultMaximumDepth)
    {
        if (utf8Json.IsEmpty)
        {
            throw new InvalidDataException("The JSON document was empty.");
        }

        if (maximumDepth is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDepth));
        }

        Utf8JsonReader reader = new(
            utf8Json,
            new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = maximumDepth
            });
        Stack<HashSet<string>?> scopes = new();
        try
        {
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        scopes.Push(new HashSet<string>(StringComparer.Ordinal));
                        break;
                    case JsonTokenType.EndObject:
                        PopExpectedScope(scopes, expectObject: true);
                        break;
                    case JsonTokenType.StartArray:
                        scopes.Push(null);
                        break;
                    case JsonTokenType.EndArray:
                        PopExpectedScope(scopes, expectObject: false);
                        break;
                    case JsonTokenType.PropertyName:
                    {
                        if (scopes.Count == 0 || scopes.Peek() is not HashSet<string> properties)
                        {
                            throw new JsonException("A JSON property appeared outside an object.");
                        }

                        string propertyName = reader.GetString()
                            ?? throw new JsonException("A JSON property name was unavailable.");
                        if (!properties.Add(propertyName))
                        {
                            throw new JsonException(
                                $"The JSON object contains the duplicate property '{Bound(propertyName)}'.");
                        }

                        break;
                    }
                }
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The JSON document is not strict canonical input.", exception);
        }

        if (scopes.Count != 0)
        {
            throw new InvalidDataException("The JSON document ended with an incomplete container.");
        }
    }

    private static void PopExpectedScope(Stack<HashSet<string>?> scopes, bool expectObject)
    {
        if (scopes.Count == 0)
        {
            throw new JsonException("A JSON container ended without a matching start token.");
        }

        HashSet<string>? scope = scopes.Pop();
        if (expectObject != (scope is not null))
        {
            throw new JsonException("A JSON container ended with the wrong token type.");
        }
    }

    private static string Bound(string value) => value.Length <= 80 ? value : value[..80];
}
