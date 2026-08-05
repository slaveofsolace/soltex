namespace Soltex.Security;

public interface IProtectionHealthSource
{
    Task<DefenderHealthSnapshot> GetHealthAsync(CancellationToken cancellationToken = default);
}
