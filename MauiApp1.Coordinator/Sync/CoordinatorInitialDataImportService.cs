using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MauiApp1.Application.Sync;
using MauiApp1.Application.Products;
using MauiApp1.Coordinator.Persistence;
using MauiApp1.Infrastructure.Persistence;
using MauiApp1.Infrastructure.Persistence.Entities;
using MauiApp1.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MauiApp1.Coordinator.Sync;

internal sealed class CoordinatorInitialDataImportService(
    string databasePath,
    IDbContextFactory<LocalDbContext> contextFactory,
    CoordinatorOperationStore operationStore,
    CoordinatorBusinessOperationApplier businessApplier)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly SemaphoreSlim ImportGate = new(1, 1);
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWrite,
        Pooling = false
    }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS CoordinatorInitialImportBatches (
                BatchId TEXT NOT NULL PRIMARY KEY,
                SourceDeviceId TEXT NOT NULL,
                AdministratorUserId TEXT NOT NULL,
                SnapshotHash TEXT NOT NULL,
                SnapshotJson TEXT NOT NULL,
                PreviewJson TEXT NOT NULL,
                OptionsHash TEXT NULL,
                Status INTEGER NOT NULL DEFAULT 0,
                ImportedRecords INTEGER NOT NULL DEFAULT 0,
                LastError TEXT NULL,
                CreatedAtUtc INTEGER NOT NULL,
                UpdatedAtUtc INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_CoordinatorInitialImportBatches_Source_Created
                ON CoordinatorInitialImportBatches (SourceDeviceId, CreatedAtUtc);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<InitialImportClientResult<InitialImportPreview>> PreviewAsync(
        InitialDataImportSnapshot snapshot, Guid authenticatedDeviceId, Guid administratorUserId,
        CancellationToken cancellationToken = default)
    {
        if (snapshot is null || snapshot.BatchId == Guid.Empty || snapshot.SchemaVersion != 1 ||
            snapshot.SourceDeviceId != authenticatedDeviceId || snapshot.CapturedAtUtc.Kind != DateTimeKind.Utc ||
            administratorUserId == Guid.Empty)
            return new(false, null, "La solicitud de importación no coincide con esta versión, dispositivo o sesión.");

        string json;
        try { json = JsonSerializer.Serialize(snapshot, JsonOptions); }
        catch (Exception) { return new(false, null, "No se pudo leer el paquete de datos del dispositivo."); }
        if (Encoding.UTF8.GetByteCount(json) > 40 * 1024 * 1024)
            return new(false, null, "El paquete supera el tamaño permitido para esta importación. No se modificó ninguna base.");

        await ImportGate.WaitAsync(cancellationToken);
        try
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
            var preview = await BuildPreviewAsync(snapshot, hash, cancellationToken);
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var existing = connection.CreateCommand();
            existing.CommandText = "SELECT SourceDeviceId, AdministratorUserId, SnapshotHash FROM CoordinatorInitialImportBatches WHERE BatchId=$id;";
            existing.Parameters.AddWithValue("$id", snapshot.BatchId.ToString("D"));
            await using var reader = await existing.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var same = Guid.Parse(reader.GetString(0)) == authenticatedDeviceId &&
                    Guid.Parse(reader.GetString(1)) == administratorUserId &&
                    string.Equals(reader.GetString(2), hash, StringComparison.Ordinal);
                await reader.DisposeAsync();
                if (!same) return new(false, null, "El identificador de esta importación ya se usó con otros datos. Analiza nuevamente el dispositivo.");
                await using var update = connection.CreateCommand();
                update.CommandText = "UPDATE CoordinatorInitialImportBatches SET PreviewJson=$preview, UpdatedAtUtc=$now WHERE BatchId=$id AND Status=0;";
                update.Parameters.AddWithValue("$preview", JsonSerializer.Serialize(preview, JsonOptions));
                update.Parameters.AddWithValue("$now", DateTime.UtcNow.Ticks);
                update.Parameters.AddWithValue("$id", snapshot.BatchId.ToString("D"));
                await update.ExecuteNonQueryAsync(cancellationToken);
                return new(true, preview, null);
            }

            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO CoordinatorInitialImportBatches
                    (BatchId, SourceDeviceId, AdministratorUserId, SnapshotHash, SnapshotJson, PreviewJson,
                     Status, ImportedRecords, CreatedAtUtc, UpdatedAtUtc)
                VALUES ($id, $device, $admin, $hash, $snapshot, $preview, 0, 0, $now, $now);
                """;
            var now = DateTime.UtcNow.Ticks;
            insert.Parameters.AddWithValue("$id", snapshot.BatchId.ToString("D"));
            insert.Parameters.AddWithValue("$device", authenticatedDeviceId.ToString("D"));
            insert.Parameters.AddWithValue("$admin", administratorUserId.ToString("D"));
            insert.Parameters.AddWithValue("$hash", hash);
            insert.Parameters.AddWithValue("$snapshot", json);
            insert.Parameters.AddWithValue("$preview", JsonSerializer.Serialize(preview, JsonOptions));
            insert.Parameters.AddWithValue("$now", now);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            return new(true, preview, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return new(false, null, "No se pudo guardar la vista previa en el coordinador. No se importaron datos.");
        }
        finally { ImportGate.Release(); }
    }

    public async Task<InitialImportClientResult<InitialImportApplyResult>> ApplyAsync(
        InitialImportApplyRequest request, Guid authenticatedDeviceId, Guid administratorUserId,
        CancellationToken cancellationToken = default)
    {
        if (request is null || request.BatchId == Guid.Empty || string.IsNullOrWhiteSpace(request.SnapshotHash))
            return new(false, null, "La confirmación de importación no es válida.");

        await ImportGate.WaitAsync(cancellationToken);
        try
        {
            var stored = await ReadBatchAsync(request.BatchId, cancellationToken);
            if (stored is null) return new(false, null, "No se encontró esta vista previa. Analiza nuevamente los datos.");
            if (stored.SourceDeviceId != authenticatedDeviceId || stored.AdministratorUserId != administratorUserId)
                return new(false, null, "La vista previa pertenece a otro dispositivo o sesión de Administrador.");
            if (!string.Equals(stored.Hash, request.SnapshotHash, StringComparison.Ordinal))
                return new(false, null, "Los datos de la vista previa cambiaron. Analiza nuevamente antes de confirmar.");
            var snapshot = JsonSerializer.Deserialize<InitialDataImportSnapshot>(stored.Json, JsonOptions);
            if (snapshot is null) return new(false, null, "No se pudo recuperar la vista previa guardada.");
            var currentPreview = await BuildPreviewAsync(snapshot, stored.Hash, cancellationToken);
            if (currentPreview.Blockers.Count > 0)
                return new(false, null, "La base central cambió desde la vista previa y ahora hay datos que requieren revisión. Analiza nuevamente.");

            var actorMapResult = await ValidateActorMappingsAsync(snapshot, request.ActorMappings, cancellationToken);
            if (actorMapResult.Error is not null) return new(false, null, actorMapResult.Error);
            var authorError = await ValidateExistingAuthorsAsync(snapshot, actorMapResult.Map!, cancellationToken);
            if (authorError is not null) return new(false, null, authorError);
            var stockResult = await ResolveProductStocksAsync(snapshot, request.StockChoices, cancellationToken);
            if (stockResult.Error is not null) return new(false, null, stockResult.Error);

            var optionsHash = Hash(JsonSerializer.Serialize(new
            {
                actorMappings = request.ActorMappings.OrderBy(item => item.LocalUserId),
                stockChoices = request.StockChoices.OrderBy(item => item.ProductId)
            }, JsonOptions));
            if (stored.OptionsHash is not null && !string.Equals(stored.OptionsHash, optionsHash, StringComparison.Ordinal))
                return new(false, null, "La importación ya comenzó con otras decisiones de autores o existencias. Revisa el estado antes de iniciar otra importación.");
            if (stored.Status == 2)
            {
                var completedOperations = BuildImportOperations(snapshot, administratorUserId, actorMapResult.Map!, stockResult.Stocks!);
                await ResolveCoveredConflictsAsync(snapshot, authenticatedDeviceId, actorMapResult.Map!, cancellationToken);
                return new(true, new(true, stored.ImportedRecords,
                    "La importación ya estaba completada; no se duplicó ningún registro.",
                    GetProcessedProductStocks(completedOperations, completedOperations.Count)), null);
            }
            if (stored.OptionsHash is null)
            {
                await UpdateBatchAsync(request.BatchId, 1, 0, null, optionsHash, cancellationToken);
                stored = stored with { Status = 1, OptionsHash = optionsHash };
            }

            if (stored.ImportedRecords == 0)
                await CreatePreImportBackupAsync(request.BatchId, cancellationToken);

            var operations = BuildImportOperations(snapshot, administratorUserId, actorMapResult.Map!, stockResult.Stocks!);
            var completed = 0;
            foreach (var operation in operations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var incoming = new IncomingOperation(operation.OperationId, authenticatedDeviceId,
                    operation.ActorUserId, operation.OperationType, operation.PayloadJson, operation.CreatedAtUtc);
                var storedOperation = await operationStore.StoreAsync(incoming, authenticatedDeviceId,
                    operation.ActorUserId, businessApplier, cancellationToken);
                if (!storedOperation.Accepted)
                {
                    var message = storedOperation.ConflictMessage ?? "Un registro no pudo integrarse con los datos centrales.";
                    await UpdateBatchAsync(request.BatchId, 1, completed, message, optionsHash, cancellationToken);
                    return new(false, new(false, completed,
                        $"Importación parcial: {completed} de {operations.Count} registros confirmados. {message} Puedes reintentar la misma vista previa.",
                        GetProcessedProductStocks(operations, completed)), null);
                }
                completed++;
                await UpdateBatchAsync(request.BatchId, 1, completed, null, optionsHash, cancellationToken);
            }

            await ResolveCoveredConflictsAsync(snapshot, authenticatedDeviceId, actorMapResult.Map!, cancellationToken);
            await UpdateBatchAsync(request.BatchId, 2, completed, null, optionsHash, cancellationToken);
            return new(true, new(true, completed,
                $"Importación completada: {completed} registros incorporados y disponibles para los demás dispositivos.",
                GetProcessedProductStocks(operations, completed)), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return new(false, new(false, 0,
                "La importación se interrumpió. Reintenta la misma vista previa para continuar sin duplicar los registros ya confirmados."), null);
        }
        finally { ImportGate.Release(); }
    }

    private async Task<InitialImportPreview> BuildPreviewAsync(
        InitialDataImportSnapshot snapshot, string hash, CancellationToken cancellationToken)
    {
        var blockers = new List<string>();
        var warnings = new List<string>();
        if (snapshot.SchemaVersion != 1) blockers.Add("La versión de los datos no es compatible con este coordinador.");
        if (snapshot.BatchId == Guid.Empty || snapshot.SourceDeviceId == Guid.Empty || snapshot.CapturedAtUtc.Kind != DateTimeKind.Utc)
            blockers.Add("Falta un identificador válido del lote, del dispositivo o de la fecha de captura.");
        if (snapshot.Actors is null || snapshot.Categories is null || snapshot.Products is null || snapshot.Customers is null ||
            snapshot.Purchases is null || snapshot.Sales is null || snapshot.Receivables is null || snapshot.Movements is null ||
            snapshot.Payments is null || snapshot.Discrepancies is null)
        {
            blockers.Add("El paquete está incompleto. No se modificará la base central.");
            return EmptyPreview(snapshot, hash, blockers);
        }
        if (blockers.Count > 0)
            return EmptyPreview(snapshot, hash, blockers);

        if (snapshot.Categories.Count + snapshot.Products.Count + snapshot.Customers.Count + snapshot.Purchases.Count +
            snapshot.Sales.Count + snapshot.Receivables.Count + snapshot.Movements.Count + snapshot.Payments.Count +
            snapshot.Discrepancies.Count == 0)
            blockers.Add("No se encontraron datos de negocio para importar. No se requiere ninguna acción.");

        var exactMatchCount = 0;

        CheckUnique(snapshot.Actors.Select(item => item.LocalUserId), "usuarios locales", blockers);
        CheckUnique(snapshot.Categories.Select(item => item.CategoryId), "categorías", blockers);
        CheckUnique(snapshot.Products.Select(item => item.ProductId), "productos", blockers);
        CheckUnique(snapshot.Customers.Select(item => item.CustomerId), "clientes", blockers);
        CheckUnique(snapshot.Purchases.Select(item => item.OperationId), "compras", blockers);
        CheckUnique(snapshot.Sales.Select(item => item.OperationId), "ventas", blockers);
        CheckUnique(snapshot.Receivables.Select(item => item.OperationId), "cuentas por cobrar", blockers);
        CheckUnique(snapshot.Movements.Select(item => item.MovementId), "movimientos", blockers);
        CheckUnique(snapshot.Payments.Select(item => item.PaymentId), "pagos", blockers);
        CheckUnique(snapshot.Discrepancies.Select(item => item.DiscrepancyId), "diferencias de inventario", blockers);
        if (snapshot.Movements.GroupBy(item => (item.OperationId, item.LineNumber)).Any(group => group.Count() > 1))
            blockers.Add("El paquete contiene dos movimientos para la misma operación y renglón.");

        var localActorIds = snapshot.Actors.Select(item => item.LocalUserId).ToHashSet();
        var referencedActors = snapshot.Purchases.Select(item => item.ActorLocalUserId)
            .Concat(snapshot.Sales.Select(item => item.ActorLocalUserId))
            .Concat(snapshot.Movements.Select(item => item.ActorLocalUserId))
            .Concat(snapshot.Payments.Select(item => item.ActorLocalUserId))
            .Concat(snapshot.Discrepancies.Select(item => item.ActorLocalUserId)).Distinct();
        if (referencedActors.Any(id => id == Guid.Empty || !localActorIds.Contains(id)))
            blockers.Add("Hay registros históricos cuyo autor local no está disponible para conciliar.");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var centralCategories = await context.Categories.AsNoTracking().ToListAsync(cancellationToken);
        var centralProducts = await context.Products.AsNoTracking().ToListAsync(cancellationToken);
        var centralCustomers = await context.Customers.AsNoTracking().ToListAsync(cancellationToken);
        var centralUsers = await context.UserAccounts.AsNoTracking()
            .Select(item => new InitialImportUserOption(item.UserId, item.UserName, item.Role)).ToListAsync(cancellationToken);
        var centralPurchases = await context.Purchases.AsNoTracking().ToListAsync(cancellationToken);
        var centralPurchaseLines = await context.PurchaseLines.AsNoTracking().ToListAsync(cancellationToken);
        var centralSales = await context.Sales.AsNoTracking().ToListAsync(cancellationToken);
        var centralSaleLines = await context.SaleLines.AsNoTracking().ToListAsync(cancellationToken);
        var centralReceivables = await context.Receivables.AsNoTracking().ToListAsync(cancellationToken);
        var centralMovements = await context.InventoryMovements.AsNoTracking().ToListAsync(cancellationToken);
        var centralPayments = await context.Payments.AsNoTracking().ToListAsync(cancellationToken);
        var centralDiscrepancies = await context.InventoryDiscrepancies.AsNoTracking().ToListAsync(cancellationToken);

        var categoryById = centralCategories.ToDictionary(item => item.CategoryId);
        foreach (var category in snapshot.Categories)
        {
            if (category.CategoryId == Guid.Empty || string.IsNullOrWhiteSpace(category.Name))
                blockers.Add("Hay una categoría sin identificador o nombre.");
            else if (categoryById.TryGetValue(category.CategoryId, out var current) && current.Name != category.Name.Trim())
                blockers.Add($"La categoría «{category.Name}» usa un identificador que ya tiene otro nombre en el coordinador.");
            else if (categoryById.ContainsKey(category.CategoryId))
                exactMatchCount++;
            else if (centralCategories.Any(item => item.CategoryId != category.CategoryId && SameName(item.Name, category.Name)))
                blockers.Add($"La categoría «{category.Name}» ya existe con otro identificador; revisa la coincidencia antes de importarla.");
        }
        if (snapshot.Categories.GroupBy(item => Normalize(item.Name)).Any(group => group.Count() > 1))
            blockers.Add("El dispositivo contiene categorías con el mismo nombre y distintos identificadores.");

        var categoryIds = categoryById.Keys.Concat(snapshot.Categories.Select(item => item.CategoryId)).ToHashSet();
        foreach (var product in snapshot.Products)
        {
            if (product.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(product.Name) || string.IsNullOrWhiteSpace(product.Unit) ||
                product.SalePriceMinorUnits < 0 || product.StockMilliUnits < 0 || product.MinimumStockMilliUnits < 0 ||
                !ProductImageRules.IsSupported(product.ImageData))
                blockers.Add($"El producto «{product.Name}» contiene datos inválidos.");
            if (!categoryIds.Contains(product.CategoryId))
                blockers.Add($"El producto «{product.Name}» hace referencia a una categoría que no se importó ni existe en el coordinador.");
        }
        if (snapshot.Products.Sum(item => (long)(item.ImageData?.Length ?? 0)) > ProductImageRules.MaximumInitialImportBytes)
            blockers.Add("Las imágenes de este equipo superan el máximo combinado de 24 MB para la importación inicial. Reduce el tamaño de algunas imágenes y vuelve a analizar.");
        foreach (var product in snapshot.Products)
            if (centralProducts.Any(item => item.ProductId != product.ProductId && item.CategoryId == product.CategoryId && SameName(item.Name, product.Name)))
                warnings.Add($"El producto «{product.Name}» coincide por nombre y categoría con un producto central distinto. Se conservarán ambos identificadores.");
        if (snapshot.Products.GroupBy(item => (Normalize(item.Name), item.CategoryId)).Any(group => group.Count() > 1))
            warnings.Add("El dispositivo contiene productos con el mismo nombre y categoría bajo identificadores distintos; se conservarán por separado.");

        foreach (var customer in snapshot.Customers)
        {
            if (customer.CustomerId == Guid.Empty || string.IsNullOrWhiteSpace(customer.Name) || customer.CreatedAtUtc.Kind != DateTimeKind.Utc)
                blockers.Add($"El cliente «{customer.Name}» contiene datos inválidos.");
            var sameId = centralCustomers.FirstOrDefault(item => item.CustomerId == customer.CustomerId);
            if (sameId is not null && (sameId.Name != customer.Name.Trim() || sameId.Phone != customer.Phone || sameId.CreatedAtUtc != customer.CreatedAtUtc))
                blockers.Add($"El cliente «{customer.Name}» ya existe con el mismo identificador y otros datos.");
            else if (sameId is not null)
                exactMatchCount++;
            if (centralCustomers.Any(item => item.CustomerId != customer.CustomerId && SameName(item.Name, customer.Name)))
                blockers.Add($"El cliente «{customer.Name}» coincide por nombre con otro registro central.");
        }
        if (snapshot.Customers.GroupBy(item => Normalize(item.Name)).Any(group => group.Count() > 1))
            blockers.Add("El dispositivo contiene clientes con el mismo nombre y distintos identificadores.");

        var productIds = centralProducts.Select(item => item.ProductId).Concat(snapshot.Products.Select(item => item.ProductId)).ToHashSet();
        var customerIds = centralCustomers.Select(item => item.CustomerId).Concat(snapshot.Customers.Select(item => item.CustomerId)).ToHashSet();
        foreach (var product in snapshot.Products)
        {
            var sameId = centralProducts.FirstOrDefault(item => item.ProductId == product.ProductId);
            if (sameId is not null && (sameId.Name != product.Name.Trim() || sameId.SalePriceMinorUnits != product.SalePriceMinorUnits ||
                sameId.Unit != product.Unit.Trim() || sameId.CategoryId != product.CategoryId ||
                sameId.MinimumStockMilliUnits != product.MinimumStockMilliUnits || sameId.IsActive != product.IsActive ||
                !ImagesEqual(sameId.ImageData, product.ImageData)))
                blockers.Add($"El producto «{product.Name}» ya existe con el mismo identificador y otro catálogo.");
            else if (sameId is not null)
                exactMatchCount++;
        }

        foreach (var purchase in snapshot.Purchases)
        {
            if (purchase.OccurredAtUtc.Kind != DateTimeKind.Utc || purchase.Lines.Count == 0 ||
                purchase.Lines.Any(line => line.ProductId == Guid.Empty || line.QuantityMilliUnits <= 0 ||
                    string.IsNullOrWhiteSpace(line.ProductNameSnapshot) || string.IsNullOrWhiteSpace(line.UnitSnapshot) ||
                    !productIds.Contains(line.ProductId)) ||
                Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(purchase, JsonOptions)) > 900_000)
                blockers.Add($"La compra {purchase.OperationId} está incompleta o usa productos faltantes.");
            var current = centralPurchases.FirstOrDefault(item => item.OperationId == purchase.OperationId);
            if (current is not null)
            {
                var lines = centralPurchaseLines.Where(item => item.OperationId == purchase.OperationId).ToList();
                if (current.OccurredAtUtc != purchase.OccurredAtUtc || lines.Count != purchase.Lines.Count ||
                    purchase.Lines.Any(line => line.LineNumber < 0 || !lines.Any(saved => saved.LineNumber == line.LineNumber && saved.ProductId == line.ProductId &&
                        saved.ProductNameSnapshot == line.ProductNameSnapshot && saved.UnitSnapshot == line.UnitSnapshot &&
                        saved.QuantityMilliUnits == line.QuantityMilliUnits)))
                    blockers.Add($"La compra {purchase.OperationId} ya existe con datos distintos.");
                else exactMatchCount++;
            }
            if (purchase.Lines.Select(item => item.LineNumber).Distinct().Count() != purchase.Lines.Count || purchase.Lines.Any(item => item.LineNumber < 0))
                blockers.Add($"La compra {purchase.OperationId} contiene renglones repetidos o inválidos.");
        }

        foreach (var sale in snapshot.Sales)
        {
            if (sale.OccurredAtUtc.Kind != DateTimeKind.Utc || sale.TotalMinorUnits < 0 || sale.Lines.Count == 0 ||
                sale.Lines.Any(line => line.ProductId == Guid.Empty || line.QuantityMilliUnits <= 0 ||
                    string.IsNullOrWhiteSpace(line.ProductNameSnapshot) || string.IsNullOrWhiteSpace(line.UnitSnapshot) ||
                    !productIds.Contains(line.ProductId)) ||
                sale.Lines.Sum(line => line.LineTotalMinorUnits) != sale.TotalMinorUnits ||
                sale.Lines.Select(item => item.LineNumber).Distinct().Count() != sale.Lines.Count || sale.Lines.Any(item => item.LineNumber < 0) ||
                Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(sale, JsonOptions)) > 900_000 ||
                (sale.CustomerId is Guid customerId && !customerIds.Contains(customerId)) ||
                (sale.IsCredit && (sale.CustomerId is null ||
                    !snapshot.Receivables.Any(item => item.OperationId == sale.OperationId) &&
                    !centralReceivables.Any(item => item.OperationId == sale.OperationId))))
                blockers.Add($"La venta {sale.OperationId} está incompleta o usa referencias faltantes.");
            var current = centralSales.FirstOrDefault(item => item.OperationId == sale.OperationId);
            if (current is not null)
            {
                var lines = centralSaleLines.Where(item => item.OperationId == sale.OperationId).ToList();
                if (current.OccurredAtUtc != sale.OccurredAtUtc || current.TotalMinorUnits != sale.TotalMinorUnits ||
                    current.IsCredit != sale.IsCredit || current.CustomerId != sale.CustomerId || lines.Count != sale.Lines.Count ||
                    sale.Lines.Any(line => line.LineNumber < 0 || !lines.Any(saved => saved.LineNumber == line.LineNumber && saved.ProductId == line.ProductId &&
                        saved.ProductNameSnapshot == line.ProductNameSnapshot && saved.UnitSnapshot == line.UnitSnapshot &&
                        saved.QuantityMilliUnits == line.QuantityMilliUnits && saved.SalePriceMinorUnits == line.SalePriceMinorUnits &&
                        saved.LineTotalMinorUnits == line.LineTotalMinorUnits)))
                    blockers.Add($"La venta {sale.OperationId} ya existe con datos distintos.");
                else exactMatchCount++;
            }
        }

        foreach (var receivable in snapshot.Receivables)
        {
            if (receivable.DueAtUtc.Kind != DateTimeKind.Utc || receivable.OriginalAmountMinorUnits < 0 ||
                receivable.OutstandingMinorUnits < 0 || receivable.OutstandingMinorUnits > receivable.OriginalAmountMinorUnits ||
                !customerIds.Contains(receivable.CustomerId) ||
                !centralSales.Any(item => item.OperationId == receivable.OperationId) && !snapshot.Sales.Any(item => item.OperationId == receivable.OperationId))
                blockers.Add($"La cuenta por cobrar de la operación {receivable.OperationId} está incompleta o tiene referencias faltantes.");
            var current = centralReceivables.FirstOrDefault(item => item.OperationId == receivable.OperationId);
            if (current is not null && (current.CustomerId != receivable.CustomerId || current.OriginalAmountMinorUnits != receivable.OriginalAmountMinorUnits ||
                current.OutstandingMinorUnits != receivable.OutstandingMinorUnits || current.DueAtUtc != receivable.DueAtUtc))
                blockers.Add($"La cuenta por cobrar de la operación {receivable.OperationId} ya existe con datos diferentes.");
            else if (current is not null)
                exactMatchCount++;
        }

        foreach (var movement in snapshot.Movements)
        {
            if (movement.MovementId == Guid.Empty || movement.OperationId == Guid.Empty || movement.LineNumber < 0 ||
                movement.QuantityMilliUnits <= 0 || movement.OccurredAtUtc.Kind != DateTimeKind.Utc ||
                string.IsNullOrWhiteSpace(movement.UnitSnapshot) || string.IsNullOrWhiteSpace(movement.ProductNameSnapshot) ||
                !Enum.IsDefined(typeof(MauiApp1.Domain.Inventory.InventoryMovementType), movement.Type) ||
                (movement.ProductId is Guid productId && !productIds.Contains(productId)) ||
                (movement.Type == (int)MauiApp1.Domain.Inventory.InventoryMovementType.MixedWaste && movement.ProductId is not null) ||
                (movement.Type != (int)MauiApp1.Domain.Inventory.InventoryMovementType.MixedWaste && movement.ProductId is null) ||
                (movement.Type == (int)MauiApp1.Domain.Inventory.InventoryMovementType.MixedWaste &&
                    (movement.Reason is null || !movement.Reason.StartsWith("Merma mixta", StringComparison.Ordinal))))
                blockers.Add($"El movimiento {movement.MovementId} está incompleto o usa un producto faltante.");
            var sameLine = centralMovements.FirstOrDefault(item => item.OperationId == movement.OperationId && item.LineNumber == movement.LineNumber);
            if (sameLine is not null && (sameLine.ProductId != movement.ProductId || sameLine.Type != movement.Type ||
                sameLine.QuantityMilliUnits != movement.QuantityMilliUnits || sameLine.Reason != movement.Reason ||
                sameLine.OccurredAtUtc != movement.OccurredAtUtc || sameLine.UnitSnapshot != movement.UnitSnapshot ||
                sameLine.ProductNameSnapshot != movement.ProductNameSnapshot))
                blockers.Add($"El movimiento {movement.MovementId} coincide con una operación y renglón centrales con datos diferentes.");
            else if (sameLine is not null)
                exactMatchCount++;
            var sameId = centralMovements.FirstOrDefault(item => item.MovementId == movement.MovementId);
            if (sameId is not null && (sameId.OperationId != movement.OperationId || sameId.LineNumber != movement.LineNumber))
                blockers.Add($"El identificador del movimiento {movement.MovementId} ya pertenece a otro registro central.");
        }

        foreach (var payment in snapshot.Payments)
        {
            if (payment.PaymentId == Guid.Empty || payment.OperationId == Guid.Empty || payment.AmountMinorUnits <= 0 ||
                payment.OccurredAtUtc.Kind != DateTimeKind.Utc ||
                (!centralReceivables.Any(item => item.OperationId == payment.OperationId) && !snapshot.Receivables.Any(item => item.OperationId == payment.OperationId)))
                blockers.Add($"El pago {payment.PaymentId} está incompleto o no tiene cuenta por cobrar.");
            var current = centralPayments.FirstOrDefault(item => item.PaymentId == payment.PaymentId);
            if (current is not null && (current.OperationId != payment.OperationId || current.AmountMinorUnits != payment.AmountMinorUnits || current.OccurredAtUtc != payment.OccurredAtUtc))
                blockers.Add($"El pago {payment.PaymentId} ya existe con datos diferentes.");
            else if (current is not null)
                exactMatchCount++;
        }

        foreach (var discrepancy in snapshot.Discrepancies)
        {
            if (discrepancy.DiscrepancyId == Guid.Empty || discrepancy.OperationId == Guid.Empty ||
                !productIds.Contains(discrepancy.ProductId) || discrepancy.ShortageMilliUnits <= 0 || discrepancy.CreatedAtUtc.Kind != DateTimeKind.Utc)
                blockers.Add($"La diferencia de inventario {discrepancy.DiscrepancyId} está incompleta o usa un producto faltante.");
            var current = centralDiscrepancies.FirstOrDefault(item => item.DiscrepancyId == discrepancy.DiscrepancyId);
            if (current is not null && (current.OperationId != discrepancy.OperationId || current.ProductId != discrepancy.ProductId ||
                current.ShortageMilliUnits != discrepancy.ShortageMilliUnits || current.CreatedAtUtc != discrepancy.CreatedAtUtc ||
                current.IsResolved != discrepancy.IsResolved))
                blockers.Add($"La diferencia de inventario {discrepancy.DiscrepancyId} ya existe con datos diferentes.");
            else if (current is not null)
                exactMatchCount++;
        }

        var productStockConflicts = snapshot.Products.Select(product =>
        {
            var existing = centralProducts.FirstOrDefault(item => item.ProductId == product.ProductId);
            return existing is not null && existing.StockMilliUnits != product.StockMilliUnits
                ? new InitialImportStockConflict(product.ProductId, product.Name, existing.StockMilliUnits, product.StockMilliUnits)
                : null;
        }).Where(item => item is not null).Cast<InitialImportStockConflict>().ToArray();
        if (productStockConflicts.Length > 0)
            warnings.Add($"{productStockConflicts.Length} producto(s) tienen existencias diferentes; el Administrador debe elegir qué saldo conservar.");

        var centralUsersByName = centralUsers.GroupBy(item => Normalize(item.UserName))
            .Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single().UserId);
        var actors = snapshot.Actors.Select(actor => new InitialImportActorMappingOption(actor.LocalUserId, actor.UserName,
            actor.Role, centralUsersByName.TryGetValue(Normalize(actor.UserName), out var userId) ? userId : null)).ToArray();
        if (actors.Any(item => item.SuggestedCoordinatorUserId is null))
            warnings.Add("Hay autores históricos sin coincidencia exacta por nombre. Asígnales una cuenta central antes de importar.");

        return new InitialImportPreview(snapshot.BatchId, hash, snapshot.SourceDeviceId, snapshot.CapturedAtUtc,
            snapshot.Categories.Count, snapshot.Products.Count, snapshot.Customers.Count,
            snapshot.Purchases.Count, snapshot.Sales.Count, snapshot.Movements.Count,
            snapshot.Receivables.Count, snapshot.Payments.Count, snapshot.Discrepancies.Count, exactMatchCount,
            blockers.Distinct().ToArray(), warnings.Distinct().ToArray(), actors, centralUsers, productStockConflicts);
    }

    private async Task<(Dictionary<Guid, Guid>? Map, string? Error)> ValidateActorMappingsAsync(
        InitialDataImportSnapshot snapshot, IReadOnlyList<InitialImportActorMapping> mappings, CancellationToken ct)
    {
        if (mappings is null || mappings.Select(item => item.LocalUserId).Distinct().Count() != mappings.Count ||
            mappings.Count != snapshot.Actors.Count || snapshot.Actors.Any(actor => !mappings.Any(item => item.LocalUserId == actor.LocalUserId)))
            return (null, "Asigna una cuenta central a cada autor histórico antes de confirmar.");
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var ids = mappings.Select(item => item.CoordinatorUserId).Distinct().ToArray();
        var users = await context.UserAccounts.AsNoTracking().Where(item => ids.Contains(item.UserId))
            .Select(item => item.UserId).ToListAsync(ct);
        if (mappings.Any(item => item.CoordinatorUserId == Guid.Empty || !users.Contains(item.CoordinatorUserId)))
            return (null, "Una cuenta elegida ya no existe o está desactivada. Actualiza la vista previa.");
        return (mappings.ToDictionary(item => item.LocalUserId, item => item.CoordinatorUserId), null);
    }

    private async Task<string?> ValidateExistingAuthorsAsync(
        InitialDataImportSnapshot snapshot, IReadOnlyDictionary<Guid, Guid> actorMap, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var purchaseIds = snapshot.Purchases.Select(item => item.OperationId).ToArray();
        var existingPurchases = await context.Purchases.AsNoTracking().Where(item => purchaseIds.Contains(item.OperationId))
            .ToDictionaryAsync(item => item.OperationId, item => item.ActorUserId, ct);
        if (snapshot.Purchases.Any(item => existingPurchases.TryGetValue(item.OperationId, out var actor) &&
                actor != actorMap[item.ActorLocalUserId]))
            return "Una compra con el mismo identificador ya existe con otro responsable central. Ajusta la asignación de autores y vuelve a analizar.";

        var saleIds = snapshot.Sales.Select(item => item.OperationId).ToArray();
        var existingSales = await context.Sales.AsNoTracking().Where(item => saleIds.Contains(item.OperationId))
            .ToDictionaryAsync(item => item.OperationId, item => item.ActorUserId, ct);
        if (snapshot.Sales.Any(item => existingSales.TryGetValue(item.OperationId, out var actor) &&
                actor != actorMap[item.ActorLocalUserId]))
            return "Una venta con el mismo identificador ya existe con otro responsable central. Ajusta la asignación de autores y vuelve a analizar.";

        var movementKeys = snapshot.Movements.Select(item => (item.OperationId, item.LineNumber)).ToHashSet();
        if (movementKeys.Count > 0)
        {
            var operationIds = movementKeys.Select(item => item.OperationId).Distinct().ToArray();
            var existingMovements = await context.InventoryMovements.AsNoTracking()
                .Where(item => operationIds.Contains(item.OperationId)).ToListAsync(ct);
            foreach (var movement in snapshot.Movements)
            {
                var existing = existingMovements.FirstOrDefault(item => item.OperationId == movement.OperationId && item.LineNumber == movement.LineNumber);
                if (existing is not null && existing.ActorUserId != actorMap[movement.ActorLocalUserId])
                    return "Un movimiento con el mismo identificador ya existe con otro responsable central. Ajusta la asignación de autores y vuelve a analizar.";
            }
        }

        var paymentIds = snapshot.Payments.Select(item => item.PaymentId).ToArray();
        var existingPayments = await context.Payments.AsNoTracking().Where(item => paymentIds.Contains(item.PaymentId))
            .ToDictionaryAsync(item => item.PaymentId, item => item.ActorUserId, ct);
        if (snapshot.Payments.Any(item => existingPayments.TryGetValue(item.PaymentId, out var actor) &&
                actor != actorMap[item.ActorLocalUserId]))
            return "Un pago con el mismo identificador ya existe con otro responsable central. Ajusta la asignación de autores y vuelve a analizar.";

        var discrepancyIds = snapshot.Discrepancies.Select(item => item.DiscrepancyId).ToArray();
        var existingDiscrepancies = await context.InventoryDiscrepancies.AsNoTracking()
            .Where(item => discrepancyIds.Contains(item.DiscrepancyId))
            .ToDictionaryAsync(item => item.DiscrepancyId, item => item.ActorUserId, ct);
        if (snapshot.Discrepancies.Any(item => existingDiscrepancies.TryGetValue(item.DiscrepancyId, out var actor) &&
                actor != actorMap[item.ActorLocalUserId]))
            return "Una diferencia de inventario con el mismo identificador ya existe con otro responsable central. Ajusta la asignación de autores y vuelve a analizar.";
        return null;
    }

    private async Task<(Dictionary<Guid, long>? Stocks, string? Error)> ResolveProductStocksAsync(
        InitialDataImportSnapshot snapshot, IReadOnlyList<InitialImportStockChoice> choices, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var products = await context.Products.AsNoTracking().ToDictionaryAsync(item => item.ProductId, ct);
        var conflicts = snapshot.Products.Where(item => products.TryGetValue(item.ProductId, out var current) &&
                current.StockMilliUnits != item.StockMilliUnits).Select(item => item.ProductId).ToHashSet();
        if (choices is null || choices.Select(item => item.ProductId).Distinct().Count() != choices.Count ||
            conflicts.Any(id => !choices.Any(item => item.ProductId == id)) || choices.Any(item => !snapshot.Products.Any(product => product.ProductId == item.ProductId)))
            return (null, "Elige qué existencia conservar para cada producto que difiere del coordinador.");
        var result = new Dictionary<Guid, long>();
        foreach (var product in snapshot.Products)
        {
            if (!products.TryGetValue(product.ProductId, out var current)) result[product.ProductId] = product.StockMilliUnits;
            else if (conflicts.Contains(product.ProductId))
                result[product.ProductId] = choices.Single(item => item.ProductId == product.ProductId).UseSourceStock
                    ? product.StockMilliUnits : current.StockMilliUnits;
            else result[product.ProductId] = current.StockMilliUnits;
        }
        return (result, null);
    }

    private List<ImportJournalOperation> BuildImportOperations(InitialDataImportSnapshot snapshot,
        Guid administratorUserId, Dictionary<Guid, Guid> actorMap, Dictionary<Guid, long> stocks)
    {
        var operations = new List<ImportJournalOperation>();
        void Add(string entity, Guid id, string type, object payload, Guid actor, DateTime time) =>
            operations.Add(new ImportJournalOperation(StableOperationId(snapshot.BatchId, entity, id),
                type, JsonSerializer.Serialize(payload, JsonOptions), actor, AsUtc(time)));

        foreach (var item in snapshot.Categories.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            Add("category", item.CategoryId, "Import.Category", item, administratorUserId, snapshot.CapturedAtUtc);
        foreach (var item in snapshot.Products.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            Add("product", item.ProductId, "Import.Product", item with { StockMilliUnits = stocks[item.ProductId] }, administratorUserId, snapshot.CapturedAtUtc);
        foreach (var item in snapshot.Customers.OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.CustomerId))
            Add("customer", item.CustomerId, "Import.Customer", item, administratorUserId, item.CreatedAtUtc);
        foreach (var item in snapshot.Purchases.OrderBy(item => item.OccurredAtUtc).ThenBy(item => item.OperationId))
            Add("purchase", item.OperationId, "Import.Purchase", new InitialImportPurchasePayload(
                item.OperationId, item.OccurredAtUtc, item.Lines), actorMap[item.ActorLocalUserId], item.OccurredAtUtc);
        foreach (var item in snapshot.Sales.OrderBy(item => item.OccurredAtUtc).ThenBy(item => item.OperationId))
            Add("sale", item.OperationId, "Import.Sale", new InitialImportSalePayload(item.OperationId,
                item.OccurredAtUtc, item.TotalMinorUnits, item.IsCredit, item.CustomerId, item.Lines),
                actorMap[item.ActorLocalUserId], item.OccurredAtUtc);
        foreach (var item in snapshot.Receivables.OrderBy(item => item.DueAtUtc).ThenBy(item => item.OperationId))
            Add("receivable", item.OperationId, "Import.Receivable", item, administratorUserId, item.DueAtUtc);
        foreach (var item in snapshot.Movements.OrderBy(item => item.OccurredAtUtc).ThenBy(item => item.OperationId).ThenBy(item => item.LineNumber))
            Add("movement", item.MovementId, "Import.Movement", new InitialImportMovementPayload(item.MovementId,
                item.OperationId, item.LineNumber, item.ProductId, item.ProductNameSnapshot, item.UnitSnapshot,
                item.Type, item.QuantityMilliUnits, item.Reason, item.OccurredAtUtc),
                actorMap[item.ActorLocalUserId], item.OccurredAtUtc);
        foreach (var item in snapshot.Payments.OrderBy(item => item.OccurredAtUtc).ThenBy(item => item.PaymentId))
            Add("payment", item.PaymentId, "Import.Payment", new InitialImportPaymentPayload(item.PaymentId,
                item.OperationId, item.AmountMinorUnits, item.OccurredAtUtc),
                actorMap[item.ActorLocalUserId], item.OccurredAtUtc);
        foreach (var item in snapshot.Discrepancies.OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.DiscrepancyId))
            Add("discrepancy", item.DiscrepancyId, "Import.Discrepancy", new InitialImportDiscrepancyPayload(
                item.DiscrepancyId, item.OperationId, item.ProductId, item.ShortageMilliUnits,
                item.CreatedAtUtc, item.IsResolved),
                actorMap[item.ActorLocalUserId], item.CreatedAtUtc);
        return operations;
    }

    private static bool ImagesEqual(byte[]? left, byte[]? right) =>
        left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);

    private static IReadOnlyList<InitialImportStockValue> GetProcessedProductStocks(
        IReadOnlyList<ImportJournalOperation> operations, int processedCount) => operations.Take(processedCount)
        .Where(item => item.OperationType == "Import.Product")
        .Select(item => JsonSerializer.Deserialize<InitialImportProduct>(item.PayloadJson, JsonOptions))
        .Where(item => item is not null)
        .Select(item => new InitialImportStockValue(item!.ProductId, item.StockMilliUnits)).ToArray();

    private async Task<StoredBatch?> ReadBatchAsync(Guid batchId, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SourceDeviceId, AdministratorUserId, SnapshotHash, SnapshotJson, OptionsHash, Status, ImportedRecords FROM CoordinatorInitialImportBatches WHERE BatchId=$id;";
        command.Parameters.AddWithValue("$id", batchId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new StoredBatch(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2),
                reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetInt32(5), reader.GetInt32(6))
            : null;
    }

    private async Task UpdateBatchAsync(Guid batchId, int status, int imported, string? error,
        string? optionsHash, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE CoordinatorInitialImportBatches SET Status=$status, ImportedRecords=$imported, LastError=$error, OptionsHash=COALESCE(OptionsHash,$options), UpdatedAtUtc=$now WHERE BatchId=$id;";
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$imported", imported);
        command.Parameters.AddWithValue("$error", error is null ? DBNull.Value : error);
        command.Parameters.AddWithValue("$options", optionsHash is null ? DBNull.Value : optionsHash);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.Ticks);
        command.Parameters.AddWithValue("$id", batchId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task CreatePreImportBackupAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var backupDirectory = Path.Combine(Path.GetDirectoryName(databasePath)!, "Backups");
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(backupDirectory, $"BeforeImport-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{batchId:N}.db");
        await using var source = new SqliteConnection(_connectionString);
        await source.OpenAsync(cancellationToken);
        await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = backupPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());
        await destination.OpenAsync(cancellationToken);
        source.BackupDatabase(destination);
    }

    private async Task ResolveCoveredConflictsAsync(InitialDataImportSnapshot snapshot, Guid sourceDeviceId,
        IReadOnlyDictionary<Guid, Guid> actorMap, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: false);
        var conflicts = new List<StoredImportConflict>();
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT OperationId, ActorUserId, OperationType, PayloadJson FROM CoordinatorOperations WHERE DeviceId=$device AND Status=2 ORDER BY Sequence;";
            select.Parameters.AddWithValue("$device", sourceDeviceId.ToString("D"));
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                conflicts.Add(new StoredImportConflict(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)),
                    reader.GetString(2), reader.GetString(3)));
        }

        foreach (var conflict in conflicts)
        {
            if (!InitialImportOutboxMatcher.IsCoveredBySnapshot(conflict.OperationType, conflict.OperationId,
                    conflict.PayloadJson, snapshot) || !IsHistoricalAuthorMapped(conflict, snapshot, actorMap))
                continue;
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE CoordinatorOperations SET Status=3, ConflictMessage='Resuelto: el registro se incorporó mediante importación inicial.' WHERE OperationId=$id AND DeviceId=$device AND ActorUserId=$actor AND Status=2;";
            update.Parameters.AddWithValue("$id", conflict.OperationId.ToString("D"));
            update.Parameters.AddWithValue("$device", sourceDeviceId.ToString("D"));
            update.Parameters.AddWithValue("$actor", conflict.ActorUserId.ToString("D"));
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private static bool IsHistoricalAuthorMapped(StoredImportConflict conflict, InitialDataImportSnapshot snapshot,
        IReadOnlyDictionary<Guid, Guid> actorMap)
    {
        Guid? localActorId = conflict.OperationType switch
        {
            "Purchase.Recorded" => snapshot.Purchases.FirstOrDefault(item => item.OperationId == conflict.OperationId)?.ActorLocalUserId,
            "Sale.Recorded" => snapshot.Sales.FirstOrDefault(item => item.OperationId == conflict.OperationId)?.ActorLocalUserId,
            "Inventory.Recorded" or "Inventory.MixedWasteRecorded" => snapshot.Movements
                .Where(item => item.OperationId == conflict.OperationId).Select(item => item.ActorLocalUserId)
                .Distinct().Take(2).ToArray() is [var actor] ? actor : null,
            "Credit.PaymentRecorded" => snapshot.Payments.FirstOrDefault(item => item.PaymentId == conflict.OperationId)?.ActorLocalUserId,
            _ => null
        };
        return localActorId is null || actorMap.TryGetValue(localActorId.Value, out var mappedActorId) &&
            mappedActorId == conflict.ActorUserId;
    }

    private static InitialImportPreview EmptyPreview(InitialDataImportSnapshot snapshot, string hash, IReadOnlyList<string> blockers) =>
        new(snapshot.BatchId, hash, snapshot.SourceDeviceId, snapshot.CapturedAtUtc,
            snapshot.Categories?.Count ?? 0, snapshot.Products?.Count ?? 0,
            snapshot.Customers?.Count ?? 0, snapshot.Purchases?.Count ?? 0, snapshot.Sales?.Count ?? 0,
            snapshot.Movements?.Count ?? 0, snapshot.Receivables?.Count ?? 0, snapshot.Payments?.Count ?? 0,
            snapshot.Discrepancies?.Count ?? 0, 0, blockers, [], [], [], []);

    private static void CheckUnique(IEnumerable<Guid> ids, string label, List<string> blockers)
    {
        if (ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Count())
            blockers.Add($"El paquete contiene identificadores vacíos o repetidos en {label}.");
    }

    private static bool SameName(string? first, string? second) => Normalize(first) == Normalize(second);
    private static string Normalize(string? value) => value?.Trim().ToUpperInvariant() ?? string.Empty;
    private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static Guid StableOperationId(Guid batchId, string entity, Guid entityId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{batchId:D}/{entity}/{entityId:D}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private sealed record ImportJournalOperation(Guid OperationId, string OperationType, string PayloadJson,
        Guid ActorUserId, DateTime CreatedAtUtc);
    private sealed record StoredImportConflict(Guid OperationId, Guid ActorUserId, string OperationType,
        string PayloadJson);
    private sealed record StoredBatch(Guid SourceDeviceId, Guid AdministratorUserId, string Hash, string Json,
        string? OptionsHash, int Status, int ImportedRecords);
}
