namespace MauiApp1.Application.Sync;

/// <summary>
/// A business-only snapshot. Credentials, device authorization, sessions and the local
/// Outbox are deliberately excluded from this contract.
/// </summary>
public sealed record InitialDataImportSnapshot(
    Guid BatchId,
    int SchemaVersion,
    Guid SourceDeviceId,
    DateTime CapturedAtUtc,
    IReadOnlyList<InitialImportActor> Actors,
    IReadOnlyList<InitialImportCategory> Categories,
    IReadOnlyList<InitialImportProduct> Products,
    IReadOnlyList<InitialImportCustomer> Customers,
    IReadOnlyList<InitialImportPurchase> Purchases,
    IReadOnlyList<InitialImportSale> Sales,
    IReadOnlyList<InitialImportReceivable> Receivables,
    IReadOnlyList<InitialImportMovement> Movements,
    IReadOnlyList<InitialImportPayment> Payments,
    IReadOnlyList<InitialImportDiscrepancy> Discrepancies);

public sealed record InitialImportActor(Guid LocalUserId, string UserName, int Role);
public sealed record InitialImportCategory(Guid CategoryId, string Name);
public sealed record InitialImportProduct(Guid ProductId, string Name, long SalePriceMinorUnits,
    long StockMilliUnits, string Unit, Guid CategoryId, long MinimumStockMilliUnits, bool IsActive,
    byte[]? ImageData = null);
public sealed record InitialImportCustomer(Guid CustomerId, string Name, string? Phone, DateTime CreatedAtUtc);
public sealed record InitialImportPurchase(Guid OperationId, DateTime OccurredAtUtc, Guid ActorLocalUserId,
    IReadOnlyList<InitialImportPurchaseLine> Lines);
public sealed record InitialImportPurchaseLine(int LineNumber, Guid ProductId, string ProductNameSnapshot,
    string UnitSnapshot, long QuantityMilliUnits);
public sealed record InitialImportSale(Guid OperationId, DateTime OccurredAtUtc, Guid ActorLocalUserId,
    long TotalMinorUnits, bool IsCredit, Guid? CustomerId, IReadOnlyList<InitialImportSaleLine> Lines);
public sealed record InitialImportSaleLine(int LineNumber, Guid ProductId, string ProductNameSnapshot,
    string UnitSnapshot, long QuantityMilliUnits, long SalePriceMinorUnits, long LineTotalMinorUnits);
public sealed record InitialImportReceivable(Guid OperationId, Guid CustomerId, long OriginalAmountMinorUnits,
    long OutstandingMinorUnits, DateTime DueAtUtc);
public sealed record InitialImportMovement(Guid MovementId, Guid OperationId, int LineNumber, Guid? ProductId,
    string ProductNameSnapshot, string UnitSnapshot, int Type, long QuantityMilliUnits, string? Reason,
    DateTime OccurredAtUtc, Guid ActorLocalUserId);
public sealed record InitialImportPayment(Guid PaymentId, Guid OperationId, Guid ActorLocalUserId,
    long AmountMinorUnits, DateTime OccurredAtUtc);
public sealed record InitialImportDiscrepancy(Guid DiscrepancyId, Guid OperationId, Guid ProductId,
    Guid ActorLocalUserId, long ShortageMilliUnits, DateTime CreatedAtUtc, bool IsResolved);

public sealed record InitialImportPurchasePayload(Guid OperationId, DateTime OccurredAtUtc,
    IReadOnlyList<InitialImportPurchaseLine> Lines);
public sealed record InitialImportSalePayload(Guid OperationId, DateTime OccurredAtUtc,
    long TotalMinorUnits, bool IsCredit, Guid? CustomerId, IReadOnlyList<InitialImportSaleLine> Lines);
public sealed record InitialImportMovementPayload(Guid MovementId, Guid OperationId, int LineNumber,
    Guid? ProductId, string ProductNameSnapshot, string UnitSnapshot, int Type, long QuantityMilliUnits,
    string? Reason, DateTime OccurredAtUtc);
public sealed record InitialImportPaymentPayload(Guid PaymentId, Guid OperationId, long AmountMinorUnits,
    DateTime OccurredAtUtc);
public sealed record InitialImportDiscrepancyPayload(Guid DiscrepancyId, Guid OperationId, Guid ProductId,
    long ShortageMilliUnits, DateTime CreatedAtUtc, bool IsResolved);

public sealed record InitialImportPreview(
    Guid BatchId,
    string SnapshotHash,
    Guid SourceDeviceId,
    DateTime CapturedAtUtc,
    int CategoryCount,
    int ProductCount,
    int CustomerCount,
    int PurchaseCount,
    int SaleCount,
    int MovementCount,
    int ReceivableCount,
    int PaymentCount,
    int DiscrepancyCount,
    int ExactMatchCount,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<InitialImportActorMappingOption> Actors,
    IReadOnlyList<InitialImportUserOption> CoordinatorUsers,
    IReadOnlyList<InitialImportStockConflict> StockConflicts);

public sealed record InitialImportActorMappingOption(Guid LocalUserId, string UserName, int Role,
    Guid? SuggestedCoordinatorUserId);
public sealed record InitialImportUserOption(Guid UserId, string UserName, int Role);
public sealed record InitialImportStockConflict(Guid ProductId, string ProductName,
    long CoordinatorStockMilliUnits, long SourceStockMilliUnits);
public sealed record InitialImportActorMapping(Guid LocalUserId, Guid CoordinatorUserId);
public sealed record InitialImportStockChoice(Guid ProductId, bool UseSourceStock);
public sealed record InitialImportApplyRequest(Guid BatchId, string SnapshotHash,
    IReadOnlyList<InitialImportActorMapping> ActorMappings,
    IReadOnlyList<InitialImportStockChoice> StockChoices);
public sealed record InitialImportApplyResult(bool Completed, int ImportedRecords, string Message,
    IReadOnlyList<InitialImportStockValue>? ProductStocks = null);
public sealed record InitialImportStockValue(Guid ProductId, long StockMilliUnits);

public sealed record InitialImportClientResult<T>(bool Succeeded, T? Value, string? ErrorMessage);
