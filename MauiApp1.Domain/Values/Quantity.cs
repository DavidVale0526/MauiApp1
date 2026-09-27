namespace MauiApp1.Domain.Values;

/// <summary>Cantidad exacta en milésimas; la unidad del producto se definirá en el módulo de catálogo.</summary>
public readonly record struct Quantity(long MilliUnits)
{
    public const int DecimalPlaces = 3;

    public static Quantity FromDecimal(decimal value)
    {
        var scaled = value * 1000m;
        if (scaled != decimal.Truncate(scaled))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "La cantidad admite como máximo tres decimales.");
        }

        return new Quantity(checked((long)scaled));
    }

    public decimal ToDecimal() => MilliUnits / 1000m;
}
