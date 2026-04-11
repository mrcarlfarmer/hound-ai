using Hound.Core.Models;
using Raven.Client.Documents.Indexes;

namespace Hound.Api.Indexes;

public class ActivityLog_ByPackAndTime : AbstractIndexCreationTask<ActivityLog>
{
    public ActivityLog_ByPackAndTime()
    {
        Map = activities => from a in activities
                            select new { a.PackId, a.Timestamp };
    }
}
