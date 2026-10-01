using MauiApp1.Application.Sync;

namespace MauiApp1.Presentation.Views;

public sealed class ConflictReviewPage : ContentPage
{
    private readonly ICoordinatorConflictReviewService _service;
    private readonly VerticalStackLayout _items = new() { Spacing = 14, Padding = new Thickness(16) };

    public ConflictReviewPage(ICoordinatorConflictReviewService service)
    {
        _service = service;
        Title = "Revisión de conflictos";
        var refresh = new Button { Text = "Actualizar conflictos" };
        refresh.Clicked += async (_, _) => await LoadAsync();
        _items.Add(new Label { Text = "Cambios simultáneos", FontSize = 24, FontAttributes = FontAttributes.Bold });
        _items.Add(new Label { Text = "Elige qué versión conservar. Los registros de las dos operaciones se mantienen." });
        _items.Add(refresh);
        Content = new ScrollView { Content = _items };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        while (_items.Children.Count > 3) _items.Children.RemoveAt(3);
        IReadOnlyList<CoordinatorConflictItem> conflicts;
        try { conflicts = await _service.GetOpenAsync(); }
        catch { _items.Add(new Label { Text = "No se pudieron consultar los conflictos. Comprueba la conexión local." }); return; }
        if (conflicts.Count == 0)
        {
            _items.Add(new Label { Text = "No hay conflictos pendientes de revisión." });
            return;
        }
        foreach (var conflict in conflicts)
        {
            var card = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(12) };
            card.Add(new Label { Text = conflict.OperationType, FontAttributes = FontAttributes.Bold, FontSize = 18 });
            card.Add(new Label { Text = conflict.Message });
            card.Add(new Label { Text = conflict.IsLocalConflict ? "Versión actual de este dispositivo" : "Versión vigente en el coordinador", FontAttributes = FontAttributes.Bold });
            card.Add(new Label { Text = conflict.CurrentSummary });
            card.Add(new Label { Text = "Versión recibida", FontAttributes = FontAttributes.Bold });
            card.Add(new Label { Text = conflict.ProposedSummary });
            var keep = new Button { Text = conflict.IsLocalConflict ? "Conservar versión de este dispositivo" : "Conservar versión del coordinador" };
            keep.Clicked += async (_, _) => await ResolveAsync(conflict, applyIncoming: false);
            card.Add(keep);
            if (conflict.CanApplyIncoming)
            {
                var apply = new Button { Text = conflict.IsLocalConflict ? "Aplicar versión recibida del coordinador" : "Usar versión recibida" };
                apply.Clicked += async (_, _) => await ResolveAsync(conflict, applyIncoming: true);
                card.Add(apply);
            }
            _items.Add(new Border { Stroke = Colors.Gray, Padding = 1, Content = card });
        }
    }

    private async Task ResolveAsync(CoordinatorConflictItem conflict, bool applyIncoming)
    {
        var choice = applyIncoming ? "la versión recibida" : "la versión vigente del coordinador";
        if (!await DisplayAlertAsync("Confirmar resolución", $"¿Deseas conservar {choice}? El registro de la otra operación se mantendrá en el historial.", "Confirmar", "Cancelar")) return;
        string? error;
        try { error = await _service.ResolveAsync(conflict.OperationId, applyIncoming); }
        catch { error = "No se pudo guardar la resolución. Comprueba la conexión local."; }
        if (error is null) await DisplayAlertAsync("Conflicto resuelto", "La decisión quedó guardada.", "Aceptar");
        else await DisplayAlertAsync("No se pudo resolver", error, "Aceptar");
        await LoadAsync();
    }
}
