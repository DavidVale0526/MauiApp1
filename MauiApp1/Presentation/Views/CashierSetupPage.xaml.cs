using MauiApp1.Domain.Users;
using MauiApp1.Presentation.ViewModels;

namespace MauiApp1.Presentation.Views;

public partial class CashierSetupPage : ContentPage
{
    private readonly CashierSetupViewModel _viewModel;

    public CashierSetupPage(CashierSetupViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCashiersAsync();
    }

    private async void OnBackClicked(object? sender, EventArgs eventArgs)
    {
        await Navigation.PopModalAsync();
    }

    private async void OnChangeRoleClicked(object? sender, EventArgs eventArgs)
    {
        if (sender is not Button { CommandParameter: CashierAccountRow row } || !row.CanChangeRole)
            return;

        var newRole = row.Role == UserRole.Administrator ? "Cajero" : "Administrador";
        var confirmed = await DisplayAlertAsync("Cambiar rol",
            $"¿Deseas convertir la cuenta «{row.UserName}» en {newRole}? " +
            "El cambio se aplica de inmediato; si la cuenta está abierta en otro equipo, deberá iniciar sesión de nuevo.",
            "Sí, cambiar", "Cancelar");
        if (!confirmed) return;

        var (succeeded, message) = await _viewModel.ChangeRoleAsync(row);
        await DisplayAlertAsync(
            succeeded ? "Rol actualizado" : "No se pudo cambiar el rol",
            message,
            "Aceptar");
    }
}
