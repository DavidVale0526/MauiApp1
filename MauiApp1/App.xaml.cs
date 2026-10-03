namespace MauiApp1
{
    public partial class App : Microsoft.Maui.Controls.Application
    {
        private readonly AppShell _appShell;
        private readonly MauiApp1.Application.Sync.ICoordinatorRealtimeClient _realtimeClient;

        public App(AppShell appShell, MauiApp1.Application.Sync.ICoordinatorRealtimeClient realtimeClient)
        {
            _appShell = appShell;
            _realtimeClient = realtimeClient;
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(_appShell);
            window.Stopped += async (_, _) =>
            {
                try { await _realtimeClient.StopAsync(); } catch { }
            };
            window.Resumed += async (_, _) =>
            {
                try { await _realtimeClient.StartAsync(); } catch { }
            };
            return window;
        }
    }
}
