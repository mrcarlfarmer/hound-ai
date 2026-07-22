using Hound.Api.Services;

namespace Hound.Api.Tests.Services;

[TestClass]
public sealed class TunerStateServiceTests
{
    [TestMethod]
    public void IsPaused_DefaultsToFalse()
    {
        var service = new TunerStateService();

        Assert.IsFalse(service.IsPaused);
    }

    [TestMethod]
    public void Pause_SetsIsPausedToTrue()
    {
        var service = new TunerStateService();

        service.Pause();

        Assert.IsTrue(service.IsPaused);
    }

    [TestMethod]
    public void Resume_AfterPause_SetsIsPausedToFalse()
    {
        var service = new TunerStateService();
        service.Pause();

        service.Resume();

        Assert.IsFalse(service.IsPaused);
    }

    [TestMethod]
    public void PauseAndResume_WhenCalledRepeatedly_ReflectLatestState()
    {
        var service = new TunerStateService();

        service.Pause();
        service.Pause();
        Assert.IsTrue(service.IsPaused);

        service.Resume();
        service.Resume();
        Assert.IsFalse(service.IsPaused);
    }
}
