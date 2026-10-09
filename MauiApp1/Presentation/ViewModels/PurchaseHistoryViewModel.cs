using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using MauiApp1.Application.Purchases;

namespace MauiApp1.Presentation.ViewModels;

public sealed class PurchaseHistoryViewModel(IPurchaseService purchaseService) : INotifyPropertyChanged
{
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<PurchaseHistoryCard> Purchases { get; } = [];

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public bool HasPurchases => Purchases.Count > 0;
    public bool HasNoPurchases => !HasPurchases;

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var history = await purchaseService.GetHistoryAsync();
            Purchases.Clear();
            foreach (var purchase in history)
            {
                Purchases.Add(new PurchaseHistoryCard(purchase));
            }
            OnPropertyChanged(nameof(HasPurchases));
            OnPropertyChanged(nameof(HasNoPurchases));
        }
        catch
        {
            ErrorMessage = "No se pudo cargar el historial de compras. Inténtalo de nuevo.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value)) return false;
        storage = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record PurchaseHistoryCard(PurchaseHistoryEntry Purchase)
{
    public string DateText => Purchase.OccurredAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public string ProductsText => string.Join(
        Environment.NewLine,
        Purchase.Lines.Select(line =>
            $"• {line.ProductName}: {(line.QuantityMilliUnits / 1000m).ToString("0.000", CultureInfo.CurrentCulture)} {line.Unit}"));
}
