using System.Reflection;
using ThalesBridge;

// Usage: ThalesBridge.exe [port] [--verbose] [--debug-log <file>] [--version]
//   port      must match NEXT_PUBLIC_THALES_BRIDGE_URL in the frontend (default 8765)
//   --verbose log every outgoing JSON frame (contract tracking during integration).
//             Result frames contain patron PII — use during integration/testing only.
//   --debug-log <file>  write full SDK diagnostics (level 5, all modules) to <file>.
//             Can contain document data — integration debugging only.
//   --version print the release version (set at publish time via /p:Version) and exit.
if (args.Contains("--version"))
{
    var version = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0-dev";
    Console.WriteLine(version);
    return 0;
}

bool verbose = args.Contains("--verbose");
int port = args.FirstOrDefault(a => int.TryParse(a, out _)) is { } pArg ? int.Parse(pArg) : 8765;
string? debugLog = null;
if (Array.IndexOf(args, "--debug-log") is var dbgIdx and >= 0 && dbgIdx + 1 < args.Length)
    debugLog = args[dbgIdx + 1];

Console.WriteLine($"[bridge] Thales Document Reader bridge starting on ws://localhost:{port}"
    + (verbose ? " (verbose: logging contract frames)" : ""));

await using var hub = new WebSocketHub(port);

try
{
    hub.Start();
}
catch (InvalidOperationException ex)
{
    // Port in use / URL ACL missing — the hub already built an actionable message.
    Console.Error.WriteLine($"[bridge] FATAL: {ex.Message}");
    return 1;
}

using var scanner = new ScannerService();

// Broadcast + optional contract-frame log (base64 images truncated by ToLogJson).
Task Send(ScannerMessage message)
{
    if (verbose) Console.WriteLine($"[bridge] >> {message.ToLogJson()}");
    return hub.BroadcastAsync(message);
}

// Marshal scanner callbacks onto the WebSocket. Fire-and-forget is fine — broadcast is resilient.
scanner.StatusChanged += status =>
{
    Console.WriteLine($"[bridge] status: {status}");
    _ = Send(ScannerMessage.StatusMsg(status));
};
scanner.ScanCompleted += result =>
{
    Console.WriteLine($"[bridge] scan complete: {result.Mrz.LastName}, {result.Mrz.FirstName} ({result.Mrz.DocumentNumber})");
    _ = Send(ScannerMessage.Result(result));
};
scanner.ScanError += (code, message) =>
{
    Console.WriteLine($"[bridge] error {code}: {message}");
    _ = Send(ScannerMessage.Error(code, message));
};

if (debugLog != null) Console.WriteLine($"[bridge] SDK debug log -> {debugLog}");
scanner.Start(debugLog);

Console.WriteLine("[bridge] running. Place a document on the reader. Press Ctrl+C to quit.");

// Block until Ctrl+C / SIGTERM so the reader keeps serving.
var exit = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; exit.TrySetResult(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => exit.TrySetResult();
await exit.Task;

Console.WriteLine("[bridge] shutting down.");
return 0;
