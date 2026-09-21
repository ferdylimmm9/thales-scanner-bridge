namespace ThalesBridge;

/// <summary>Bridge-wide mode and busy state. SDK calls run on the SDK work queue.</summary>
internal sealed class CaptureCoordinator(Func<bool, string?> setAutomatic, Func<string?> forceRead)
{
    private readonly object _gate = new();
    private string _mode = "automatic";
    private bool _ready;
    private bool _reading;
    private bool _changing;
    public event Action? Changed;
    public (string Mode, string Phase) Snapshot
    {
        get { lock (_gate) return (_mode, !_ready ? "idle" : _reading ? "reading" : "waiting_for_document"); }
    }
    public void SetReady(bool ready)
    {
        lock (_gate) { _ready = ready; if (!ready) _reading = false; }
        Changed?.Invoke();
    }
    public void BeginRead()
    {
        lock (_gate) { if (!_ready) return; _reading = true; }
        Changed?.Invoke();
    }
    public void EndRead()
    {
        lock (_gate) _reading = false;
        Changed?.Invoke();
    }
    public string? ApplyCurrentMode() => setAutomatic(Snapshot.Mode == "automatic");
    public string? SetMode(string mode)
    {
        if (mode is not ("automatic" or "manual")) return "INVALID_MODE";
        lock (_gate)
        {
            if (!_ready) return "SCANNER_NOT_CONNECTED";
            if (_reading || _changing) return "SCANNER_BUSY";
            if (_mode == mode) return null;
            _changing = true;
        }
        try
        {
            var error = setAutomatic(mode == "automatic");
            lock (_gate)
            {
                if (error != null) return error;
                if (!_ready) return "SCANNER_NOT_CONNECTED";
                _mode = mode;
            }
            Changed?.Invoke();
            return null;
        }
        finally { lock (_gate) _changing = false; }
    }
    public string? Capture()
    {
        lock (_gate)
        {
            if (!_ready) return "SCANNER_NOT_CONNECTED";
            if (_reading || _changing) return "SCANNER_BUSY";
            if (_mode != "manual") return "MANUAL_MODE_REQUIRED";
            _reading = true; // Reserve before native callbacks or another browser's command.
        }
        Changed?.Invoke();
        try
        {
            var error = forceRead();
            if (error != null) EndRead();
            return error;
        }
        catch { EndRead(); throw; }
    }
}
