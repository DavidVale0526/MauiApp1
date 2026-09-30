using ZXing.Net.Maui;

namespace MauiApp1.Presentation.Views;

public partial class PairingQrScannerPage : ContentPage
{
    private readonly Func<string, bool> _onQrRead;
    private int _processing;

    public PairingQrScannerPage(Func<string, bool> onQrRead)
    {
        InitializeComponent();
        _onQrRead = onQrRead;
        CameraView.Options = new BarcodeReaderOptions
        {
            Formats = BarcodeFormats.TwoDimensional,
            AutoRotate = true,
            Multiple = false,
            TryHarder = true
        };
    }

    private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs eventArgs)
    {
        var value = eventArgs.Results.FirstOrDefault()?.Value;
        if (string.IsNullOrWhiteSpace(value) || Interlocked.Exchange(ref _processing, 1) != 0)
            return;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            CameraView.IsDetecting = false;
            if (_onQrRead(value))
            {
                await Navigation.PopModalAsync();
                return;
            }

            StatusLabel.Text = "Ese QR no sirve o venció. Enfoca el QR de vinculación vigente de Windows.";
            Interlocked.Exchange(ref _processing, 0);
            CameraView.IsDetecting = true;
        });
    }

    private async void OnCancelClicked(object? sender, EventArgs args) => await Navigation.PopModalAsync();
}
