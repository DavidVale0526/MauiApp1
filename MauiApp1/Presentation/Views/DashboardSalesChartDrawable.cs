using System.Globalization;
using MauiApp1.Application.Dashboard;
using Microsoft.Maui.Graphics;

namespace MauiApp1.Presentation.Views;

internal sealed class DashboardSalesChartDrawable : IDrawable
{
    private IReadOnlyList<DashboardSalesPoint> _points = [];

    public IReadOnlyList<DashboardSalesPoint> Points
    {
        get => _points;
        set => _points = value ?? [];
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (dirtyRect.Width <= 90 || dirtyRect.Height <= 80 || _points.Count == 0)
            return;

        const float left = 64;
        const float right = 10;
        const float top = 10;
        const float bottom = 34;
        var plotWidth = Math.Max(1, dirtyRect.Width - left - right);
        var plotHeight = Math.Max(1, dirtyRect.Height - top - bottom);
        var maximum = _points.Max(point => (double)point.TotalMinorUnits);
        var axisMaximum = GetNiceAxisMaximum(Math.Max(maximum, 100));
        var baseline = top + plotHeight;

        canvas.FontSize = 9;
        canvas.FontColor = Color.FromArgb("#8A97A7");
        canvas.StrokeColor = Color.FromArgb("#E9EEF4");
        canvas.StrokeSize = 1;

        for (var division = 0; division <= 3; division++)
        {
            var fraction = division / 3f;
            var y = top + plotHeight * fraction;
            var amount = axisMaximum * (1 - fraction);
            canvas.DrawLine(left, y, left + plotWidth, y);
            canvas.DrawString(
                $"C$ {(amount / 100d).ToString("N0", CultureInfo.CurrentCulture)}",
                0, y - 8, left - 6, 16,
                HorizontalAlignment.Right, VerticalAlignment.Center);
        }

        var coordinates = new List<PointF>(_points.Count);
        for (var index = 0; index < _points.Count; index++)
        {
            var x = left + (_points.Count == 1 ? plotWidth / 2 : plotWidth * index / (_points.Count - 1));
            var fraction = Math.Clamp(_points[index].TotalMinorUnits / axisMaximum, 0d, 1d);
            var y = baseline - (float)(plotHeight * fraction);
            coordinates.Add(new PointF(x, y));
        }

        if (coordinates.Count > 1)
        {
            var area = new PathF();
            area.MoveTo(coordinates[0].X, baseline);
            area.LineTo(coordinates[0].X, coordinates[0].Y);
            foreach (var point in coordinates.Skip(1))
                area.LineTo(point.X, point.Y);
            area.LineTo(coordinates[^1].X, baseline);
            area.Close();
            canvas.FillColor = Color.FromArgb("#E5F4EC");
            canvas.FillPath(area);
        }

        canvas.StrokeColor = Color.FromArgb("#16805F");
        canvas.StrokeSize = 2.5f;
        for (var index = 1; index < coordinates.Count; index++)
            canvas.DrawLine(coordinates[index - 1], coordinates[index]);

        for (var index = 0; index < coordinates.Count; index++)
        {
            var point = coordinates[index];
            canvas.FillColor = index == coordinates.Count - 1
                ? Color.FromArgb("#008060")
                : Color.FromArgb("#4B966F");
            canvas.FillCircle(point.X - 3.5f, point.Y - 3.5f, 7);

            var date = _points[index].LocalDate.Date;
            var label = date == DateTime.Today ? "Hoy" : date.ToString("ddd", CultureInfo.CurrentCulture);
            canvas.FontSize = 10;
            canvas.FontColor = index == coordinates.Count - 1
                ? Color.FromArgb("#008060")
                : Color.FromArgb("#52657D");
            var slotWidth = plotWidth / _points.Count;
            var labelX = Math.Clamp(point.X - slotWidth / 2, left, left + plotWidth - slotWidth);
            canvas.DrawString(label, labelX, baseline + 5, slotWidth, 18,
                HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }

    private static double GetNiceAxisMaximum(double value)
    {
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        var normalized = value / magnitude;
        var rounded = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
        return rounded * magnitude;
    }
}
