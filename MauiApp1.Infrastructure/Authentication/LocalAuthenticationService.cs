using MauiApp1.Application.Authentication;
using MauiApp1.Domain.Identifiers;
using MauiApp1.Domain.Users;
using MauiApp1.Infrastructure.Persistence;
using MauiApp1.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MauiApp1.Infrastructure.Authentication;

internal sealed class LocalAuthenticationService(
    IDbContextFactory<LocalDbContext> contextFactory,
    Pbkdf2PasswordHasher passwordHasher,
    ILogger<LocalAuthenticationService> logger) : IAuthenticationService
{
    private const int MinimumPasswordLength = 5;
    private const int MaximumPasswordLength = 1024;
    private const int MaximumUserNameLength = 80;
    private AuthenticatedUser? _currentUser;

    public AuthenticatedUser? CurrentUser => _currentUser;

    public event EventHandler? AuthenticationStateChanged;

    public async Task<bool> HasAdministratorAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.UserAccounts.AnyAsync(
            account => account.Role == (int)UserRole.Administrator, cancellationToken);
    }

    public async Task<AuthenticationResult> CreateInitialAdministratorAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateCredentials(userName, password);
        if (validationError is not null)
        {
            return AuthenticationResult.Failure(validationError);
        }

        var normalizedUserName = NormalizeUserName(userName);
        logger.LogDebug("Acceso: consultando cuenta local (longitud del usuario: {UserNameLength}).", userName.Length);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (await context.UserAccounts.AnyAsync(
                account => account.Role == (int)UserRole.Administrator, cancellationToken))
        {
            return AuthenticationResult.Failure("La cuenta inicial ya fue configurada en este dispositivo.");
        }

        if (await context.UserAccounts.AnyAsync(
                account => account.NormalizedUserName == normalizedUserName, cancellationToken))
        {
            return AuthenticationResult.Failure("Ese nombre de usuario ya está registrado en este dispositivo.");
        }

        var (salt, hash) = await Task.Run(() => passwordHasher.Hash(password), cancellationToken);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (await context.UserAccounts.AnyAsync(
                account => account.Role == (int)UserRole.Administrator, cancellationToken))
        {
            return AuthenticationResult.Failure("La cuenta inicial ya fue configurada en este dispositivo.");
        }

        if (await context.UserAccounts.AnyAsync(
                account => account.NormalizedUserName == normalizedUserName, cancellationToken))
        {
            return AuthenticationResult.Failure("Ese nombre de usuario ya está registrado en este dispositivo.");
        }

        var account = new UserAccountEntity
        {
            UserId = Guid.NewGuid(),
            UserName = userName.Trim(),
            NormalizedUserName = normalizedUserName,
            PasswordSalt = salt,
            PasswordHash = hash,
            PasswordIterations = Pbkdf2PasswordHasher.Iterations,
            Role = (int)UserRole.Administrator,
            IsEnabled = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        context.UserAccounts.Add(account);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        SetCurrentUser(ToAuthenticatedUser(account));
        return AuthenticationResult.Success(_currentUser!);
    }

    public async Task<AuthenticationResult> CreateCashierAccountAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser?.Role != UserRole.Administrator)
        {
            return AuthenticationResult.Failure("Solo una sesión de Administrador puede crear cuentas.");
        }

        var validationError = ValidateCredentials(userName, password);
        if (validationError is not null)
        {
            return AuthenticationResult.Failure(validationError);
        }

        var normalizedUserName = NormalizeUserName(userName);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (await context.UserAccounts.AnyAsync(
                account => account.NormalizedUserName == normalizedUserName, cancellationToken))
        {
            return AuthenticationResult.Failure("Ese nombre de usuario ya está registrado en este dispositivo.");
        }

        var (salt, hash) = await Task.Run(() => passwordHasher.Hash(password), cancellationToken);

        if (await context.UserAccounts.AnyAsync(
                account => account.NormalizedUserName == normalizedUserName, cancellationToken))
        {
            return AuthenticationResult.Failure("Ese nombre de usuario ya está registrado en este dispositivo.");
        }

        var account = new UserAccountEntity
        {
            UserId = Guid.NewGuid(),
            UserName = userName.Trim(),
            NormalizedUserName = normalizedUserName,
            PasswordSalt = salt,
            PasswordHash = hash,
            PasswordIterations = Pbkdf2PasswordHasher.Iterations,
            Role = (int)UserRole.Cashier,
            IsEnabled = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        context.UserAccounts.Add(account);
        await context.SaveChangesAsync(cancellationToken);
        return AuthenticationResult.Success(ToAuthenticatedUser(account));
    }

    public async Task<AuthenticationResult> SignInAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName) || userName.Length > MaximumUserNameLength ||
            string.IsNullOrEmpty(password) || password.Length > MaximumPasswordLength)
        {
            return AuthenticationResult.Failure("El nombre de usuario o la contraseña no son correctos.");
        }

        var normalizedUserName = NormalizeUserName(userName);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var account = await context.UserAccounts.SingleOrDefaultAsync(
            user => user.NormalizedUserName == normalizedUserName && user.IsEnabled, cancellationToken);

        if (account is null)
        {
            logger.LogInformation("Acceso rechazado: no se encontró una cuenta local habilitada.");
            return AuthenticationResult.Failure("El nombre de usuario o la contraseña no son correctos.");
        }

        logger.LogDebug("Acceso: cuenta habilitada encontrada; verificando contraseña.");
        var passwordIsValid = await Task.Run(
            () => passwordHasher.Verify(password, account.PasswordSalt, account.PasswordHash, account.PasswordIterations),
            cancellationToken);
        if (!passwordIsValid)
        {
            logger.LogInformation("Acceso rechazado: la verificación de contraseña no coincidió.");
            return AuthenticationResult.Failure("El nombre de usuario o la contraseña no son correctos.");
        }

        var user = ToAuthenticatedUser(account);
        SetCurrentUser(user);
        logger.LogInformation("Acceso aceptado para el rol {Role}.", user.Role);
        return AuthenticationResult.Success(user);
    }

    public async Task<AuthenticationResult> SignInWithCoordinatorUserAsync(
        Guid coordinatorUserId,
        string userName,
        UserRole role,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (coordinatorUserId == Guid.Empty || string.IsNullOrWhiteSpace(userName) ||
            userName.Trim().Length > MaximumUserNameLength || string.IsNullOrEmpty(password) ||
            password.Length > MaximumPasswordLength ||
            role is not (UserRole.Administrator or UserRole.Cashier))
            return AuthenticationResult.Failure("El coordinador devolvió una cuenta no válida. Inténtalo de nuevo.");

        var normalizedUserName = NormalizeUserName(userName);
        var (salt, hash) = await Task.Run(() => passwordHasher.Hash(password), cancellationToken);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var mappedAccount = await context.UserAccounts.SingleOrDefaultAsync(
            account => account.CoordinatorUserId == coordinatorUserId, cancellationToken);
        var matchingName = await context.UserAccounts.SingleOrDefaultAsync(
            account => account.NormalizedUserName == normalizedUserName, cancellationToken);

        if (mappedAccount is not null && matchingName is not null && mappedAccount.UserId != matchingName.UserId)
            return AuthenticationResult.Failure("La cuenta central entra en conflicto con una cuenta local. No se modificaron los datos; solicita ayuda al Administrador.");
        if (matchingName?.CoordinatorUserId is Guid otherCoordinatorUserId && otherCoordinatorUserId != coordinatorUserId)
            return AuthenticationResult.Failure("La cuenta local ya está asociada a otra cuenta central. No se modificaron los datos.");

        var account = mappedAccount ?? matchingName;
        if (account is null)
        {
            account = new UserAccountEntity
            {
                UserId = Guid.NewGuid(),
                CreatedAtUtc = DateTime.UtcNow
            };
            context.UserAccounts.Add(account);
        }

        account.CoordinatorUserId = coordinatorUserId;
        account.UserName = userName.Trim();
        account.NormalizedUserName = normalizedUserName;
        account.PasswordSalt = salt;
        account.PasswordHash = hash;
        account.PasswordIterations = Pbkdf2PasswordHasher.Iterations;
        account.Role = (int)role;
        account.IsEnabled = true;

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var user = ToAuthenticatedUser(account);
        SetCurrentUser(user);
        return AuthenticationResult.Success(user);
    }

    public async Task<Guid?> GetCoordinatorUserIdAsync(Guid localUserId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.UserAccounts.AsNoTracking()
            .Where(account => account.UserId == localUserId)
            .Select(account => account.CoordinatorUserId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public void SignOut() => SetCurrentUser(null);

    private static string? ValidateCredentials(string userName, string password)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return "Escribe un nombre de usuario.";
        }

        if (userName.Trim().Length > MaximumUserNameLength)
        {
            return $"El nombre de usuario no puede superar {MaximumUserNameLength} caracteres.";
        }

        if (string.IsNullOrEmpty(password) || password.Length < MinimumPasswordLength)
        {
            return $"La contraseña debe tener al menos {MinimumPasswordLength} caracteres.";
        }

        if (password.Length > MaximumPasswordLength)
        {
            return $"La contraseña no puede superar {MaximumPasswordLength} caracteres.";
        }

        return null;
    }

    private static string NormalizeUserName(string userName) => userName.Trim().ToUpperInvariant();

    private static AuthenticatedUser ToAuthenticatedUser(UserAccountEntity account) =>
        new(new UserId(account.UserId), account.UserName, (UserRole)account.Role);

    private void SetCurrentUser(AuthenticatedUser? user)
    {
        _currentUser = user;
        AuthenticationStateChanged?.Invoke(this, EventArgs.Empty);
    }
}
