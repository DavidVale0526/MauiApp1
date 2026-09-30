using MauiApp1.Presentation.ViewModels;

namespace MauiApp1.Presentation.Views;

public partial class DevicePairingPage : ContentPage
{
    private readonly DevicePairingViewModel _viewModel;
    public DevicePairingPage(DevicePairingViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }
    private async void OnBackClicked(object? sender, EventArgs args) => await Navigation.PopModalAsync();

    private async void OnUnlinkClicked(object? sender, EventArgs args)
    {
        var confirmed = await DisplayAlertAsync("Desvincular este dispositivo",
            "Se revocará el acceso al coordinador y se cerrarán las sesiones de este dispositivo. Sus productos, ventas y demás datos locales se conservarán. ¿Deseas continuar?",
            "Desvincular", "Cancelar");
        if (confirmed && _viewModel.UnlinkCommand.CanExecute(null))
            _viewModel.UnlinkCommand.Execute(null);
    }

    private async void OnScanQrClicked(object? sender, EventArgs args)
    {
#if ANDROID
        if (!ZXing.Net.Maui.BarcodeScanning.IsSupported)
        {
            await DisplayAlertAsync("Cámara no disponible", "Este dispositivo no tiene una cámara compatible para leer el QR.", "Aceptar");
            return;
        }

        var permission = await Permissions.CheckStatusAsync<Permissions.Camera>();
        if (permission != PermissionStatus.Granted)
            permission = await Permissions.RequestAsync<Permissions.Camera>();
        if (permission != PermissionStatus.Granted)
        {
            await DisplayAlertAsync("Permiso de cámara", "Permite el acceso a la cámara para escanear el QR de vinculación.", "Aceptar");
            return;
        }

        await Navigation.PushModalAsync(new PairingQrScannerPage(_viewModel.ApplyScannedQr));
#endif
    }
}
