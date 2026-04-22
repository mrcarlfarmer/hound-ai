using Hound.Core.Models;

namespace Hound.Api.Services;

public interface IHealthCheckService
{
    Task<HealthReport> CheckAllAsync(CancellationToken cancellationToken = default);
}
