namespace MauiApp1
{
    public partial class App : Microsoft.Maui.Controls.Application
    {
        private readonly AppShell _appShell;
        private readonly MauiApp1.Infrastructure.Sync.LocalOperationSyncWorker _syncWorker;

        public App(AppShell appShell, MauiApp1.Infrastructure.Sync.LocalOperationSyncWorker syncWorker)
        {
            _appShell = appShell;
            _syncWorker = syncWorker;
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(_appShell);
            window.Created += async (_, _) =>
            {
                try { await _syncWorker.StartAsync(); } catch { }
            };
            window.Stopped += async (_, _) =>
            {
                try { await _syncWorker.StopAsync(); } catch { }
            };
            window.Resumed += async (_, _) =>
            {
                try { await _syncWorker.StartAsync(); } catch { }
            };
            return window;
        }
    }
}
