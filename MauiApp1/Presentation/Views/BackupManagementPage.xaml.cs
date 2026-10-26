using System.Collections.ObjectModel;
using System.Diagnostics;
using MauiApp1.Application.Authentication;
using MauiApp1.Application.Backups;
using MauiApp1.Configuration;
using MauiApp1.Domain.Users;

namespace MauiApp1.Presentation.Views;

public partial class BackupManagementPage : ContentPage
{
    private const string SelectedVolumePreferenceKey = "central-backup-usb-volume-id";
    private readonly IAuthenticationService _authentication;
    private readonly DesktopCoordinatorModeOptions _desktopMode;
    private readonly IBackupDestinationProvider _destinationProvider;
    private readonly ICentralDatabaseBackupService _backupService;
    private readonly ICoordinatorBackupScheduleStore _scheduleStore;
    private readonly ObservableCollection<BackupHistoryEntry> _backupHistory = [];
    private bool _isLoadingDestinations;
    private bool _isLoadingSchedule;
    private bool _isBusy;
    private string? _configuredTargetVolumeId;
    private BackupHistoryEntry? _selectedBackup;

    private sealed record DayOption(string DisplayName, DayOfWeek Day);

    public BackupManagementPage(
        IAuthenticationService authentication,
        DesktopCoordinatorModeOptions desktopMode,
        IBackupDestinationProvider destinationProvider,
        ICentralDatabaseBackupService backupService,
        ICoordinatorBackupScheduleStore scheduleStore)
    {
        InitializeComponent();
        _authentication = authentication;
        _desktopMode = desktopMode;
        _destinationProvider = destinationProvider;
        _backupService = backupService;
        _scheduleStore = scheduleStore;
        BindingContext = this;

        if (!IsAuthorized)
        {
            CreateBackupButton.IsEnabled = false;
            UsbPicker.IsEnabled = false;
            SaveScheduleButton.IsEnabled = false;
            ScheduleEnabledSwitch.IsEnabled = false;
            ValidateBackupButton.IsEnabled = false;
            OperationStatusLabel.Text = "Esta sección solo está disponible para el Administrador en la PC coordinadora.";
            OperationStatusLabel.IsVisible = true;
        }
    }

    public ObservableCollection<BackupHistoryEntry> BackupHistory => _backupHistory;

    private bool IsAuthorized => OperatingSystem.IsWindows() && _desktopMode.IsHost &&
                                 _authentication.CurrentUser?.Role == UserRole.Administrator;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!IsAuthorized)
        {
            await DisplayAlertAsync("Acceso restringido",
                "Los respaldos de la base central solo se administran como Administrador en la PC coordinadora.", "Aceptar");
            await ClosePageAsync();
            return;
        }

        await LoadScheduleAsync();
        await RefreshDestinationsAsync();
    }

    private async Task RefreshDestinationsAsync()
    {
        if (_isBusy || _isLoadingDestinations) return;
        _isLoadingDestinations = true;
        var currentlySelectedVolumeId = (UsbPicker.SelectedItem as BackupDestination)?.VolumeId;
        var preferredVolumeId = Preferences.Default.Get(SelectedVolumePreferenceKey, string.Empty);
        try
        {
            var destinations = await _destinationProvider.GetAvailableAsync();
            UsbPicker.ItemsSource = destinations.ToList();
            var schedule = await _scheduleStore.ReadSettingsAsync();
            var selected = destinations.FirstOrDefault(destination =>
                string.Equals(destination.VolumeId, currentlySelectedVolumeId, StringComparison.OrdinalIgnoreCase));
            selected ??= destinations.FirstOrDefault(destination =>
                string.Equals(destination.VolumeId, schedule?.TargetVolumeId, StringComparison.OrdinalIgnoreCase));
            selected ??= destinations.FirstOrDefault(destination =>
                string.Equals(destination.VolumeId, preferredVolumeId, StringComparison.OrdinalIgnoreCase));
            UsbPicker.SelectedItem = selected;
            CreateBackupButton.IsEnabled = selected is not null;
            OpenFolderButton.IsEnabled = selected is not null;

            if (selected is null)
            {
                _backupHistory.Clear();
                ScheduleTargetLabel.Text = string.IsNullOrWhiteSpace(schedule?.TargetVolumeId)
                    ? "Destino programado: no hay una memoria USB configurada."
                    : "Destino programado: la memoria USB guardada no está conectada. Si falta a la hora indicada, se omitirá ese intento y se probará en la siguiente fecha programada.";
                DestinationDetailLabel.Text = destinations.Count == 0
                    ? "No se detectó una memoria extraíble lista. Conecta una USB y pulsa «Actualizar unidades»."
                    : "Selecciona la memoria USB donde se guardarán las copias.";
                HistoryEmptyTitleLabel.Text = destinations.Count == 0
                    ? "No hay ninguna memoria USB lista."
                    : "Selecciona una USB para consultar el historial.";
                UpdateLatestBackupSummary();
                await RefreshScheduleStatusAsync();
                return;
            }

            Preferences.Default.Set(SelectedVolumePreferenceKey, selected.VolumeId);
            DestinationDetailLabel.Text = $"Disponible: {FormatBytes(selected.AvailableBytes)} de {FormatBytes(selected.TotalBytes)}. " +
                                          $"Ubicación: {Path.Combine(selected.MountPath, "Miscelanea Ari", "Respaldos")}";
            ScheduleTargetLabel.Text = string.Equals(schedule?.TargetVolumeId, selected.VolumeId, StringComparison.OrdinalIgnoreCase)
                ? $"USB programada: {selected.DisplayName}."
                : $"USB disponible: {selected.DisplayName}. Para usarla automáticamente, guarda la programación con esta unidad seleccionada.";
            await LoadHistoryAsync(selected);
            await RefreshScheduleStatusAsync();
        }
        catch (Exception exception)
        {
            _backupHistory.Clear();
            CreateBackupButton.IsEnabled = false;
            OpenFolderButton.IsEnabled = false;
            OperationStatusLabel.Text = GetUserError(exception, "No se pudieron consultar las memorias USB.");
            OperationStatusLabel.TextColor = Color.FromArgb("#B42318");
            OperationStatusLabel.IsVisible = true;
            HistoryEmptyTitleLabel.Text = "No se pudo consultar el historial.";
            UpdateLatestBackupSummary();
        }
        finally
        {
            _isLoadingDestinations = false;
        }
    }

    private async Task LoadHistoryAsync(BackupDestination destination)
    {
        _selectedBackup = null;
        BackupHistoryView.SelectedItem = null;
        ValidateBackupButton.IsEnabled = false;
        BackupValidationLabel.IsVisible = false;
        SelectedBackupLabel.Text = "Selecciona una copia válida para comprobarla antes de restaurar.";
        _backupHistory.Clear();
        var entries = await _backupService.GetHistoryAsync(destination);
        foreach (var entry in entries)
            _backupHistory.Add(entry);

        HistoryEmptyTitleLabel.Text = entries.Count == 0
            ? "Esta USB aún no contiene respaldos de Miscelánea Ari."
            : "No hay respaldos disponibles.";
        UpdateLatestBackupSummary();
    }

    private void OnBackupSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        _selectedBackup = eventArgs.CurrentSelection.FirstOrDefault() as BackupHistoryEntry;
        ValidateBackupButton.IsEnabled = IsAuthorized && _selectedBackup?.Status == "Creado y verificado";
        BackupValidationLabel.IsVisible = false;
        SelectedBackupLabel.Text = _selectedBackup is null
            ? "Selecciona una copia válida para comprobarla antes de restaurar."
            : $"Copia seleccionada: {_selectedBackup.FileName} · " +
              (_selectedBackup.CreatedAtUtc?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "fecha desconocida");
    }

    private async void OnValidateBackupClicked(object? sender, EventArgs eventArgs)
    {
        if (!IsAuthorized || _selectedBackup is not { Status: "Creado y verificado" } selected ||
            UsbPicker.SelectedItem is not BackupDestination destination)
            return;

        ValidateBackupButton.IsEnabled = false;
        BackupValidationLabel.Text = "Comprobando ficha, hash, integridad SQLite y esquema…";
        BackupValidationLabel.TextColor = Color.FromArgb("#61738A");
        BackupValidationLabel.IsVisible = true;
        try
        {
            var result = await _backupService.ValidateAsync(
                _desktopMode.GetCentralDatabasePath(), destination, selected.FileName);
            BackupValidationLabel.Text = result.Message +
                (result.BackupSchemaVersion is not null && result.CurrentSchemaVersion is not null
                    ? $" Esquema de copia: {result.BackupSchemaVersion}. Esquema actual: {result.CurrentSchemaVersion}."
                    : string.Empty) +
                (result.IsValid && result.IsSchemaCompatible
                    ? " La restauración seguirá deshabilitada hasta completar la pausa segura del coordinador."
                    : string.Empty);
            BackupValidationLabel.TextColor = result.IsValid && result.IsSchemaCompatible
                ? Color.FromArgb("#07805F")
                : Color.FromArgb("#B42318");
        }
        catch (Exception exception)
        {
            BackupValidationLabel.Text = GetUserError(exception, "No se pudo validar esta copia.");
            BackupValidationLabel.TextColor = Color.FromArgb("#B42318");
        }
        finally
        {
            BackupValidationLabel.IsVisible = true;
            ValidateBackupButton.IsEnabled = IsAuthorized && _selectedBackup?.Status == "Creado y verificado";
        }
    }

    private void UpdateLatestBackupSummary()
    {
        var latest = _backupHistory.FirstOrDefault(entry => entry.Status == "Creado y verificado");
        if (latest is null)
        {
            LastBackupDateLabel.Text = "Aún no hay respaldos válidos";
            LastBackupSizeLabel.Text = _backupHistory.Count == 0
                ? "Conecta una USB para guardar la base central."
                : "El historial contiene archivos que requieren revisión.";
            LastBackupStatusLabel.Text = _backupHistory.Any(entry => entry.Status is "Revisar" or "Incompleto")
                ? "Revisar historial"
                : "Sin respaldo";
            LastBackupStatusLabel.TextColor = _backupHistory.Any(entry => entry.Status is "Revisar" or "Incompleto")
                ? Color.FromArgb("#B54708")
                : Color.FromArgb("#61738A");
            return;
        }

        LastBackupDateLabel.Text = latest.CreatedAtUtc?.ToLocalTime()
            .ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.CurrentCulture) ?? "Fecha desconocida";
        LastBackupSizeLabel.Text = $"{FormatBytes(latest.SizeBytes)} · {latest.FileName}";
        LastBackupStatusLabel.Text = "Correcto";
        LastBackupStatusLabel.TextColor = Color.FromArgb("#07805F");
    }

    private async void OnUsbSelectionChanged(object? sender, EventArgs eventArgs)
    {
        if (_isLoadingDestinations || _isBusy) return;
        if (UsbPicker.SelectedItem is not BackupDestination destination)
        {
            CreateBackupButton.IsEnabled = false;
            OpenFolderButton.IsEnabled = false;
            _backupHistory.Clear();
            UpdateLatestBackupSummary();
            return;
        }

        Preferences.Default.Set(SelectedVolumePreferenceKey, destination.VolumeId);
        CreateBackupButton.IsEnabled = IsAuthorized;
        OpenFolderButton.IsEnabled = true;
        DestinationDetailLabel.Text = $"Disponible: {FormatBytes(destination.AvailableBytes)} de {FormatBytes(destination.TotalBytes)}. " +
                                      $"Ubicación: {Path.Combine(destination.MountPath, "Miscelanea Ari", "Respaldos")}";
        OperationStatusLabel.IsVisible = false;
        try
        {
            await LoadHistoryAsync(destination);
        }
        catch (Exception exception)
        {
            _backupHistory.Clear();
            HistoryEmptyTitleLabel.Text = GetUserError(exception, "No se pudo leer el historial de la USB.");
            UpdateLatestBackupSummary();
        }
    }

    private async void OnCreateBackupClicked(object? sender, EventArgs eventArgs)
    {
        if (!IsAuthorized)
        {
            await DisplayAlertAsync("Acceso restringido", "Solo el Administrador de la PC coordinadora puede crear respaldos.", "Aceptar");
            return;
        }
        if (UsbPicker.SelectedItem is not BackupDestination destination)
        {
            OperationStatusLabel.Text = "Conecta y selecciona una memoria USB antes de crear el respaldo.";
            OperationStatusLabel.TextColor = Color.FromArgb("#B42318");
            OperationStatusLabel.IsVisible = true;
            return;
        }

        SetBusy(true);
        OperationStatusLabel.Text = "Creando y verificando la copia en la memoria USB…";
        OperationStatusLabel.TextColor = Color.FromArgb("#07805F");
        OperationStatusLabel.IsVisible = true;
        try
        {
            var backup = await _backupService.CreateAsync(_desktopMode.GetCentralDatabasePath(), destination);
            OperationStatusLabel.Text = $"Respaldo creado y verificado: {FormatBytes(backup.SizeBytes)}. " +
                                        $"Archivo: {backup.DatabasePath}";
            OperationStatusLabel.TextColor = Color.FromArgb("#07805F");
            await LoadHistoryAsync(destination);
        }
        catch (Exception exception)
        {
            OperationStatusLabel.Text = GetUserError(exception, "No se pudo crear el respaldo.");
            OperationStatusLabel.TextColor = Color.FromArgb("#B42318");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnRefreshClicked(object? sender, EventArgs eventArgs) => await RefreshDestinationsAsync();

    private async void OnOpenFolderClicked(object? sender, EventArgs eventArgs)
    {
        if (!IsAuthorized || UsbPicker.SelectedItem is not BackupDestination destination)
            return;

        try
        {
            var currentDestination = await _destinationProvider.ResolveAsync(destination.VolumeId);
            if (currentDestination is null)
                throw new IOException("La memoria USB seleccionada ya no está conectada.");
            var folder = Path.Combine(currentDestination.MountPath, "Miscelanea Ari", "Respaldos");
            if (!Directory.Exists(folder))
                folder = currentDestination.MountPath;
#if WINDOWS
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
#endif
        }
        catch (Exception exception)
        {
            OperationStatusLabel.Text = GetUserError(exception, "No se pudo abrir la carpeta de respaldos.");
            OperationStatusLabel.TextColor = Color.FromArgb("#B42318");
            OperationStatusLabel.IsVisible = true;
        }
    }

    private void SetBusy(bool value)
    {
        _isBusy = value;
        UsbPicker.IsEnabled = !value;
        CreateBackupButton.IsEnabled = !value && UsbPicker.SelectedItem is BackupDestination;
        OpenFolderButton.IsEnabled = !value && UsbPicker.SelectedItem is BackupDestination;
        BackupActivityIndicator.IsVisible = value;
        BackupActivityIndicator.IsRunning = value;
        CreateBackupButton.Text = value ? "Creando respaldo…" : "Crear respaldo";
    }

    private async Task LoadScheduleAsync()
    {
        _isLoadingSchedule = true;
        try
        {
            ScheduleFrequencyPicker.ItemsSource = new[] { "Diaria", "Semanal", "Mensual" };
            var dayOptions = new[]
            {
                new DayOption("Domingo", DayOfWeek.Sunday),
                new DayOption("Lunes", DayOfWeek.Monday),
                new DayOption("Martes", DayOfWeek.Tuesday),
                new DayOption("Miércoles", DayOfWeek.Wednesday),
                new DayOption("Jueves", DayOfWeek.Thursday),
                new DayOption("Viernes", DayOfWeek.Friday),
                new DayOption("Sábado", DayOfWeek.Saturday)
            };
            ScheduleWeeklyDayPicker.ItemsSource = dayOptions;
            ScheduleMonthlyDayPicker.ItemsSource = Enumerable.Range(1, 28).Select(day => day.ToString()).ToArray();

            var settings = await _scheduleStore.ReadSettingsAsync();
            _configuredTargetVolumeId = settings?.TargetVolumeId;
            ScheduleFrequencyPicker.SelectedIndex = settings?.Frequency switch
            {
                BackupScheduleFrequency.Weekly => 1,
                BackupScheduleFrequency.Monthly => 2,
                _ => 0
            };
            ScheduleTimePicker.Time = settings?.LocalTime.ToTimeSpan() ?? new TimeSpan(2, 0, 0);
            ScheduleEnabledSwitch.IsToggled = settings?.IsEnabled ?? false;
            ScheduleWeeklyDayPicker.SelectedItem = dayOptions
                .FirstOrDefault(option => option.Day == (settings?.WeeklyDay ?? DayOfWeek.Sunday));
            ScheduleMonthlyDayPicker.SelectedIndex = Math.Clamp((settings?.MonthlyDay ?? 1) - 1, 0, 27);
            UpdateScheduleFields();

            ScheduleStatusLabel.Text = settings?.IsEnabled == true
                ? "Programación activada. El servicio del coordinador ejecutará el respaldo sin sesión abierta."
                : "Programación desactivada.";
            await RefreshScheduleStatusAsync();
        }
        catch (Exception exception)
        {
            ScheduleStatusLabel.Text = GetUserError(exception, "No se pudo leer la programación guardada.");
            ScheduleStatusLabel.TextColor = Color.FromArgb("#B42318");
        }
        finally
        {
            _isLoadingSchedule = false;
        }
    }

    private async Task RefreshScheduleStatusAsync()
    {
        try
        {
            var settings = await _scheduleStore.ReadSettingsAsync();
            var status = await _scheduleStore.ReadLastRunAsync();
            if (status is not null)
            {
                var time = status.LastFinishedAtUtc?.ToLocalTime() ?? status.LastAttemptedAtUtc.ToLocalTime();
                ScheduleStatusLabel.Text = $"Último intento: {time:dd/MM/yyyy HH:mm}. {status.Message}";
                ScheduleStatusLabel.TextColor = status.Result switch
                {
                    BackupScheduleRunResult.Completed => Color.FromArgb("#07805F"),
                    BackupScheduleRunResult.Omitted => Color.FromArgb("#B54708"),
                    BackupScheduleRunResult.Failed => Color.FromArgb("#B42318"),
                    _ => Color.FromArgb("#61738A")
                };
            }
            UpdateNextScheduledRun(settings);
        }
        catch (Exception exception)
        {
            ScheduleStatusLabel.Text = GetUserError(exception, "No se pudo consultar el estado de la programación.");
            ScheduleStatusLabel.TextColor = Color.FromArgb("#B42318");
        }
    }

    private void OnScheduleFrequencyChanged(object? sender, EventArgs eventArgs)
    {
        if (_isLoadingSchedule) return;
        UpdateScheduleFields();
        UpdateNextScheduledRun(CreateScheduleSettings(requireDestination: false));
    }

    private void UpdateScheduleFields()
    {
        WeeklyDayField.IsVisible = ScheduleFrequencyPicker.SelectedIndex == 1;
        MonthlyDayField.IsVisible = ScheduleFrequencyPicker.SelectedIndex == 2;
    }

    private async void OnSaveScheduleClicked(object? sender, EventArgs eventArgs)
    {
        if (!IsAuthorized) return;
        try
        {
            var settings = CreateScheduleSettings(requireDestination: ScheduleEnabledSwitch.IsToggled);
            await _scheduleStore.SaveSettingsAsync(settings);
            _configuredTargetVolumeId = settings.TargetVolumeId;
            ScheduleStatusLabel.Text = settings.IsEnabled
                ? "Programación guardada. El servicio del coordinador la ejecutará aunque cierres la aplicación."
                : "Programación desactivada y guardada.";
            ScheduleStatusLabel.TextColor = Color.FromArgb("#07805F");
            if (!string.IsNullOrWhiteSpace(settings.TargetVolumeId))
                Preferences.Default.Set(SelectedVolumePreferenceKey, settings.TargetVolumeId);
            ScheduleTargetLabel.Text = UsbPicker.SelectedItem is BackupDestination target
                ? $"USB programada: {target.DisplayName}. Si se desconecta a la hora indicada, el respaldo se marcará como omitido."
                : "La USB guardada no está conectada. Este intento se omitirá; el próximo será en la siguiente fecha programada.";
            UpdateNextScheduledRun(settings);
        }
        catch (Exception exception)
        {
            ScheduleStatusLabel.Text = GetUserError(exception, "No se pudo guardar la programación.");
            ScheduleStatusLabel.TextColor = Color.FromArgb("#B42318");
        }
    }

    private BackupScheduleSettings CreateScheduleSettings(bool requireDestination)
    {
        if (ScheduleFrequencyPicker.SelectedIndex < 0)
            throw new InvalidOperationException("Selecciona la frecuencia del respaldo.");
        var frequency = ScheduleFrequencyPicker.SelectedIndex switch
        {
            1 => BackupScheduleFrequency.Weekly,
            2 => BackupScheduleFrequency.Monthly,
            _ => BackupScheduleFrequency.Daily
        };
        var day = ScheduleWeeklyDayPicker.SelectedItem is DayOption option ? option.Day : DayOfWeek.Sunday;
        var monthlyDay = int.TryParse(ScheduleMonthlyDayPicker.SelectedItem?.ToString(), out var selectedDay)
            ? selectedDay
            : 1;
        var targetVolumeId = (UsbPicker.SelectedItem as BackupDestination)?.VolumeId;
        if (string.IsNullOrWhiteSpace(targetVolumeId))
            targetVolumeId = _configuredTargetVolumeId;
        if (requireDestination && string.IsNullOrWhiteSpace(targetVolumeId))
            throw new InvalidOperationException("Conecta y selecciona una memoria USB antes de activar la programación.");

        return new BackupScheduleSettings(ScheduleEnabledSwitch.IsToggled, frequency,
            TimeOnly.FromTimeSpan(ScheduleTimePicker.Time ?? new TimeSpan(2, 0, 0)),
            day, monthlyDay, targetVolumeId);
    }

    private void UpdateNextScheduledRun(BackupScheduleSettings? settings)
    {
        if (settings is null || !settings.IsEnabled)
        {
            NextScheduledRunLabel.Text = "Próximo respaldo: programación desactivada.";
            return;
        }

        var now = DateTime.Now;
        var candidate = now.Date.Add(settings.LocalTime.ToTimeSpan());
        if (settings.Frequency == BackupScheduleFrequency.Daily)
        {
            if (candidate <= now) candidate = candidate.AddDays(1);
        }
        else if (settings.Frequency == BackupScheduleFrequency.Weekly)
        {
            var days = ((int)settings.WeeklyDay - (int)candidate.DayOfWeek + 7) % 7;
            candidate = candidate.AddDays(days);
            if (candidate <= now) candidate = candidate.AddDays(7);
        }
        else
        {
            candidate = new DateTime(now.Year, now.Month, settings.MonthlyDay).Add(settings.LocalTime.ToTimeSpan());
            if (candidate <= now)
            {
                var nextMonth = now.AddMonths(1);
                candidate = new DateTime(nextMonth.Year, nextMonth.Month, settings.MonthlyDay).Add(settings.LocalTime.ToTimeSpan());
            }
        }

        NextScheduledRunLabel.Text = $"Próximo intento: {candidate:dd/MM/yyyy HH:mm} (hora de Windows). Si no hay USB, se omitirá.";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = Math.Max(0, bytes);
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:N1} {units[unit]}";
    }

    private static string GetUserError(Exception exception, string fallback) => exception switch
    {
        UnauthorizedAccessException => "Windows no permite escribir en la memoria USB. Revisa sus permisos o usa otra unidad.",
        IOException => exception.Message,
        InvalidOperationException => exception.Message,
        OperationCanceledException => "La operación se canceló.",
        _ => fallback
    };

    private async void OnBackClicked(object? sender, EventArgs eventArgs) => await ClosePageAsync();

    private async Task ClosePageAsync()
    {
        if (Navigation.ModalStack.Count > 0) await Navigation.PopModalAsync();
        else if (Navigation.NavigationStack.Count > 1) await Navigation.PopAsync();
    }
}
