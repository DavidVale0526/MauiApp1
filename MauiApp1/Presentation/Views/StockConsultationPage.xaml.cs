using MauiApp1.Presentation.ViewModels;
using MauiApp1.Application.Sync;

namespace MauiApp1.Presentation.Views;

public partial class StockConsultationPage : CoordinatorRefreshPage
{
    private void OnRootLayoutSizeChanged(object? sender, EventArgs args)
    {
        RootLayout.Padding = RootLayout.Width >= 920
            ? new Thickness(32, 28)
            : new Thickness(16, 20);
    }

    private readonly StockConsultationViewModel _viewModel;

    public StockConsultationPage(StockConsultationViewModel viewModel, ILocalDataChangeNotifier changeNotifier) : base(changeNotifier)
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
}
