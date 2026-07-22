using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;

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

internal sealed class InstallerForm : Form
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
    private Process? installerProcess;
    private bool installSucceeded;

    public InstallerForm()
    {
        Text = "Thales Scanner Bridge Setup";
        ClientSize = new Size(720, 610);
        MinimumSize = new Size(650, 560);
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

        var logLabel = new Label { Text = "Installation log", AutoSize = true, Location = new Point(27, 284) };
        log.Location = new Point(30, 307);
        log.Size = new Size(660, 225);
        log.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        log.ReadOnly = true;
        log.BackColor = Color.FromArgb(28, 28, 28);
        log.ForeColor = Color.Gainsboro;
        log.Font = new Font("Consolas", 9F);
        log.WordWrap = false;

        install.Text = "Install";
        install.Size = new Size(110, 34);
        install.Location = new Point(460, 552);
        install.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        install.Click += async (_, _) => await RunInstallAsync();

        close.Text = "Close";
        close.Size = new Size(110, 34);
        close.Location = new Point(580, 552);
        close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        close.Click += (_, _) => Close();

        AcceptButton = install;
        CancelButton = close;
        Controls.AddRange([
            title, intro, status, sdkLabel, sdkMsi, browse, portLabel, port,
            disableUvIr, startAtLogon, logLabel, log, install, close
        ]);
        FormClosing += ConfirmCloseWhileRunning;
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
            status.Text = $"Installed successfully. Bridge URL: ws://localhost:{(int)port.Value}";
            status.ForeColor = Color.DarkGreen;
            install.Text = "Installed";
            AppendLog("Installation completed successfully.");
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
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => AppendLog(text)));
            return;
        }
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
