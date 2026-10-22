using MauiApp1.Presentation.ViewModels;

namespace MauiApp1.Presentation.Views;

// Native MAUI controls keep the seven columns responsive without an extra chart package.
public sealed class WeeklySalesChart : Grid
{
    public static readonly BindableProperty DaysProperty = BindableProperty.Create(
        nameof(Days), typeof(IReadOnlyList<DailySalesTotal>), typeof(WeeklySalesChart), null,
        propertyChanged: (bindable, _, _) => ((WeeklySalesChart)bindable).Render());

    public IReadOnlyList<DailySalesTotal>? Days
    {
        get => (IReadOnlyList<DailySalesTotal>?)GetValue(DaysProperty);
        set => SetValue(DaysProperty, value);
    }

    public WeeklySalesChart()
    {
        ColumnSpacing = 6;
        HeightRequest = 200;
    }

    private void Render()
    {
        Children.Clear();
        ColumnDefinitions.Clear();
        if (Days is not { Count: 7 } days) return;
        var maximum = days.Max(day => day.Total);
        var previousPeak = days.Take(6).Max(day => day.Total);
        for (var index = 0; index < days.Count; index++)
        {
            ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var day = days[index];
            var isPeak = !day.IsToday && previousPeak > 0 && day.Total == previousPeak;
            var color = Color.FromArgb(day.IsToday ? "#007A56" : isPeak ? "#1D293D" : "#D6E2FF");
            var column = new Grid
            {
                RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) },
                RowSpacing = 10
            };
            var plot = new Grid { Padding = new Thickness(4, 8, 4, 0) };
            foreach (var fraction in new[] { 0d, .5d, 1d })
            {
                plot.Add(new BoxView
                {
                    HeightRequest = 1, Color = Color.FromArgb("#EDF0F4"),
                    VerticalOptions = fraction == 0 ? LayoutOptions.Start : fraction == 1 ? LayoutOptions.End : LayoutOptions.Center
                });
            }
            plot.Add(new Border
            {
                BackgroundColor = color, StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(4, 4, 0, 0) },
                HeightRequest = maximum > 0 ? (double)(day.Total / maximum) * 150 : 0,
                IsVisible = day.Total > 0,
                MaximumWidthRequest = 42,
                HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.End
            });
            column.Add(plot);
            column.Add(new Label
            {
                Text = day.DayLabel, FontSize = 11, HorizontalTextAlignment = TextAlignment.Center,
                TextColor = day.IsToday || isPeak ? color : Color.FromArgb("#57616F"),
                FontAttributes = day.IsToday || isPeak ? FontAttributes.Bold : FontAttributes.None
            }, 0, 1);
            var description = $"{day.Date:dd/MM/yyyy}: {day.AmountText}";
            SemanticProperties.SetDescription(column, description);
            ToolTipProperties.SetText(column, description);
            this.Add(column, index);
        }
    }
}
