namespace Shush.Design.Services;

/// <summary>
/// Coalesces bursts of change notifications (a run reports many times a second) into at most one refresh per
/// interval. Call <see cref="Mark"/> on every notification.
/// </summary>
public sealed class LiveRefresh : IDisposable
{
    private readonly Func<Task> _refresh;
    private readonly Timer _timer;
    private int _dirty;
    private int _running;

    public LiveRefresh(Func<Task> refresh, TimeSpan? interval = null)
    {
        _refresh = refresh;
        var every = interval ?? TimeSpan.FromMilliseconds(600);
        _timer = new Timer(_ => _ = TickAsync(), null, every, every);
    }

    public void Mark() => Interlocked.Exchange(ref _dirty, 1);

    private async Task TickAsync()
    {
        if (Interlocked.Exchange(ref _dirty, 0) == 0 || Interlocked.Exchange(ref _running, 1) == 1)
            return;
        try
        {
            await _refresh();
        }
        catch (ObjectDisposedException)
        {
            // The page went away mid-refresh.
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    public void Dispose() => _timer.Dispose();
}
