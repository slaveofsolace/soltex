namespace Soltex.Security;

public sealed class SecurityRuntime : IDisposable
{
    private readonly IContentScanner _scanner;
    private readonly AuthenticatedJsonStore[] _authenticatedStores;
    private bool _disposed;

    private SecurityRuntime(
        string dataRoot,
        string importsPath,
        ProductDataRootKind dataRootKind,
        IContentScanner scanner,
        PowerShellDefenderClient defender,
        AllowListStore allowList,
        QuarantineStore quarantine,
        FileAssessmentService assessor,
        SecurityAuditLog auditLog,
        AuthenticatedJsonStore[] authenticatedStores)
    {
        DataRoot = dataRoot;
        ImportsPath = importsPath;
        DataRootKind = dataRootKind;
        _scanner = scanner;
        Defender = defender;
        AllowList = allowList;
        Quarantine = quarantine;
        Assessor = assessor;
        AuditLog = auditLog;
        _authenticatedStores = authenticatedStores;
    }

    public string DataRoot { get; }
    public string ImportsPath { get; }
    public ProductDataRootKind DataRootKind { get; }
    public PowerShellDefenderClient Defender { get; }
    public AllowListStore AllowList { get; }
    public QuarantineStore Quarantine { get; }
    public FileAssessmentService Assessor { get; }
    public SecurityAuditLog AuditLog { get; }

    public static SecurityRuntime CreateDefault(string? dataRoot = null)
    {
        ProductDataRootResolution? resolution = dataRoot is null
            ? ProductDataRootResolver.ResolveDefault()
            : null;
        string root = dataRoot is null
            ? Path.Combine(resolution!.ProductRoot, "Security")
            : Path.GetFullPath(dataRoot);
        ProductDataRootKind dataRootKind = resolution?.Kind ?? ProductDataRootKind.Explicit;
        string stateRoot = Path.Combine(root, "state");
        string quarantineRoot = Path.Combine(root, "quarantine");
        string importsPath = dataRoot is null
            ? Path.Combine(resolution!.ProductRoot, "Imports")
            : Path.Combine(root, "imports");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(stateRoot);
        Directory.CreateDirectory(quarantineRoot);
        Directory.CreateDirectory(importsPath);
        _ = PathSafety.NormalizeExistingDirectory(root);
        _ = PathSafety.NormalizeExistingDirectory(stateRoot);
        _ = PathSafety.NormalizeExistingDirectory(quarantineRoot);
        _ = PathSafety.NormalizeExistingDirectory(importsPath);

        AuthenticatedJsonStore allowStore = new(stateRoot, "allow-list");
        AuthenticatedJsonStore quarantineIndexStore = new(stateRoot, "quarantine-index");
        AuthenticatedJsonStore auditStateStore = new(stateRoot, "audit-state");
        IContentScanner scanner = AmsiContentScanner.CreateOrUnavailable();
        AllowListStore allowList = new(allowStore);
        QuarantineStore quarantine = new(quarantineRoot, quarantineIndexStore);
        FileAssessmentService assessor = new(scanner, allowList);
        SecurityAuditLog audit = new(Path.Combine(root, "events.jsonl"), auditStateStore);

        return new SecurityRuntime(
            root,
            importsPath,
            dataRootKind,
            scanner,
            new PowerShellDefenderClient(),
            allowList,
            quarantine,
            assessor,
            audit,
            [allowStore, quarantineIndexStore, auditStateStore]);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        AuditLog.Dispose();
        Quarantine.Dispose();
        _scanner.Dispose();
        foreach (AuthenticatedJsonStore store in _authenticatedStores)
        {
            store.Dispose();
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
