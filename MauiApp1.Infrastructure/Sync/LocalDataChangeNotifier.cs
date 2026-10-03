using MauiApp1.Application.Sync;

namespace MauiApp1.Infrastructure.Sync;

public sealed class LocalDataChangeNotifier : ILocalDataChangeNotifier
{
    public event Action? DataChanged;

    public void NotifyDataChanged()
    {
        if (DataChanged is not { } subscribers) return;
        foreach (Action subscriber in subscribers.GetInvocationList())
        {
            try { subscriber(); }
            catch { }
        }
    }
}
