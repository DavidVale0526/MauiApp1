namespace MauiApp1.Infrastructure.Lan;

/// <summary>
/// Normaliza la huella SHA-256 del certificado del coordinador.
/// El archivo generado por el instalador contiene la huella, pero puede acabar
/// con texto adicional (notas del operador, por ejemplo). Por eso no se deben
/// eliminar los caracteres no hexadecimales de todo el contenido: las letras
/// a-f de ese texto se mezclarían con la huella. Se toma la primera secuencia
/// de exactamente 64 caracteres hexadecimales.
/// </summary>
public static class CoordinatorFingerprint
{
    private const int Sha256HexLength = 64;

    /// <summary>
    /// Devuelve la huella en mayúsculas y sin separadores, o <c>null</c> si el
    /// texto no contiene una huella SHA-256 válida.
    /// </summary>
    public static string? TryNormalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var span = value.AsSpan();
        var consecutive = 0;
        for (var index = 0; index < span.Length; index++)
        {
            if (Uri.IsHexDigit(span[index]))
            {
                consecutive++;
                if (consecutive == Sha256HexLength)
                {
                    return span.Slice(index - Sha256HexLength + 1, Sha256HexLength)
                        .ToString()
                        .ToUpperInvariant();
                }
            }
            else
            {
                consecutive = 0;
            }
        }

        // Acepta huellas escritas con espacios o dos puntos (EC 5E 82 ...).
        // Solo se usa como respaldo cuando no hubo una secuencia de 64 caracteres.
        var compact = new string(value.Where(Uri.IsHexDigit).ToArray());
        return compact.Length == Sha256HexLength ? compact.ToUpperInvariant() : null;
    }
}
