using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MauiApp1.Application.Categories;

namespace MauiApp1.Presentation.ViewModels;

public sealed class CategoryManagementViewModel : INotifyPropertyChanged
{
    private readonly ICategoryService _categoryService;
    private string _categoryName = string.Empty;
    private string _errorMessage = string.Empty;
    private Guid? _editingCategoryId;
    private bool _isBusy;

    public CategoryManagementViewModel(ICategoryService categoryService)
    {
        _categoryService = categoryService;
        SaveCommand = new Command(async () => await SaveAsync(), () => !IsBusy);
        CancelEditCommand = new Command(CancelEdit);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<CategorySummary> Categories { get; } = [];

    public ICommand SaveCommand { get; }

    public ICommand CancelEditCommand { get; }

    public string CategoryName
    {
        get => _categoryName;
        set => SetProperty(ref _categoryName, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool IsEditing => _editingCategoryId.HasValue;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ((Command)SaveCommand).ChangeCanExecute();
            }
        }
    }

    public string SaveButtonText => IsEditing ? "Guardar cambios" : "Crear categoría";

    public async Task LoadAsync()
    {
        try
        {
            var categories = await _categoryService.GetAllAsync();
            Categories.Clear();
            foreach (var category in categories)
            {
                Categories.Add(category);
            }

            ErrorMessage = string.Empty;
        }
        catch
        {
            ErrorMessage = "No se pudieron cargar las categorías. Inténtalo de nuevo.";
        }
    }

    public void BeginEdit(CategorySummary category)
    {
        _editingCategoryId = category.Id.Value;
        CategoryName = category.Name;
        ErrorMessage = string.Empty;
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(SaveButtonText));
    }

    public async Task DeleteAsync(CategorySummary category)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _categoryService.DeleteAsync(category.Id.Value);
            if (!result.Succeeded)
            {
                ErrorMessage = result.ErrorMessage ?? "No se pudo eliminar la categoría.";
                return;
            }

            if (_editingCategoryId == category.Id.Value)
            {
                CancelEdit();
            }

            await LoadAsync();
        }
        catch
        {
            ErrorMessage = "No se pudo eliminar la categoría. Si está asociada a productos, reasígnalos antes.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var result = _editingCategoryId is Guid categoryId
                ? await _categoryService.RenameAsync(categoryId, CategoryName)
                : await _categoryService.CreateAsync(CategoryName);

            if (!result.Succeeded)
            {
                ErrorMessage = result.ErrorMessage ?? "No se pudo guardar la categoría.";
                return;
            }

            CancelEdit();
            await LoadAsync();
        }
        catch
        {
            ErrorMessage = "No se pudo guardar la categoría. Inténtalo de nuevo.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CancelEdit()
    {
        _editingCategoryId = null;
        CategoryName = string.Empty;
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(SaveButtonText));
        ErrorMessage = string.Empty;
    }

    private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
