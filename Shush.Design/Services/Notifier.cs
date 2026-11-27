namespace Shush.Design.Services;

public enum NoticeKind { Success, Error, Info }

public sealed record Notice(Guid Id, NoticeKind Kind, string Message);

/// <summary>Per browser tab: short messages ("Saved.") and errors shown by the layout.</summary>
public sealed class Notifier
{
    private readonly List<Notice> _notices = [];

    public event Action? Changed;

    public IReadOnlyList<Notice> Notices => _notices;

    public void Success(string message) => Add(NoticeKind.Success, message, TimeSpan.FromSeconds(4));

    public void Info(string message) => Add(NoticeKind.Info, message, TimeSpan.FromSeconds(6));

    /// <summary>Errors stay until dismissed.</summary>
    public void Error(string message) => Add(NoticeKind.Error, message, null);

    public void Dismiss(Guid id)
    {
        _notices.RemoveAll(n => n.Id == id);
        Changed?.Invoke();
    }

    private void Add(NoticeKind kind, string message, TimeSpan? lifetime)
    {
        var notice = new Notice(Guid.NewGuid(), kind, message);
        _notices.Add(notice);
        if (_notices.Count > 4)
            _notices.RemoveAt(0);
        Changed?.Invoke();

        if (lifetime is { } delay)
            _ = Task.Delay(delay).ContinueWith(_ => Dismiss(notice.Id), TaskScheduler.Default);
    }
}
