using System.Text.Json;
namespace ThalesBridge;

internal sealed record ScannerCommand(string Type, string RequestId, string? Mode)
{
    public static ScannerCommand Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("Expected a command object.");
        string? Get(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        var type = Get("type");
        var id = Get("requestId");
        var mode = Get("mode");
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64) throw new FormatException("A requestId of 1–64 characters is required.");
        if (type is not ("capture" or "set_capture_mode")) throw new FormatException("Unknown scanner command.");
        if (type == "set_capture_mode" && mode is not ("automatic" or "manual")) throw new FormatException("Mode must be automatic or manual.");
        return new ScannerCommand(type, id, mode);
    }
}
