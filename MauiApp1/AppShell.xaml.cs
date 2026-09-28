using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MauiApp1.Application.Authentication;
using MauiApp1.Presentation.Views;

namespace MauiApp1
{
    public partial class AppShell : Shell
    {
        private readonly IServiceProvider _services;
        private readonly IAuthenticationService _authenticationService;
        private readonly ILogger<AppShell> _logger;

        public AppShell(
            IServiceProvider services,
            IAuthenticationService authenticationService,
            ILogger<AppShell> logger)
        {
            InitializeComponent();
            _services = services;
            _authenticationService = authenticationService;
            _logger = logger;
            FlyoutBehavior = FlyoutBehavior.Disabled;
            _authenticationService.AuthenticationStateChanged += OnAuthenticationStateChanged;
            ShowCurrentPage();
        }

        private void OnAuthenticationStateChanged(object? sender, EventArgs eventArgs)
        {
            _logger.LogInformation("Shell recibió un cambio de autenticación; encolando actualización de pantalla.");
            Dispatcher.Dispatch(ShowCurrentPage);
        }

        private void ShowCurrentPage()
        {
            Page page = _authenticationService.CurrentUser is null
                ? _services.GetRequiredService<AccessPage>()
                : _services.GetRequiredService<MainPage>();

            var rootContent = new ShellContent
            {
                Title = "Miscelánea Ari",
                Route = "app",
                Content = page
            };

            Items.Clear();
            Items.Add(rootContent);
            _logger.LogInformation("Shell reemplazó su página raíz por {PageType}.", page.GetType().Name);
        }
    }
}
