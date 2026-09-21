using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ThalesBridgeInstaller;

internal sealed partial class InstallerForm
{
    private readonly ComboBox captureModePicker = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly Button captureNow = MakeManagerButton("Capture now");
    private ClientWebSocket? commandSocket;
    private string? confirmedCaptureMode;
    private string? capturePhase;
    private string? pendingCommandId;
    private TaskCompletionSource<bool>? commandCompletion;

    private Control CreateCaptureControls()
    {
        var row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 8, 0, 8) };
        row.Controls.Add(new Label { Text = "Capture mode", AutoSize = true, Padding = new Padding(0, 6, 8, 0) });
        captureModePicker.Items.AddRange(new object[] { "Automatic", "Manual" });
        captureModePicker.AccessibleName = "Scanner capture mode";
        captureModePicker.SelectionChangeCommitted += async (_, _) =>
        {
            var mode = captureModePicker.SelectedIndex == 1 ? "manual" : "automatic";
            UpdateCaptureControls();
            await SendCaptureCommandAsync("set_capture_mode", mode);
        };
        captureNow.Click += async (_, _) => await SendCaptureCommandAsync("capture");
        row.Controls.Add(captureModePicker);
        row.Controls.Add(captureNow);
        row.Controls.Add(new Label { Text = "Manual: place document, then capture. Mode is shared with Player360.", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        UpdateCaptureControls();
        return row;
    }

    private void UpdateCaptureControls()
    {
        captureModePicker.SelectedIndex = confirmedCaptureMode == "automatic" ? 0 : confirmedCaptureMode == "manual" ? 1 : -1;
        var ready = commandSocket?.State == WebSocketState.Open && capturePhase == "waiting_for_document"
            && confirmedCaptureMode != null && pendingCommandId == null && !managementBusy;
        captureModePicker.Enabled = ready;
        captureNow.Enabled = ready && confirmedCaptureMode == "manual";
        captureNow.Text = capturePhase == "reading" ? "Capturing…" : "Capture now";
    }

    private void ResetCaptureConnection()
    {
        commandSocket = null;
        confirmedCaptureMode = capturePhase = null;
        commandCompletion?.TrySetException(new IOException("Bridge disconnected before command confirmation."));
        UpdateCaptureControls();
    }

    private async Task SendCaptureCommandAsync(string type, string? mode = null)
    {
        var socket = commandSocket;
        if (pendingCommandId != null || socket?.State != WebSocketState.Open) return;
        pendingCommandId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        commandCompletion = completion;
        UpdateCaptureControls();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type, requestId = pendingCommandId, mode }));
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, timeout.Token);
            await completion.Task.WaitAsync(timeout.Token);
        }
        catch (Exception ex)
        {
            var message = ex is OperationCanceledException ? "Command confirmation timed out. Check scanner status before retrying." : ex.Message;
            AppendLog(message);
            SetConnectionLabel(lastError, message, Color.DarkRed);
        }
        finally { pendingCommandId = null; commandCompletion = null; UpdateCaptureControls(); }
    }

    private void ProcessCaptureMessage(JsonElement root)
    {
        if (root.GetProperty("type").GetString() == "status")
        {
            confirmedCaptureMode = root.TryGetProperty("captureMode", out var mode) && mode.GetString() is "automatic" or "manual" ? mode.GetString() : null;
            capturePhase = root.GetProperty("status").GetString();
            UpdateCaptureControls();
        }
        else if (root.GetProperty("requestId").GetString() == pendingCommandId)
        {
            if (root.GetProperty("success").GetBoolean()) commandCompletion?.TrySetResult(true);
            else commandCompletion?.TrySetException(new IOException(root.TryGetProperty("message", out var error) ? error.GetString() : "Scanner command failed."));
        }
    }
}
