using MauiApp1.Presentation.ViewModels;

namespace MauiApp1.Presentation.Views;

public partial class DevicePairingPage : ContentPage
{
    private readonly DevicePairingViewModel _viewModel;
    public DevicePairingPage(DevicePairingViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }
    private async void OnBackClicked(object? sender, EventArgs args) => await Navigation.PopModalAsync();
}
