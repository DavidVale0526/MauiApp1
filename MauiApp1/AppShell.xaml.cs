using Microsoft.Extensions.DependencyInjection;
using MauiApp1.Application.Authentication;
using MauiApp1.Presentation.Views;

namespace MauiApp1
{
    public partial class AppShell : Shell
    {
        private readonly IServiceProvider _services;
        private readonly IAuthenticationService _authenticationService;
        private readonly ShellContent _rootContent;

        public AppShell(IServiceProvider services, IAuthenticationService authenticationService)
        {
            InitializeComponent();
            _services = services;
            _authenticationService = authenticationService;
            FlyoutBehavior = FlyoutBehavior.Disabled;
            _rootContent = new ShellContent
            {
                Title = "Miscelánea Ari",
                Route = "app",
                Content = _services.GetRequiredService<AccessPage>()
            };
            Items.Add(_rootContent);
            _authenticationService.AuthenticationStateChanged += OnAuthenticationStateChanged;
            ShowCurrentPage();
        }

        private void OnAuthenticationStateChanged(object? sender, EventArgs eventArgs)
        {
            Dispatcher.Dispatch(ShowCurrentPage);
        }

        private void ShowCurrentPage()
        {
            Page page = _authenticationService.CurrentUser is null
                ? _services.GetRequiredService<AccessPage>()
                : _services.GetRequiredService<MainPage>();

            _rootContent.Content = page;
        }
    }
}
