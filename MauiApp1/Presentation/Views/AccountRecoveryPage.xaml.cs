using MauiApp1.Application.Authentication;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace MauiApp1.Presentation.Views;

public partial class AccountRecoveryPage : ContentPage
{
    private readonly IAuthenticationService _authenticationService;
    private readonly ICoordinatedSignInService _coordinatedSignInService;

    public AccountRecoveryPage(
        IAuthenticationService authenticationService,
        ICoordinatedSignInService coordinatedSignInService)
    {
        InitializeComponent();
        _authenticationService = authenticationService;
        _coordinatedSignInService = coordinatedSignInService;
        UserNameLabel.Text = _authenticationService.CurrentUser is { } user
            ? $"Cuenta: {user.UserName}"
            : "Confirma tus credenciales para proteger la cuenta.";
    }

    private async void OnGenerateClicked(object? sender, EventArgs eventArgs)
    {
        ErrorLabel.IsVisible = false;
        RecoveryCodeLabel.Text = string.Empty;
        RecoveryCodeCard.IsVisible = false;
        GenerateButton.IsEnabled = false;
        try
        {
            var result = await _coordinatedSignInService.ConfigureRecoveryCodeAsync(CurrentPasswordEntry.Text ?? string.Empty);
            if (!result.Succeeded || string.IsNullOrWhiteSpace(result.RecoveryCode))
            {
                ErrorLabel.Text = result.ErrorMessage ?? "No se pudo generar el código.";
                ErrorLabel.IsVisible = true;
                return;
            }

            RecoveryCodeLabel.Text = result.RecoveryCode;
            RecoveryCodeCard.IsVisible = true;
            CurrentPasswordEntry.Text = string.Empty;
        }
        catch
        {
            ErrorLabel.Text = "No se pudo generar el código. Comprueba la conexión e inténtalo de nuevo.";
            ErrorLabel.IsVisible = true;
        }
        finally
        {
            GenerateButton.IsEnabled = true;
        }
    }

    private async void OnCopyClicked(object? sender, EventArgs eventArgs)
    {
        if (!string.IsNullOrWhiteSpace(RecoveryCodeLabel.Text))
            await Clipboard.Default.SetTextAsync(RecoveryCodeLabel.Text);
    }

    private async void OnBackClicked(object? sender, EventArgs eventArgs)
    {
        if (Navigation.ModalStack.Count > 0) await Navigation.PopModalAsync();
        else if (Navigation.NavigationStack.Count > 1) await Navigation.PopAsync();
    }
}
