using System.Globalization;
using System.Text;
using MauiApp1.Application.Sales;

namespace MauiApp1.Presentation.Services;

/// <summary>Creates a small, offline PDF using standard WinAnsi Helvetica fonts.</summary>
public static class SaleReceiptPdfGenerator
{
    private const int PageWidth = 595;
    private const int PageHeight = 842;
    private const int Left = 48;
    private const int Right = 547;
    private const int BodyTop = 704;
    private const int BodyBottom = 64;
    private const string Ink = "0.09 0.15 0.25";
    private const string Muted = "0.37 0.45 0.56";
    private const string Green = "0.00 0.48 0.36";
    private const string Rule = "0.85 0.89 0.93";

    public static byte[] Generate(SaleHistoryEntry sale)
        => GenerateDocument(sale, null);

    public static byte[] GenerateReversal(SaleHistoryEntry sale, SaleReversalEntry reversal)
    {
        ArgumentNullException.ThrowIfNull(reversal);
        if (reversal.SaleOperationId != sale.OperationId)
            throw new ArgumentException("La reversión no pertenece a la venta indicada.", nameof(reversal));
        return GenerateDocument(sale, reversal);
    }

    private static byte[] GenerateDocument(SaleHistoryEntry sale, SaleReversalEntry? reversal)
    {
        ArgumentNullException.ThrowIfNull(sale);
        if (sale.OperationId == Guid.Empty) throw new ArgumentException("La venta no tiene una referencia válida.", nameof(sale));

        var rows = BuildRows(sale, reversal);
        var pages = Paginate(rows);
        var objects = new List<byte[]>
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Array.Empty<byte>() // populated with the Pages tree after page ids are known
        };
        objects.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
        objects.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"));

        var pageIds = Enumerable.Range(0, pages.Count).Select(index => 5 + index * 2).ToArray();
        var kids = string.Join(" ", pageIds.Select(id => $"{id} 0 R"));
        objects[1] = Ascii($"<< /Type /Pages /Count {pages.Count} /Kids [{kids}] >>");

        for (var index = 0; index < pages.Count; index++)
        {
            var pageId = 5 + index * 2;
            var contentId = pageId + 1;
            objects.Add(Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentId} 0 R >>"));
            var content = BuildPageContent(sale, reversal, pages[index], index + 1, pages.Count);
            objects.Add(Ascii($"<< /Length {content.Length} >>\nstream\n{content}\nendstream"));
        }

        using var output = new MemoryStream();
        WriteAscii(output, "%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(output.Position);
            WriteAscii(output, $"{index + 1} 0 obj\n");
            output.Write(objects[index]);
            WriteAscii(output, "\nendobj\n");
        }

        var xrefOffset = output.Position;
        WriteAscii(output, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
            WriteAscii(output, $"{offset:0000000000} 00000 n \n");
        WriteAscii(output, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF");
        return output.ToArray();
    }

    private static List<PdfRow> BuildRows(SaleHistoryEntry sale, SaleReversalEntry? reversal)
    {
        if (reversal is not null)
        {
            var reversalRows = new List<PdfRow>
            {
                new("CONSTANCIA INTERNA DE ANULACION / DEVOLUCION", 11, true, Ink, IsSection: true),
                new($"Referencia de reversión: R-{reversal.OperationId:N}".ToUpperInvariant(), 9, true, Muted),
                new($"Venta original: V-{sale.OperationId:N}".ToUpperInvariant(), 9, true, Muted),
                new($"Tipo: {(reversal.Kind == SaleReversalKind.Cancellation ? "Anulación completa" : "Devolución")}", 10, true, Ink),
                new($"Fecha: {reversal.OccurredAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}", 10, false, Muted),
                new($"Responsable: {reversal.ActorUserName}", 10, false, Muted)
            };
            reversalRows.AddRange(Wrapped($"Motivo: {reversal.Reason}", 10, Muted));
            reversalRows.Add(new(string.Empty, 8, false, Rule, IsSeparator: true));
            reversalRows.Add(new("PRODUCTOS AFECTADOS", 11, true, Ink, IsSection: true));
            foreach (var returned in reversal.Lines)
            {
                if (returned.SaleLineNumber < 0 || returned.SaleLineNumber >= sale.Lines.Count) continue;
                var line = sale.Lines[returned.SaleLineNumber];
                reversalRows.AddRange(Wrapped(line.ProductName, 10, Ink, true));
                reversalRows.Add(new($"Cantidad: {Quantity(returned.QuantityMilliUnits)} {line.Unit}", 9, false, Muted,
                    RightText: $"C$ {Money(returned.AmountMinorUnits)}"));
                reversalRows.Add(new(returned.ReturnToStock ? "Destino: regresa a inventario vendible" : $"Destino: no vendible · {returned.DisposalReason}", 9, false, Muted));
                reversalRows.Add(new(string.Empty, 5, false, Rule, IsSeparator: true));
            }
            reversalRows.Add(new(reversal.RefundMethod is SaleRefundMethod method
                ? $"Reembolso declarado: {method switch { SaleRefundMethod.Cash => "Efectivo", SaleRefundMethod.Transfer => "Transferencia", SaleRefundMethod.Card => "Tarjeta", _ => "No disponible" }}"
                : "Aplicación financiera: reducción de saldo de crédito", 9, false, Muted));
            reversalRows.Add(new("IMPORTE DE LA REVERSIÓN", 12, true, Ink,
                RightText: $"C$ {Money(reversal.AmountMinorUnits)}", RightFontSize: 13, RightColor: Green));
            reversalRows.Add(new(string.Empty, 8, false, Rule, IsSeparator: true));
            reversalRows.Add(new("Documento interno de trazabilidad. No es factura ni comprobante fiscal.", 9, true, Muted, IsSection: true));
            reversalRows.Add(new("El reembolso contado documenta lo declarado por el Administrador y no sustituye el registro de caja.", 9, false, Muted));
            return reversalRows;
        }

        var rows = new List<PdfRow>
        {
            new("INFORMACION DE LA VENTA", 11, true, Ink, IsSection: true),
            new($"Fecha y hora: {sale.OccurredAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}", 10, false, Muted),
            new($"Atendio: {sale.ActorUserName}", 10, false, Muted)
        };

        if (sale.IsCredit)
        {
            rows.AddRange(Wrapped($"Cliente: {sale.CustomerName ?? "No disponible"}", 10, Muted));
            rows.Add(new($"Saldo pendiente actual: C$ {Money(sale.OutstandingMinorUnits ?? sale.TotalMinorUnits)}", 10, true, Green));
            rows.Add(new($"Vencimiento: {sale.CreditDueAtUtc?.ToLocalTime().ToString("d", CultureInfo.CurrentCulture) ?? "No disponible"}", 10, false, Muted));
        }
        else
        {
            rows.Add(new("Modalidad: contado", 10, false, Muted));
        }

        rows.Add(new(string.Empty, 8, false, Rule, IsSeparator: true));
        rows.Add(new("PRODUCTOS", 11, true, Ink, IsSection: true));
        foreach (var line in sale.Lines)
        {
            rows.AddRange(Wrapped(line.ProductName, 11, Ink, true));
            rows.Add(new($"{Quantity(line.QuantityMilliUnits)} {line.Unit} x C$ {Money(line.SalePriceMinorUnits)}",
                9, false, Muted, RightText: $"C$ {Money(line.LineTotalMinorUnits)}"));
            rows.Add(new(string.Empty, 5, false, Rule, IsSeparator: true));
        }

        rows.Add(new("TOTAL DE LA VENTA", 13, true, Ink, RightText: $"C$ {Money(sale.TotalMinorUnits)}", RightFontSize: 14, RightColor: Green));
        rows.Add(new(string.Empty, 8, false, Rule, IsSeparator: true));
        rows.Add(new("DETALLE DEL PAGO", 11, true, Ink, IsSection: true));
        if (sale.IsCredit)
        {
            rows.Add(new("Venta registrada como cuenta por cobrar; los abonos se controlan por separado.", 9, false, Muted));
        }
        else if (sale.Payments is not { Count: > 0 })
        {
            rows.Add(new("Sin detalle de pago registrado para esta venta historica.", 9, false, Muted));
        }
        else
        {
            foreach (var payment in sale.Payments)
            {
                var (label, amount) = payment.Method switch
                {
                    SalePaymentMethod.Cash => ($"Efectivo aplicado (recibido C$ {Money(payment.CashReceivedMinorUnits)})", payment.AmountMinorUnits),
                    SalePaymentMethod.Card => ("Tarjeta", payment.AmountMinorUnits),
                    SalePaymentMethod.Transfer => ("Transferencia", payment.AmountMinorUnits),
                    _ => ("Otro metodo", payment.AmountMinorUnits)
                };
                rows.Add(new(label, 9, false, Muted, RightText: $"C$ {Money(amount)}"));
                if (payment.Method == SalePaymentMethod.Cash)
                    rows.Add(new("Cambio entregado", 9, false, Green, RightText: $"C$ {Money(payment.ChangeMinorUnits)}"));
            }
        }

        rows.Add(new(string.Empty, 10, false, Rule, IsSeparator: true));
        rows.Add(new("Comprobante interno. No es factura ni comprobante fiscal.", 9, true, Muted, IsSection: true));
        rows.Add(new("No se incluyen impuestos ni campos fiscales.", 9, false, Muted));
        return rows;
    }

    private static List<List<PdfRow>> Paginate(IReadOnlyList<PdfRow> rows)
    {
        var pages = new List<List<PdfRow>>();
        var current = new List<PdfRow>();
        var used = 0;
        foreach (var row in rows)
        {
            var rowHeight = row.IsSeparator ? Math.Max(8, row.FontSize) : row.FontSize + 6;
            if (current.Count > 0 && used + rowHeight > BodyTop - BodyBottom)
            {
                pages.Add(current);
                current = [];
                used = 0;
            }
            current.Add(row);
            used += rowHeight;
        }
        if (current.Count > 0 || pages.Count == 0) pages.Add(current);
        return pages;
    }

    private static string BuildPageContent(SaleHistoryEntry sale, SaleReversalEntry? reversal, IReadOnlyList<PdfRow> rows, int pageNumber, int pageCount)
    {
        var content = new StringBuilder();
        AddText(content, "Miscelanea Ari", Left, 800, 19, true, Ink);
        AddText(content, reversal is null ? "COMPROBANTE INTERNO DE VENTA" : "CONSTANCIA INTERNA DE REVERSIÓN", Left, 779, 11, true, Green);
        AddText(content, reversal is null ? $"Folio: V-{sale.OperationId:N}".ToUpperInvariant() : $"Folio: R-{reversal.OperationId:N}".ToUpperInvariant(), Left, 758, 9, true, Muted);
        AddText(content, $"Pagina {pageNumber} de {pageCount}", 455, 758, 8, false, Muted);
        AddRule(content, 742);

        var y = BodyTop;
        foreach (var row in rows)
        {
            if (row.IsSeparator)
            {
                y -= row.FontSize;
                AddRule(content, y);
                continue;
            }

            AddText(content, row.Text, Left, y, row.FontSize, row.Bold, row.Color);
            if (row.RightText is not null)
                AddText(content, row.RightText, row.RightX, y, row.RightFontSize, row.RightBold, row.RightColor);
            y -= row.FontSize + 6;
        }

        AddRule(content, 52);
        AddText(content, "Generado localmente por Miscelanea Ari - documento interno no fiscal", Left, 37, 8, false, Muted);
        return content.ToString();
    }

    private static IEnumerable<PdfRow> Wrapped(string text, int size, string color, bool bold = false)
    {
        const int maxCharacters = 78;
        var remaining = (text ?? string.Empty).Trim();
        if (remaining.Length == 0)
        {
            yield return new(string.Empty, size, bold, color);
            yield break;
        }

        while (remaining.Length > maxCharacters)
        {
            var breakAt = remaining.LastIndexOf(' ', maxCharacters);
            if (breakAt < 1) breakAt = maxCharacters;
            yield return new(remaining[..breakAt], size, bold, color);
            remaining = remaining[(breakAt + (remaining[breakAt] == ' ' ? 1 : 0))..].TrimStart();
        }
        yield return new(remaining, size, bold, color);
    }

    private static void AddText(StringBuilder content, string? text, int x, int y, int size, bool bold, string color)
    {
        var bytes = Encoding.Latin1.GetBytes(ToWinAnsi(text ?? string.Empty));
        content.Append("BT /").Append(bold ? "F2" : "F1").Append(' ').Append(size)
            .Append(" Tf ").Append(color).Append(" rg 1 0 0 1 ").Append(x).Append(' ').Append(y)
            .Append(" Tm <").Append(Convert.ToHexString(bytes)).Append("> Tj ET\n");
    }

    private static void AddRule(StringBuilder content, int y) =>
        content.Append(Rule).Append(" RG 0.8 w ").Append(Left).Append(' ').Append(y).Append(" m ")
            .Append(Right).Append(' ').Append(y).Append(" l S\n");

    private static string ToWinAnsi(string value) => new(value.Select(character => character switch
    {
        '—' or '–' => '-',
        '·' => '|',
        '×' => 'x',
        '✓' => ' ',
        _ when character <= 255 => character,
        _ => '?'
    }).ToArray());

    private static string Money(long minorUnits) => (minorUnits / 100m).ToString("0.00", CultureInfo.CurrentCulture);

    private static string Quantity(long milliUnits) => (milliUnits / 1000m).ToString("0.###", CultureInfo.CurrentCulture);

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);

    private static void WriteAscii(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }

    private sealed record PdfRow(string Text, int FontSize, bool Bold, string Color,
        bool IsSeparator = false, bool IsSection = false, string? RightText = null,
        int RightFontSize = 10, string RightColor = Ink, int RightX = 440, bool RightBold = true);
}
