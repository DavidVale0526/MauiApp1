using MauiApp1.Application.Authentication;

namespace MauiApp1.Infrastructure.Authentication;

public sealed class CoordinatedSignInService(
    IAuthenticationService localAuthentication,
    ICoordinatorAuthenticationClient coordinatorClient,
    ICoordinatorUserSessionStorage sessionStorage) : ICoordinatedSignInService
{
    public async Task<CoordinatedSignInResult> SignInAsync(
        string userName, string password, CancellationToken cancellationToken = default)
    {
        var central = await coordinatorClient.SignInAsync(userName, password, cancellationToken);
        if (central.Status == CoordinatorSignInStatus.Rejected)
        {
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
                try { await sessionStorage.RemoveAsync(); } catch { }
                localAuthentication.SignOut();
                return new(localResult);
            }

            try
            {
                await sessionStorage.SaveAsync(new CoordinatorUserSession(
                    localResult.User.Id.Value,
                    central.UserId,
                    central.UserName,
                    central.Role,
                    central.SessionToken,
                    central.ExpiresAtUtc));
            }
            catch
            {
                localAuthentication.SignOut();
                return new(AuthenticationResult.Failure(
                    "No se pudo guardar de forma segura la sesión central. Intenta iniciar sesión de nuevo."));
            }

            return new(localResult, "Acceso validado por el coordinador. La cuenta local quedó preparada para usar la aplicación sin conexión.");
        }

        var local = await localAuthentication.SignInAsync(userName, password, cancellationToken);
        if (!local.Succeeded || local.User is null)
            return new(local, central.Status == CoordinatorSignInStatus.Unavailable
                ? "El coordinador no está disponible; puedes acceder con una cuenta local de este dispositivo."
                : null);

        if (central.Status == CoordinatorSignInStatus.Unavailable)
        {
            await KeepOnlyMatchingSessionAsync(local.User.Id.Value, cancellationToken);
            return new(local, "El coordinador no está disponible. Entraste con la cuenta local de este dispositivo; los cambios podrán sincronizarse al reconectar.");
        }

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
