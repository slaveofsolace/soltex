namespace Soltex.Security;

/// <summary>
/// Canonical current identity plus narrowly scoped compatibility identifiers.
/// Compatibility values are security contracts and must not be changed as branding copy.
/// </summary>
public static class ProductIdentity
{
    public const string DisplayName = "Soltex";
    public const string CanonicalStorageDirectoryName = "Soltex";
    public const string LegacyStorageDirectoryName = "WaveSlate";
    public const string IntegrityManifestProduct = "Soltex";
    public const string LegacyIntegrityManifestProduct = "WaveSlate";
    public const string RunEicarEnvironmentVariable = "SOLTEX_RUN_EICAR";
    public const string LegacyRunEicarEnvironmentVariable = "WAVESLATE_RUN_EICAR";

    // This description is embedded in existing per-user DPAPI state. Changing it
    // requires a separately versioned key migration and recovery path.
    internal const string LegacyDpapiDescription = "WaveSlate authenticated state";

    internal static bool IsSupportedIntegrityManifestProduct(int schemaVersion, string? product) =>
        schemaVersion == 1 &&
        (string.Equals(product, IntegrityManifestProduct, StringComparison.Ordinal) ||
         string.Equals(product, LegacyIntegrityManifestProduct, StringComparison.Ordinal));
}
