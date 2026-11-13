using System.Diagnostics;
using System.Text.Json;

namespace ThalesBridgeInstaller;

internal sealed partial class InstallerForm
{
    private bool managementBusy;
    private string endpointScheme = "wss";

    private readonly TextBox endpointEditor = new();
    private readonly Label endpointFeedback = new();
    private readonly ProgressBar scannerProgress = new();
    private FlowLayoutPanel? managerActions;
    private Button? applyEndpoint;
    private bool managerView;
    private Process? bridgeProcess;
    private Task? bridgeLifetime;
    private bool closingAfterPause;
    private Button? runBridgeButton;
    private Button? pauseBridgeButton;

    private void InitializeManagerView()
    {
        managerView = true;
        Text = "Thales Scanner Bridge Manager";
        ClientSize = new Size(940, 800);
        MinimumSize = new Size(760, 740);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10F);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(246, 248, 251);
        ForeColor = Color.FromArgb(35, 48, 65);
        int configuredPort = 8765;
        try
        {
            using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "bridge-settings.json")));
            configuredPort = config.RootElement.GetProperty("port").GetInt32();
            if (config.RootElement.TryGetProperty("protocol", out var protocol) && protocol.GetString() == "ws") endpointScheme = "ws";
        }
        catch (Exception ex) { AppendLog("Using default port: " + ex.Message); }
        port.Minimum = 1;
        port.Maximum = 65535;
        port.Value = Math.Clamp(configuredPort, 1, 65535);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26), ColumnCount = 1, RowCount = 13 };
        for (int i = 0; i < 12; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var title = new Label { Text = "Thales Scanner Bridge", Font = new Font("Segoe UI", 21F), AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        var description = new Label { Text = "Click Run to use the scanner. Pause or close this window to release the reader.", AutoSize = true, Margin = new Padding(0, 0, 0, 22) };
        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(description, 0, 1);
        var endpointRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Margin = new Padding(0) };
        endpointRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        endpointRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        endpointRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        endpointRow.Controls.Add(new Label { Text = "Bridge endpoint", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 16, 0) }, 0, 0);
        endpointEditor.Text = $"{endpointScheme}://localhost:{port.Value}/";
        endpointEditor.Dock = DockStyle.Fill;
        endpointEditor.AccessibleName = "Local WebSocket endpoint";
        applyEndpoint = MakeManagerButton("Apply endpoint");
        applyEndpoint.Click += async (_, _) => await ApplyEndpointAsync();
        endpointRow.Controls.Add(endpointEditor, 1, 0);
        endpointRow.Controls.Add(applyEndpoint, 2, 0);
        layout.Controls.Add(endpointRow, 0, 2);
        endpointFeedback.Text = "Use ws://localhost:PORT/ or wss://localhost:PORT/. WSS uses TLS; WS is unencrypted. Match the URL in P360.";
        endpointFeedback.AutoSize = true;
        endpointFeedback.Margin = new Padding(0, 8, 0, 20);
        endpointFeedback.ForeColor = Color.FromArgb(89, 104, 124);
        layout.Controls.Add(endpointFeedback, 0, 3);
        var states = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0) };
        states.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        states.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        foreach (var label in new[] { bridgeConnection, scannerConnection })
        {
            label.Dock = DockStyle.Fill;
            label.Height = 64;
            label.Padding = new Padding(14, 8, 12, 8);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.BackColor = Color.FromArgb(235, 240, 247);
            label.AutoEllipsis = true;
            label.Margin = new Padding(0, 0, 10, 0);
            states.Controls.Add(label);
        }
        bridgeConnection.Text = "Bridge: paused";
        scannerConnection.Text = "Scanner: released";
        activity.Text = "Click Run to connect the scanner.";
        layout.Controls.Add(states, 0, 4);
        scannerProgress.Dock = DockStyle.Top;
        scannerProgress.Height = 4;
        scannerProgress.Margin = new Padding(0, 8, 10, 12);
        layout.Controls.Add(scannerProgress, 0, 5);
        activity.AutoSize = true;
        activity.Margin = new Padding(0, 0, 0, 18);
        layout.Controls.Add(activity, 0, 6);
        connectionDetails.AutoSize = true;
        connectionDetails.Margin = new Padding(0, 0, 0, 8);
        layout.Controls.Add(connectionDetails, 0, 7);
        scanDetails.AutoSize = true;
        scanDetails.Text = "Completed scans: 0 this session";
        scanDetails.Margin = new Padding(0, 0, 0, 8);
        layout.Controls.Add(scanDetails, 0, 8);
        lastError.AutoSize = true;
        lastError.Text = "Last error: none in this session";
        lastError.Margin = new Padding(0, 0, 0, 10);
        layout.Controls.Add(lastError, 0, 9);
        managerActions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 8, 0, 12) };
        foreach (var (label, command) in new[] { ("Run", "Run"), ("Pause", "Pause"), ("Diagnostics", "Doctor") })
        {
            var button = MakeManagerButton(label);
            if (command == "Run") runBridgeButton = button;
            if (command == "Pause") pauseBridgeButton = button;
            button.Click += async (_, _) =>
            {
                SetManagerBusy(true);
                try
                {
                    if (command == "Run") RunOwnedBridge();
                    else if (command == "Pause") await PauseOwnedBridgeAsync();
                    else await RunManagementCommandAsync(command);
                }
                catch (Exception ex) { ShowBridgeFailure(ex); }
                finally { SetManagerBusy(false); }
            };
            managerActions.Controls.Add(button);
        }
        var saveReport = MakeManagerButton("Save report");
        saveReport.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog { Filter = "Text report (*.txt)|*.txt", FileName = $"ThalesBridge-{DateTime.Now:yyyyMMdd-HHmmss}.txt" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { File.WriteAllText(dialog.FileName, $"{endpointScheme}://localhost:{port.Value}/\n{bridgeConnection.Text}\n{scannerConnection.Text}\n{connectionDetails.Text}\n{scanDetails.Text}\n{lastError.Text}\n\n{log.Text}"); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save report"); }
        };
        managerActions.Controls.Add(saveReport);
        var openLogs = MakeManagerButton("Open logs");
        openLogs.Click += (_, _) =>
        {
            try
            {
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ThalesBridge", "Logs");
                Directory.CreateDirectory(directory);
                Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
            }
            catch (Exception ex) { AppendLog("Cannot open logs: " + ex.Message); }
        };
        managerActions.Controls.Add(openLogs);
        layout.Controls.Add(CreateCaptureControls(), 0, 10);
        layout.Controls.Add(managerActions, 0, 11);
        log.Dock = DockStyle.Fill;
        log.ReadOnly = true;
        log.BackColor = Color.White;
        log.ForeColor = Color.FromArgb(55, 70, 90);
        log.BorderStyle = BorderStyle.FixedSingle;
        log.Font = new Font("Consolas", 9F);
        layout.Controls.Add(log, 0, 12);
        Controls.Add(layout);
        void FitText()
        {
            foreach (var label in new[] { description, endpointFeedback, activity, connectionDetails, scanDetails, lastError })
                label.MaximumSize = new Size(Math.Max(300, ClientSize.Width - 70), 0);
        }
        Resize += (_, _) => FitText();
        FitText();
        SetManagerBusy(false);
        FormClosing += async (_, e) =>
        {
            if (closingAfterPause) return;
            if (managementBusy) { e.Cancel = true; return; }
            if (bridgeProcess == null) return;
            e.Cancel = true;
            SetManagerBusy(true);
            try
            {
                await PauseOwnedBridgeAsync();
                closingAfterPause = true;
                BeginInvoke(new Action(Close));
            }
            catch (Exception ex) { ShowBridgeFailure(ex); }
            finally { SetManagerBusy(false); }
        };
    }

    private void ShowBridgeFailure(Exception ex)
    {
        AppendLog("ERROR: " + ex.Message);
        SetConnectionLabel(lastError, ex.Message, Color.DarkRed);
    }

    private void RunOwnedBridge()
    {
        if (bridgeProcess != null) return;
        var existing = Process.GetProcessesByName("ThalesBridge");
        try
        {
            if (existing.Length != 0)
                throw new InvalidOperationException("Another bridge process is running. Stop the old bridge before clicking Run.");
        }
        finally { foreach (var process in existing) process.Dispose(); }

        if (!Directory.Exists(SdkRoot)) throw new DirectoryNotFoundException("Thales SDK not found. Check the licensed SDK installation.");
        var sdk = Directory.GetDirectories(SdkRoot).OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        var sdkBin = sdk == null ? null : Path.Combine(sdk, "Bin");
        if (sdkBin == null || !File.Exists(Path.Combine(sdkBin, "MMMReaderHighLevelAPI.dll")))
            throw new FileNotFoundException("Thales SDK native library missing. Check SDK installation.");
        var work = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ThalesBridge");
        Directory.CreateDirectory(work);
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "ThalesBridge.exe"))
        {
            WorkingDirectory = work, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(((int)port.Value).ToString());
        start.ArgumentList.Add("--manager-owned");
        if (endpointScheme == "ws") start.ArgumentList.Add("--ws");
        start.Environment["PATH"] = sdkBin + Path.PathSeparator + start.Environment["PATH"];
        var child = Process.Start(start) ?? throw new InvalidOperationException("Could not launch the bridge.");
        bridgeProcess = child;
        previousScannerStatus = null;
        AppendLog($"Bridge started (PID {child.Id}). Waiting for its WebSocket connection.");
        StartMonitor((int)port.Value);
        bridgeLifetime = WatchOwnedBridgeAsync(child);
    }

    private async Task WatchOwnedBridgeAsync(Process child)
    {
        try
        {
            var output = ReadEndpointLogAsync(child.StandardOutput, "bridge");
            var error = ReadEndpointLogAsync(child.StandardError, "bridge ERROR");
            await child.WaitForExitAsync();
            await Task.WhenAll(output, error);
            AppendLog($"Bridge exited with code {child.ExitCode}.");
            if (child.ExitCode != 0) SetConnectionLabel(lastError, $"Bridge exited with code {child.ExitCode}. See logs.", Color.DarkRed);
        }
        catch (Exception ex) { ShowBridgeFailure(ex); }
        finally
        {
            if (bridgeProcess == child)
            {
                bridgeProcess = null;
                StopMonitor();
                ShowPaused();
            }
            child.Dispose();
            SetManagerBusy(managementBusy);
        }
    }

    private void ShowPaused()
    {
        SetConnectionLabel(bridgeConnection, "Bridge: paused", Color.DimGray);
        SetConnectionLabel(scannerConnection, "Scanner: released", Color.DimGray);
        SetActivity("Click Run to reconnect the scanner.", Color.DimGray);
    }

    private async Task PauseOwnedBridgeAsync()
    {
        var child = bridgeProcess;
        if (child == null) return;
        var previousMonitor = monitorTask;
        StopMonitor();
        var lifetime = bridgeLifetime ?? Task.CompletedTask;
        child.StandardInput.Close();
        try { await lifetime.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (TimeoutException)
        {
            if (bridgeProcess == child && !child.HasExited)
            {
                AppendLog("Bridge shutdown timed out; terminating the process to release the reader.");
                try { child.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (child.HasExited) { }
            }
            await lifetime.WaitAsync(TimeSpan.FromSeconds(5));
        }
        if (previousMonitor != null) await previousMonitor;
        ShowPaused();
    }

    private static Button MakeManagerButton(string text)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 36, FlatStyle = FlatStyle.Flat,
            BackColor = Color.White, ForeColor = Color.FromArgb(38, 62, 88), Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 8, 6) };
        button.FlatAppearance.BorderColor = Color.FromArgb(215, 224, 234);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(231, 240, 252);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(218, 232, 250);
        return button;
    }

    private void SetManagerBusy(bool busy)
    {
        managementBusy = busy;
        UpdateCaptureControls();
        if (managerActions != null) managerActions.Enabled = !busy;
        if (applyEndpoint != null) applyEndpoint.Enabled = !busy;
        endpointEditor.Enabled = !busy;
        if (runBridgeButton != null) runBridgeButton.Enabled = !busy && bridgeProcess == null;
        if (pauseBridgeButton != null) pauseBridgeButton.Enabled = !busy && bridgeProcess != null;
    }

    private void UpdateManagerIndicator(Label label, Color originalColor)
    {
        if (!managerView || (label != bridgeConnection && label != scannerConnection)) return;
        var ready = originalColor == Color.DarkGreen;
        var warning = originalColor == Color.DarkOrange;
        var error = originalColor == Color.DarkRed;
        label.BackColor = ready ? Color.FromArgb(228, 246, 237) : warning ? Color.FromArgb(231, 240, 253)
            : error ? Color.FromArgb(255, 240, 231) : Color.FromArgb(235, 240, 247);
        label.ForeColor = ready ? Color.FromArgb(29, 104, 72) : warning ? Color.FromArgb(40, 88, 151)
            : error ? Color.FromArgb(143, 68, 30) : Color.FromArgb(83, 100, 120);
        if (label == scannerConnection)
        {
            scannerProgress.Style = warning ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
            scannerProgress.MarqueeAnimationSpeed = warning ? 35 : 0;
            scannerProgress.Value = ready ? 100 : 0;
        }
    }

    private async Task ApplyEndpointAsync()
    {
        if (!EndpointSettings.TryGetLocalEndpoint(endpointEditor.Text, out var newPort, out var newScheme))
        {
            endpointFeedback.Text = "Enter ws://localhost:PORT/ or wss://localhost:PORT/ with a port from 1 to 65535. Remote hosts and paths are not supported.";
            endpointFeedback.ForeColor = Color.FromArgb(143, 68, 30);
            AppendLog("ERROR endpoint validation: " + endpointFeedback.Text);
            return;
        }
        if (newPort == (int)port.Value && newScheme == endpointScheme)
        {
            endpointEditor.Text = $"{endpointScheme}://localhost:{port.Value}/";
            if (bridgeProcess != null) StartMonitor(newPort);
            endpointFeedback.Text = "Endpoint already saved.";
            AppendLog($"Reconnect requested: {newScheme}://localhost:{newPort}/");
            return;
        }
        SetManagerBusy(true);
        endpointFeedback.Text = "Saving endpoint…";
        endpointFeedback.ForeColor = Color.FromArgb(40, 88, 151);
        AppendLog($"Endpoint change requested: {endpointScheme}://localhost:{port.Value}/ -> {newScheme}://localhost:{newPort}/");
        var wasRunning = bridgeProcess != null;
        try
        {
            if (wasRunning) await PauseOwnedBridgeAsync();
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
                Path.Combine(AppContext.BaseDirectory, "setup.ps1"), "-ConfigureEndpoint", "-ManagerOwnedEndpoint", "-Port", newPort.ToString(), "-Protocol", newScheme, "-Verbose" })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start endpoint configuration.");
            var output = ReadEndpointLogAsync(process.StandardOutput, "setup");
            var error = ReadEndpointLogAsync(process.StandardError, "setup ERROR");
            await process.WaitForExitAsync();
            await Task.WhenAll(output, error);
            AppendLog($"Endpoint configuration exited with code {process.ExitCode}.");
            if (process.ExitCode != 0) throw new InvalidOperationException("Endpoint update failed. See the diagnostic log; the previous endpoint is retained unless recovery also failed.");
            endpointScheme = newScheme;
            port.Value = newPort;
            endpointEditor.Text = $"{endpointScheme}://localhost:{newPort}/";
            endpointFeedback.Text = $"Saved. Update your frontend URL to {endpointScheme}://localhost:{newPort}/ and reconnect.";
            endpointFeedback.ForeColor = Color.FromArgb(29, 104, 72);
        }
        catch (Exception ex)
        {
            endpointFeedback.Text = ex.Message;
            endpointFeedback.ForeColor = Color.FromArgb(143, 68, 30);
            AppendLog("ERROR endpoint change: " + ex);
        }
        finally
        {
            try { if (wasRunning && bridgeProcess == null) RunOwnedBridge(); }
            catch (Exception ex) { ShowBridgeFailure(ex); }
            finally { SetManagerBusy(false); }
        }
    }

    private async Task ReadEndpointLogAsync(StreamReader reader, string source)
    {
        while (await reader.ReadLineAsync() is { } line)
            if (!string.IsNullOrWhiteSpace(line)) AppendLog($"[{source}] {line}");
    }

    private async Task RunManagementCommandAsync(string command)
    {
        if (command != "Doctor") throw new ArgumentOutOfRangeException(nameof(command));
        var script = "& (Join-Path $env:ProgramFiles 'ThalesBridge\\setup.ps1') -Doctor -Port " + (int)port.Value + " -Protocol " + endpointScheme;
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", "$ErrorActionPreference = 'Stop'; " + script })
            start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start bridge management.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            AppendLog((await output).Trim());
            var errors = (await error).Trim();
            if (errors.Length > 0) AppendLog(errors);
            AppendLog($"{command}: {(process.ExitCode == 0 ? "completed" : "failed - run the installer to repair this installation")}");
        }
        catch (Exception ex) { AppendLog($"{command} failed: {ex.Message}"); }
    }
}
