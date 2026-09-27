using System.Windows.Input;
using MauiApp1.Application.Authentication;
using MauiApp1.Domain.Users;

namespace MauiApp1.Presentation.ViewModels;

public sealed class MainPageViewModel
{
    private readonly IAuthenticationService _authenticationService;

    public MainPageViewModel(IAuthenticationService authenticationService)
    {
        _authenticationService = authenticationService;
        SignOutCommand = new Command(_authenticationService.SignOut);
    }

    public string Title => "Miscelánea Ari";

    public string CurrentUserName => _authenticationService.CurrentUser?.UserName ?? string.Empty;

    public string CurrentRole => _authenticationService.CurrentUser?.Role switch
    {
        UserRole.Administrator => "Administrador",
        UserRole.Cashier => "Cajero",
        _ => string.Empty
    };

    public bool IsAdministrator => _authenticationService.CurrentUser?.Role == UserRole.Administrator;

    public ICommand SignOutCommand { get; }
}
