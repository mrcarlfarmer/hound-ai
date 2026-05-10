using Hound.Core.Models;
using Raven.Client.Documents.Indexes;

namespace Hound.Api.Indexes;

public class TradeDocument_BySymbolAndStatus : AbstractIndexCreationTask<TradeDocument>
{
    public TradeDocument_BySymbolAndStatus()
    {
        Map = trades => from t in trades
                        select new { t.Symbol, t.FillStatus, t.CreatedAt };
    }
}
