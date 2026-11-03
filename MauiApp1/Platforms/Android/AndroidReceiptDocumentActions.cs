using Android.App;
using Android.Content;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using MauiApp1.Presentation.Services;

namespace MauiApp1.Platforms.Android;

public sealed class AndroidReceiptDocumentActions : IReceiptDocumentActions
{
    private const int SaveRequestCode = 0x41B;
    private static readonly object Gate = new();
    private static TaskCompletionSource<global::Android.Net.Uri?>? _pendingSave;

    public async Task<string?> SavePdfAsync(string fileName, byte[] contents, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("No se encontró la ventana de Android.");
        var completion = new TaskCompletionSource<global::Android.Net.Uri?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (Gate)
        {
            if (_pendingSave is not null)
                throw new InvalidOperationException("Ya hay otra ventana para guardar un archivo abierta.");
            _pendingSave = completion;
        }

        var intent = new Intent(Intent.ActionCreateDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("application/pdf");
        intent.PutExtra(Intent.ExtraTitle, Path.GetFileName(fileName));
        try
        {
#pragma warning disable CA1422
            activity.StartActivityForResult(intent, SaveRequestCode);
#pragma warning restore CA1422
            var uri = await completion.Task.WaitAsync(cancellationToken);
            if (uri is null) return null;

            using var stream = activity.ContentResolver?.OpenOutputStream(uri)
                ?? throw new IOException("Android no pudo abrir el destino seleccionado.");
            await stream.WriteAsync(contents, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            return uri.ToString();
        }
        finally
        {
            lock (Gate)
            {
                if (ReferenceEquals(_pendingSave, completion)) _pendingSave = null;
            }
        }
    }

    internal static void CompleteSaveRequest(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != SaveRequestCode) return;
        TaskCompletionSource<global::Android.Net.Uri?>? completion;
        lock (Gate)
        {
            completion = _pendingSave;
            _pendingSave = null;
        }
        if (completion is null) return;
        completion.TrySetResult(resultCode == Result.Ok ? data?.Data : null);
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
