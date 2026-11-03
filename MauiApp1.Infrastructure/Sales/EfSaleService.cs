using System.Text.Json;
using MauiApp1.Application.Authentication;
using MauiApp1.Application.Inventory;
using MauiApp1.Application.Sales;
using MauiApp1.Application.Storage;
using MauiApp1.Domain.Identifiers;
using MauiApp1.Domain.Inventory;
using MauiApp1.Domain.Values;
using MauiApp1.Domain.Users;
using MauiApp1.Infrastructure.Inventory;
using MauiApp1.Infrastructure.Persistence;
using MauiApp1.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MauiApp1.Infrastructure.Sales;

internal sealed class EfSaleService(
    IDbContextFactory<LocalDbContext> contextFactory,
    InventoryMovementWriter movementWriter,
    IAuthenticationService authenticationService,
    ILocalDeviceIdentity deviceIdentity) : ISaleService
{
    private const string OutboxOperationType = "Sale.Recorded";

    public async Task<SaleResult> RecordAsync(SaleDraft sale, CancellationToken cancellationToken = default)
    {
        var actor = authenticationService.CurrentUser;
        if (actor is null)
        {
            return SaleResult.Failure("Inicia sesión para registrar una venta.");
        }

        if (sale is null || sale.OperationId == Guid.Empty || sale.OccurredAtUtc.Kind != DateTimeKind.Utc ||
            sale.Lines is null || sale.Lines.Count == 0 || sale.Lines.Any(line =>
                line.ProductId.IsEmpty || line.Quantity.MilliUnits <= 0 || line.SalePrice.MinorUnits < 0 ||
                string.IsNullOrWhiteSpace(line.ProductNameSnapshot) || string.IsNullOrWhiteSpace(line.UnitSnapshot)) ||
            sale.IsCredit != sale.CustomerId.HasValue || sale.IsCredit != sale.CreditDueAtUtc.HasValue ||
            (sale.CreditDueAtUtc.HasValue && sale.CreditDueAtUtc.Value.Kind != DateTimeKind.Utc))
        {
            return SaleResult.Failure("La venta requiere fecha UTC, productos válidos, cantidades positivas y precios no negativos.");
        }

        List<(Guid ProductId, string Name, string Unit, long Quantity, long Price, long LineTotal)> lines;
        long totalMinorUnits;
        try
        {
            lines = sale.Lines
                .Select(line => line with
                {
                    ProductNameSnapshot = line.ProductNameSnapshot.Trim(),
                    UnitSnapshot = line.UnitSnapshot.Trim()
                })
                .GroupBy(line => line.ProductId.Value)
                .Select(group =>
                {
                    var first = group.First();
                    if (group.Any(line => line.ProductNameSnapshot != first.ProductNameSnapshot ||
                                          line.UnitSnapshot != first.UnitSnapshot || line.SalePrice != first.SalePrice))
                    {
                        throw new ArgumentException("Un mismo producto debe conservar el mismo nombre, unidad y precio durante la venta.");
                    }

                    var quantity = group.Aggregate(0L, (sum, line) => checked(sum + line.Quantity.MilliUnits));
                    var lineTotal = SaleAmountCalculator.CalculateLineTotalMinorUnits(first.SalePrice, new Quantity(quantity));
                    return (group.Key, first.ProductNameSnapshot, first.UnitSnapshot, quantity,
                        first.SalePrice.MinorUnits, lineTotal);
                })
                .ToList();
            totalMinorUnits = lines.Aggregate(0L, (sum, line) => checked(sum + line.LineTotal));
        }
        catch (ArgumentException exception)
        {
            return SaleResult.Failure(exception.Message);
        }
        catch (OverflowException)
        {
            return SaleResult.Failure("El total de la venta supera el valor permitido.");
        }

        var paymentValidationError = SalePaymentRules.Validate(totalMinorUnits, sale.IsCredit, sale.Payments);
        if (paymentValidationError is not null)
            return SaleResult.Failure(paymentValidationError);

        var movementLines = lines.Select((line, index) => new InventoryMovementLineDraft(
            index,
            new ProductId(line.ProductId),
            InventoryMovementType.SaleExit,
            new Quantity(line.Quantity),
            ProductNameSnapshot: line.Name,
            UnitSnapshot: line.Unit)).ToArray();
        var operation = new InventoryOperationDraft(
            sale.OperationId,
            sale.OccurredAtUtc,
            movementLines,
            NegativeStockPolicy.Reject);
        var payload = JsonSerializer.Serialize(new SaleRecordedPayload(
            sale.OperationId,
            sale.OccurredAtUtc,
            lines.Select((line, index) => new SaleRecordedLinePayload(
                index, line.ProductId, line.Name, line.Unit, line.Quantity, line.Price, line.LineTotal)).ToArray(),
            totalMinorUnits,
            sale.IsCredit,
            sale.CustomerId,
            sale.CreditDueAtUtc,
            sale.Payments?.Select(payment => new SaleRecordedPaymentPayload(payment.PaymentId, (int)payment.Method,
                payment.AmountMinorUnits, payment.CashReceivedMinorUnits, payment.ChangeMinorUnits)).ToArray()));

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (sale.IsCredit)
        {
            if (!await context.Customers.AnyAsync(customer => customer.CustomerId == sale.CustomerId, cancellationToken))
            {
                return SaleResult.Failure("El cliente seleccionado ya no existe. Actualiza la lista e inténtalo de nuevo.");
            }

            if (sale.CreditDueAtUtc != sale.OccurredAtUtc.AddDays(15))
            {
                return SaleResult.Failure("La fecha de vencimiento debe ser 15 días después de la fecha de venta.");
            }
        }

        var stageResult = await movementWriter.StageAsync(
            context,
            operation,
            actor.Id.Value,
            OutboxOperationType,
            payload,
            requireActiveProducts: true,
            cancellationToken);
        if (!stageResult.Succeeded)
        {
            return SaleResult.Failure(stageResult.ErrorMessage ?? "No se pudo preparar la venta.");
        }

        if (stageResult.AlreadyApplied)
        {
            var existingSale = await context.Sales.AsNoTracking().SingleOrDefaultAsync(
                existing => existing.OperationId == sale.OperationId && existing.ActorUserId == actor.Id.Value,
                cancellationToken);
            var persistedLineCount = existingSale is null ? 0 : await context.SaleLines.AsNoTracking()
                .CountAsync(line => line.OperationId == sale.OperationId, cancellationToken);
            var persistedReceivable = sale.IsCredit
                ? await context.Receivables.AsNoTracking().SingleOrDefaultAsync(item => item.OperationId == sale.OperationId, cancellationToken)
                : null;
            var creditMatches = sale.IsCredit
                ? persistedReceivable is not null && persistedReceivable.CustomerId == sale.CustomerId &&
                  persistedReceivable.OriginalAmountMinorUnits == totalMinorUnits &&
                  persistedReceivable.OutstandingMinorUnits == totalMinorUnits && persistedReceivable.DueAtUtc == sale.CreditDueAtUtc
                : !await context.Receivables.AnyAsync(item => item.OperationId == sale.OperationId, cancellationToken);
            var savedPayments = await context.SalePayments.AsNoTracking()
                .Where(item => item.OperationId == sale.OperationId)
                .OrderBy(item => item.Method)
                .ToListAsync(cancellationToken);
            var expectedPayments = (sale.Payments ?? []).OrderBy(item => item.Method).ToArray();
            var paymentDetailsMatch = savedPayments.Count == expectedPayments.Length && expectedPayments.All(expected =>
                savedPayments.Any(saved => saved.PaymentId == expected.PaymentId && saved.Method == (int)expected.Method &&
                    saved.AmountMinorUnits == expected.AmountMinorUnits &&
                    saved.CashReceivedMinorUnits == expected.CashReceivedMinorUnits &&
                    saved.ChangeMinorUnits == expected.ChangeMinorUnits));
            return existingSale is not null && existingSale.TotalMinorUnits == totalMinorUnits &&
                   existingSale.IsCredit == sale.IsCredit && existingSale.CustomerId == sale.CustomerId &&
                   persistedLineCount == lines.Count && creditMatches && paymentDetailsMatch
                ? SaleResult.Success(alreadyApplied: true)
                : SaleResult.Failure("La operación ya existe, pero no se encontró la venta y deuda completas asociadas. No se modificó el inventario.");
        }

        context.Sales.Add(new SaleEntity
        {
            OperationId = sale.OperationId,
            OccurredAtUtc = sale.OccurredAtUtc,
            ActorUserId = actor.Id.Value,
            TotalMinorUnits = totalMinorUnits,
            IsCredit = sale.IsCredit,
            CustomerId = sale.CustomerId
        });
        if (sale.IsCredit)
        {
            context.Receivables.Add(new ReceivableEntity
            {
                OperationId = sale.OperationId,
                CustomerId = sale.CustomerId!.Value,
                OriginalAmountMinorUnits = totalMinorUnits,
                OutstandingMinorUnits = totalMinorUnits,
                DueAtUtc = sale.CreditDueAtUtc!.Value
            });
        }
        foreach (var (line, index) in lines.Select((line, index) => (line, index)))
        {
            context.SaleLines.Add(new SaleLineEntity
            {
                OperationId = sale.OperationId,
                LineNumber = index,
                ProductId = line.ProductId,
                ProductNameSnapshot = line.Name,
                UnitSnapshot = line.Unit,
                QuantityMilliUnits = line.Quantity,
                SalePriceMinorUnits = line.Price,
                LineTotalMinorUnits = line.LineTotal
            });
        }
        if (sale.Payments is not null)
        {
            context.SalePayments.AddRange(sale.Payments.Select(payment => new SalePaymentEntity
            {
                PaymentId = payment.PaymentId,
                OperationId = sale.OperationId,
                Method = (int)payment.Method,
                AmountMinorUnits = payment.AmountMinorUnits,
                CashReceivedMinorUnits = payment.CashReceivedMinorUnits,
                ChangeMinorUnits = payment.ChangeMinorUnits
            }));
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return SaleResult.Success();
        }
        catch (DbUpdateException)
        {
            return SaleResult.Failure("No se pudo guardar la venta completa. Reintenta con el mismo identificador de operación.");
        }
    }

    public async Task<IReadOnlyList<SaleHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        if (authenticationService.CurrentUser is null)
        {
            return [];
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var sales = await (
            from sale in context.Sales.AsNoTracking()
            join actor in context.UserAccounts.AsNoTracking() on sale.ActorUserId equals actor.UserId
            orderby sale.OccurredAtUtc descending, sale.OperationId descending
            select new
            {
                sale.OperationId,
                sale.OccurredAtUtc,
                ActorUserName = actor.UserName,
                sale.IsCredit,
                sale.CustomerId,
                sale.TotalMinorUnits
            }).ToListAsync(cancellationToken);
        if (sales.Count == 0)
        {
            return [];
        }

        var operationIds = sales.Select(sale => sale.OperationId).ToArray();
        var lines = await context.SaleLines.AsNoTracking()
            .Where(line => operationIds.Contains(line.OperationId))
            .OrderBy(line => line.LineNumber)
            .Select(line => new
            {
                line.OperationId,
                line.ProductNameSnapshot,
                line.UnitSnapshot,
                line.QuantityMilliUnits,
                line.SalePriceMinorUnits,
                line.LineTotalMinorUnits
            })
            .ToListAsync(cancellationToken);
        var linesBySale = lines.GroupBy(line => line.OperationId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<SaleHistoryLine>)group.Select(line => new SaleHistoryLine(
                    line.ProductNameSnapshot,
                    line.UnitSnapshot,
                    line.QuantityMilliUnits,
                    line.SalePriceMinorUnits,
                    line.LineTotalMinorUnits)).ToArray());

        var customerIds = sales.Where(sale => sale.CustomerId.HasValue).Select(sale => sale.CustomerId!.Value).Distinct().ToArray();
        var customerNames = customerIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await context.Customers.AsNoTracking()
                .Where(customer => customerIds.Contains(customer.CustomerId))
                .ToDictionaryAsync(customer => customer.CustomerId, customer => customer.Name, cancellationToken);
        var dueDates = await context.Receivables.AsNoTracking()
            .Where(item => operationIds.Contains(item.OperationId))
            .ToDictionaryAsync(item => item.OperationId, item => item.DueAtUtc, cancellationToken);
        var outstandingBySale = await context.Receivables.AsNoTracking()
            .Where(item => operationIds.Contains(item.OperationId))
            .ToDictionaryAsync(item => item.OperationId, item => item.OutstandingMinorUnits, cancellationToken);
        var payments = await context.SalePayments.AsNoTracking()
            .Where(item => operationIds.Contains(item.OperationId))
            .OrderBy(item => item.Method)
            .Select(item => new { item.OperationId, item.Method, item.AmountMinorUnits,
                item.CashReceivedMinorUnits, item.ChangeMinorUnits })
            .ToListAsync(cancellationToken);
        var paymentsBySale = payments.GroupBy(item => item.OperationId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<SaleHistoryPayment>)group.Select(item =>
                new SaleHistoryPayment((SalePaymentMethod)item.Method, item.AmountMinorUnits,
                    item.CashReceivedMinorUnits, item.ChangeMinorUnits)).ToArray());

        var reversalRows = await (from reversal in context.SaleReversals.AsNoTracking()
            join reversalActor in context.UserAccounts.AsNoTracking() on reversal.ActorUserId equals reversalActor.UserId
            where operationIds.Contains(reversal.SaleOperationId)
            orderby reversal.OccurredAtUtc
            select new { reversal.OperationId, reversal.SaleOperationId, reversal.OccurredAtUtc,
                ActorUserName = reversalActor.UserName, reversal.Kind, reversal.Reason, reversal.AmountMinorUnits, reversal.RefundMethod })
            .ToListAsync(cancellationToken);
        var reversalIds = reversalRows.Select(item => item.OperationId).ToArray();
        var reversalLines = await context.SaleReversalLines.AsNoTracking().Where(item => reversalIds.Contains(item.OperationId))
            .OrderBy(item => item.LineNumber).ToListAsync(cancellationToken);
        var reversalsBySale = reversalRows.GroupBy(item => item.SaleOperationId).ToDictionary(group => group.Key,
            group => (IReadOnlyList<SaleReversalEntry>)group.Select(item => new SaleReversalEntry(item.OperationId,
                item.SaleOperationId, item.OccurredAtUtc, item.ActorUserName, (SaleReversalKind)item.Kind,
                item.Reason, item.AmountMinorUnits, item.RefundMethod is int method ? (SaleRefundMethod)method : null,
                reversalLines.Where(line => line.OperationId == item.OperationId).Select(line => new SaleReversalLineDraft(
                    line.SaleLineNumber, line.QuantityMilliUnits, line.ReturnToStock, line.DisposalReason, line.AmountMinorUnits)).ToArray())).ToArray());

        return sales.Select(sale => new SaleHistoryEntry(
            sale.OperationId,
            sale.OccurredAtUtc,
            sale.ActorUserName,
            sale.TotalMinorUnits,
            linesBySale.GetValueOrDefault(sale.OperationId, []),
            sale.IsCredit,
            sale.CustomerId.HasValue ? customerNames.GetValueOrDefault(sale.CustomerId.Value) : null,
            dueDates.TryGetValue(sale.OperationId, out var dueAtUtc) ? dueAtUtc : null,
            paymentsBySale.GetValueOrDefault(sale.OperationId, []),
            outstandingBySale.TryGetValue(sale.OperationId, out var outstanding) ? outstanding : null,
            reversalsBySale.GetValueOrDefault(sale.OperationId, []))).ToArray();
    }

    public async Task<SaleReversalResult> RecordReversalAsync(SaleReversalDraft reversal, CancellationToken cancellationToken = default)
    {
        var actor = authenticationService.CurrentUser;
        if (actor is null) return SaleReversalResult.Failure("Inicia sesión para corregir una venta.");
        if (actor.Role != UserRole.Administrator)
            return SaleReversalResult.Failure("Solo el Administrador puede anular o devolver ventas.");
        if (reversal is null || reversal.OperationId == Guid.Empty || reversal.SaleOperationId == Guid.Empty ||
            reversal.OperationId == reversal.SaleOperationId || reversal.OccurredAtUtc.Kind != DateTimeKind.Utc ||
            !Enum.IsDefined(reversal.Kind) || string.IsNullOrWhiteSpace(reversal.Reason) || reversal.Reason.Trim().Length > 500 ||
            reversal.Lines is null || reversal.Lines.Count == 0 ||
            reversal.Lines.Any(line => line.SaleLineNumber < 0 || line.QuantityMilliUnits <= 0 ||
                (line.ReturnToStock && !string.IsNullOrWhiteSpace(line.DisposalReason)) ||
                (!line.ReturnToStock && string.IsNullOrWhiteSpace(line.DisposalReason))))
            return SaleReversalResult.Failure("La reversión necesita motivo, líneas y cantidades válidas; especifica si cada producto vuelve a inventario o se desecha.");
        if (reversal.Lines.Select(line => line.SaleLineNumber).Distinct().Count() != reversal.Lines.Count)
            return SaleReversalResult.Failure("Cada renglón de la venta solo puede aparecer una vez en la devolución.");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var sale = await context.Sales.AsNoTracking().SingleOrDefaultAsync(item => item.OperationId == reversal.SaleOperationId, cancellationToken);
        if (sale is null) return SaleReversalResult.Failure("No se encontró la venta original en este dispositivo.");

        var saleLines = await context.SaleLines.AsNoTracking().Where(item => item.OperationId == sale.OperationId)
            .OrderBy(item => item.LineNumber).ToListAsync(cancellationToken);
        if (saleLines.Count == 0) return SaleReversalResult.Failure("La venta original no tiene renglones para devolver.");

        var existingReversal = await context.SaleReversals.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OperationId == reversal.OperationId, cancellationToken);
        var normalizedDraft = reversal with { Reason = reversal.Reason.Trim(), Lines = reversal.Lines.OrderBy(line => line.SaleLineNumber).ToArray() };
        var payload = JsonSerializer.Serialize(new SaleReversalRecordedPayload(
            normalizedDraft.OperationId, normalizedDraft.SaleOperationId, normalizedDraft.OccurredAtUtc,
            (int)normalizedDraft.Kind, normalizedDraft.Reason,
            normalizedDraft.Lines.Select((line, index) => new SaleReversalRecordedLinePayload(index, line.SaleLineNumber,
                line.QuantityMilliUnits, line.ReturnToStock, line.DisposalReason?.Trim())).ToArray(),
            normalizedDraft.RefundMethod is null ? null : (int)normalizedDraft.RefundMethod.Value));
        if (existingReversal is not null)
        {
            var existingOutbox = await context.OutboxMessages.AsNoTracking()
                .SingleOrDefaultAsync(item => item.OperationId == reversal.OperationId, cancellationToken);
            return existingReversal.ActorUserId == actor.Id.Value && existingOutbox?.OperationType == "Sale.Reversed" &&
                   existingOutbox.PayloadJson == payload
                ? SaleReversalResult.Success(alreadyApplied: true)
                : SaleReversalResult.Failure("El identificador de reversión ya existe con otros datos.");
        }

        var prior = await context.SaleReversalLines.AsNoTracking()
            .Where(line => line.SaleOperationId == sale.OperationId)
            .GroupBy(line => line.SaleLineNumber)
            .Select(group => new { Line = group.Key, Quantity = group.Sum(item => item.QuantityMilliUnits) })
            .ToDictionaryAsync(item => item.Line, item => item.Quantity, cancellationToken);
        var priorAmounts = await context.SaleReversalLines.AsNoTracking()
            .Where(line => line.SaleOperationId == sale.OperationId)
            .GroupBy(line => line.SaleLineNumber)
            .Select(group => new { Line = group.Key, Amount = group.Sum(item => item.AmountMinorUnits) })
            .ToDictionaryAsync(item => item.Line, item => item.Amount, cancellationToken);
        var selected = new List<(SaleLineEntity SaleLine, SaleReversalLineDraft Draft, long Amount)>();
        long refundTotal;
        try
        {
            foreach (var requested in normalizedDraft.Lines)
            {
                var saleLine = saleLines.SingleOrDefault(line => line.LineNumber == requested.SaleLineNumber);
                if (saleLine is null) return SaleReversalResult.Failure("Uno de los renglones ya no pertenece a la venta original.");
                var returned = prior.GetValueOrDefault(saleLine.LineNumber);
                if (requested.QuantityMilliUnits > saleLine.QuantityMilliUnits - returned)
                    return SaleReversalResult.Failure($"La cantidad a devolver supera lo que queda disponible de {saleLine.ProductNameSnapshot}.");
                var returnedAfter = checked(returned + requested.QuantityMilliUnits);
                var cumulativeAmount = SaleAmountCalculator.CalculateLineTotalMinorUnits(new MoneyAmount(saleLine.SalePriceMinorUnits),
                    new Quantity(returnedAfter));
                var amount = checked(cumulativeAmount - priorAmounts.GetValueOrDefault(saleLine.LineNumber));
                if (amount < 0 || checked(priorAmounts.GetValueOrDefault(saleLine.LineNumber) + amount) > saleLine.LineTotalMinorUnits)
                    return SaleReversalResult.Failure("El cálculo excede el importe registrado en el renglón original.");
                selected.Add((saleLine, requested, amount));
            }
            refundTotal = selected.Aggregate(0L, (sum, item) => checked(sum + item.Amount));
        }
        catch (OverflowException)
        {
            return SaleReversalResult.Failure("El importe de la devolución supera el límite permitido.");
        }

        if (normalizedDraft.Kind == SaleReversalKind.Cancellation &&
            (prior.Count > 0 || selected.Count != saleLines.Count || selected.Any(item => item.Draft.QuantityMilliUnits != item.SaleLine.QuantityMilliUnits)))
            return SaleReversalResult.Failure("La anulación completa solo se permite una vez y cuando incluye todos los renglones originales.");
        var receivable = sale.IsCredit
            ? await context.Receivables.SingleOrDefaultAsync(item => item.OperationId == sale.OperationId, cancellationToken)
            : null;
        if (sale.IsCredit)
        {
            if (receivable is null) return SaleReversalResult.Failure("La cuenta por cobrar original no está disponible; no se modificó la venta.");
            if (normalizedDraft.RefundMethod is not null) return SaleReversalResult.Failure("Una devolución a crédito reduce la deuda abierta; no se registra como reembolso de contado.");
            if (refundTotal > receivable.OutstandingMinorUnits)
                return SaleReversalResult.Failure("La devolución supera el saldo pendiente. Primero debe conciliarse el abono realizado; no se generó un saldo negativo.");
        }
        else if (normalizedDraft.RefundMethod is null || !Enum.IsDefined(normalizedDraft.RefundMethod.Value))
        {
            return SaleReversalResult.Failure("Selecciona el método usado para entregar el reembolso de la venta de contado.");
        }

        var savedOperation = await context.OutboxMessages.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OperationId == normalizedDraft.OperationId, cancellationToken);
        if (savedOperation is not null) return SaleReversalResult.Failure("El identificador de operación ya se utilizó.");

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            context.SaleReversals.Add(new SaleReversalEntity
            {
                OperationId = normalizedDraft.OperationId, SaleOperationId = sale.OperationId,
                OccurredAtUtc = normalizedDraft.OccurredAtUtc, ActorUserId = actor.Id.Value,
                Kind = (int)normalizedDraft.Kind, Reason = normalizedDraft.Reason,
                AmountMinorUnits = refundTotal, RefundMethod = sale.IsCredit ? null : (int)normalizedDraft.RefundMethod!.Value
            });
            var lineNumber = 0;
            foreach (var item in selected)
            {
                context.SaleReversalLines.Add(new SaleReversalLineEntity
                {
                    OperationId = normalizedDraft.OperationId, LineNumber = lineNumber++, SaleLineNumber = item.SaleLine.LineNumber,
                    SaleOperationId = sale.OperationId,
                    ProductId = item.SaleLine.ProductId, ProductNameSnapshot = item.SaleLine.ProductNameSnapshot,
                    UnitSnapshot = item.SaleLine.UnitSnapshot, QuantityMilliUnits = item.Draft.QuantityMilliUnits,
                    AmountMinorUnits = item.Amount, ReturnToStock = item.Draft.ReturnToStock,
                    DisposalReason = item.Draft.ReturnToStock ? null : item.Draft.DisposalReason!.Trim()
                });
                if (item.Draft.ReturnToStock)
                {
                    var product = await context.Products.SingleOrDefaultAsync(product => product.ProductId == item.SaleLine.ProductId, cancellationToken);
                    if (product is null) return SaleReversalResult.Failure("El producto original ya no existe; no se pudo restituir la existencia.");
                    product.StockMilliUnits = checked(product.StockMilliUnits + item.Draft.QuantityMilliUnits);
                    context.InventoryMovements.Add(new InventoryMovementEntity
                    {
                        MovementId = Guid.NewGuid(), OperationId = normalizedDraft.OperationId, LineNumber = item.SaleLine.LineNumber,
                        ProductId = product.ProductId, ProductNameSnapshot = item.SaleLine.ProductNameSnapshot,
                        UnitSnapshot = item.SaleLine.UnitSnapshot, Type = (int)InventoryMovementType.SaleReturnEntry,
                        QuantityMilliUnits = item.Draft.QuantityMilliUnits, Reason = $"Devolución de venta {sale.OperationId:N}",
                        OccurredAtUtc = normalizedDraft.OccurredAtUtc, ActorUserId = actor.Id.Value
                    });
                }
            }
            if (receivable is not null) receivable.OutstandingMinorUnits = checked(receivable.OutstandingMinorUnits - refundTotal);
            var outbox = new EfOperationOutbox(context, deviceIdentity);
            await outbox.EnqueueAsync(new NewOutboxOperation(normalizedDraft.OperationId, actor.Id.Value, "Sale.Reversed", payload), cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return SaleReversalResult.Success();
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return SaleReversalResult.Failure("La devolución chocó con otro cambio. Actualiza el historial y vuelve a revisar las cantidades disponibles.");
        }
        catch (OverflowException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return SaleReversalResult.Failure("La existencia resultante supera el valor permitido.");
        }
    }

    public async Task<IReadOnlyList<SaleReversalEntry>> GetReversalsAsync(Guid saleOperationId, CancellationToken cancellationToken = default)
    {
        if (authenticationService.CurrentUser is null || saleOperationId == Guid.Empty) return [];
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var reversals = await (from reversal in context.SaleReversals.AsNoTracking()
            join actor in context.UserAccounts.AsNoTracking() on reversal.ActorUserId equals actor.UserId
            where reversal.SaleOperationId == saleOperationId
            orderby reversal.OccurredAtUtc
            select new { reversal.OperationId, reversal.SaleOperationId, reversal.OccurredAtUtc, ActorUserName = actor.UserName,
                reversal.Kind, reversal.Reason, reversal.AmountMinorUnits, reversal.RefundMethod }).ToListAsync(cancellationToken);
        var ids = reversals.Select(item => item.OperationId).ToArray();
        var lines = await context.SaleReversalLines.AsNoTracking().Where(item => ids.Contains(item.OperationId))
            .OrderBy(item => item.LineNumber).ToListAsync(cancellationToken);
        return reversals.Select(item => new SaleReversalEntry(item.OperationId, item.SaleOperationId,
            item.OccurredAtUtc, item.ActorUserName, (SaleReversalKind)item.Kind, item.Reason, item.AmountMinorUnits,
            item.RefundMethod is int method ? (SaleRefundMethod)method : null,
            lines.Where(line => line.OperationId == item.OperationId).Select(line => new SaleReversalLineDraft(
                line.SaleLineNumber, line.QuantityMilliUnits, line.ReturnToStock, line.DisposalReason, line.AmountMinorUnits)).ToArray())).ToArray();
    }
}
