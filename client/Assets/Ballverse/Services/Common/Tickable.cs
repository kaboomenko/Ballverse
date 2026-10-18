namespace Ballverse.Services
{
    /// <summary>A service that needs a per-frame call for timeouts and retries (driven by <see cref="ServicesRunner"/>).</summary>
    public interface ITickable
    {
        void Tick();
    }
}
