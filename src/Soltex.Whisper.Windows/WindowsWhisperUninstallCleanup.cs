using Soltex.Security;
using Soltex.Whisper;

namespace Soltex.Whisper.Windows;

public sealed record WhisperUninstallCleanupResult(
    bool ModelArtifactRemoved,
    int InterruptedDownloadsRemoved,
    bool InstallLockRemoved,
    bool SettingsRemoved,
    int SettingsTemporaryArtifactsRemoved,
    bool EncryptedHistoryRemoved,
    bool ProviderCredentialRemoved);

/// <summary>
/// Removes only the persisted Whisper artifacts owned by the current Soltex
/// contract. Shared authenticated-state keys and unrelated product data remain.
/// </summary>
public static class WindowsWhisperUninstallCleanup
{
    private const string SettingsFileName = "whisper-settings.json";
    private const string HistoryStoreName = "whisper-history";
    private static readonly string[] StoreSuffixes = [".json", ".json.bak", ".mac"];

    public static async ValueTask<WhisperUninstallCleanupResult> CleanAsync(
        string productDataRoot,
        CancellationToken cancellationToken = default)
    {
        string root = PathSafety.NormalizeExistingDirectory(productDataRoot);
        cancellationToken.ThrowIfCancellationRequested();

        ModelCleanupResult model = CleanModelStore(root, cancellationToken);
        StateCleanupResult state = await CleanProtectedStateAsync(root, cancellationToken)
            .ConfigureAwait(false);
        FileCleanupResult settings = CleanSettings(root, cancellationToken);
        TryDeleteEmptyDirectory(root);

        return new WhisperUninstallCleanupResult(
            model.ModelArtifactRemoved,
            model.InterruptedDownloadsRemoved,
            model.InstallLockRemoved,
            settings.PrimaryRemoved,
            settings.TemporaryArtifactsRemoved,
            state.HistoryRemoved,
            state.CredentialRemoved);
    }

    private static ModelCleanupResult CleanModelStore(
        string root,
        CancellationToken cancellationToken)
    {
        string whisperRoot = PathSafety.CombineUnderRoot(root, "whisper");
        if (!Directory.Exists(whisperRoot))
        {
            return new ModelCleanupResult(false, 0, false);
        }

        whisperRoot = PathSafety.NormalizeExistingDirectory(whisperRoot);
        string modelsRoot = PathSafety.CombineUnderRoot(whisperRoot, "models");
        if (!Directory.Exists(modelsRoot))
        {
            TryDeleteEmptyDirectory(whisperRoot);
            return new ModelCleanupResult(false, 0, false);
        }

        modelsRoot = PathSafety.NormalizeExistingDirectory(modelsRoot);
        string modelPath = PathSafety.CombineUnderRoot(
            modelsRoot,
            WhisperLocalModelArtifact.TurboQ5Cpu.FileName);
        string lockPath = PathSafety.CombineUnderRoot(modelsRoot, ".install.lock");
        bool modelRemoved = false;
        int partialsRemoved = 0;
        bool lockRemoved;

        using (FileStream installLock = OpenInstallLock(lockPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            modelRemoved = DeleteExactFile(modelPath);
            foreach (string candidate in Directory.EnumerateFiles(
                         modelsRoot,
                         WhisperLocalModelArtifact.TurboQ5Cpu.FileName + ".*.partial",
                         SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsRecognizedPartial(candidate))
                {
                    partialsRemoved += DeleteExactFile(candidate) ? 1 : 0;
                }
            }
        }

        lockRemoved = DeleteExactFile(lockPath);
        TryDeleteEmptyDirectory(modelsRoot);
        TryDeleteEmptyDirectory(whisperRoot);
        return new ModelCleanupResult(modelRemoved, partialsRemoved, lockRemoved);
    }

    private static async ValueTask<StateCleanupResult> CleanProtectedStateAsync(
        string root,
        CancellationToken cancellationToken)
    {
        string stateRoot = PathSafety.CombineUnderRoot(root, "state");
        if (!Directory.Exists(stateRoot))
        {
            return new StateCleanupResult(false, false);
        }

        stateRoot = PathSafety.NormalizeExistingDirectory(stateRoot);
        bool historyExisted = StoreArtifactsExist(stateRoot, HistoryStoreName);
        await using (WindowsWhisperHistoryRetentionStore history = new(stateRoot))
        {
            await history.ClearAsync(cancellationToken).ConfigureAwait(false);
        }

        string credentialStoreName =
            "whisper-credential-" + WhisperLocalModelDefaults.ProviderId;
        bool credentialExisted = StoreArtifactsExist(stateRoot, credentialStoreName);
        await using (WindowsWhisperCredentialStore credential = new(
                         stateRoot,
                         WhisperLocalModelDefaults.ProviderId))
        {
            await credential.DeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        TryDeleteEmptyDirectory(stateRoot);
        return new StateCleanupResult(historyExisted, credentialExisted);
    }

    private static FileCleanupResult CleanSettings(
        string root,
        CancellationToken cancellationToken)
    {
        string settingsPath = PathSafety.CombineUnderRoot(root, SettingsFileName);
        bool primaryRemoved = DeleteExactFile(settingsPath);
        int temporaryArtifactsRemoved = 0;
        foreach (string candidate in Directory.EnumerateFiles(
                     root,
                     SettingsFileName + ".tmp-*",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsRecognizedSettingsTemporary(candidate))
            {
                temporaryArtifactsRemoved += DeleteExactFile(candidate) ? 1 : 0;
            }
        }

        return new FileCleanupResult(primaryRemoved, temporaryArtifactsRemoved);
    }

    private static FileStream OpenInstallLock(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                _ = PathSafety.NormalizeExistingFile(path);
            }

            return new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.WriteThrough);
        }
        catch (IOException exception)
        {
            throw new IOException(
                "The local Whisper model store is busy and could not be cleaned.",
                exception);
        }
    }

    private static bool DeleteExactFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        string normalized = PathSafety.NormalizeExistingFile(path);
        File.Delete(normalized);
        if (File.Exists(normalized))
        {
            throw new IOException("An exact-owned Whisper artifact could not be deleted.");
        }

        return true;
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        string normalized = PathSafety.NormalizeExistingDirectory(path);
        if (!Directory.EnumerateFileSystemEntries(normalized).Any())
        {
            Directory.Delete(normalized);
        }
    }

    private static bool StoreArtifactsExist(string root, string storeName) =>
        StoreSuffixes
            .Select(suffix => PathSafety.CombineUnderRoot(root, storeName + suffix))
            .Any(File.Exists);

    private static bool IsRecognizedPartial(string path)
    {
        string fileName = Path.GetFileName(path);
        string prefix = WhisperLocalModelArtifact.TurboQ5Cpu.FileName + ".";
        const string suffix = ".partial";
        if (!fileName.StartsWith(prefix, StringComparison.Ordinal) ||
            !fileName.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        string token = fileName[prefix.Length..^suffix.Length];
        return token.Length == 32 && token.All(Uri.IsHexDigit);
    }

    private static bool IsRecognizedSettingsTemporary(string path)
    {
        string fileName = Path.GetFileName(path);
        string prefix = SettingsFileName + ".tmp-";
        if (!fileName.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        string token = fileName[prefix.Length..];
        return token.Length == 32 && token.All(Uri.IsHexDigit);
    }

    private readonly record struct ModelCleanupResult(
        bool ModelArtifactRemoved,
        int InterruptedDownloadsRemoved,
        bool InstallLockRemoved);

    private readonly record struct StateCleanupResult(
        bool HistoryRemoved,
        bool CredentialRemoved);

    private readonly record struct FileCleanupResult(
        bool PrimaryRemoved,
        int TemporaryArtifactsRemoved);
}
