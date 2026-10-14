using MauiApp1.Application.Sync;
using MauiApp1.Domain.Users;
using Microsoft.Maui.Controls.Shapes;

namespace MauiApp1.Presentation.Views;

public sealed class InitialDataImportPage : ContentPage
{
    private readonly ILocalInitialDataImportService _importService;
    private readonly VerticalStackLayout _content = new() { Spacing = 16, Padding = new Thickness(18, 20) };
    private readonly VerticalStackLayout _reviewContent = new() { Spacing = 12 };
    private readonly Label _statusLabel = new() { FontSize = 14, TextColor = Color.FromArgb("#52657D") };
    private readonly Button _analyzeButton = CreateSecondaryButton("Analizar datos de este dispositivo");
    private readonly Button _applyButton = CreatePrimaryButton("Confirmar importación");
    private readonly List<(InitialImportActorMappingOption Actor, Picker Picker)> _actorPickers = [];
    private readonly List<(InitialImportStockConflict Product, Picker Picker)> _stockPickers = [];
    private InitialImportPreview? _preview;
    private bool _isCompleted;

    public InitialDataImportPage(ILocalInitialDataImportService importService)
    {
        _importService = importService;
        Title = "Importar datos previos";
        BackgroundColor = Color.FromArgb("#F7F9FB");
        _applyButton.IsEnabled = false;
        _analyzeButton.Clicked += OnAnalyzeClicked;
        _applyButton.Clicked += OnApplyClicked;
        var back = CreateSecondaryButton("← Volver a sincronización");
        back.HorizontalOptions = LayoutOptions.Start;
        back.Clicked += async (_, _) => await Navigation.PopModalAsync();

        _content.Add(back);
        _content.Add(new Label { Text = "Importar datos previos", FontSize = 28, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#111D31") });
        _content.Add(new Label
        {
            Text = "Usa esta opción si este dispositivo ya tenía información antes de vincularse. Se revisará el catálogo, las existencias y el historial antes de enviarlo al coordinador.",
            FontSize = 14, TextColor = Color.FromArgb("#61738A")
        });
        _content.Add(new Border
        {
            BackgroundColor = Color.FromArgb("#EEF8F5"), Stroke = Color.FromArgb("#CBEADF"),
            StrokeShape = new RoundRectangle { CornerRadius = 12 }, Padding = 14,
            Content = new Label
            {
                Text = "La instalación limpia no necesita importar datos. Esta acción no copia cuentas ni contraseñas; requiere conexión local y una sesión central de Administrador.",
                FontSize = 13, TextColor = Color.FromArgb("#255E50")
            }
        });
        _content.Add(_analyzeButton);
        _content.Add(_statusLabel);
        _content.Add(_reviewContent);
        _content.Add(_applyButton);
        Content = new ScrollView { Content = _content };
    }

    private async void OnAnalyzeClicked(object? sender, EventArgs e)
    {
        _analyzeButton.IsEnabled = false;
        _applyButton.IsEnabled = false;
        _analyzeButton.Text = "Analizando…";
        _preview = null;
        _isCompleted = false;
        _actorPickers.Clear();
        _stockPickers.Clear();
        _reviewContent.Children.Clear();
        _statusLabel.Text = "Preparando la vista previa segura…";
        try
        {
            var result = await _importService.PreviewCurrentDeviceAsync();
            if (!result.Succeeded || result.Value is null)
            {
                _statusLabel.Text = result.ErrorMessage ?? "No se pudo analizar la información.";
                return;
            }
            _preview = result.Value;
            BuildReview(_preview);
            _statusLabel.Text = _preview.Blockers.Count > 0
                ? "Hay datos que requieren revisión antes de importar. No se ha modificado ninguna base."
                : "Revisa el resumen, las cuentas responsables y los saldos antes de confirmar.";
            UpdateApplyEnabled();
        }
        catch
        {
            _statusLabel.Text = "No se pudo completar el análisis. No se modificaron los datos locales ni centrales.";
        }
        finally
        {
            _analyzeButton.Text = "Analizar datos de este dispositivo";
            _analyzeButton.IsEnabled = true;
        }
    }

    private void BuildReview(InitialImportPreview preview)
    {
        var summary = new VerticalStackLayout { Spacing = 6 };
        summary.Add(new Label { Text = "Resumen encontrado", FontSize = 19, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#111D31") });
        summary.Add(new Label
        {
            Text = $"Dispositivo: {preview.SourceDeviceId:D}\nCaptura: {preview.CapturedAtUtc.ToLocalTime():g}\n" +
                   $"Categorías: {preview.CategoryCount} · Productos: {preview.ProductCount} · Clientes: {preview.CustomerCount}\n" +
                   $"Compras: {preview.PurchaseCount} · Ventas: {preview.SaleCount} · Movimientos: {preview.MovementCount}\n" +
                   $"Cuentas por cobrar: {preview.ReceivableCount} · Pagos: {preview.PaymentCount} · Diferencias: {preview.DiscrepancyCount}",
            FontSize = 14, TextColor = Color.FromArgb("#52657D")
        });
        summary.Add(new Label
        {
            Text = $"Coincidencias exactas que se omitirán sin duplicar: {preview.ExactMatchCount}. Las existencias se tratarán como saldos actuales; nunca se sumarán automáticamente.",
            FontSize = 13, TextColor = Color.FromArgb("#255E50")
        });
        AddCard(summary);

        if (preview.Blockers.Count > 0)
        {
            var issues = new VerticalStackLayout { Spacing = 7 };
            issues.Add(new Label { Text = "Requiere revisión", FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#B42318") });
            foreach (var item in preview.Blockers)
                issues.Add(new Label { Text = "• " + item, FontSize = 13, TextColor = Color.FromArgb("#8C2D25") });
            AddCard(issues, "#FFF7F5", "#F2C8C2");
        }
        if (preview.Warnings.Count > 0)
        {
            var warnings = new VerticalStackLayout { Spacing = 7 };
            warnings.Add(new Label { Text = "Avisos", FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#8A4B08") });
            foreach (var item in preview.Warnings)
                warnings.Add(new Label { Text = "• " + item, FontSize = 13, TextColor = Color.FromArgb("#704214") });
            AddCard(warnings, "#FFFBF3", "#F2DEB9");
        }

        if (preview.Actors.Count > 0)
        {
            var actors = new VerticalStackLayout { Spacing = 10 };
            actors.Add(new Label { Text = "Responsables del historial", FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#111D31") });
            actors.Add(new Label { Text = "Indica qué cuenta central corresponde a cada autor local. La selección se usará para conservar quién realizó cada operación.", FontSize = 13, TextColor = Color.FromArgb("#61738A") });
            if (preview.CoordinatorUsers.Count == 0)
                actors.Add(new Label { Text = "No hay cuentas centrales disponibles para asignar. Crea o habilita una cuenta antes de continuar.", TextColor = Color.FromArgb("#B42318") });
            foreach (var actor in preview.Actors)
            {
                var roleName = Enum.IsDefined(typeof(UserRole), actor.Role) ? ((UserRole)actor.Role).ToString() : "Usuario";
                var picker = new Picker { Title = "Selecciona una cuenta central", BackgroundColor = Color.FromArgb("#F7F9FB") };
                foreach (var user in preview.CoordinatorUsers)
                    picker.Items.Add($"{user.UserName} · {RoleName(user.Role)}");
                if (actor.SuggestedCoordinatorUserId is Guid suggested)
                    picker.SelectedIndex = preview.CoordinatorUsers.ToList().FindIndex(item => item.UserId == suggested);
                picker.SelectedIndexChanged += (_, _) => UpdateApplyEnabled();
                _actorPickers.Add((actor, picker));
                actors.Add(new Label { Text = $"{actor.UserName} · {roleName}", FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#26384D") });
                actors.Add(picker);
            }
            AddCard(actors);
        }

        if (preview.StockConflicts.Count > 0)
        {
            var stocks = new VerticalStackLayout { Spacing = 10 };
            stocks.Add(new Label { Text = "Existencias diferentes", FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#111D31") });
            stocks.Add(new Label { Text = "Elige un saldo para cada producto. El saldo seleccionado será el que reciban los dispositivos vinculados.", FontSize = 13, TextColor = Color.FromArgb("#61738A") });
            foreach (var product in preview.StockConflicts)
            {
                var picker = new Picker { Title = "Selecciona el saldo que se conservará", BackgroundColor = Color.FromArgb("#F7F9FB") };
                picker.Items.Add($"Conservar coordinador · {FormatQuantity(product.CoordinatorStockMilliUnits)}");
                picker.Items.Add($"Usar este dispositivo · {FormatQuantity(product.SourceStockMilliUnits)}");
                picker.SelectedIndexChanged += (_, _) => UpdateApplyEnabled();
                _stockPickers.Add((product, picker));
                stocks.Add(new Label { Text = product.ProductName, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#26384D") });
                stocks.Add(picker);
            }
            AddCard(stocks);
        }
    }

    private async void OnApplyClicked(object? sender, EventArgs e)
    {
        if (_preview is null || !CanApply()) return;
        var accepted = await DisplayAlertAsync("Confirmar importación",
            "Se incorporarán los datos revisados al coordinador y se publicarán para los demás dispositivos. El sistema creará un respaldo antes de aplicar el primer registro. ¿Continuar?",
            "Importar", "Cancelar");
        if (!accepted) return;

        _applyButton.IsEnabled = false;
        _analyzeButton.IsEnabled = false;
        _applyButton.Text = "Importando…";
        try
        {
            var request = new InitialImportApplyRequest(_preview.BatchId, _preview.SnapshotHash,
                _actorPickers.Select(item => new InitialImportActorMapping(item.Actor.LocalUserId,
                    _preview.CoordinatorUsers[item.Picker.SelectedIndex].UserId)).ToArray(),
                _stockPickers.Select(item => new InitialImportStockChoice(item.Product.ProductId,
                    item.Picker.SelectedIndex == 1)).ToArray());
            var result = await _importService.ApplyAsync(request);
            _statusLabel.Text = result.Value?.Message ?? result.ErrorMessage ?? "No se pudo completar la importación.";
            _isCompleted = result.Value?.Completed == true;
            _applyButton.Text = result.Value?.Completed == true ? "Importación completada" : "Reintentar importación";
        }
        catch
        {
            _statusLabel.Text = "Se interrumpió la conexión. Puedes reintentar la misma importación sin duplicar los registros ya confirmados.";
            _applyButton.Text = "Reintentar importación";
        }
        finally
        {
            _analyzeButton.IsEnabled = true;
            UpdateApplyEnabled();
        }
    }

    private void AddCard(View content, string background = "#FFFFFF", string stroke = "#E3EAF2") =>
        _reviewContent.Add(new Border
        {
            BackgroundColor = Color.FromArgb(background), Stroke = Color.FromArgb(stroke), StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 14 }, Padding = 16, Content = content
        });

    private bool CanApply() => _preview is { Blockers.Count: 0 } &&
        _actorPickers.All(item => item.Picker.SelectedIndex >= 0) &&
        _stockPickers.All(item => item.Picker.SelectedIndex >= 0) &&
        _actorPickers.All(item => item.Picker.Items.Count > item.Picker.SelectedIndex);

    private void UpdateApplyEnabled() => _applyButton.IsEnabled = !_isCompleted && CanApply();

    private static string RoleName(int role) => Enum.IsDefined(typeof(UserRole), role) ? ((UserRole)role).ToString() : "Usuario";
    private static string FormatQuantity(long milliUnits) => (milliUnits / 1000m).ToString("0.###", System.Globalization.CultureInfo.CurrentCulture);

    private static Button CreatePrimaryButton(string text) => new()
    {
        Text = text, BackgroundColor = Color.FromArgb("#008060"), TextColor = Colors.White,
        CornerRadius = 11, FontAttributes = FontAttributes.Bold, Padding = new Thickness(18, 13)
    };

    private static Button CreateSecondaryButton(string text) => new()
    {
        Text = text, BackgroundColor = Color.FromArgb("#E8EEF5"), TextColor = Color.FromArgb("#0D1B2A"),
        CornerRadius = 11, Padding = new Thickness(18, 13)
    };
}
