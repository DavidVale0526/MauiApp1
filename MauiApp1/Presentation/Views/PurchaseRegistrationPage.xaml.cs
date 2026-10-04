using MauiApp1.Presentation.ViewModels;
using MauiApp1.Application.Sync;

namespace MauiApp1.Presentation.Views;

public partial class PurchaseRegistrationPage : CoordinatorRefreshPage
{
    private readonly PurchaseRegistrationViewModel _viewModel;

    public PurchaseRegistrationPage(PurchaseRegistrationViewModel viewModel, ILocalDataChangeNotifier changeNotifier) : base(changeNotifier)
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

    protected override Task RefreshAfterCoordinatorChangesAsync() => _viewModel.RefreshAfterCoordinatorChangesAsync();

    private async void OnBackClicked(object? sender, EventArgs eventArgs) => await Navigation.PopModalAsync();

    private void OnRemoveLineClicked(object? sender, EventArgs eventArgs)
    {
        if ((sender as Button)?.BindingContext is PurchaseCartLine line)
        {
            _viewModel.RemoveLine(line);
        }
    }

    private async void OnReviewClicked(object? sender, EventArgs eventArgs)
    {
        var purchase = _viewModel.PreparePurchase();
        if (purchase is null)
        {
            return;
        }

        var confirmed = await DisplayAlertAsync(
            "Confirmar compra",
            _viewModel.BuildReviewText(),
            "Confirmar",
            "Volver a revisar");
        if (!confirmed)
        {
            _viewModel.CancelReview();
            return;
        }

        await _viewModel.RecordPendingAsync();
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
        Grid.SetRow(AddProductCard, 1);
        Grid.SetColumn(AddProductCard, 0);
        Grid.SetRow(PurchaseSummaryCard, isWide ? 1 : 2);
        Grid.SetColumn(PurchaseSummaryCard, isWide ? 1 : 0);
        Grid.SetRow(FeedbackArea, isWide ? 2 : 3);
        Grid.SetColumn(FeedbackArea, 0);
        Grid.SetColumnSpan(FeedbackArea, isWide ? 2 : 1);
    }
}
