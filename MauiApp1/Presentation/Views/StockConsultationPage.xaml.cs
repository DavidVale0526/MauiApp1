using MauiApp1.Presentation.ViewModels;

namespace MauiApp1.Presentation.Views;

public partial class StockConsultationPage : ContentPage
{
    private readonly StockConsultationViewModel _viewModel;

    public StockConsultationPage(StockConsultationViewModel viewModel)
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

    private async void OnBackClicked(object? sender, EventArgs eventArgs) => await Navigation.PopModalAsync();
}
