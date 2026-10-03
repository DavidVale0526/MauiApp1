using MauiApp1.Application.Sync;

namespace MauiApp1.Presentation.Views;

public abstract class CoordinatorRefreshPage : ContentPage
{
    private readonly ILocalDataChangeNotifier _changeNotifier;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    protected CoordinatorRefreshPage(ILocalDataChangeNotifier changeNotifier) => _changeNotifier = changeNotifier;

    protected abstract Task RefreshAfterCoordinatorChangesAsync();

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _changeNotifier.DataChanged += OnLocalDataChanged;
    }

    protected override void OnDisappearing()
    {
        _changeNotifier.DataChanged -= OnLocalDataChanged;
        base.OnDisappearing();
    }

    private async void OnLocalDataChanged()
    {
        if (!IsVisible || !await _refreshGate.WaitAsync(0)) return;
        try
        {
            await MainThread.InvokeOnMainThreadAsync(RefreshAfterCoordinatorChangesAsync);
        }
        catch
        {
            // The next screen entry or a manual refresh can reload the local data.
        }
        finally
        {
            _refreshGate.Release();
        }
    }
}
