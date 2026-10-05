using MauiApp1.Presentation.ViewModels;
using MauiApp1.Application.Sync;

namespace MauiApp1.Presentation.Views;

public partial class OtherExitPage : CoordinatorRefreshPage
{
    private readonly OtherExitViewModel _viewModel;

    public OtherExitPage(OtherExitViewModel viewModel, ILocalDataChangeNotifier changeNotifier) : base(changeNotifier)
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

    protected override Task RefreshAfterCoordinatorChangesAsync() =>
        _viewModel.CanEdit ? _viewModel.LoadAsync() : Task.CompletedTask;

    private async void OnBackClicked(object? sender, EventArgs eventArgs) => await Navigation.PopModalAsync();

    private async void OnReviewClicked(object? sender, EventArgs eventArgs)
    {
        var operation = _viewModel.PrepareOperation();
        if (operation is null)
        {
            return;
        }

        var confirmed = await DisplayAlertAsync(
            "Confirmar salida",
            _viewModel.BuildReviewText(),
            "Registrar salida",
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

        SelectionGrid.ColumnDefinitions.Clear();
        SelectionGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        SelectionGrid.RowDefinitions.Clear();
        SelectionGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        SelectionGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        SelectionGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Grid.SetRow(ExitTypeField, 0);
        Grid.SetColumn(ExitTypeField, 0);
        Grid.SetRow(ProductField, 1);
        Grid.SetColumn(ProductField, 0);
        Grid.SetRow(ReasonField, 2);
        Grid.SetColumn(ReasonField, 0);

        StockQuantityGrid.ColumnDefinitions.Clear();
        StockQuantityGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        if (isWide)
        {
            StockQuantityGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }
        StockQuantityGrid.RowDefinitions.Clear();
        StockQuantityGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        StockQuantityGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        StockQuantityGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Grid.SetRow(StockCard, 0);
        Grid.SetColumn(StockCard, 0);
        Grid.SetColumnSpan(StockCard, isWide ? 2 : 1);
        Grid.SetRow(MixedWasteUnitField, isWide ? 1 : 1);
        Grid.SetColumn(MixedWasteUnitField, 0);
        Grid.SetRow(QuantityField, isWide ? 1 : 2);
        Grid.SetColumn(QuantityField, isWide ? 1 : 0);
    }
}
