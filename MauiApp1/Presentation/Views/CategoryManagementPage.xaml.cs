using MauiApp1.Application.Categories;
using MauiApp1.Application.Sync;
using MauiApp1.Presentation.ViewModels;

namespace MauiApp1.Presentation.Views;

public partial class CategoryManagementPage : CoordinatorRefreshPage
{
    private readonly CategoryManagementViewModel _viewModel;

    public CategoryManagementPage(CategoryManagementViewModel viewModel, ILocalDataChangeNotifier changeNotifier) : base(changeNotifier)
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
        if (isWide)
        {
            ContentLayout.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        }
        else
        {
            ContentLayout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            ContentLayout.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        }
        Grid.SetColumn(FormCard, 0);
        Grid.SetRow(FormCard, 0);
        FormFields.ColumnDefinitions.Clear();
        FormFields.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        if (isWide)
        {
            FormFields.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        }
        FormFields.RowDefinitions.Clear();
        FormFields.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        if (!isWide)
        {
            FormFields.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }
        Grid.SetColumn(CategoryNameEntry, 0);
        Grid.SetRow(CategoryNameEntry, 0);
        Grid.SetColumn(FormActions, isWide ? 1 : 0);
        Grid.SetRow(FormActions, isWide ? 0 : 1);
        FormActions.HorizontalOptions = isWide ? LayoutOptions.End : LayoutOptions.Start;
        if (ContentLayout.Children.Count > 1)
        {
            var list = (View)ContentLayout.Children[1];
            Grid.SetColumn(list, isWide ? 1 : 0);
            Grid.SetRow(list, isWide ? 0 : 1);
        }
    }
}
