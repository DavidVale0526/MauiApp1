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

    public string PurchaseCountText => FormatCount(Purchases.Count, "compra", "compras");

    public string MonthlyPurchaseCountText
    {
        get
        {
            var today = DateTime.Now;
            var count = Purchases.Count(card =>
            {
                var date = card.Purchase.OccurredAtUtc.ToLocalTime();
                return date.Year == today.Year && date.Month == today.Month;
            });
            return FormatCount(count, "compra", "compras");
        }
    }

    public string UnitsReceivedText
    {
        get
        {
            decimal units = 0;
            foreach (var card in Purchases)
            {
                foreach (var line in card.Purchase.Lines)
                {
                    units += line.QuantityMilliUnits / 1000m;
                }
            }

            return $"{units.ToString("0.###", CultureInfo.CurrentCulture)} {(units == 1m ? "unidad" : "unidades")}";
        }
    }

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
            OnPropertyChanged(nameof(PurchaseCountText));
            OnPropertyChanged(nameof(MonthlyPurchaseCountText));
            OnPropertyChanged(nameof(UnitsReceivedText));
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

    private static string FormatCount(int count, string singular, string plural) =>
        $"{count} {(count == 1 ? singular : plural)}";

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record PurchaseHistoryCard(PurchaseHistoryEntry Purchase)
{
    public string DateText => Purchase.OccurredAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public string ReferenceText =>
        $"#CMP-{Purchase.OccurredAtUtc.ToLocalTime():yyyy}-{Purchase.OperationId.ToString("N")[..4].ToUpperInvariant()}";

    public string SubtitleText => $"{ReferenceText} • {DateText}";

    public string ItemsHeader => $"ARTÍCULOS RECIBIDOS ({Purchase.Lines.Count})";

    public string ItemCountText => Purchase.Lines.Count.ToString(CultureInfo.CurrentCulture);

    public IReadOnlyList<PurchaseHistoryItem> Items => Purchase.Lines
        .Select(line => new PurchaseHistoryItem(
            $"{line.ProductName} • {(line.QuantityMilliUnits / 1000m).ToString("0.###", CultureInfo.CurrentCulture)} {line.Unit}"))
        .ToArray();
}

public sealed record PurchaseHistoryItem(string Text);
