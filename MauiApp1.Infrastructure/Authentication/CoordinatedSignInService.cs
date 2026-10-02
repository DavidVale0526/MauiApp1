using MauiApp1.Application.Authentication;

namespace MauiApp1.Infrastructure.Authentication;

public sealed class CoordinatedSignInService(
    IAuthenticationService localAuthentication,
    ICoordinatorAuthenticationClient coordinatorClient,
    ICoordinatorUserSessionStorage sessionStorage) : ICoordinatedSignInService
{
    public string? LastSignInStatus { get; private set; }

    public void ReportSessionProblem(string message) => LastSignInStatus = message;

    public async Task SignOutAsync()
    {
        localAuthentication.SignOut();
        try { await sessionStorage.RemoveAsync(); } catch { }
        LastSignInStatus = null;
    }

    public async Task<CoordinatedSignInResult> SignInAsync(
        string userName, string password, CancellationToken cancellationToken = default)
    {
        var central = await coordinatorClient.SignInAsync(userName, password, cancellationToken);
        if (central.Status == CoordinatorSignInStatus.RetryLater)
        {
            LastSignInStatus = central.Message ?? "Espera un minuto antes de volver a intentar el acceso al coordinador.";
            return new(AuthenticationResult.Failure(LastSignInStatus));
        }

        if (central.Status == CoordinatorSignInStatus.Rejected)
        {
            LastSignInStatus = "El coordinador rechazó el usuario o la contraseña.";
            try { await sessionStorage.RemoveAsync(); } catch { }
            localAuthentication.SignOut();
            return new(AuthenticationResult.Failure("El nombre de usuario o la contraseña no son correctos."));
        }

        if (central.Status == CoordinatorSignInStatus.Authenticated)
        {
            var localResult = await localAuthentication.SignInWithCoordinatorUserAsync(
                central.UserId, central.UserName, central.Role, password, cancellationToken);
            if (!localResult.Succeeded || localResult.User is null)
            {
                LastSignInStatus = localResult.ErrorMessage;
                try { await sessionStorage.RemoveAsync(); } catch { }
                localAuthentication.SignOut();
                return new(localResult);
            }

            try
            {
                var newSession = new CoordinatorUserSession(
                    localResult.User.Id.Value,
                    central.UserId,
                    central.UserName,
                    central.Role,
                    central.SessionToken,
                    central.ExpiresAtUtc);
                await sessionStorage.SaveAsync(newSession);
                var storedSession = await sessionStorage.ReadAsync();
                if (storedSession != newSession)
                    throw new InvalidDataException("La sesión central no se pudo verificar después de guardarla.");
            }
            catch
            {
                LastSignInStatus = "No se pudo guardar de forma segura la sesión del coordinador.";
                localAuthentication.SignOut();
                return new(AuthenticationResult.Failure(
                    "No se pudo guardar de forma segura la sesión central. Intenta iniciar sesión de nuevo."));
            }

            LastSignInStatus = "Sesión compartida creada en el coordinador.";
            return new(localResult, "Acceso validado por el coordinador. La cuenta local quedó preparada para usar la aplicación sin conexión.");
        }

        var local = await localAuthentication.SignInAsync(userName, password, cancellationToken);
        if (central.Status == CoordinatorSignInStatus.NotLinked)
            LastSignInStatus = "Acceso local: este dispositivo no tiene una vinculación confirmada con el coordinador.";
        else if (central.Status == CoordinatorSignInStatus.Unavailable)
            LastSignInStatus = central.Message ?? "Acceso local: no se pudo crear una sesión compartida con el coordinador.";
        if (!local.Succeeded || local.User is null)
            return new(local, central.Status == CoordinatorSignInStatus.Unavailable
                ? central.Message ?? "El coordinador no está disponible; puedes acceder con una cuenta local de este dispositivo."
                : null);

        if (central.Status == CoordinatorSignInStatus.Unavailable)
        {
            await KeepOnlyMatchingSessionAsync(local.User.Id.Value, cancellationToken);
            return new(local, central.Message ?? "El coordinador no está disponible. Entraste con la cuenta local de este dispositivo; los cambios podrán sincronizarse al reconectar.");
        }

        LastSignInStatus = "Acceso local. La sesión compartida no está activa.";
        return new(local);
    }

    private async Task KeepOnlyMatchingSessionAsync(Guid localUserId, CancellationToken cancellationToken)
    {
        try
        {
            var session = await sessionStorage.ReadAsync();
            var coordinatorUserId = await localAuthentication.GetCoordinatorUserIdAsync(localUserId, cancellationToken);
            if (session is null || session.LocalUserId != localUserId ||
                session.CoordinatorUserId != coordinatorUserId || session.ExpiresAtUtc <= DateTime.UtcNow)
                await sessionStorage.RemoveAsync();
        }
        catch
        {
            // Offline local access must not be blocked by secure-storage/session-cache issues.
        }
    }
}
