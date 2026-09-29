using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using MauiApp1.Application.Credit;

namespace MauiApp1.Presentation.ViewModels;

public sealed class PaymentManagementViewModel : INotifyPropertyChanged
{
    private readonly IPaymentService _paymentService;
    private PaymentAccountOption? _selectedAccount;
    private string _amountInput = string.Empty;
    private string _errorMessage = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isBusy;
    private PaymentDraft? _pendingPayment;
    private bool _pendingPaymentAttempted;

    public PaymentManagementViewModel(IPaymentService paymentService) => _paymentService = paymentService;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<PaymentAccountOption> Accounts { get; } = [];

    public ObservableCollection<PaymentHistoryItem> Payments { get; } = [];

    public PaymentAccountOption? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (SetProperty(ref _selectedAccount, value))
            {
                OnPropertyChanged(nameof(CanEditPayment));
                _ = RefreshPaymentHistoryAsync();
            }
        }
    }

    public string AmountInput
    {
        get => _amountInput;
        set
        {
            if (SetProperty(ref _amountInput, value)) OnPropertyChanged(nameof(CanRecord));
        }
    }

    public string ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }

    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanEditPayment));
                OnPropertyChanged(nameof(CanRecord));
            }
        }
    }

    public bool CanEditPayment => !IsBusy && (_pendingPayment is null || !_pendingPaymentAttempted);

    public bool CanRecord => !IsBusy && SelectedAccount is { OutstandingMinorUnits: > 0 } &&
                             (_pendingPayment is not null || !string.IsNullOrWhiteSpace(AmountInput));

    public string RecordButtonText => _pendingPaymentAttempted ? "Reintentar el mismo pago" : "Revisar y registrar pago";

    public async Task LoadAsync()
    {
        try
        {
            var previousId = SelectedAccount?.OperationId;
            Accounts.Clear();
            foreach (var account in await _paymentService.GetAccountsAsync())
                Accounts.Add(new PaymentAccountOption(account));
            SelectedAccount = Accounts.FirstOrDefault(item => item.OperationId == previousId) ?? Accounts.FirstOrDefault();
            if (SelectedAccount is null) Payments.Clear();
        }
        catch
        {
            ErrorMessage = "No se pudieron cargar las cuentas por cobrar. Inténtalo de nuevo.";
        }
    }

    public async Task RefreshAsync() => await LoadAsync();

    public PaymentDraft? PreparePayment()
    {
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        if (_pendingPayment is not null) return _pendingPayment;
        if (SelectedAccount is null || SelectedAccount.OutstandingMinorUnits <= 0)
        {
            ErrorMessage = SelectedAccount is null ? "Selecciona una cuenta por cobrar." : "Esta deuda ya está saldada.";
            return null;
        }
        if (!decimal.TryParse(AmountInput, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) || amount <= 0)
        {
            ErrorMessage = "Escribe un monto de pago positivo y válido.";
            return null;
        }

        long minorUnits;
        try
        {
            var scaled = amount * 100m;
            if (scaled != decimal.Truncate(scaled))
            {
                ErrorMessage = "El monto admite hasta 2 decimales.";
                return null;
            }
            minorUnits = checked((long)scaled);
        }
        catch (OverflowException)
        {
            ErrorMessage = "El monto supera el valor permitido.";
            return null;
        }

        if (minorUnits > SelectedAccount.OutstandingMinorUnits)
        {
            ErrorMessage = "El pago no puede superar el saldo pendiente.";
            return null;
        }

        _pendingPayment = new PaymentDraft(Guid.NewGuid(), SelectedAccount.OperationId, minorUnits, DateTime.UtcNow);
        _pendingPaymentAttempted = false;
        OnPropertyChanged(nameof(CanEditPayment));
        OnPropertyChanged(nameof(CanRecord));
        OnPropertyChanged(nameof(RecordButtonText));
        return _pendingPayment;
    }

    public string BuildReviewText()
    {
        if (_pendingPayment is null || SelectedAccount is null) return string.Empty;
        return $"Cliente: {SelectedAccount.CustomerName}\nDeuda original: C$ {SelectedAccount.OriginalText}\nSaldo pendiente: C$ {SelectedAccount.OutstandingText}\nAbono: C$ {(_pendingPayment.AmountMinorUnits / 100m).ToString("0.00", CultureInfo.CurrentCulture)}\nSaldo después del pago: C$ {((SelectedAccount.OutstandingMinorUnits - _pendingPayment.AmountMinorUnits) / 100m).ToString("0.00", CultureInfo.CurrentCulture)}\n\nLa fecha y hora se registrarán automáticamente.";
    }

    public void CancelReview()
    {
        if (_pendingPaymentAttempted) return;
        _pendingPayment = null;
        OnPropertyChanged(nameof(CanEditPayment));
        OnPropertyChanged(nameof(CanRecord));
        OnPropertyChanged(nameof(RecordButtonText));
    }

    public async Task<bool> RecordPendingAsync()
    {
        if (IsBusy || _pendingPayment is null) return false;
        IsBusy = true;
        _pendingPaymentAttempted = true;
        OnPropertyChanged(nameof(RecordButtonText));
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            var result = await _paymentService.RecordAsync(_pendingPayment);
            if (!result.Succeeded)
            {
                ErrorMessage = result.ErrorMessage ?? "No se pudo registrar el pago.";
                _pendingPayment = null;
                _pendingPaymentAttempted = false;
                OnPropertyChanged(nameof(CanEditPayment));
                OnPropertyChanged(nameof(CanRecord));
                OnPropertyChanged(nameof(RecordButtonText));
                await LoadAsync();
                return false;
            }

            _pendingPayment = null;
            _pendingPaymentAttempted = false;
            AmountInput = string.Empty;
            StatusMessage = result.AlreadyApplied
                ? "Este pago ya estaba registrado; no se volvió a restar del saldo."
                : "Pago registrado y saldo actualizado.";
            await LoadAsync();
            OnPropertyChanged(nameof(CanEditPayment));
            OnPropertyChanged(nameof(CanRecord));
            OnPropertyChanged(nameof(RecordButtonText));
            return true;
        }
        catch
        {
            ErrorMessage = "No se pudo confirmar el pago. Reintenta con el mismo pago para evitar duplicados.";
            return false;
        }
        finally { IsBusy = false; }
    }

    private async Task RefreshPaymentHistoryAsync()
    {
        if (SelectedAccount is null)
        {
            Payments.Clear();
            return;
        }
        try
        {
            var history = await _paymentService.GetHistoryAsync(SelectedAccount.OperationId);
            Payments.Clear();
            foreach (var item in history) Payments.Add(new PaymentHistoryItem(item));
        }
        catch
        {
            ErrorMessage = "No se pudo cargar el historial de pagos.";
        }
    }

    private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value)) return false;
        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record PaymentAccountOption(CreditAccountSummary Account)
{
    public Guid OperationId => Account.OperationId;
    public string CustomerName => Account.CustomerName;
    public long OutstandingMinorUnits => Account.OutstandingMinorUnits;
    public string OriginalText => (Account.OriginalAmountMinorUnits / 100m).ToString("0.00", CultureInfo.CurrentCulture);
    public string OutstandingText => (Account.OutstandingMinorUnits / 100m).ToString("0.00", CultureInfo.CurrentCulture);
    public string SaleDateText => Account.SaleOccurredAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    public string DueDateText => Account.DueAtUtc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
    public string DisplayText => $"{CustomerName} · Saldo C$ {OutstandingText} · Vence {DueDateText}";
    public string AccountDetails => $"Venta: {SaleDateText} · Original: C$ {OriginalText} · Saldo: C$ {OutstandingText} · Vence: {DueDateText}";
}

public sealed record PaymentHistoryItem(PaymentHistoryEntry Payment)
{
    public string DateText => Payment.OccurredAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    public string ActorText => $"Registró: {Payment.ActorUserName}";
    public string AmountText => $"C$ {(Payment.AmountMinorUnits / 100m).ToString("0.00", CultureInfo.CurrentCulture)}";
    public string ReferenceText => $"Pago {Payment.PaymentId.ToString("N")[..8].ToUpperInvariant()}";
}
