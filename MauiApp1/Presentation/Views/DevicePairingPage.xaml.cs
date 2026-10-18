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
        _viewModel.ResumeDiscoveryPolling();
    }
    protected override void OnDisappearing()
    {
        _viewModel.CancelCoordinatorSearch();
        _viewModel.StopDiscoveryPolling();
        base.OnDisappearing();
    }
    private async void OnBackClicked(object? sender, EventArgs args) => await Navigation.PopModalAsync();

    private async void OnRequestDiscoveredPairingClicked(object? sender, EventArgs args)
    {
        var coordinatorName = _viewModel.SelectedCoordinator?.DisplayName ?? "el coordinador seleccionado";
        var confirmed = await DisplayAlertAsync("Solicitar vínculo",
            $"Se enviará una solicitud a {coordinatorName}. El Administrador debe comparar el código de este dispositivo con el que aparece en Windows. ¿Deseas continuar?",
            "Solicitar", "Cancelar");
        if (confirmed) await _viewModel.RequestDiscoveredPairingAsync();
    }

    private async void OnUnlinkClicked(object? sender, EventArgs args)
    {
        var confirmed = await DisplayAlertAsync("Desvincular este dispositivo",
            "Se revocará el acceso al coordinador y se cerrarán las sesiones de este dispositivo. Sus productos, ventas y demás datos locales se conservarán. ¿Deseas continuar?",
            "Desvincular", "Cancelar");
        if (!confirmed) return;
        var result = await _viewModel.UnlinkAsync();
        await DisplayAlertAsync(result.Succeeded ? "Dispositivo desvinculado" : "No se pudo desvincular",
            result.Message, "Aceptar");
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

    private void OnCoordinatorSelected(object? sender, SelectionChangedEventArgs args)
    {
        _viewModel.SelectCoordinator(args.CurrentSelection.FirstOrDefault() as MauiApp1.Application.Lan.CoordinatorCandidate);
        if (sender is CollectionView collectionView)
            collectionView.SelectedItem = null;
    }

    private async void OnApproveDiscoveredPairingClicked(object? sender, EventArgs args)
    {
        if (sender is Button { BindingContext: MauiApp1.Application.Lan.CoordinatorPendingPairing request })
            await _viewModel.DecideDiscoveredPairingAsync(request, approve: true);
    }

    private async void OnRejectDiscoveredPairingClicked(object? sender, EventArgs args)
    {
        if (sender is Button { BindingContext: MauiApp1.Application.Lan.CoordinatorPendingPairing request })
            await _viewModel.DecideDiscoveredPairingAsync(request, approve: false);
    }
}
