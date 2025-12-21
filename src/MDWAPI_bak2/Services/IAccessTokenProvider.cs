using System.Threading;
using System.Threading.Tasks;

namespace MDWAPI.Services;

public interface IAccessTokenProvider
{
    Task<string> GetValidAccessTokenAsync(long shopId, CancellationToken ct = default);
}
