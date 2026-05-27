using Alpaca.Markets;
using AlpacaEnvironments = Alpaca.Markets.Environments;

namespace Hound.Api.Services;

public interface IAlpacaPortfolioService
{
    Task<IAccount> GetAccountAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IPosition>> ListPositionsAsync(CancellationToken cancellationToken = default);
    Task<IOrder> ClosePositionAsync(string symbol, CancellationToken cancellationToken = default);
}

public class AlpacaPortfolioService : IAlpacaPortfolioService, IDisposable
{
    private readonly IAlpacaTradingClient _client;

    public AlpacaPortfolioService(IConfiguration configuration)
    {
        var keyId = configuration["Alpaca:ApiKeyId"] ?? string.Empty;
        var secret = configuration["Alpaca:SecretKey"] ?? string.Empty;
        var env = configuration["Alpaca:Environment"] ?? "Paper";

        var secretKey = new SecretKey(keyId, secret);
        var environment = string.Equals(env, "Live", StringComparison.OrdinalIgnoreCase)
            ? AlpacaEnvironments.Live
            : AlpacaEnvironments.Paper;

        _client = environment.GetAlpacaTradingClient(secretKey);
    }

    public Task<IAccount> GetAccountAsync(CancellationToken cancellationToken = default)
        => _client.GetAccountAsync(cancellationToken);

    public Task<IReadOnlyList<IPosition>> ListPositionsAsync(CancellationToken cancellationToken = default)
        => _client.ListPositionsAsync(cancellationToken);

    public async Task<IOrder> ClosePositionAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var request = new DeletePositionRequest(symbol);
        return await _client.DeletePositionAsync(request, cancellationToken);
    }

    public void Dispose() => _client.Dispose();
}
