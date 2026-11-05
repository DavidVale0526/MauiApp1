using MauiApp1.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;
#if ANDROID
using Android.Views;
#endif
#if WINDOWS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
#endif

namespace MauiApp1.Presentation.Views;

public partial class AccessPage : ContentPage
{
    private readonly AccessPageViewModel _viewModel;

    public AccessPage(AccessPageViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;

        ApplyResponsiveCardWidth();
        SetLeftTextAlignment(UserNameEntry);
        SetLeftTextAlignment(PrimaryPasswordEntry);
        SetLeftTextAlignment(ConfirmationPasswordEntry);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializeAsync();
    }

    private void OnTogglePasswordVisibility(object? sender, EventArgs eventArgs)
    {
        PrimaryPasswordEntry.IsPassword = !PrimaryPasswordEntry.IsPassword;
        PasswordVisibilityButton.ImageSource = PrimaryPasswordEntry.IsPassword
            ? "eye_off.svg"
            : "eye.svg";
    }

    private async void OnLinkDeviceClicked(object? sender, EventArgs eventArgs)
    {
        if (Handler?.MauiContext?.Services.GetRequiredService<DevicePairingPage>() is not { } page)
        {
            await DisplayAlertAsync("Vinculación",
                "No se pudo abrir la pantalla de vinculación. Cierra y vuelve a abrir la aplicación.", "Aceptar");
            return;
        }

        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async void OnRefreshLinkClicked(object? sender, EventArgs eventArgs)
    {
        await _viewModel.InitializeAsync();
        if (!_viewModel.RequiresCoordinatorLink) return;
        await DisplayAlertAsync("Vinculación pendiente",
            "Todavía no se detecta un vínculo confirmado con el coordinador. Completa la vinculación desde " +
            "«Vincular este dispositivo» y vuelve a intentar.", "Aceptar");
    }

    private async void OnUseWithoutCoordinatorClicked(object? sender, EventArgs eventArgs)
    {
        var confirmed = await DisplayAlertAsync("Usar sin coordinador",
            "Esta cuenta quedará solo en este teléfono y sus ventas no se sincronizarán con el coordinador. " +
            "Úsala solo si este equipo trabajará de forma independiente. ¿Deseas continuar?",
            "Sí, continuar", "Cancelar");
        if (confirmed) _viewModel.EnableLocalAdministratorSetup();
    }

    private void ApplyResponsiveCardWidth()
    {
        CardContainer.HorizontalOptions = DeviceInfo.Idiom == DeviceIdiom.Phone
            ? LayoutOptions.Fill
            : LayoutOptions.Center;
        LoginCard.HorizontalOptions = LayoutOptions.Fill;
    }

    private static void SetLeftTextAlignment(Entry entry)
    {
        entry.HorizontalTextAlignment = Microsoft.Maui.TextAlignment.Start;
        entry.HorizontalOptions = LayoutOptions.Fill;
        entry.HandlerChanged += (_, _) => ApplyNativeLeftAlignment(entry);
        ApplyNativeLeftAlignment(entry);
    }

    private static void ApplyNativeLeftAlignment(Entry entry)
    {
#if ANDROID
        if (entry.Handler?.PlatformView is Android.Widget.EditText nativeEntry)
        {
            nativeEntry.Gravity = GravityFlags.Start | GravityFlags.CenterVertical;
        }
#elif WINDOWS
        if (entry.Handler?.PlatformView is TextBox nativeEntry)
        {
            nativeEntry.TextAlignment = Microsoft.UI.Xaml.TextAlignment.Left;
            nativeEntry.HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left;
        }
#endif
    }
}
