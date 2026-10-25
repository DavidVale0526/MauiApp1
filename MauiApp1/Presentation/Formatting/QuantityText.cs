using System.Globalization;

namespace MauiApp1.Presentation.Formatting;

public static class QuantityText
{
    private static readonly (string Singular, string Plural)[] Units =
    [
        ("pieza", "piezas"), ("unidad", "unidades"), ("caja", "cajas"),
        ("paquete", "paquetes"), ("bolsa", "bolsas"), ("botella", "botellas"),
        ("lata", "latas"), ("litro", "litros"), ("libra", "libras"),
        ("galón", "galones"), ("docena", "docenas"), ("quintal", "quintales"),
        ("gramo", "gramos"), ("kilogramo", "kilogramos"), ("metro", "metros")
    ];

    public static string Format(decimal quantity, string unit)
    {
        var displayUnit = unit.Trim();
        foreach (var pair in Units)
        {
            if (!string.Equals(displayUnit, pair.Singular, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(displayUnit, pair.Plural, StringComparison.OrdinalIgnoreCase)) continue;
            displayUnit = Math.Abs(quantity) == 1m ? pair.Singular : pair.Plural;
            break;
        }
        // Symbols and custom units retain their original spelling.
        return $"{quantity.ToString("0.###", CultureInfo.CurrentCulture)} {displayUnit}".TrimEnd();
    }
}
