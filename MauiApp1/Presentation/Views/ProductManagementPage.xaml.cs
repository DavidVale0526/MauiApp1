using MauiApp1.Application.Products;
using MauiApp1.Application.Sync;
using MauiApp1.Presentation.ViewModels;

namespace MauiApp1.Presentation.Views;

public partial class ProductManagementPage : CoordinatorRefreshPage
{
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
