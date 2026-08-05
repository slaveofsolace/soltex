namespace Soltex.Security;

public enum ProductDataRootKind
{
    Explicit,
    Canonical,
    LegacyCompatibility
}

public sealed record ProductDataRootResolution(
    string ProductRoot,
    ProductDataRootKind Kind)
{
    public bool UsesLegacyCompatibility => Kind == ProductDataRootKind.LegacyCompatibility;
}

/// <summary>
/// Selects one product root without copying, merging, or deleting security state.
/// </summary>
public static class ProductDataRootResolver
{
    public static ProductDataRootResolution ResolveDefault() => Resolve(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public static ProductDataRootResolution Resolve(string localApplicationDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationDataRoot);
        string localRoot = PathSafety.NormalizeExistingDirectory(localApplicationDataRoot);
        string canonicalRoot = Path.Combine(localRoot, ProductIdentity.CanonicalStorageDirectoryName);
        string legacyRoot = Path.Combine(localRoot, ProductIdentity.LegacyStorageDirectoryName);

        bool canonicalExists = InspectProductRoot(canonicalRoot);
        bool legacyExists = InspectProductRoot(legacyRoot);
        ThrowIfAmbiguous(canonicalExists, legacyExists);

        if (canonicalExists)
        {
            return new ProductDataRootResolution(canonicalRoot, ProductDataRootKind.Canonical);
        }

        if (legacyExists)
        {
            return new ProductDataRootResolution(legacyRoot, ProductDataRootKind.LegacyCompatibility);
        }

        Directory.CreateDirectory(canonicalRoot);
        _ = PathSafety.NormalizeExistingDirectory(canonicalRoot);

        // Re-evaluate the legacy name after creation. A competing or interrupted
        // migration must fail closed instead of producing implicit precedence.
        legacyExists = InspectProductRoot(legacyRoot);
        ThrowIfAmbiguous(canonicalExists: true, legacyExists);
        return new ProductDataRootResolution(canonicalRoot, ProductDataRootKind.Canonical);
    }

    private static bool InspectProductRoot(string path)
    {
        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    "A product data root is a reparse point and cannot be trusted.");
            }

            if ((attributes & FileAttributes.Directory) == 0)
            {
                throw new InvalidDataException(
                    "A product data root name is occupied by a non-directory entry.");
            }

            _ = PathSafety.NormalizeExistingDirectory(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static void ThrowIfAmbiguous(bool canonicalExists, bool legacyExists)
    {
        if (canonicalExists && legacyExists)
        {
            throw new InvalidDataException(
                "Both the Soltex and legacy WaveSlate data roots exist. " +
                "Soltex will not merge, delete, or choose between security states automatically.");
        }
    }
}
