using MauiApp1.Presentation.ViewModels;
using MauiApp1.Application.Sync;
using MauiApp1.Presentation.Services;

namespace MauiApp1.Presentation.Views;

public partial class SaleHistoryPage : CoordinatorRefreshPage
{
    private readonly SaleHistoryViewModel _viewModel;
    private readonly IReceiptDocumentActions _documentActions;

    public SaleHistoryPage(SaleHistoryViewModel viewModel, ILocalDataChangeNotifier changeNotifier,
        IReceiptDocumentActions documentActions) : base(changeNotifier)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _documentActions = documentActions;
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

        await Navigation.PushAsync(new SaleTicketPage(card.Sale, _documentActions));
    }

    private void OnPageSizeChanged(object? sender, EventArgs eventArgs)
    {
        var isWide = Width >= 900;
        if (sender is Grid pageLayout)
        {
            pageLayout.Padding = isWide ? new Thickness(32, 28) : new Thickness(16, 20);
        }
    }
}
