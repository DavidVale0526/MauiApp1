using System.Text.Json;
using MauiApp1.Application.Authentication;
using MauiApp1.Application.Credit;
using MauiApp1.Application.Storage;
using MauiApp1.Infrastructure.Persistence;
using MauiApp1.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MauiApp1.Infrastructure.Credit;

internal sealed class EfPaymentService(
    IDbContextFactory<LocalDbContext> contextFactory,
    ILocalDeviceIdentity deviceIdentity,
    IAuthenticationService authenticationService) : IPaymentService
{
    private const string OperationType = "Credit.PaymentRecorded";

    public async Task<PaymentResult> RecordAsync(PaymentDraft payment, CancellationToken cancellationToken = default)
    {
        var actor = authenticationService.CurrentUser;
        if (actor is null)
            return PaymentResult.Failure("Inicia sesión para registrar un pago.");

        if (payment is null || payment.PaymentId == Guid.Empty || payment.OperationId == Guid.Empty ||
            payment.AmountMinorUnits <= 0 || payment.OccurredAtUtc.Kind != DateTimeKind.Utc)
            return PaymentResult.Failure("El pago requiere una deuda, un monto positivo y una fecha UTC válida.");

        var payload = JsonSerializer.Serialize(new PaymentRecordedPayload(
            payment.PaymentId, payment.OperationId, actor.Id.Value, payment.AmountMinorUnits, payment.OccurredAtUtc));
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await context.Payments.AsNoTracking()
            .SingleOrDefaultAsync(item => item.PaymentId == payment.PaymentId, cancellationToken);
        if (existing is not null)
        {
            var outbox = await context.OutboxMessages.AsNoTracking()
                .SingleOrDefaultAsync(item => item.OperationId == payment.PaymentId, cancellationToken);
            return existing.OperationId == payment.OperationId && existing.ActorUserId == actor.Id.Value &&
                   existing.AmountMinorUnits == payment.AmountMinorUnits && existing.OccurredAtUtc == payment.OccurredAtUtc &&
                   outbox is not null && outbox.OperationType == OperationType && outbox.PayloadJson == payload
                ? PaymentResult.Success(alreadyApplied: true)
                : PaymentResult.Failure("El identificador del pago ya fue usado con otros datos; no se aplicó ningún cambio.");
        }

        var receivable = await context.Receivables.SingleOrDefaultAsync(
            item => item.OperationId == payment.OperationId, cancellationToken);
        if (receivable is null)
            return PaymentResult.Failure("No se encontró la cuenta por cobrar seleccionada.");
        if (payment.AmountMinorUnits > receivable.OutstandingMinorUnits)
            return PaymentResult.Failure("El pago no puede superar el saldo pendiente.");

        var originalOutstanding = receivable.OutstandingMinorUnits;
        receivable.OutstandingMinorUnits = checked(originalOutstanding - payment.AmountMinorUnits);
        context.Payments.Add(new PaymentEntity
        {
            PaymentId = payment.PaymentId,
            OperationId = payment.OperationId,
            ActorUserId = actor.Id.Value,
            AmountMinorUnits = payment.AmountMinorUnits,
            OccurredAtUtc = payment.OccurredAtUtc
        });

        var operationOutbox = new EfOperationOutbox(context, deviceIdentity);
        await operationOutbox.EnqueueAsync(new NewOutboxOperation(
            payment.PaymentId, actor.Id.Value, OperationType, payload), cancellationToken);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return PaymentResult.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return PaymentResult.Failure("El saldo cambió mientras se registraba el pago. Actualiza la cuenta e inténtalo otra vez.");
        }
        catch (DbUpdateException)
        {
            return PaymentResult.Failure("No se pudo guardar el pago completo. Actualiza la cuenta y vuelve a intentarlo.");
        }
    }

    public async Task<IReadOnlyList<CreditAccountSummary>> GetAccountsAsync(CancellationToken cancellationToken = default)
    {
        if (authenticationService.CurrentUser is null)
            throw new UnauthorizedAccessException("Inicia sesión para consultar cuentas por cobrar.");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await (
            from receivable in context.Receivables.AsNoTracking()
            join sale in context.Sales.AsNoTracking() on receivable.OperationId equals sale.OperationId
            join customer in context.Customers.AsNoTracking() on receivable.CustomerId equals customer.CustomerId
            orderby receivable.DueAtUtc, customer.NormalizedName
            select new CreditAccountSummary(
                receivable.OperationId,
                customer.CustomerId,
                customer.Name,
                sale.OccurredAtUtc,
                receivable.DueAtUtc,
                receivable.OriginalAmountMinorUnits,
                receivable.OutstandingMinorUnits)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PaymentHistoryEntry>> GetHistoryAsync(
        Guid operationId, CancellationToken cancellationToken = default)
    {
        if (authenticationService.CurrentUser is null || operationId == Guid.Empty)
            return [];

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await (
            from payment in context.Payments.AsNoTracking()
            join actor in context.UserAccounts.AsNoTracking() on payment.ActorUserId equals actor.UserId
            where payment.OperationId == operationId
            orderby payment.OccurredAtUtc descending, payment.PaymentId descending
            select new PaymentHistoryEntry(
                payment.PaymentId,
                payment.OperationId,
                payment.OccurredAtUtc,
                actor.UserName,
                payment.AmountMinorUnits)).ToListAsync(cancellationToken);
    }

    private sealed record PaymentRecordedPayload(
        Guid PaymentId,
        Guid OperationId,
        Guid ActorUserId,
        long AmountMinorUnits,
        DateTime OccurredAtUtc);
}
