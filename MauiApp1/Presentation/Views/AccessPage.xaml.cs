using MauiApp1.Presentation.ViewModels;

namespace MauiApp1.Presentation.Views;

public partial class AccessPage : ContentPage
{
    private readonly AccessPageViewModel _viewModel;

    public AccessPage(AccessPageViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializeAsync();
    }
}
