using System.Text.Json;
using MauiApp1.Application.Categories;
using MauiApp1.Application.Inventory;
using MauiApp1.Application.Products;
using MauiApp1.Application.Purchases;
using MauiApp1.Application.Sales;
using MauiApp1.Application.Sync;

namespace MauiApp1.Infrastructure.Sync;

/// <summary>Checks whether an existing local outbox event is fully represented by a business snapshot.</summary>
public static class InitialImportOutboxMatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static bool IsCoveredBySnapshot(string operationType, Guid operationId, string payloadJson,
        InitialDataImportSnapshot snapshot)
    {
        try
        {
            switch (operationType)
            {
                case "Category.Created":
                case "Category.Renamed":
                {
                    var payload = JsonSerializer.Deserialize<CategoryChangedPayload>(payloadJson, JsonOptions);
                    return payload is not null && !string.IsNullOrWhiteSpace(payload.Name) &&
                        snapshot.Categories.Any(item => item.CategoryId == payload.CategoryId && item.Name == payload.Name.Trim());
                }
                case "Product.Created":
                case "Product.Updated":
                case "Product.Deactivated":
                case "Product.Reactivated":
                {
                    var payload = JsonSerializer.Deserialize<ProductChangedPayload>(payloadJson, JsonOptions);
                    return payload is not null && !string.IsNullOrWhiteSpace(payload.Name) && !string.IsNullOrWhiteSpace(payload.Unit) &&
                        snapshot.Products.Any(item => item.ProductId == payload.ProductId && item.Name == payload.Name.Trim() &&
                            item.SalePriceMinorUnits == payload.SalePriceMinorUnits && item.Unit == payload.Unit.Trim() &&
                            item.CategoryId == payload.CategoryId && item.MinimumStockMilliUnits == payload.MinimumStockMilliUnits &&
                            item.IsActive == payload.IsActive);
                }
                case "Customer.Created":
                {
                    var payload = JsonSerializer.Deserialize<CustomerCreatedPayload>(payloadJson, JsonOptions);
                    return payload is not null && !string.IsNullOrWhiteSpace(payload.Name) &&
                        snapshot.Customers.Any(item => item.CustomerId == payload.CustomerId &&
                            item.Name == payload.Name.Trim() && item.Phone == payload.Phone);
                }
                case "Purchase.Recorded":
                {
                    var payload = JsonSerializer.Deserialize<PurchaseRecordedPayload>(payloadJson, JsonOptions);
                    var purchase = snapshot.Purchases.FirstOrDefault(item => item.OperationId == operationId);
                    return payload?.Lines is not null && purchase is not null && payload.OperationId == purchase.OperationId &&
                        payload.OccurredAtUtc == purchase.OccurredAtUtc && payload.Lines.Count == purchase.Lines.Count &&
                        payload.Lines.All(line => purchase.Lines.Any(saved => saved.LineNumber == line.LineNumber &&
                            saved.ProductId == line.ProductId && saved.QuantityMilliUnits == line.QuantityMilliUnits &&
                            saved.ProductNameSnapshot == line.ProductNameSnapshot && saved.UnitSnapshot == line.UnitSnapshot));
                }
                case "Sale.Recorded":
                {
                    var payload = JsonSerializer.Deserialize<SaleRecordedPayload>(payloadJson, JsonOptions);
                    var sale = snapshot.Sales.FirstOrDefault(item => item.OperationId == operationId);
                    return payload?.Lines is not null && sale is not null && payload.OperationId == sale.OperationId &&
                        payload.OccurredAtUtc == sale.OccurredAtUtc && payload.TotalMinorUnits == sale.TotalMinorUnits &&
                        payload.IsCredit == sale.IsCredit && payload.CustomerId == sale.CustomerId && payload.Lines.Count == sale.Lines.Count &&
                        payload.Lines.All(line => sale.Lines.Any(saved => saved.LineNumber == line.LineNumber &&
                            saved.ProductId == line.ProductId && saved.QuantityMilliUnits == line.QuantityMilliUnits &&
                            saved.ProductNameSnapshot == line.ProductNameSnapshot && saved.UnitSnapshot == line.UnitSnapshot &&
                            saved.SalePriceMinorUnits == line.SalePriceMinorUnits && saved.LineTotalMinorUnits == line.LineTotalMinorUnits));
                }
                case "Inventory.Recorded":
                case "Inventory.MixedWasteRecorded":
                {
                    var payload = JsonSerializer.Deserialize<InventoryOperationPayload>(payloadJson, JsonOptions);
                    var movements = snapshot.Movements.Where(item => item.OperationId == operationId).ToArray();
                    return payload?.Lines is not null && movements.Length > 0 && payload.OperationId == operationId &&
                        payload.OccurredAtUtc == movements[0].OccurredAtUtc && payload.Lines.Count == movements.Length &&
                        payload.Lines.All(line => movements.Any(saved => saved.LineNumber == line.LineNumber &&
                            saved.ProductId == (line.ProductId == Guid.Empty ? null : (Guid?)line.ProductId) &&
                            saved.Type == (int)line.Type && saved.QuantityMilliUnits == line.QuantityMilliUnits &&
                            saved.Reason == line.Reason && saved.ProductNameSnapshot == line.ProductNameSnapshot &&
                            saved.UnitSnapshot == line.UnitSnapshot));
                }
                case "Credit.PaymentRecorded":
                {
                    var payload = JsonSerializer.Deserialize<PaymentRecordedPayload>(payloadJson, JsonOptions);
                    return payload is not null && snapshot.Payments.Any(item => item.PaymentId == payload.PaymentId &&
                        item.OperationId == payload.OperationId && item.ActorLocalUserId == payload.ActorUserId &&
                        item.AmountMinorUnits == payload.AmountMinorUnits && item.OccurredAtUtc == payload.OccurredAtUtc);
                }
                default:
                    return false;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    private sealed record CustomerCreatedPayload(Guid CustomerId, string Name, string? Phone);
    private sealed record PaymentRecordedPayload(Guid PaymentId, Guid OperationId, Guid ActorUserId,
        long AmountMinorUnits, DateTime OccurredAtUtc);
}
