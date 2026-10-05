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

    private void OnPageSizeChanged(object? sender, EventArgs eventArgs)
    {
        var isWide = Width >= 900;
        PageLayout.Padding = isWide ? new Thickness(32, 28) : new Thickness(16, 20);

        ContentLayout.ColumnDefinitions.Clear();
        ContentLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        if (isWide)
        {
            ContentLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }
        ContentLayout.RowDefinitions.Clear();
        ContentLayout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        if (isWide)
        {
            ContentLayout.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            ContentLayout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }
        else
        {
            ContentLayout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            ContentLayout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            ContentLayout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        Grid.SetRow(HeaderArea, 0);
        Grid.SetColumn(HeaderArea, 0);
        Grid.SetColumnSpan(HeaderArea, isWide ? 2 : 1);
        Grid.SetRow(CreateCustomerCard, 1);
        Grid.SetColumn(CreateCustomerCard, 0);
        Grid.SetRow(CustomerListCard, isWide ? 1 : 2);
        Grid.SetColumn(CustomerListCard, isWide ? 1 : 0);
        Grid.SetRow(ErrorLabel, isWide ? 2 : 3);
        Grid.SetColumn(ErrorLabel, 0);
        Grid.SetColumnSpan(ErrorLabel, isWide ? 2 : 1);
        CustomersCollection.HeightRequest = isWide ? 560 : 420;
    }
}
