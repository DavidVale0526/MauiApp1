global using MauiApp1.Presentation.ViewModels;

// Only UI platform primitives are replaced. The real ViewModel and LAN policy are linked above.
namespace MauiApp1.Presentation.ViewModels
{
    public sealed class Command(Action action, Func<bool>? canExecute = null) : System.Windows.Input.ICommand
    {
        public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
        public void Execute(object? parameter) => action();
        public event EventHandler? CanExecuteChanged;
        public void ChangeCanExecute() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
    public static class DeviceInfo
    {
        public static DeviceName Current { get; } = new();
        public sealed class DeviceName { public string Name => "Simulated device"; }
    }
}
namespace Microsoft.Maui.Storage
{
    public sealed class PickOptions { public string PickerTitle { get; set; } = string.Empty; }
    public sealed class FileResult
    {
        public Task<Stream> OpenReadAsync() => throw new Exception("Unexpected file picker access");
    }
    public sealed class FilePicker
    {
        public static FilePicker Default { get; } = new();
        public Task<FileResult?> PickAsync(PickOptions options) => throw new Exception("Unexpected file picker access");
    }
}
