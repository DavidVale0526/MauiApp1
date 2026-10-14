using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;

namespace MauiApp1.Setup;

internal sealed class SetupForm : Form
{
    private const string ProductDisplayName = "Miscelánea Ari";
    private const string ServiceName = "MiscelaneaAriCoordinator";
    private readonly RadioButton _clientRole = new()
    {
        Text = "Conectar esta PC a un coordinador existente",
        Checked = true,
        AutoSize = true,
        Font = new Font("Segoe UI", 11, FontStyle.Bold),
        ForeColor = Color.FromArgb(17, 29, 49)
    };
    private readonly RadioButton _hostRole = new()
    {
        Text = "Usar esta PC como coordinador del negocio",
        AutoSize = true,
        Font = new Font("Segoe UI", 11, FontStyle.Bold),
        ForeColor = Color.FromArgb(17, 29, 49)
    };
    private readonly RadioButton _newBusiness = new()
    {
        Text = "Es un negocio nuevo; crear una base vacía",
        Checked = true,
        AutoSize = true
    };
    private readonly RadioButton _importBusiness = new()
    {
        Text = "Conservar datos de una base existente de Miscelánea Ari",
        AutoSize = true
    };
    private readonly TextBox _databasePath = new() { ReadOnly = true, Width = 385, Enabled = false };
    private readonly Button _browseDatabase = new() { Text = "Elegir archivo…", Enabled = false, AutoSize = true };
    private readonly Panel _hostOptions = new() { Visible = false, Location = new Point(18, 98), Height = 132, Width = 584 };
    private readonly Label _status = new()
    {
        AutoSize = false,
        Height = 62,
        Dock = DockStyle.Fill,
        ForeColor = Color.FromArgb(83, 101, 125),
        Text = "Elige cómo se usará esta PC. Puedes cambiar de opinión antes de instalar."
    };
    private readonly Button _install = new()
    {
        Text = "Instalar",
        Width = 180,
        Height = 46,
        BackColor = Color.FromArgb(0, 128, 96),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        Font = new Font("Segoe UI", 10, FontStyle.Bold),
        Anchor = AnchorStyles.Right | AnchorStyles.Bottom
    };
    private readonly ProgressBar _progress = new() { Visible = false, Style = ProgressBarStyle.Marquee, Dock = DockStyle.Bottom, Height = 8 };
    private readonly bool _elevatedHostFlow;
    private string? _importSource;
    private readonly string? _applicationUser;
    private readonly string? _applicationDirectory;
    private readonly bool _coordinatorAlreadyInstalled;
    private bool _busy;

    public SetupForm(string[] args)
    {
        _elevatedHostFlow = args.Contains("--install-host", StringComparer.OrdinalIgnoreCase);
        _importSource = ReadArgument(args, "--source-database");
        _applicationUser = ReadArgument(args, "--application-user");
        _applicationDirectory = ReadArgument(args, "--application-directory");
        _coordinatorAlreadyInstalled = IsCoordinatorServiceInstalled();

        Text = "Instalar Miscelánea Ari";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(650, 570);
        Size = new Size(720, 640);
        BackColor = Color.FromArgb(247, 249, 251);
        Font = new Font("Segoe UI", 10);
        MaximizeBox = false;

        BuildLayout();
        _hostRole.CheckedChanged += (_, _) =>
        {
            if (_hostRole.Checked) _clientRole.Checked = false;
            UpdateRoleOptions();
        };
        _clientRole.CheckedChanged += (_, _) =>
        {
            if (_clientRole.Checked) _hostRole.Checked = false;
            UpdateRoleOptions();
        };
        _newBusiness.CheckedChanged += (_, _) => UpdateImportOptions();
        _importBusiness.CheckedChanged += (_, _) => UpdateImportOptions();
        _browseDatabase.Click += BrowseDatabaseClicked;
        _install.Click += InstallClicked;

        if (_coordinatorAlreadyInstalled)
        {
            _hostRole.Checked = true;
            _hostRole.Enabled = false;
            _clientRole.Enabled = false;
            _newBusiness.Enabled = false;
            _importBusiness.Enabled = false;
            _browseDatabase.Enabled = false;
            _status.Text = "Esta PC ya es coordinadora. El asistente actualizará el servicio y conservará sus datos con un respaldo previo.";
        }

        if (_elevatedHostFlow)
        {
            _hostRole.Checked = true;
            _clientRole.Enabled = false;
            _hostRole.Enabled = false;
            _newBusiness.Enabled = false;
            _importBusiness.Enabled = false;
            _browseDatabase.Enabled = false;
            Shown += async (_, _) => await InstallHostAsync();
        }
    }

    private void BuildLayout()
    {
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(34, 28, 34, 26),
            ColumnCount = 1,
            RowCount = 5
        };
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "Instalar Miscelánea Ari",
            AutoSize = true,
            Font = new Font("Segoe UI", 22, FontStyle.Bold),
            ForeColor = Color.FromArgb(17, 29, 49),
            Margin = new Padding(0, 0, 0, 8)
        };
        var subtitle = new Label
        {
            Text = "Indica cómo participará este equipo en el negocio. El asistente hará la configuración necesaria.",
            AutoSize = true,
            MaximumSize = new Size(620, 0),
            ForeColor = Color.FromArgb(83, 101, 125),
            Margin = new Padding(0, 0, 0, 22)
        };

        var choices = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Color.Transparent
        };
        var clientCard = CreateCard(_clientRole,
            "Para las demás PC del negocio. Después de instalar, abre Sincronización y conexión para vincularte con el coordinador.");
        var hostCard = CreateCard(_hostRole,
            "Solo una PC Windows debe coordinar el negocio. Instala el servicio local y prepara su base central.");
        hostCard.Height = 238;
        _hostOptions.Controls.Add(BuildHostOptions());
        hostCard.Controls.Add(_hostOptions);
            hostCard.Controls.SetChildIndex(_hostOptions, 1);
        choices.Controls.Add(clientCard);
        choices.Controls.Add(hostCard);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Height = 76 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(_status, 0, 0);
        bottom.Controls.Add(_install, 1, 0);
        _install.Margin = new Padding(14, 10, 0, 10);

        outer.Controls.Add(title, 0, 0);
        outer.Controls.Add(subtitle, 0, 1);
        outer.Controls.Add(choices, 0, 2);
        outer.Controls.Add(bottom, 0, 3);
        outer.Controls.Add(_progress, 0, 4);
        Controls.Add(outer);
    }

    private static Panel CreateCard(RadioButton role, string description)
    {
        var card = new Panel
        {
            Width = 620,
            Height = 118,
            BackColor = Color.White,
            Padding = new Padding(20),
            Margin = new Padding(0, 0, 0, 14),
            BorderStyle = BorderStyle.FixedSingle
        };
        role.Location = new Point(18, 14);
        var detail = new Label
        {
            Text = description,
            Location = new Point(20, 46),
            Width = 565,
            Height = 52,
            ForeColor = Color.FromArgb(83, 101, 125)
        };
        card.Controls.Add(role);
        card.Controls.Add(detail);
        return card;
    }

    private Control BuildHostOptions()
    {
        var wrapper = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 2, 12, 0) };
        var label = new Label
        {
            Text = "¿Cómo quieres iniciar la base central?",
            AutoSize = true,
            Location = new Point(2, 0),
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(83, 101, 125)
        };
        _newBusiness.Location = new Point(2, 29);
        _importBusiness.Location = new Point(2, 56);
        _databasePath.Location = new Point(22, 87);
        _browseDatabase.Location = new Point(418, 84);
        wrapper.Controls.Add(label);
        wrapper.Controls.Add(_newBusiness);
        wrapper.Controls.Add(_importBusiness);
        wrapper.Controls.Add(_databasePath);
        wrapper.Controls.Add(_browseDatabase);
        return wrapper;
    }

    private void UpdateRoleOptions()
    {
        _hostOptions.Visible = _hostRole.Checked;
        UpdateImportOptions();
    }

    private void UpdateImportOptions()
    {
        var canBrowse = _hostRole.Checked && _importBusiness.Checked && !_elevatedHostFlow;
        _databasePath.Enabled = canBrowse;
        _browseDatabase.Enabled = canBrowse;
        _databasePath.Text = _importSource ?? string.Empty;
    }

    private void BrowseDatabaseClicked(object? sender, EventArgs args)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Selecciona la base de datos de Miscelánea Ari que deseas conservar",
            Filter = "Bases de datos SQLite (*.db;*.sqlite)|*.db;*.sqlite|Todos los archivos (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _databasePath.Text = dialog.FileName;
        _importSource = dialog.FileName;
    }

    private async void InstallClicked(object? sender, EventArgs args)
    {
        if (_busy) return;
        if (_hostRole.Checked)
        {
            if (_importBusiness.Checked && string.IsNullOrWhiteSpace(_databasePath.Text))
            {
                ShowError("Selecciona la base de datos que quieres conservar o elige iniciar un negocio nuevo.");
                return;
            }

            var appDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Miscelanea Ari", "App");
            try
            {
                InstallApplicationFiles(appDirectory);
                CreateShortcuts(appDirectory);
                if (!IsAdministrator())
                {
                    StartElevatedHostSetup(appDirectory);
                    Close();
                    return;
                }
                await InstallHostAsync(appDirectory);
            }
            catch (Exception exception) { ShowError(exception.Message); }
            return;
        }

        try
        {
            EnsureThisIsNotAnInstalledCoordinator();
            var appDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Miscelanea Ari", "App");
            SetBusy(true, "Instalando la aplicación para este equipo…");
            InstallApplicationFiles(appDirectory);
            CreateShortcuts(appDirectory);
            SetBusy(false, "Instalación completada. La app está lista para vincularse con el coordinador.");
            _install.Text = "Abrir Miscelánea Ari";
            _install.Click -= InstallClicked;
            _install.Click += (_, _) => StartApplication(appDirectory);
        }
        catch (Exception exception)
        {
            SetBusy(false, "No se pudo completar la instalación.");
            ShowError(exception.Message);
        }
    }

    private async Task InstallHostAsync(string? applicationDirectoryOverride = null)
    {
        if (_busy) return;
        try
        {
            var appDirectory = applicationDirectoryOverride ?? _applicationDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Miscelanea Ari", "App");
            if (!Directory.Exists(appDirectory))
            {
                InstallApplicationFiles(appDirectory);
                CreateShortcuts(appDirectory);
            }

            var payload = GetPayloadDirectory();
            var coordinatorSource = Path.Combine(payload, "Coordinator");
            var coordinatorExecutable = Path.Combine(coordinatorSource, "MauiApp1.Coordinator.exe");
            var installScript = Path.Combine(payload, "Scripts", "install-coordinator.ps1");
            var updateScript = Path.Combine(payload, "Scripts", "update-coordinator.ps1");
            var isUpdatingCoordinator = IsCoordinatorServiceInstalled();
            if (!File.Exists(coordinatorExecutable) || !File.Exists(installScript) || !File.Exists(updateScript))
                throw new InvalidOperationException("Falta el paquete del coordinador. Vuelve a descargar el instalador completo.");
            if (!IsAdministrator())
            {
                StartElevatedHostSetup(appDirectory);
                Close();
                return;
            }

            var coordinatorDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Miscelanea Ari", "Coordinator");
            var dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Miscelanea Ari", "Coordinator");
            SetBusy(true, "Preparando el coordinador y su base de datos…");
            var installedCoordinator = coordinatorExecutable;
            var centralDatabase = Path.Combine(dataDirectory, "miscelanea-ari-central.db");
            var source = _elevatedHostFlow
                ? ReadArgument(Environment.GetCommandLineArgs(), "--source-database")
                : _importBusiness.Checked ? _importSource : null;
            if (!isUpdatingCoordinator && !File.Exists(centralDatabase))
            {
                if (!string.IsNullOrWhiteSpace(source))
                    await RunCoordinatorCommandAsync(installedCoordinator, "--bootstrap-from", source, "--data-directory", dataDirectory);
                else
                    await RunCoordinatorCommandAsync(installedCoordinator, "--initialize-database", "--data-directory", dataDirectory);
            }
            else if (!isUpdatingCoordinator && !string.IsNullOrWhiteSpace(source))
            {
                throw new InvalidOperationException("Ya existe una base central. El instalador no la reemplazará; no se importaron otros datos.");
            }
            else if (isUpdatingCoordinator && !File.Exists(centralDatabase))
            {
                throw new InvalidOperationException("Se encontró el servicio coordinador, pero falta su base central. No se actualizará el servicio hasta recuperar la base.");
            }

            if (!isUpdatingCoordinator)
            {
                CopyDirectory(coordinatorSource, coordinatorDirectory, overwrite: true);
                installedCoordinator = Path.Combine(coordinatorDirectory, "MauiApp1.Coordinator.exe");
            }
            var windowsUser = _applicationUser ?? $"{Environment.UserDomainName}\\{Environment.UserName}";
            if (isUpdatingCoordinator)
            {
                await RunPowerShellAsync(updateScript,
                    "-AppDirectory", coordinatorDirectory,
                    "-PublishDirectory", coordinatorSource,
                    "-DataDirectory", dataDirectory,
                    "-WindowsAppUser", windowsUser,
                    "-SetHostMode");
            }
            else
            {
                await RunPowerShellAsync(installScript,
                    "-AppDirectory", coordinatorDirectory,
                    "-DataDirectory", dataDirectory,
                    "-WindowsAppUser", windowsUser,
                    "-SetHostMode");
            }

            SetBusy(false, isUpdatingCoordinator
                ? "Coordinador actualizado. Se conservó la base y quedó un respaldo. Abre la aplicación para continuar."
                : "Coordinador instalado. Abre la aplicación para crear o usar la cuenta Administrador y vincular los demás equipos.");
            _install.Text = "Abrir Miscelánea Ari";
            _install.Click -= InstallClicked;
            _install.Click += (_, _) => StartApplication(appDirectory);
        }
        catch (Exception exception)
        {
            SetBusy(false, "La instalación no terminó. No se borraron las bases existentes.");
            ShowError(exception.Message);
        }
    }

    private void StartElevatedHostSetup(string appDirectory)
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            throw new InvalidOperationException("No se pudo iniciar el asistente con permisos de administrador.");

        var info = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" };
        info.ArgumentList.Add("--install-host");
        info.ArgumentList.Add("--application-user");
        info.ArgumentList.Add(_applicationUser ?? $"{Environment.UserDomainName}\\{Environment.UserName}");
        info.ArgumentList.Add("--application-directory");
        info.ArgumentList.Add(appDirectory);
        var source = _importBusiness.Checked ? _importSource ?? _databasePath.Text : null;
        if (!string.IsNullOrWhiteSpace(source))
        {
            info.ArgumentList.Add("--source-database");
            info.ArgumentList.Add(source);
        }
        Process.Start(info);
    }

    private async Task RunCoordinatorCommandAsync(string executable, params string[] arguments)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        await RunHiddenProcessAsync(info, "Inicializando la base central…");
    }

    private async Task RunPowerShellAsync(string script, params string[] arguments)
    {
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell))
            throw new InvalidOperationException("Windows PowerShell no está disponible en este equipo.");

        var info = new ProcessStartInfo(powershell)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add("-NoLogo");
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-ExecutionPolicy");
        info.ArgumentList.Add("Bypass");
        info.ArgumentList.Add("-File");
        info.ArgumentList.Add(script);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        await RunHiddenProcessAsync(info, "Configurando el servicio seguro y el acceso de red…");
    }

    private async Task RunHiddenProcessAsync(ProcessStartInfo info, string progressMessage)
    {
        SetBusy(true, progressMessage);
        using var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var output = new List<string>();
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data)) lock (output) output.Add(eventArgs.Data);
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data)) lock (output) output.Add(eventArgs.Data);
        };
        if (!process.Start()) throw new InvalidOperationException("No se pudo iniciar una tarea requerida por el instalador.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            string details;
            lock (output) details = string.Join(Environment.NewLine, output.TakeLast(8));
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(details)
                ? "Una tarea de instalación terminó con error."
                : details);
        }
    }

    private void InstallApplicationFiles(string destination)
    {
        var source = Path.Combine(GetPayloadDirectory(), "App");
        if (!File.Exists(Path.Combine(source, "MauiApp1.exe")))
            throw new InvalidOperationException("Falta la aplicación Windows en el paquete. Vuelve a descargar el instalador completo.");
        if (Directory.Exists(destination))
        {
            if (!File.Exists(Path.Combine(destination, "MauiApp1.exe")))
                throw new InvalidOperationException($"La carpeta de instalación existe, pero está incompleta: {destination}");
            if (IsApplicationRunningFrom(destination))
                throw new InvalidOperationException("Cierra Miscelánea Ari y vuelve a ejecutar el instalador para actualizar sus archivos.");
            CopyDirectory(source, destination, overwrite: true);
            return;
        }
        CopyDirectory(source, destination);
    }

    private static bool IsApplicationRunningFrom(string appDirectory)
    {
        var expectedPath = Path.GetFullPath(Path.Combine(appDirectory, "MauiApp1.exe"));
        foreach (var process in Process.GetProcessesByName("MauiApp1"))
        {
            using (process)
            {
                try
                {
                    if (string.Equals(Path.GetFullPath(process.MainModule?.FileName ?? string.Empty),
                            expectedPath, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // A process from another Windows account may not expose its module path.
                }
            }
        }
        return false;
    }

    private void EnsureThisIsNotAnInstalledCoordinator()
    {
        using var service = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{ServiceName}");
        if (service is not null)
            throw new InvalidOperationException("Este equipo ya tiene el servicio coordinador instalado. No se cambiará su rol ni se tocarán sus datos.");

        var modePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Miscelanea Ari", "Desktop", "windows-app-mode.json");
        if (!File.Exists(modePath)) return;
        using var document = JsonDocument.Parse(File.ReadAllText(modePath));
        if (!document.RootElement.TryGetProperty("Coordinator", out var coordinator) ||
            !coordinator.TryGetProperty("Mode", out var mode) ||
            !string.Equals(mode.GetString(), "Host", StringComparison.OrdinalIgnoreCase)) return;
        throw new InvalidOperationException("Este equipo ya está configurado como coordinador. Para proteger sus datos, no se instalará como cliente.");
    }

    private static bool IsCoordinatorServiceInstalled()
    {
        using var service = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{ServiceName}");
        return service is not null;
    }

    private void CreateShortcuts(string appDirectory)
    {
        var target = Path.Combine(appDirectory, "MauiApp1.exe");
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", ProductDisplayName);
        Directory.CreateDirectory(startMenu);
        CreateShortcut(Path.Combine(desktop, $"{ProductDisplayName}.lnk"), target, appDirectory);
        CreateShortcut(Path.Combine(startMenu, $"{ProductDisplayName}.lnk"), target, appDirectory);
    }

    private static void CreateShortcut(string shortcutPath, string target, string workingDirectory)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows no pudo crear los accesos directos de la aplicación.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = target;
        shortcut.WorkingDirectory = workingDirectory;
        shortcut.IconLocation = target;
        shortcut.Save();
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
    }

    private static void CopyDirectory(string source, string destination, bool overwrite = false)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite);
        }
    }

    private static string GetPayloadDirectory() => Path.Combine(AppContext.BaseDirectory, "Payload");

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string? ReadArgument(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count - 1; index++)
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
        return null;
    }

    private void SetBusy(bool busy, string message)
    {
        _busy = busy;
        _clientRole.Enabled = !busy && !_elevatedHostFlow && !_coordinatorAlreadyInstalled;
        _hostRole.Enabled = !busy && !_elevatedHostFlow && !_coordinatorAlreadyInstalled;
        _install.Enabled = !busy;
        _newBusiness.Enabled = !busy && !_elevatedHostFlow && !_coordinatorAlreadyInstalled;
        _importBusiness.Enabled = !busy && !_elevatedHostFlow && !_coordinatorAlreadyInstalled;
        _browseDatabase.Enabled = !busy && _hostRole.Checked && _importBusiness.Checked &&
            !_elevatedHostFlow && !_coordinatorAlreadyInstalled;
        _progress.Visible = busy;
        _status.Text = message;
        if (_install.Text is not ("Abrir Miscelánea Ari" or "Finalizar"))
            _install.Text = busy ? "Instalando…" : "Instalar";
    }

    private void StartApplication(string appDirectory)
    {
        var executable = Path.Combine(appDirectory, "MauiApp1.exe");
        Process.Start(new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            WorkingDirectory = appDirectory
        });
        Close();
    }

    private void ShowError(string message) => MessageBox.Show(this, message, "No se pudo instalar", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
