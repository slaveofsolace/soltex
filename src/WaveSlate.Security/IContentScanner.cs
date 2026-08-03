namespace WaveSlate.Security;

public interface IContentScanner : IDisposable
{
    string EngineName { get; }

    ContentScanResult Scan(byte[] content, string contentName);
}

public sealed class UnavailableContentScanner(string reason) : IContentScanner
{
    public string EngineName => "Windows AMSI unavailable";

    public ContentScanResult Scan(byte[] content, string contentName) =>
        new(ContentVerdict.Unavailable, EngineName, 0, reason);

    public void Dispose()
    {
    }
}
