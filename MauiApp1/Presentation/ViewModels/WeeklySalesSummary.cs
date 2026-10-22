using System.Globalization;
using MauiApp1.Application.Reports;

namespace MauiApp1.Presentation.ViewModels;

public sealed record DailySalesTotal(DateTime Date, decimal Total, bool IsToday)
{
    public string DayLabel => IsToday ? "Hoy" : Date.DayOfWeek switch
    {
        DayOfWeek.Monday => "Lun", DayOfWeek.Tuesday => "Mar",
        DayOfWeek.Wednesday => "Mié", DayOfWeek.Thursday => "Jue",
        DayOfWeek.Friday => "Vie", DayOfWeek.Saturday => "Sáb", _ => "Dom"
    };

    public string AmountText => WeeklySalesSummary.FormatAmount(Total);
}

public sealed record WeeklySalesSummary(IReadOnlyList<DailySalesTotal> Days, bool HasSales)
{
    public decimal DailyAverage => Days.Sum(day => day.Total) / 7m;

    public static string FormatAmount(decimal amount) =>
        "C$" + amount.ToString("N2", CultureInfo.GetCultureInfo("es-NI"));

    public static WeeklySalesSummary Create(IReadOnlyList<SalesReportRow> sales, DateTime today, TimeZoneInfo timeZone)
    {
        var firstDay = today.Date.AddDays(-6);
        var totals = new decimal[7];
        var hasSales = false;
        foreach (var sale in sales)
        {
            // SQLite can materialize UTC timestamps with an Unspecified Kind.
            var localDate = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(sale.OccurredAtUtc, DateTimeKind.Utc), timeZone).Date;
            var index = (localDate - firstDay).Days;
            if (index < 0 || index >= 7) continue;
            totals[index] += sale.TotalMinorUnits / 100m;
            hasSales = true;
        }

        return new WeeklySalesSummary(Enumerable.Range(0, 7)
            .Select(index => new DailySalesTotal(firstDay.AddDays(index), totals[index], index == 6))
            .ToArray(), hasSales);
    }
}
