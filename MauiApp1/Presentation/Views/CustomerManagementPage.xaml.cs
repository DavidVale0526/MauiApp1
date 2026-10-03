using MauiApp1.Presentation.ViewModels;
using MauiApp1.Application.Sync;

namespace MauiApp1.Presentation.Views;

public partial class CustomerManagementPage : CoordinatorRefreshPage
{
    private readonly CustomerManagementViewModel _viewModel;

    public CustomerManagementPage(CustomerManagementViewModel viewModel, ILocalDataChangeNotifier changeNotifier) : base(changeNotifier)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override Task RefreshAfterCoordinatorChangesAsync() => _viewModel.LoadAsync();

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is CustomerManagementViewModel viewModel)
            await viewModel.LoadAsync();
    }

    private async void OnBackClicked(object? sender, EventArgs eventArgs)
    {
        if (Navigation.ModalStack.Count > 0) await Navigation.PopModalAsync();
        else if (Navigation.NavigationStack.Count > 1) await Navigation.PopAsync();
    }
}
