using System.Diagnostics;
using System.IO.Compression;
using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json;

namespace ThalesBridgeInstaller;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new InstallerForm());
    }
}

internal sealed partial class InstallerForm : Form
{
    private const string SdkRoot = @"C:\Program Files\Thales\Thales Document Reader SDK x64";

    private readonly TextBox sdkMsi = new();
    private readonly NumericUpDown port = new();
    private readonly CheckBox disableUvIr = new();
    private readonly CheckBox startAtLogon = new();
    private readonly Button browse = new();
    private readonly Button install = new();
    private readonly Button close = new();
    private readonly RichTextBox log = new();
    private readonly Label status = new();
    private readonly Label bridgeConnection = new();
    private readonly Label scannerConnection = new();
    private readonly Label activity = new();
    private Process? installerProcess;
    private CancellationTokenSource? monitorCancellation;
    private Task? monitorTask;
    private bool installSucceeded;
    private int completedScans;
    private int connectionAttempts;
    private int connectionsOpened;
    private int framesReceived;
    private readonly Label connectionDetails = new();
    private readonly Label scanDetails = new();
    private readonly Label lastError = new();
    private string? previousScannerStatus;


    public InstallerForm()
    {
#if BRIDGE_MANAGER
        InitializeManagerView();
#else
        Text = "Thales Scanner Bridge Setup";
        ClientSize = new Size(720, 690);
        MinimumSize = new Size(650, 640);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        Font = new Font("Segoe UI", 9F);

        var title = new Label
        {
            Text = "Install Thales Scanner Bridge",
            Font = new Font("Segoe UI Semibold", 18F),
            AutoSize = true,
            Location = new Point(24, 20)
        };
        var intro = new Label
        {
            Text = "This installs the bridge as a Windows startup task. The licensed Thales SDK is not bundled.",
            AutoSize = true,
            Location = new Point(27, 61)
        };

        status.AutoSize = true;
        status.Location = new Point(27, 90);
        status.ForeColor = Directory.Exists(SdkRoot) ? Color.DarkGreen : Color.DarkOrange;
        status.Text = Directory.Exists(SdkRoot)
            ? "Thales Document Reader SDK detected."
            : "Thales SDK not detected. Select the SDK installer (.msi) below.";

        var sdkLabel = new Label
        {
            Text = "Thales SDK installer (optional if already installed)",
            AutoSize = true,
            Location = new Point(27, 124)
        };
        sdkMsi.Location = new Point(30, 147);
        sdkMsi.Size = new Size(555, 26);
        sdkMsi.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        sdkMsi.PlaceholderText = @"C:\path\to\Thales Document Reader SDK x64.msi";

        browse.Text = "Browse...";
        browse.Location = new Point(595, 145);
        browse.Size = new Size(95, 29);
        browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        browse.Click += BrowseForSdk;

        var portLabel = new Label { Text = "WebSocket port", AutoSize = true, Location = new Point(27, 191) };
        port.Location = new Point(30, 214);
        port.Size = new Size(110, 26);
        port.Minimum = 1;
        port.Maximum = 65535;
        port.Value = 8765;

        disableUvIr.Text = "Disable UV/IR capture (recommended for QS2000)";
        disableUvIr.Checked = true;
        disableUvIr.AutoSize = true;
        disableUvIr.Location = new Point(175, 215);

        startAtLogon.Text = "Start at user logon instead of at boot";
        startAtLogon.AutoSize = true;
        startAtLogon.Location = new Point(175, 244);

        var connectionGroup = new GroupBox
        {
            Text = "Live connection and activity",
            Location = new Point(30, 278),
            Size = new Size(660, 112),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        bridgeConnection.Text = "Bridge: checking...";
        bridgeConnection.AutoSize = true;
        bridgeConnection.Location = new Point(16, 27);
        bridgeConnection.ForeColor = Color.DarkOrange;

        scannerConnection.Text = "Scanner: waiting for bridge";
        scannerConnection.AutoSize = true;
        scannerConnection.Location = new Point(16, 53);
        scannerConnection.ForeColor = Color.DimGray;

        activity.Text = "Activity: waiting for installation";
        activity.AutoEllipsis = true;
        activity.Location = new Point(16, 79);
        activity.Size = new Size(625, 20);
        activity.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        activity.ForeColor = Color.DimGray;
        connectionGroup.Controls.AddRange([bridgeConnection, scannerConnection, activity]);

        var logLabel = new Label { Text = "Installation log", AutoSize = true, Location = new Point(27, 403) };
        log.Location = new Point(30, 426);
        log.Size = new Size(660, 190);
        log.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        log.ReadOnly = true;
        log.BackColor = Color.FromArgb(28, 28, 28);
        log.ForeColor = Color.Gainsboro;
        log.Font = new Font("Consolas", 9F);
        log.WordWrap = false;

        install.Text = "Install";
        install.Size = new Size(110, 34);
        install.Location = new Point(460, 632);
        install.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        install.Click += async (_, _) => await RunInstallAsync();

        close.Text = "Close";
        close.Size = new Size(110, 34);
        close.Location = new Point(580, 632);
        close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        close.Click += (_, _) => Close();

        AcceptButton = install;
        CancelButton = close;
        Controls.AddRange([
            title, intro, status, sdkLabel, sdkMsi, browse, portLabel, port,
            disableUvIr, startAtLogon, connectionGroup, logLabel, log, install, close
        ]);
        FormClosing += ConfirmCloseWhileRunning;
        FormClosed += (_, _) => StopMonitor();
        Shown += (_, _) => StartMonitor((int)port.Value);
        port.ValueChanged += (_, _) =>
        {
            if (installerProcess is null)
                StartMonitor((int)port.Value);
        };
#endif
    }

    private void BrowseForSdk(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select the Thales SDK installer",
            Filter = "Windows Installer (*.msi)|*.msi|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            sdkMsi.Text = dialog.FileName;
    }

    private async Task RunInstallAsync()
    {
        if (!Directory.Exists(SdkRoot) && string.IsNullOrWhiteSpace(sdkMsi.Text))
        {
            MessageBox.Show(this,
                "The Thales SDK is not installed. Select the licensed Thales SDK .msi first.",
                "SDK required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!string.IsNullOrWhiteSpace(sdkMsi.Text) && !File.Exists(sdkMsi.Text))
        {
            MessageBox.Show(this, "The selected SDK installer does not exist.", "File not found",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetBusy(true);
        log.Clear();
        AppendLog("Preparing installation files...");
        var workDir = Path.Combine(Path.GetTempPath(), "ThalesBridgeSetup-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(workDir);
            ExtractPayload(workDir);

            var powerShell = Path.Combine(Environment.SystemDirectory,
                @"WindowsPowerShell\v1.0\powershell.exe");
            if (!File.Exists(powerShell))
                throw new FileNotFoundException("Windows PowerShell is not available on this PC.", powerShell);

            var start = new ProcessStartInfo(powerShell)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = workDir
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-ExecutionPolicy");
            start.ArgumentList.Add("Bypass");
            start.ArgumentList.Add("-File");
            start.ArgumentList.Add(Path.Combine(workDir, "setup.ps1"));
            start.ArgumentList.Add("-Port");
            start.ArgumentList.Add(((int)port.Value).ToString());
            if (!string.IsNullOrWhiteSpace(sdkMsi.Text))
            {
                start.ArgumentList.Add("-SdkMsi");
                start.ArgumentList.Add(sdkMsi.Text.Trim());
            }
            if (!disableUvIr.Checked)
                start.ArgumentList.Add("-SkipUvIrPatch");
            if (startAtLogon.Checked)
                start.ArgumentList.Add("-LogonStart");

            installerProcess = new Process { StartInfo = start, EnableRaisingEvents = true };
            installerProcess.OutputDataReceived += (_, e) => { if (e.Data is not null) AppendLog(e.Data); };
            installerProcess.ErrorDataReceived += (_, e) => { if (e.Data is not null) AppendLog(e.Data); };
            if (!installerProcess.Start())
                throw new InvalidOperationException("Could not start the installer.");
            installerProcess.BeginOutputReadLine();
            installerProcess.BeginErrorReadLine();
            await installerProcess.WaitForExitAsync();
            installerProcess.WaitForExit();

            if (installerProcess.ExitCode != 0)
                throw new InvalidOperationException($"Installation failed with exit code {installerProcess.ExitCode}. See the log above.");

            installSucceeded = true;
            status.Text = $"Installed successfully. Bridge URL: wss://localhost:{(int)port.Value}";
            status.ForeColor = Color.DarkGreen;
            install.Text = "Installed";
            AppendLog("Installation completed successfully.");
            StartMonitor((int)port.Value);
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ThalesBridge", "ThalesBridgeManager.exe")) { UseShellExecute = true });
            MessageBox.Show(this, "Thales Scanner Bridge was installed successfully.",
                "Setup complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            status.Text = "Installation failed. Review the log below.";
            status.ForeColor = Color.DarkRed;
            AppendLog("ERROR: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Installation failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            installerProcess?.Dispose();
            installerProcess = null;
            TryDeleteDirectory(workDir);
            SetBusy(false);
        }
    }

    private void StartMonitor(int bridgePort)
    {
        StopMonitor();
        monitorCancellation = new CancellationTokenSource();
        monitorTask = MonitorBridgeAsync(bridgePort, monitorCancellation.Token);
    }

    private void StopMonitor()
    {
        monitorCancellation?.Cancel();
        monitorCancellation?.Dispose();
        monitorCancellation = null;
        monitorTask = null;
    }

    private async Task MonitorBridgeAsync(int bridgePort, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            SetConnectionLabel(bridgeConnection, $"Bridge: connecting to localhost:{bridgePort}...", Color.DarkOrange);

            connectionAttempts++;
            SetConnectionLabel(connectionDetails, $"Connection attempts: {connectionAttempts} | Successful connections: {connectionsOpened} | Frames: {framesReceived}", Color.DimGray);
            using var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
            try
            {
                var handshake = Stopwatch.StartNew();
                using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                await socket.ConnectAsync(new Uri($"wss://localhost:{bridgePort}/"), connectTimeout.Token);
                handshake.Stop();
                connectionsOpened++;
                AppendLog($"TLS/WebSocket handshake: {handshake.ElapsedMilliseconds} ms; certificate validation passed.");
                AppendLog($"{DateTime.Now:T} WebSocket connected: wss://localhost:{bridgePort}/");
                SetConnectionLabel(connectionDetails, $"Connected at {DateTime.Now:T} | Attempts: {connectionAttempts} | Successful connections: {connectionsOpened}", Color.DimGray);
                SetConnectionLabel(bridgeConnection, $"Bridge: connected on port {bridgePort}", Color.DarkGreen);
                SetActivity("Activity: bridge connected; waiting for scanner status", Color.DimGray);

                while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    var message = await ReceiveMessageAsync(socket, cancellationToken);
                    if (message is null)
                        throw new WebSocketException("The bridge closed the connection.");
                    framesReceived++;
                    SetConnectionLabel(connectionDetails, $"Last frame: {DateTime.Now:T} | Frames: {framesReceived} | Attempts: {connectionAttempts} | Connections: {connectionsOpened}", Color.DimGray);
                    ProcessBridgeMessage(message);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                SetConnectionLabel(lastError, $"Last connection error ({DateTime.Now:T}): {ex.Message}", Color.DarkRed);
                if (connectionAttempts == 1 || connectionAttempts % 15 == 0)
                    AppendLog($"{DateTime.Now:T} Connection failed (attempt {connectionAttempts}): {ex.Message}");
                SetConnectionLabel(bridgeConnection, $"Bridge: connection failed on port {bridgePort} (check TLS/setup)", Color.DarkRed);
                SetConnectionLabel(scannerConnection, "Scanner: unavailable until bridge starts", Color.DimGray);
                SetActivity("Activity: waiting for installation or bridge startup", Color.DimGray);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static async Task<string?> ReceiveMessageAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        const int maxCapturedBytes = 256 * 1024;
        var buffer = new byte[8192];
        using var captured = new MemoryStream();
        bool truncated = false;
        WebSocketReceiveResult result;

        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            if (captured.Length + result.Count <= maxCapturedBytes)
                captured.Write(buffer, 0, result.Count);
            else
                truncated = true;
        }
        while (!result.EndOfMessage);

        if (truncated)
            return """{"type":"result"}""";
        return System.Text.Encoding.UTF8.GetString(captured.ToArray());
    }

    private void ProcessBridgeMessage(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var typeElement))
                return;

            switch (typeElement.GetString())
            {
                case "status":
                    var scannerStatus = root.TryGetProperty("status", out var statusElement)
                        ? statusElement.GetString()
                        : null;
                    if (previousScannerStatus != scannerStatus)
                        AppendLog($"{DateTime.Now:T} Reader status: {scannerStatus}");
                    previousScannerStatus = scannerStatus;
                    ShowScannerStatus(scannerStatus);
                    break;

                case "result":
                    completedScans++;
                    SetConnectionLabel(scanDetails, $"Completed scans: {completedScans} | Last completed: {DateTime.Now:T}", Color.DarkGreen);
                    AppendLog($"{DateTime.Now:T} Document scan completed ({completedScans} this session).");
                    SetConnectionLabel(scannerConnection, "Scanner: connected and ready", Color.DarkGreen);
                    SetActivity(
                        $"Activity: scan completed at {DateTime.Now:T} ({completedScans} this session)",
                        Color.DarkGreen);
                    break;

                case "error":
                    var code = root.TryGetProperty("code", out var codeElement)
                        ? codeElement.GetString()
                        : "SCANNER_ERROR";
                    var message = root.TryGetProperty("message", out var messageElement)
                        ? messageElement.GetString()
                        : "Unknown scanner error";
                    if (code is "SCANNER_NOT_CONNECTED" or "READER_DISCONNECTED")
                        SetConnectionLabel(scannerConnection, "Scanner: disconnected", Color.DarkRed);
                    SetConnectionLabel(lastError, $"Last reader error ({DateTime.Now:T}): {code} - {message}", Color.DarkRed);
                    AppendLog($"{DateTime.Now:T} Reader error: {code} - {message}");
                    SetActivity($"Activity: {code} - {message}", Color.DarkRed);
                    break;
            }
        }
        catch (JsonException)
        {
            SetActivity("Activity: received an unreadable bridge message", Color.DarkOrange);
        }
    }

    private void ShowScannerStatus(string? scannerStatus)
    {
        switch (scannerStatus)
        {
            case "waiting_for_document":
                SetConnectionLabel(scannerConnection, "Scanner: connected and ready", Color.DarkGreen);
                SetActivity("Activity: waiting for a document", Color.DimGray);
                break;
            case "reading":
                SetConnectionLabel(scannerConnection, "Scanner: reading document", Color.DarkOrange);
                SetActivity("Activity: document detected; scan in progress", Color.DarkOrange);
                break;
            default:
                SetConnectionLabel(scannerConnection, "Scanner: not detected", Color.DarkRed);
                SetActivity("Activity: connect the scanner; the bridge retries automatically", Color.DimGray);
                break;
        }
    }

    private void SetConnectionLabel(Label label, string text, Color color)
    {
        if (IsDisposed || !IsHandleCreated)
            return;
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(() => SetConnectionLabel(label, text, color))); }
            catch (InvalidOperationException) { /* form is closing */ }
            return;
        }
        label.Text = text;
        label.ForeColor = color;
    }

    private void SetActivity(string text, Color color)
        => SetConnectionLabel(activity, text, color);

    private static void ExtractPayload(string destination)
    {
        using var payload = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("ThalesBridgeInstaller.payload.zip")
            ?? throw new InvalidOperationException("The installer payload is missing.");
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read);
        archive.ExtractToDirectory(destination, overwriteFiles: true);
    }

    private void AppendLog(string text)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(() => AppendLog(text))); }
            catch (InvalidOperationException) { }
            return;
        }
        if (log.TextLength > 100_000) log.Clear();
        log.AppendText(text + Environment.NewLine);
        log.SelectionStart = log.TextLength;
        log.ScrollToCaret();
    }

    private void SetBusy(bool busy)
    {
        install.Enabled = !busy && !installSucceeded;
        close.Enabled = !busy;
        browse.Enabled = !busy;
        sdkMsi.Enabled = !busy;
        port.Enabled = !busy;
        disableUvIr.Enabled = !busy;
        startAtLogon.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private void ConfirmCloseWhileRunning(object? sender, FormClosingEventArgs e)
    {
        if (installerProcess is null)
            return;
        MessageBox.Show(this,
            "Installation is still running. Wait for it to finish before closing setup.",
            "Installation in progress", MessageBoxButtons.OK, MessageBoxIcon.Information);
        e.Cancel = true;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // The OS will eventually clean its temp directory; installation already finished.
        }
    }
}
