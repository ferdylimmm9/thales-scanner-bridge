using System.Diagnostics;
using System.Text.Json;

namespace ThalesBridgeInstaller;

internal sealed partial class InstallerForm
{
    private bool managementBusy;

    private void InitializeManagerView()
    {
        Text = "Thales Scanner Bridge Manager";
        ClientSize = new Size(900, 720);
        MinimumSize = new Size(900, 720);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10F);
        AutoScaleMode = AutoScaleMode.Font;
        var title = new Label { Text = "Thales Scanner Bridge", Font = new Font("Segoe UI", 20F), AutoSize = true, Location = new Point(24, 20) };
        var description = new Label { Text = "Closing this window keeps the bridge running. It starts automatically when Windows starts.", AutoSize = true, Location = new Point(26, 64) };
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
        var endpoint = new Label { Text = $"Secure endpoint: wss://localhost:{port.Value}/", AutoSize = true, Location = new Point(26, 95) };
        bridgeConnection.SetBounds(26, 135, 700, 25);
        scannerConnection.SetBounds(26, 165, 700, 25);
        activity.SetBounds(26, 195, 700, 45);
        activity.AutoEllipsis = true;
        var controls = new FlowLayoutPanel { Location = new Point(26, 365), Size = new Size(830, 42) };
        foreach (var (label, command) in new[] { ("Start", "Start"), ("Stop", "Stop"), ("Restart", "Restart"), ("Enable auto-start", "Enable"), ("Diagnostics", "Doctor") })
        {
            var button = new Button { Text = label, AutoSize = true, Height = 32 };
            button.Click += async (_, _) =>
            {
                controls.Enabled = false;
                managementBusy = true;
                try { await RunManagementCommandAsync(command); }
                finally { managementBusy = false; controls.Enabled = true; }
            };
            controls.Controls.Add(button);
        }
        connectionDetails.SetBounds(26, 245, 830, 25);
        scanDetails.SetBounds(26, 275, 830, 25);
        scanDetails.Text = "Completed scans: 0 this session";
        lastError.SetBounds(26, 305, 830, 50);
        lastError.Text = "Last error: none in this session";
        lastError.AutoEllipsis = true;
        log.SetBounds(26, 420, 830, 270);
        log.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        log.ReadOnly = true;
        log.BackColor = Color.FromArgb(28, 28, 28);
        log.ForeColor = Color.Gainsboro;
        var saveReport = new Button { Text = "Save report", AutoSize = true, Height = 32 };
        saveReport.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog { Filter = "Text report (*.txt)|*.txt", FileName = $"ThalesBridge-{DateTime.Now:yyyyMMdd-HHmmss}.txt" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { File.WriteAllText(dialog.FileName, $"{endpoint.Text}\n{bridgeConnection.Text}\n{scannerConnection.Text}\n{connectionDetails.Text}\n{scanDetails.Text}\n{lastError.Text}\n\n{log.Text}"); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save report"); }
        };
        controls.Controls.Add(saveReport);
        Controls.AddRange([title, description, endpoint, bridgeConnection, scannerConnection, activity, connectionDetails, scanDetails, lastError, controls, log]);
        Shown += async (_, _) =>
        {
            StartMonitor((int)port.Value);
            controls.Enabled = false;
            managementBusy = true;
            try { await RunManagementCommandAsync("Doctor"); }
            finally { managementBusy = false; controls.Enabled = true; }
        };
        FormClosed += (_, _) => StopMonitor();
        FormClosing += (_, e) => { if (managementBusy) e.Cancel = true; };
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
