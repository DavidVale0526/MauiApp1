namespace MauiApp1.Application.Authentication;

public interface IRecoveryCodeGenerator
{
    string Generate();
}

public sealed class AccountProvisioningResult
{
    private AccountProvisioningResult(bool succeeded, bool outcomeUnknown, string? recoveryCode, string? errorMessage)
    {
        Succeeded = succeeded;
        OutcomeUnknown = outcomeUnknown;
        RecoveryCode = recoveryCode;
        ErrorMessage = errorMessage;
    }

    public bool Succeeded { get; }
    public bool OutcomeUnknown { get; }
    public string? RecoveryCode { get; }
    public string? ErrorMessage { get; }

    public static AccountProvisioningResult Success(string recoveryCode) =>
        new(true, false, recoveryCode, null);

    public static AccountProvisioningResult Failure(string errorMessage) =>
        new(false, false, null, errorMessage);

    public static AccountProvisioningResult Unknown(string recoveryCode, string errorMessage) =>
        new(false, true, recoveryCode, errorMessage);

    public override string ToString() =>
        $"{nameof(AccountProvisioningResult)} {{ Succeeded = {Succeeded}, OutcomeUnknown = {OutcomeUnknown}, RecoveryCode = [REDACTED], ErrorMessage = {ErrorMessage} }}";
}

public sealed class PasswordRecoveryResult
{
    private PasswordRecoveryResult(bool succeeded, string? recoveryCode, string? errorMessage)
    {
        Succeeded = succeeded;
        RecoveryCode = recoveryCode;
        ErrorMessage = errorMessage;
    }

    public bool Succeeded { get; }
    public string? RecoveryCode { get; }
    public string? ErrorMessage { get; }

    public static PasswordRecoveryResult Success(string recoveryCode) =>
        new(true, recoveryCode, null);

    public static PasswordRecoveryResult Failure(string errorMessage) =>
        new(false, null, errorMessage);

    public override string ToString() =>
        $"{nameof(PasswordRecoveryResult)} {{ Succeeded = {Succeeded}, RecoveryCode = [REDACTED], ErrorMessage = {ErrorMessage} }}";
}

public enum CoordinatorRecoveryStatus
{
    NotLinked,
    Unavailable,
    OutcomeUnknown,
    Rejected,
    Completed
}

public sealed class CoordinatorPasswordRecoveryResult
{
    private CoordinatorPasswordRecoveryResult(
        CoordinatorRecoveryStatus status,
        string? recoveryCode = null,
        string? message = null)
    {
        Status = status;
        RecoveryCode = recoveryCode;
        Message = message;
    }

    public CoordinatorRecoveryStatus Status { get; }
    public string? RecoveryCode { get; }
    public string? Message { get; }

    public static CoordinatorPasswordRecoveryResult NotLinked() => new(CoordinatorRecoveryStatus.NotLinked);
    public static CoordinatorPasswordRecoveryResult Unavailable(string message) => new(CoordinatorRecoveryStatus.Unavailable, message: message);
    public static CoordinatorPasswordRecoveryResult OutcomeUnknown(string message) => new(CoordinatorRecoveryStatus.OutcomeUnknown, message: message);
    public static CoordinatorPasswordRecoveryResult Rejected(string message) => new(CoordinatorRecoveryStatus.Rejected, message: message);
    public static CoordinatorPasswordRecoveryResult Completed(string recoveryCode) => new(CoordinatorRecoveryStatus.Completed, recoveryCode);

    public override string ToString() =>
        $"{nameof(CoordinatorPasswordRecoveryResult)} {{ Status = {Status}, RecoveryCode = [REDACTED], Message = {Message} }}";
}

public interface ICoordinatorPasswordRecoveryClient
{
    Task<CoordinatorPasswordRecoveryResult> ResetPasswordAsync(
        string userName,
        string recoveryCode,
        string newPassword,
        CancellationToken cancellationToken = default);

    Task<CoordinatorPasswordRecoveryResult> ConfigureRecoveryCodeAsync(
        string currentPassword,
        CancellationToken cancellationToken = default);
}
