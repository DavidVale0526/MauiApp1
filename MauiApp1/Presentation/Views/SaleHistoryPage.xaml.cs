using MauiApp1.Presentation.ViewModels;
using MauiApp1.Application.Sync;

namespace MauiApp1.Presentation.Views;

public partial class SaleHistoryPage : CoordinatorRefreshPage
{
    private readonly SaleHistoryViewModel _viewModel;

    public SaleHistoryPage(SaleHistoryViewModel viewModel, ILocalDataChangeNotifier changeNotifier) : base(changeNotifier)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    protected override Task RefreshAfterCoordinatorChangesAsync() => _viewModel.LoadAsync();

    private async void OnBackClicked(object? sender, EventArgs eventArgs) => await Navigation.PopModalAsync();

    private async void OnRefreshClicked(object? sender, EventArgs eventArgs) => await _viewModel.LoadAsync();

    private async void OnViewTicketClicked(object? sender, EventArgs eventArgs)
    {
        if ((sender as Button)?.BindingContext is not SaleHistoryCard card)
        {
            return;
        }

        await Navigation.PushAsync(new SaleTicketPage(card.Sale));
    }
}
