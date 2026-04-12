namespace Hound.Core.Models;

/// <summary>
/// Represents the lifecycle state of a tuner experiment.
/// </summary>
public enum TunerExperimentStatus
{
    /// <summary>Candidate scored better than baseline; a human reviewer has acknowledged the improvement.</summary>
    Improved,
    Equal,
    Worse,
    Crash,
    /// <summary>Candidate scored better than baseline and is awaiting human review (apply or reject).</summary>
    PendingReview,
    Applied,
    Rejected
}

/// <summary>
/// Records a single TunerHound experiment: the config change proposed, eval scores, and review status.
/// Persisted to the TunerExperiments RavenDB collection.
/// </summary>
public class TunerExperiment
{
    public string Id { get; set; } = string.Empty;
    public string HoundName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>JSON snapshot of the config before the proposed change.</summary>
    public string ConfigBefore { get; set; } = string.Empty;

    /// <summary>JSON snapshot of the config after the proposed change.</summary>
    public string ConfigAfter { get; set; } = string.Empty;

    public double BaselineScore { get; set; }
    public double CandidateScore { get; set; }
    public double Delta => CandidateScore - BaselineScore;

    public TunerExperimentStatus Status { get; set; } = TunerExperimentStatus.PendingReview;

    /// <summary>TunerHound's reasoning for the proposed modification.</summary>
    public string Rationale { get; set; } = string.Empty;
}
