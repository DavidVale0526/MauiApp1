using System.Text.Json;
using MauiApp1.Application.Authentication;
using MauiApp1.Application.Products;
using MauiApp1.Application.Storage;
using MauiApp1.Domain.Identifiers;
using MauiApp1.Domain.Values;
using MauiApp1.Infrastructure.Persistence;
using MauiApp1.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MauiApp1.Infrastructure.Products;

internal sealed class EfProductService(
    IDbContextFactory<LocalDbContext> contextFactory,
    ILocalDeviceIdentity deviceIdentity,
    IAuthenticationService authenticationService) : IProductService
{
    public async Task<IReadOnlyList<ProductSummary>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        EnsureSignedIn();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = from product in context.Products.AsNoTracking()
                    join category in context.Categories.AsNoTracking()
                        on product.CategoryId equals category.CategoryId
                    orderby product.IsActive descending, product.Name
                    select new ProductSummary(
                    new ProductId(product.ProductId),
                    product.Name,
                    new MoneyAmount(product.SalePriceMinorUnits),
                    new Quantity(product.StockMilliUnits),
                    product.Unit,
                    new CategoryId(product.CategoryId),
                    category.Name,
                    new Quantity(product.MinimumStockMilliUnits),
                    product.IsActive,
                    product.ImageData);
        return await query.ToListAsync(cancellationToken);
    }

    public async Task<ProductOperationResult> CreateAsync(
        ProductDraft draft,
        CancellationToken cancellationToken = default)
    {
        var actor = authenticationService.CurrentUser;
        if (actor is null)
        {
            return ProductOperationResult.Failure("Inicia sesión para administrar productos.");
        }

        if (!TryNormalizeDraft(draft, out var name, out var unit, out var validationError))
        {
            return ProductOperationResult.Failure(validationError);
        }
        if (!ProductImageRules.IsSupported(draft.ImageData))
        {
            return ProductOperationResult.Failure("La imagen debe ser JPG o PNG y no superar 512 KB.");
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await context.Categories.AnyAsync(category => category.CategoryId == draft.CategoryId.Value, cancellationToken))
        {
            return ProductOperationResult.Failure("Selecciona una categoría que siga disponible.");
        }

        var product = new ProductEntity
        {
            ProductId = ProductId.New().Value,
            Name = name,
            SalePriceMinorUnits = draft.SalePrice.MinorUnits,
            StockMilliUnits = 0,
            Unit = unit,
            CategoryId = draft.CategoryId.Value,
            MinimumStockMilliUnits = draft.MinimumStock.MilliUnits,
            IsActive = true,
            ImageData = draft.ReplaceImage ? draft.ImageData : null
        };
        context.Products.Add(product);
        await EnqueueAsync(context, actor.Id.Value, "Product.Created", product, cancellationToken);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return ProductOperationResult.Success();
        }
        catch (DbUpdateException)
        {
            return ProductOperationResult.Failure("No se pudo guardar el producto. Comprueba que la categoría siga disponible e inténtalo de nuevo.");
        }
    }

    public async Task<ProductOperationResult> UpdateAsync(
        Guid productId,
        ProductDraft draft,
        CancellationToken cancellationToken = default)
    {
        var actor = authenticationService.CurrentUser;
        if (actor is null)
        {
            return ProductOperationResult.Failure("Inicia sesión para administrar productos.");
        }

        if (productId == Guid.Empty)
        {
            return ProductOperationResult.Failure("No se encontró el producto que se desea editar.");
        }

        if (!TryNormalizeDraft(draft, out var name, out var unit, out var validationError))
        {
            return ProductOperationResult.Failure(validationError);
        }
        if (!ProductImageRules.IsSupported(draft.ImageData))
        {
            return ProductOperationResult.Failure("La imagen debe ser JPG o PNG y no superar 512 KB.");
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var product = await context.Products.SingleOrDefaultAsync(item => item.ProductId == productId, cancellationToken);
        if (product is null)
        {
            return ProductOperationResult.Failure("El producto ya no existe. Actualiza la lista e inténtalo de nuevo.");
        }

        if (!await context.Categories.AnyAsync(category => category.CategoryId == draft.CategoryId.Value, cancellationToken))
        {
            return ProductOperationResult.Failure("Selecciona una categoría que siga disponible.");
        }

        var imageData = draft.ReplaceImage ? draft.ImageData : product.ImageData;
        if (product.Name == name && product.SalePriceMinorUnits == draft.SalePrice.MinorUnits &&
            product.Unit == unit && product.CategoryId == draft.CategoryId.Value &&
            product.MinimumStockMilliUnits == draft.MinimumStock.MilliUnits &&
            ImagesEqual(product.ImageData, imageData))
        {
            return ProductOperationResult.Success();
        }

        var expected = new ProductCatalogSnapshot(product.Name, product.SalePriceMinorUnits, product.Unit,
            product.CategoryId, product.MinimumStockMilliUnits, product.IsActive, product.ImageData, ImageDataSpecified: true);
        product.Name = name;
        product.SalePriceMinorUnits = draft.SalePrice.MinorUnits;
        product.Unit = unit;
        product.CategoryId = draft.CategoryId.Value;
        product.MinimumStockMilliUnits = draft.MinimumStock.MilliUnits;
        product.ImageData = imageData;
        await EnqueueAsync(context, actor.Id.Value, "Product.Updated", product, cancellationToken, expected);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return ProductOperationResult.Success();
        }
        catch (DbUpdateException)
        {
            return ProductOperationResult.Failure("No se pudo actualizar el producto. Comprueba que la categoría siga disponible e inténtalo de nuevo.");
        }
    }

    public async Task<ProductOperationResult> SetActiveAsync(
        Guid productId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var actor = authenticationService.CurrentUser;
        if (actor is null)
        {
            return ProductOperationResult.Failure("Inicia sesión para administrar productos.");
        }

        if (productId == Guid.Empty)
        {
            return ProductOperationResult.Failure("No se encontró el producto seleccionado.");
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var product = await context.Products.SingleOrDefaultAsync(item => item.ProductId == productId, cancellationToken);
        if (product is null)
        {
            return ProductOperationResult.Failure("El producto ya no existe. Actualiza la lista.");
        }

        if (product.IsActive == isActive)
        {
            return ProductOperationResult.Success();
        }

        var expected = new ProductCatalogSnapshot(product.Name, product.SalePriceMinorUnits, product.Unit,
            product.CategoryId, product.MinimumStockMilliUnits, product.IsActive, product.ImageData, ImageDataSpecified: true);
        product.IsActive = isActive;
        await EnqueueAsync(
            context,
            actor.Id.Value,
            isActive ? "Product.Reactivated" : "Product.Deactivated",
            product,
            cancellationToken,
            expected);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return ProductOperationResult.Success();
        }
        catch (DbUpdateException)
        {
            return ProductOperationResult.Failure("No se pudo cambiar el estado del producto. Inténtalo de nuevo.");
        }
    }

    private async Task EnqueueAsync(
        LocalDbContext context,
        Guid actorUserId,
        string operationType,
        ProductEntity product,
        CancellationToken cancellationToken,
        ProductCatalogSnapshot? expected = null)
    {
        var outbox = new EfOperationOutbox(context, deviceIdentity);
        var payload = new ProductChangedPayload(
            product.ProductId,
            product.Name,
            product.SalePriceMinorUnits,
            product.StockMilliUnits,
            product.Unit,
            product.CategoryId,
            product.MinimumStockMilliUnits,
            product.IsActive,
            expected,
            ImageData: product.ImageData,
            ImageDataSpecified: true);
        await outbox.EnqueueAsync(
            new NewOutboxOperation(
                Guid.NewGuid(),
                actorUserId,
                operationType,
                JsonSerializer.Serialize(payload)),
            cancellationToken);
    }

    private void EnsureSignedIn()
    {
        if (authenticationService.CurrentUser is null)
        {
            throw new UnauthorizedAccessException("Inicia sesión para consultar productos.");
        }
    }

    private static bool ImagesEqual(byte[]? left, byte[]? right) =>
        left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);

    private static bool TryNormalizeDraft(
        ProductDraft draft,
        out string name,
        out string unit,
        out string errorMessage)
    {
        name = draft.Name?.Trim() ?? string.Empty;
        unit = draft.Unit?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            errorMessage = "Escribe el nombre del producto.";
            return false;
        }

        if (draft.SalePrice.MinorUnits < 0)
        {
            errorMessage = "El precio no puede ser negativo.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(unit))
        {
            errorMessage = "Escribe la unidad del producto (por ejemplo: pieza, kg o litro).";
            return false;
        }

        if (draft.CategoryId.IsEmpty)
        {
            errorMessage = "Selecciona una categoría.";
            return false;
        }

        if (draft.MinimumStock.MilliUnits < 0)
        {
            errorMessage = "El nivel mínimo no puede ser negativo.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }
}
