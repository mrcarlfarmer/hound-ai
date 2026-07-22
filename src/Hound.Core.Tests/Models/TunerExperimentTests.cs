using Hound.Core.Models;

namespace Hound.Core.Tests.Models;

[TestClass]
public sealed class TunerExperimentTests
{
    [TestMethod]
    public void TunerExperiment_Defaults_AreInitialized()
    {
        var experiment = new TunerExperiment();

        Assert.AreEqual(string.Empty, experiment.Id);
        Assert.AreEqual(string.Empty, experiment.HoundName);
        Assert.AreEqual(string.Empty, experiment.ConfigBefore);
        Assert.AreEqual(string.Empty, experiment.ConfigAfter);
        Assert.AreEqual(0d, experiment.BaselineScore);
        Assert.AreEqual(0d, experiment.CandidateScore);
        Assert.AreEqual(0d, experiment.Delta);
        Assert.AreEqual(TunerExperimentStatus.PendingReview, experiment.Status);
        Assert.AreEqual(string.Empty, experiment.Rationale);
    }

    [TestMethod]
    public void TunerExperiment_DefaultTimestamp_IsRecentUtc()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var experiment = new TunerExperiment();
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.IsTrue(experiment.Timestamp >= before && experiment.Timestamp <= after);
    }

    [TestMethod]
    public void Delta_ReturnsCandidateScoreMinusBaselineScore()
    {
        var experiment = new TunerExperiment
        {
            BaselineScore = 0.42,
            CandidateScore = 0.75
        };

        Assert.AreEqual(0.33, experiment.Delta, 0.0001);
    }

    [TestMethod]
    public void Delta_WhenScoresChange_RecalculatesFromCurrentValues()
    {
        var experiment = new TunerExperiment
        {
            BaselineScore = 0.8,
            CandidateScore = 0.6
        };

        Assert.AreEqual(-0.2, experiment.Delta, 0.0001);

        experiment.CandidateScore = 0.95;

        Assert.AreEqual(0.15, experiment.Delta, 0.0001);
    }

    [TestMethod]
    public void TunerExperiment_SetProperties_ReflectsValues()
    {
        var timestamp = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc);

        var experiment = new TunerExperiment
        {
            Id = "TunerExperiments/1",
            HoundName = "StrategyHound",
            Timestamp = timestamp,
            ConfigBefore = "{\"Temperature\":0.1}",
            ConfigAfter = "{\"Temperature\":0.05}",
            BaselineScore = 0.6,
            CandidateScore = 0.7,
            Status = TunerExperimentStatus.Improved,
            Rationale = "Lower temperature improved consistency."
        };

        Assert.AreEqual("TunerExperiments/1", experiment.Id);
        Assert.AreEqual("StrategyHound", experiment.HoundName);
        Assert.AreEqual(timestamp, experiment.Timestamp);
        Assert.AreEqual("{\"Temperature\":0.1}", experiment.ConfigBefore);
        Assert.AreEqual("{\"Temperature\":0.05}", experiment.ConfigAfter);
        Assert.AreEqual(0.6, experiment.BaselineScore);
        Assert.AreEqual(0.7, experiment.CandidateScore);
        Assert.AreEqual(TunerExperimentStatus.Improved, experiment.Status);
        Assert.AreEqual("Lower temperature improved consistency.", experiment.Rationale);
    }

    [TestMethod]
    public void TunerExperimentStatus_AllValues_Defined()
    {
        Assert.IsTrue(Enum.IsDefined(typeof(TunerExperimentStatus), TunerExperimentStatus.Improved));
        Assert.IsTrue(Enum.IsDefined(typeof(TunerExperimentStatus), TunerExperimentStatus.Equal));
        Assert.IsTrue(Enum.IsDefined(typeof(TunerExperimentStatus), TunerExperimentStatus.Worse));
        Assert.IsTrue(Enum.IsDefined(typeof(TunerExperimentStatus), TunerExperimentStatus.Crash));
        Assert.IsTrue(Enum.IsDefined(typeof(TunerExperimentStatus), TunerExperimentStatus.PendingReview));
        Assert.IsTrue(Enum.IsDefined(typeof(TunerExperimentStatus), TunerExperimentStatus.Applied));
        Assert.IsTrue(Enum.IsDefined(typeof(TunerExperimentStatus), TunerExperimentStatus.Rejected));
    }
}
