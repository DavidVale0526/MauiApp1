using MauiApp1.Presentation.ViewModels;

namespace MauiApp1.Presentation.Views;

// Native MAUI controls keep the seven columns responsive without an extra chart package.
public sealed class WeeklySalesChart : Grid
{
    private readonly double _plotHeight = OperatingSystem.IsAndroid() ? 144 : 180;
    private readonly double _plotTopPadding = OperatingSystem.IsAndroid() ? 24 : 30;
    private readonly double _barAreaHeight = OperatingSystem.IsAndroid() ? 120 : 150;
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
        ColumnSpacing = 0;
        RowSpacing = OperatingSystem.IsAndroid() ? 6 : 8;
        // Android lets the day and amount rows grow with the user's font scale.
        HeightRequest = OperatingSystem.IsAndroid() ? -1 : 230;
        RowDefinitions.Add(new RowDefinition(new GridLength(_plotHeight)));
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
    }

    private void Render()
    {
        Children.Clear();
        ColumnDefinitions.Clear();
        if (Days is not { Count: 7 } days) return;
        var maximum = days.Max(day => day.Total);
        var previousPeak = days.Take(6).Max(day => day.Total);
        var references = new Grid { Padding = new Thickness(0, _plotTopPadding, 0, 0), InputTransparent = true };
        Grid.SetColumnSpan(references, 7);
        foreach (var alignment in new[] { LayoutOptions.Start, LayoutOptions.Center, LayoutOptions.End })
            references.Add(new BoxView { HeightRequest = 1, Color = Color.FromArgb("#EDF0F4"), VerticalOptions = alignment });
        Children.Add(references);

        var amountLabel = new Label
        {
            FontSize = 12, TextColor = Color.FromArgb("#111D31"), HorizontalTextAlignment = TextAlignment.Center
        };
        void ShowAmount(DailySalesTotal selected)
        {
            var dayText = selected.IsToday ? "Hoy" : selected.Date.ToString("dd/MM");
            amountLabel.Text = $"{dayText} · {selected.AmountText}";
        }
        ShowAmount(days[6]);
        this.Add(amountLabel, 0, 2);
        Grid.SetColumnSpan(amountLabel, 7);
        for (var index = 0; index < days.Count; index++)
        {
            ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var day = days[index];
            var isPeak = !day.IsToday && previousPeak > 0 && day.Total == previousPeak;
            var color = Color.FromArgb(day.IsToday ? "#007A56" : isPeak ? "#1D293D" : "#D6E2FF");
            var column = new Grid
            {
                RowDefinitions = { new RowDefinition(new GridLength(_plotHeight)), new RowDefinition(GridLength.Auto) },
                RowSpacing = this.RowSpacing,
                BackgroundColor = Colors.Transparent
            };
            var plot = new Grid { Padding = new Thickness(4, _plotTopPadding, 4, 0) };
            plot.Add(new Border
            {
                BackgroundColor = color, StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(4, 4, 0, 0) },
                HeightRequest = maximum > 0 ? (double)(day.Total / maximum) * _barAreaHeight : 0,
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
            SemanticProperties.SetHint(column, "Selecciona para ver el importe del día.");
            column.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => ShowAmount(day)) });
            this.Add(column, index);
            Grid.SetRowSpan(column, 2);
        }
    }
}
