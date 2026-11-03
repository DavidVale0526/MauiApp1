using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using MauiApp1.Presentation.Services;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace MauiApp1.Platforms.Windows;

public sealed class WindowsReceiptDocumentActions : IReceiptDocumentActions
{
    public async Task<string?> SavePdfAsync(string fileName, byte[] contents, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var picker = new FileSavePicker();
        picker.FileTypeChoices.Add("Documento PDF", new List<string> { ".pdf" });
        picker.SuggestedFileName = Path.GetFileNameWithoutExtension(fileName);

        var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView
            as Microsoft.UI.Xaml.Window
            ?? throw new InvalidOperationException("No se encontró la ventana para guardar el comprobante.");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));

        var file = await picker.PickSaveFileAsync();
        if (file is null) return null;
        cancellationToken.ThrowIfCancellationRequested();
        await FileIO.WriteBytesAsync(file, contents);
        return file.Path;
    }

    public Task SharePdfAsync(string path) => Share.Default.RequestAsync(new ShareFileRequest
    {
        Title = "Comprobante interno de venta",
        File = new ShareFile(path, "application/pdf")
    });

    public async Task OpenPdfAsync(string path)
    {
        var opened = await Launcher.Default.OpenAsync(new OpenFileRequest(
            "Abrir comprobante", new ReadOnlyFile(path, "application/pdf")));
        if (!opened) throw new InvalidOperationException("No hay una aplicación disponible para abrir archivos PDF.");
    }
}
