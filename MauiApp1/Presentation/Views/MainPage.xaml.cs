using MauiApp1.Presentation.ViewModels;

namespace MauiApp1.Presentation.Views;

public partial class MainPage : ContentPage
{
    public MainPage(MainPageViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
