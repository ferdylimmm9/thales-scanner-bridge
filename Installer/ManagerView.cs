using System.Diagnostics;
using System.Text.Json;

namespace ThalesBridgeInstaller;

internal sealed partial class InstallerForm
{
    private bool managementBusy;

    private readonly TextBox endpointEditor = new();
    private readonly Label endpointFeedback = new();
    private readonly ProgressBar scannerProgress = new();
    private FlowLayoutPanel? managerActions;
    private Button? applyEndpoint;
    private bool managerView;

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
        }
        catch (Exception ex) { AppendLog("Using default port: " + ex.Message); }
        port.Minimum = 1;
        port.Maximum = 65535;
        port.Value = Math.Clamp(configuredPort, 1, 65535);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26), ColumnCount = 1, RowCount = 12 };
        for (int i = 0; i < 11; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var title = new Label { Text = "Thales Scanner Bridge", Font = new Font("Segoe UI", 21F), AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        var description = new Label { Text = "Manage your local connection and scanner. Closing this window keeps the bridge running.", AutoSize = true, Margin = new Padding(0, 0, 0, 22) };
        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(description, 0, 1);
        var endpointRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Margin = new Padding(0) };
        endpointRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        endpointRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        endpointRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        endpointRow.Controls.Add(new Label { Text = "Secure endpoint", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 16, 0) }, 0, 0);
        endpointEditor.Text = $"wss://localhost:{port.Value}/";
        endpointEditor.Dock = DockStyle.Fill;
        endpointEditor.AccessibleName = "Local secure WebSocket endpoint";
        applyEndpoint = MakeManagerButton("Apply endpoint");
        applyEndpoint.Click += async (_, _) => await ApplyEndpointAsync();
        endpointRow.Controls.Add(endpointEditor, 1, 0);
        endpointRow.Controls.Add(applyEndpoint, 2, 0);
        layout.Controls.Add(endpointRow, 0, 2);
        endpointFeedback.Text = "Use wss://localhost:PORT/. Applying restarts the bridge; update your frontend URL too.";
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
        bridgeConnection.Text = "Bridge: checking connection";
        scannerConnection.Text = "Scanner: waiting for bridge";
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
        foreach (var (label, command) in new[] { ("Start", "Start"), ("Stop", "Stop"), ("Restart", "Restart"), ("Enable auto-start", "Enable"), ("Diagnostics", "Doctor") })
        {
            var button = MakeManagerButton(label);
            button.Click += async (_, _) =>
            {
                SetManagerBusy(true);
                try { await RunManagementCommandAsync(command); }
                finally { SetManagerBusy(false); }
            };
            managerActions.Controls.Add(button);
        }
        var saveReport = MakeManagerButton("Save report");
        saveReport.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog { Filter = "Text report (*.txt)|*.txt", FileName = $"ThalesBridge-{DateTime.Now:yyyyMMdd-HHmmss}.txt" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { File.WriteAllText(dialog.FileName, $"wss://localhost:{port.Value}/\n{bridgeConnection.Text}\n{scannerConnection.Text}\n{connectionDetails.Text}\n{scanDetails.Text}\n{lastError.Text}\n\n{log.Text}"); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save report"); }
        };
        managerActions.Controls.Add(saveReport);
        layout.Controls.Add(managerActions, 0, 10);
        log.Dock = DockStyle.Fill;
        log.ReadOnly = true;
        log.BackColor = Color.White;
        log.ForeColor = Color.FromArgb(55, 70, 90);
        log.BorderStyle = BorderStyle.FixedSingle;
        log.Font = new Font("Consolas", 9F);
        layout.Controls.Add(log, 0, 11);
        Controls.Add(layout);
        void FitText()
        {
            foreach (var label in new[] { description, endpointFeedback, activity, connectionDetails, scanDetails, lastError })
                label.MaximumSize = new Size(Math.Max(300, ClientSize.Width - 70), 0);
        }
        Resize += (_, _) => FitText();
        FitText();
        Shown += async (_, _) =>
        {
            StartMonitor((int)port.Value);
            SetManagerBusy(true);
            try { await RunManagementCommandAsync("Doctor"); }
            finally { SetManagerBusy(false); }
        };
        FormClosed += (_, _) => StopMonitor();
        FormClosing += (_, e) => { if (managementBusy) e.Cancel = true; };
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
        if (managerActions != null) managerActions.Enabled = !busy;
        if (applyEndpoint != null) applyEndpoint.Enabled = !busy;
        endpointEditor.Enabled = !busy;
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
        if (!EndpointSettings.TryGetLocalPort(endpointEditor.Text, out var newPort))
        {
            endpointFeedback.Text = "Enter wss://localhost:PORT/ with a port from 1 to 65535. Remote hosts and paths are not supported.";
            endpointFeedback.ForeColor = Color.FromArgb(143, 68, 30);
            return;
        }
        if (newPort == (int)port.Value)
        {
            endpointEditor.Text = $"wss://localhost:{port.Value}/";
            endpointFeedback.Text = "This endpoint is already active.";
            return;
        }
        SetManagerBusy(true);
        endpointFeedback.Text = "Applying endpoint and checking the secure connection…";
        endpointFeedback.ForeColor = Color.FromArgb(40, 88, 151);
        var previousMonitor = monitorTask;
        StopMonitor();
        try
        {
            if (previousMonitor != null) await previousMonitor;
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
                Path.Combine(AppContext.BaseDirectory, "setup.ps1"), "-ConfigureEndpoint", "-Port", newPort.ToString() })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start endpoint configuration.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            AppendLog((await output).Trim());
            var errors = (await error).Trim();
            if (errors.Length > 0) AppendLog(errors);
            if (process.ExitCode != 0) throw new InvalidOperationException("Endpoint update failed. See the diagnostic log; the previous endpoint is retained unless recovery also failed.");
            port.Value = newPort;
            endpointEditor.Text = $"wss://localhost:{newPort}/";
            endpointFeedback.Text = $"Saved. Update your frontend URL to wss://localhost:{newPort}/ and reconnect.";
            endpointFeedback.ForeColor = Color.FromArgb(29, 104, 72);
        }
        catch (Exception ex)
        {
            endpointFeedback.Text = ex.Message;
            endpointFeedback.ForeColor = Color.FromArgb(143, 68, 30);
            AppendLog(ex.Message);
        }
        finally
        {
            previousScannerStatus = null;
            StartMonitor((int)port.Value);
            SetManagerBusy(false);
        }
    }

    private async Task RunManagementCommandAsync(string command)
    {
        // Commands are fixed application actions, never shell text from an input field.
        var script = command switch
        {
            "Start" => "Start-ScheduledTask -TaskName ThalesBridge",
            "Stop" => "Stop-ScheduledTask -TaskName ThalesBridge; Get-Process ThalesBridge -ErrorAction SilentlyContinue | Stop-Process -Force",
            "Restart" => "Stop-ScheduledTask -TaskName ThalesBridge; Get-Process ThalesBridge -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep -Seconds 1; Start-ScheduledTask -TaskName ThalesBridge",
            "Enable" => "Enable-ScheduledTask -TaskName ThalesBridge | Out-Null; Set-ScheduledTask -TaskName ThalesBridge -Trigger (New-ScheduledTaskTrigger -AtStartup) -Principal (New-ScheduledTaskPrincipal -UserId SYSTEM -LogonType ServiceAccount -RunLevel Highest) | Out-Null; Start-ScheduledTask -TaskName ThalesBridge",
            "Doctor" => "& (Join-Path $env:ProgramFiles 'ThalesBridge\\setup.ps1') -Doctor -Port " + (int)port.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };
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
