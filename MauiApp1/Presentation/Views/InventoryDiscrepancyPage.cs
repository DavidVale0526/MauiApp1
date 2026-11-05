using System.Globalization;
using MauiApp1.Application.Inventory;
using MauiApp1.Application.Sync;
using Microsoft.Maui.Controls.Shapes;

namespace MauiApp1.Presentation.Views;

/// <summary>
/// Ventas que quedaron sin existencia suficiente al sincronizarse. La venta no se
/// pierde; el Administrador revisa el faltante y lo marca como conciliado.
/// </summary>
public sealed class InventoryDiscrepancyPage : CoordinatorRefreshPage
{
    private readonly IInventoryDiscrepancyService _service;
    private readonly ILocalOperationSyncService _operationSyncService;
    private readonly VerticalStackLayout _items = new() { Spacing = 16, Padding = new Thickness(16, 20) };

    public InventoryDiscrepancyPage(IInventoryDiscrepancyService service, ILocalOperationSyncService operationSyncService,
        ILocalDataChangeNotifier changeNotifier) : base(changeNotifier)
    {
        _service = service;
        _operationSyncService = operationSyncService;
        Title = "Faltantes de existencia";
        BackgroundColor = Color.FromArgb("#F7F9FB");

        var back = CreateSecondaryButton("← Volver a sincronización");
        back.HorizontalOptions = LayoutOptions.Start;
        back.Clicked += async (_, _) => await Navigation.PopModalAsync();
        var refresh = CreateSecondaryButton("Actualizar faltantes");
        refresh.Clicked += async (_, _) => await LoadAsync();
        var sync = CreatePrimaryButton("Sincronizar ahora");
        sync.Clicked += async (_, _) => await SynchronizeAsync();

        _items.Add(back);
        _items.Add(new Label { Text = "Faltantes de existencia", FontSize = 28, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#111D31") });
        _items.Add(new Label
        {
            Text = "Estas ventas se registraron cuando el inventario ya no alcanzaba. " +
                   "La venta y el dinero se conservan; revisa la existencia física y marca el faltante como conciliado.",
            FontSize = 14,
            TextColor = Color.FromArgb("#61738A")
        });
        _items.Add(refresh);
        _items.Add(sync);
        Content = new ScrollView { Content = _items };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    protected override Task RefreshAfterCoordinatorChangesAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        while (_items.Children.Count > 5) _items.Children.RemoveAt(5);
        IReadOnlyList<InventoryDiscrepancyEntry> shortages;
        try { shortages = await _service.GetOpenAsync(); }
        catch
        {
            _items.Add(CreateMessage("No se pudieron consultar los faltantes. Comprueba la conexión local."));
            return;
        }

        if (shortages.Count == 0)
        {
            _items.Add(new Border
            {
                Padding = 16,
                BackgroundColor = Colors.White,
                Stroke = Color.FromArgb("#E3EAF2"),
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
                Content = CreateMessage("No hay faltantes de existencia pendientes.")
            });
            return;
        }

        foreach (var shortage in shortages)
        {
            var card = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(12) };
            card.Add(new Label
            {
                Text = $"Faltan {Quantity(shortage.ShortageMilliUnits)} {shortage.Unit} de {shortage.ProductName}",
                FontAttributes = FontAttributes.Bold,
                FontSize = 18,
                TextColor = Color.FromArgb("#8A5A00")
            });
            card.Add(new Label
            {
                Text = $"Venta {Reference(shortage)} • {shortage.OccurredAtUtc.ToLocalTime():g}",
                FontSize = 13,
                TextColor = Color.FromArgb("#61738A")
            });
            card.Add(new Label
            {
                Text = $"Registrada por {shortage.ActorUserName} • Total de la venta: C$ {Money(shortage.SaleTotalMinorUnits)}",
                FontSize = 13,
                TextColor = Color.FromArgb("#61738A")
            });
            var resolve = CreatePrimaryButton("Marcar faltante como conciliado");
            resolve.Clicked += async (_, _) => await ResolveAsync(shortage);
            card.Add(resolve);
            _items.Add(new Border
            {
                BackgroundColor = Colors.White,
                Stroke = Color.FromArgb("#F0B429"),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 14 },
                Padding = 1,
                Content = card
            });
        }
    }

    private async Task ResolveAsync(InventoryDiscrepancyEntry shortage)
    {
        var confirmed = await DisplayAlertAsync(
            "Conciliar faltante",
            $"¿Confirmas que revisaste el inventario de {shortage.ProductName}? El faltante de " +
            $"{Quantity(shortage.ShortageMilliUnits)} {shortage.Unit} dejará de mostrarse como pendiente.",
            "Sí, conciliar",
            "Cancelar");
        if (!confirmed) return;

        string? error;
        try { error = await _service.ResolveAsync(shortage.DiscrepancyId); }
        catch { error = "No se pudo guardar la conciliación. Comprueba la conexión local."; }

        if (error is null)
        {
            await DisplayAlertAsync("Faltante conciliado", "La decisión quedó guardada y se enviará a los demás dispositivos.", "Aceptar");
            try { await _operationSyncService.SynchronizeAsync(); } catch { }
        }
        else
        {
            await DisplayAlertAsync("No se pudo conciliar", error, "Aceptar");
        }

        await LoadAsync();
    }

    private async Task SynchronizeAsync()
    {
        try { await _operationSyncService.SynchronizeAsync(); }
        catch
        {
            await DisplayAlertAsync("Sincronización", "No se pudo completar. Los cambios siguen guardados en este dispositivo.", "Aceptar");
        }
        await LoadAsync();
    }

    private static string Quantity(long milliUnits) =>
        (milliUnits / 1000m).ToString("0.###", CultureInfo.CurrentCulture);

    private static string Money(long minorUnits) =>
        (minorUnits / 100m).ToString("N2", CultureInfo.CurrentCulture);

    private static string Reference(InventoryDiscrepancyEntry shortage) =>
        $"#VTA-{shortage.OccurredAtUtc.ToLocalTime():yyyy}-{shortage.SaleOperationId.ToString("N")[..4].ToUpperInvariant()}";

    private static Button CreatePrimaryButton(string text) => new()
    {
        Text = text,
        BackgroundColor = Color.FromArgb("#008060"),
        TextColor = Colors.White,
        CornerRadius = 11,
        FontAttributes = FontAttributes.Bold,
        Padding = new Thickness(18, 13)
    };

    private static Button CreateSecondaryButton(string text) => new()
    {
        Text = text,
        BackgroundColor = Color.FromArgb("#E8EEF5"),
        TextColor = Color.FromArgb("#0D1B2A"),
        CornerRadius = 11,
        Padding = new Thickness(18, 13)
    };

    private static Label CreateMessage(string text) => new()
    {
        Text = text,
        FontSize = 14,
        TextColor = Color.FromArgb("#61738A")
    };
}
