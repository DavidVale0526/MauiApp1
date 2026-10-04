using MauiApp1.Application.Products;
using MauiApp1.Application.Sync;
using MauiApp1.Presentation.ViewModels;

namespace MauiApp1.Presentation.Views;

public partial class ProductManagementPage : CoordinatorRefreshPage
{
    private void OnPageContentSizeChanged(object? sender, EventArgs args)
    {
        var useDesktopLayout = PageContent.Width >= 920;
        ProductFormGrid.ColumnDefinitions.Clear();
        ProductFormGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        if (useDesktopLayout)
        {
            ProductFormGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        }

        ProductFormGrid.RowDefinitions.Clear();
        var rowCount = useDesktopLayout ? 3 : 5;
        for (var row = 0; row < rowCount; row++)
        {
            ProductFormGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        Grid.SetColumn(NameField, 0);
        Grid.SetColumnSpan(NameField, useDesktopLayout ? 2 : 1);
        Grid.SetRow(PriceField, useDesktopLayout ? 1 : 1);
        Grid.SetColumn(PriceField, 0);
        Grid.SetRow(UnitField, useDesktopLayout ? 1 : 2);
        Grid.SetColumn(UnitField, useDesktopLayout ? 1 : 0);
        Grid.SetRow(CategoryField, useDesktopLayout ? 2 : 3);
        Grid.SetColumn(CategoryField, 0);
        Grid.SetRow(MinimumStockField, useDesktopLayout ? 2 : 4);
        Grid.SetColumn(MinimumStockField, useDesktopLayout ? 1 : 0);

        PageContent.Padding = useDesktopLayout
            ? new Thickness(32, 28)
            : new Thickness(16, 20);
        ProductsCollection.HeightRequest = useDesktopLayout ? 620 : 460;
    }

    private readonly ProductManagementViewModel _viewModel;

    public ProductManagementPage(ProductManagementViewModel viewModel, ILocalDataChangeNotifier changeNotifier) : base(changeNotifier)
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
        _viewModel.IsEditing ? Task.CompletedTask : _viewModel.LoadAsync();

    private async void OnBackClicked(object? sender, EventArgs eventArgs) => await Navigation.PopModalAsync();

    private void OnEditClicked(object? sender, EventArgs eventArgs)
    {
        if ((sender as Button)?.BindingContext is ProductSummary product)
        {
            _viewModel.BeginEdit(product);
        }
    }

    private async void OnChangeStatusClicked(object? sender, EventArgs eventArgs)
    {
        if ((sender as Button)?.BindingContext is not ProductSummary product)
        {
            return;
        }

        var isActive = !product.IsActive;
        if (!isActive)
        {
            var confirmed = await DisplayAlertAsync(
                "Desactivar producto",
                $"«{product.Name}» dejará de estar activo para futuras operaciones. Sus datos se conservarán para el historial.",
                "Desactivar",
                "Cancelar");
            if (!confirmed)
            {
                return;
            }
        }

        await _viewModel.SetActiveAsync(product, isActive);
    }
}
