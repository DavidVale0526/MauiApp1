using MauiApp1.Application.Sync;
using Microsoft.Maui.Controls.Shapes;

namespace MauiApp1.Presentation.Views;

public sealed class ConflictReviewPage : CoordinatorRefreshPage
{
    private readonly ICoordinatorConflictReviewService _service;
    private readonly ILocalOperationSyncService _operationSyncService;
    private readonly VerticalStackLayout _items = new() { Spacing = 16, Padding = new Thickness(16, 20) };
    private readonly Label _queueStatus = new() { FontSize = 13, TextColor = Colors.Gray };

    public ConflictReviewPage(ICoordinatorConflictReviewService service, ILocalOperationSyncService operationSyncService,
        ILocalDataChangeNotifier changeNotifier) : base(changeNotifier)
    {
        _service = service;
        _operationSyncService = operationSyncService;
        Title = "Revisión de conflictos";
        BackgroundColor = Color.FromArgb("#F7F9FB");
        var back = new Button { Text = "← Volver a sincronización", HorizontalOptions = LayoutOptions.Start, BackgroundColor = Color.FromArgb("#E8EEF5"), TextColor = Color.FromArgb("#0D1B2A"), CornerRadius = 11, Padding = new Thickness(18, 12) };
        back.Clicked += async (_, _) => await Navigation.PopModalAsync();
        var refresh = CreateSecondaryButton("Actualizar conflictos");
        refresh.Clicked += async (_, _) => await LoadAsync();
        var retry = CreatePrimaryButton("Reintentar sincronización");
        retry.Clicked += async (_, _) => await RetrySyncAsync();
        _items.Add(back);
        _items.Add(new Label { Text = "Cambios simultáneos", FontSize = 28, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#111D31") });
        _items.Add(new Label { Text = "Aquí aparecen cambios simultáneos que el coordinador ya recibió. Las operaciones pendientes de envío se muestran aparte; por sí solas no son conflictos.", FontSize = 14, TextColor = Color.FromArgb("#61738A") });
        _items.Add(refresh);
        _items.Add(retry);
        _items.Add(_queueStatus);
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
        while (_items.Children.Count > 6) _items.Children.RemoveAt(6);
        try
        {
            var status = await _operationSyncService.GetStatusAsync();
            _queueStatus.Text = $"Operaciones pendientes de envío: {status.PendingCount}." +
                (status.LatestConflictMessage is null ? string.Empty : $"\nDetalle: {status.LatestConflictMessage}");
        }
        catch
        {
            _queueStatus.Text = "No se pudo consultar la cola local.";
        }
        IReadOnlyList<CoordinatorConflictItem> conflicts;
        try { conflicts = await _service.GetOpenAsync(); }
        catch (InvalidOperationException exception) { _items.Add(CreateMessageLabel(exception.Message)); return; }
        catch { _items.Add(CreateMessageLabel("No se pudieron consultar los conflictos. Comprueba la conexión local.")); return; }
        if (conflicts.Count == 0)
        {
            _items.Add(new Border { Padding = 16, BackgroundColor = Colors.White, Stroke = Color.FromArgb("#E3EAF2"), StrokeShape = new RoundRectangle { CornerRadius = 12 }, Content = CreateMessageLabel("No hay conflictos pendientes de revisión.") });
            return;
        }
        foreach (var conflict in conflicts)
        {
            var card = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(12) };
            card.Add(new Label { Text = conflict.OperationType, FontAttributes = FontAttributes.Bold, FontSize = 18, TextColor = Color.FromArgb("#111D31") });
            card.Add(new Label { Text = conflict.Message, FontSize = 13, TextColor = Color.FromArgb("#61738A") });
            card.Add(new Label { Text = conflict.IsLocalConflict ? "Versión actual de este dispositivo" : "Versión vigente en el coordinador", FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#52657D") });
            card.Add(new Label { Text = conflict.CurrentSummary, TextColor = Color.FromArgb("#111D31") });
            card.Add(new Label { Text = "Versión recibida", FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#52657D") });
            card.Add(new Label { Text = conflict.ProposedSummary, TextColor = Color.FromArgb("#111D31") });
            var keep = CreateSecondaryButton(conflict.IsLocalConflict ? "Conservar versión de este dispositivo" : "Conservar versión del coordinador");
            keep.Clicked += async (_, _) => await ResolveAsync(conflict, applyIncoming: false);
            card.Add(keep);
            if (conflict.CanApplyIncoming)
            {
                var apply = CreatePrimaryButton(conflict.IsLocalConflict ? "Aplicar versión recibida del coordinador" : "Usar versión recibida");
                apply.Clicked += async (_, _) => await ResolveAsync(conflict, applyIncoming: true);
                card.Add(apply);
            }
            _items.Add(new Border { BackgroundColor = Colors.White, Stroke = Color.FromArgb("#E3EAF2"), StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 14 }, Padding = 1, Content = card });
        }
    }

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

    private static Label CreateMessageLabel(string text) => new()
    {
        Text = text,
        FontSize = 14,
        TextColor = Color.FromArgb("#61738A")
    };

    private async Task RetrySyncAsync()
    {
        try { await _operationSyncService.SynchronizeAsync(); }
        catch
        {
            await DisplayAlertAsync("Sincronización", "No se pudo completar el reintento. Las operaciones siguen guardadas en este dispositivo.", "Aceptar");
        }
        await LoadAsync();
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
