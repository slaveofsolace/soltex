using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;
using Soltex.Whisper;

namespace Soltex.App;

internal sealed class WhisperSettingsStore
{
    internal const int MaximumDocumentBytes = 2 * 1024 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _filePath;

    internal WhisperSettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    internal WhisperSettingsLoadResult Load()
    {
        try
        {
            using FileStream stream = new(
                _filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            if (stream.Length > MaximumDocumentBytes)
            {
                return WhisperSettingsMigrator.Load(null);
            }

            byte[] bytes = GC.AllocateUninitializedArray<byte>(MaximumDocumentBytes + 1);
            try
            {
                int length = 0;
                while (length < bytes.Length)
                {
                    int read = stream.Read(bytes, length, bytes.Length - length);
                    if (read == 0)
                    {
                        break;
                    }

                    length += read;
                }

                if (length > MaximumDocumentBytes || stream.ReadByte() != -1)
                {
                    return WhisperSettingsMigrator.Load(null);
                }

                string json = new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true).GetString(bytes, 0, length);
                WhisperSettingsDocument? document =
                    JsonSerializer.Deserialize<WhisperSettingsDocument>(json, SerializerOptions);
                return WhisperSettingsMigrator.Load(document);
            }
            finally
            {
                Array.Clear(bytes);
            }
        }
        catch (FileNotFoundException)
        {
            return WhisperSettingsMigrator.Load(null);
        }
        catch (DirectoryNotFoundException)
        {
            return WhisperSettingsMigrator.Load(null);
        }
        catch (Exception exception) when (IsExpectedReadFailure(exception))
        {
            return WhisperSettingsMigrator.Load(null);
        }
    }

    internal void Save(WhisperSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string json = JsonSerializer.Serialize(settings.ToDocument(), SerializerOptions);
        byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);
        if (bytes.Length > MaximumDocumentBytes)
        {
            throw new InvalidOperationException("The Whisper settings document exceeded its size bound.");
        }

        string? directory = Path.GetDirectoryName(_filePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("The Whisper settings file requires a parent directory.");
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = _filePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            Array.Clear(bytes);
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or SecurityException)
            {
                // A failed cleanup must not overwrite the primary save result.
            }
        }
    }

    private static bool IsExpectedReadFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException or
            JsonException or DecoderFallbackException;
}
