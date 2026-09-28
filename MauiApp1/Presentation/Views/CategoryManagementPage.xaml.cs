using MauiApp1.Application.Categories;
using MauiApp1.Presentation.ViewModels;

namespace MauiApp1.Presentation.Views;

public partial class CategoryManagementPage : ContentPage
{
    private readonly CategoryManagementViewModel _viewModel;

    public CategoryManagementPage(CategoryManagementViewModel viewModel)
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

    private void OnEditClicked(object? sender, EventArgs eventArgs)
    {
        if ((sender as Button)?.BindingContext is CategorySummary category)
        {
            _viewModel.BeginEdit(category);
        }
    }

    private async void OnDeleteClicked(object? sender, EventArgs eventArgs)
    {
        if ((sender as Button)?.BindingContext is not CategorySummary category)
        {
            return;
        }

        var confirmed = await DisplayAlertAsync(
            "Eliminar categoría",
            $"¿Deseas eliminar la categoría «{category.Name}»? No se podrá eliminar si tiene productos asociados.",
            "Eliminar",
            "Cancelar");
        if (confirmed)
        {
            await _viewModel.DeleteAsync(category);
        }
    }

    private async void OnCloseClicked(object? sender, EventArgs eventArgs) => await Navigation.PopModalAsync();
}
