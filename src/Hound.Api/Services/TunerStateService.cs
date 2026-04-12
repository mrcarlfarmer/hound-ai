namespace Hound.Api.Services;

/// <summary>
/// Tracks the pause/resume intent for the tuner on the API side.
/// This is an in-process flag for the hound-api service. The trading-pack's
/// <c>TunerHostedService</c> has its own pause flag controlled via its own
/// <c>Pause()</c>/<c>Resume()</c> methods (e.g., via a shared config source or RavenDB signal).
/// </summary>
public class TunerStateService
{
    private volatile bool _isPaused;

    public bool IsPaused => _isPaused;

    public void Pause() => _isPaused = true;

    public void Resume() => _isPaused = false;
}
