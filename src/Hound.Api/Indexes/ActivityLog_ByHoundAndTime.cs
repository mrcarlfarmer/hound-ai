using Hound.Core.Models;
using Raven.Client.Documents.Indexes;

namespace Hound.Api.Indexes;

public class ActivityLog_ByHoundAndTime : AbstractIndexCreationTask<ActivityLog>
{
    public ActivityLog_ByHoundAndTime()
    {
        Map = activities => from a in activities
                            select new { a.HoundId, a.Timestamp };
    }
}
