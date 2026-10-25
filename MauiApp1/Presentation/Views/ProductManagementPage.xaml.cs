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
        var rowCount = useDesktopLayout ? 4 : 6;
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
        Grid.SetRow(ProductImageField, useDesktopLayout ? 3 : 5);
        Grid.SetColumn(ProductImageField, 0);
        Grid.SetColumnSpan(ProductImageField, useDesktopLayout ? 2 : 1);

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

    private async void OnChooseProductImageClicked(object? sender, EventArgs eventArgs)
    {
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Selecciona la imagen del producto",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    [DevicePlatform.Android] = ["image/jpeg", "image/png"],
                    [DevicePlatform.WinUI] = [".jpg", ".jpeg", ".png"]
                })
            });
            if (file is null) return;
            await using var input = await file.OpenReadAsync();
            await using var output = new MemoryStream();
            var buffer = new byte[16 * 1024];
            int count;
            while ((count = await input.ReadAsync(buffer)) > 0)
            {
                if (output.Length + count > ProductImageRules.MaximumBytes)
                {
                    _viewModel.SetErrorMessage("La imagen supera 512 KB. Elige una versión JPG o PNG más pequeña.");
                    return;
                }
                await output.WriteAsync(buffer.AsMemory(0, count));
            }

            var imageData = output.ToArray();
            if (!ProductImageRules.IsSupported(imageData))
            {
                _viewModel.SetErrorMessage("El archivo no es una imagen JPG o PNG válida.");
                return;
            }
            _viewModel.SetProductImage(imageData);
        }
        catch
        {
            _viewModel.SetErrorMessage("No se pudo abrir la imagen. Inténtalo de nuevo.");
        }
    }

    private void OnRemoveProductImageClicked(object? sender, EventArgs eventArgs) => _viewModel.RemoveProductImage();

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
