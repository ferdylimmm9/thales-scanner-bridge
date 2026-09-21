using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ThalesBridge;

// These DTOs are the ON-THE-WIRE contract shared with the React app.
// They MUST stay in sync with src/types/documentScan.ts in the frontend repo.
// Serialized as camelCase JSON (see JsonOpts below).

/// <summary>One completed scan produced by the reader.</summary>
public sealed class DocumentScanResult
{
    public required MrzData Mrz { get; init; }
    public required ScanImages Images { get; init; }
    public ChipData? Chip { get; init; }
    public required string CapturedAt { get; init; } // ISO 8601 UTC
}

public sealed class MrzData
{
    public string FirstName { get; set; } = "";
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public string DocumentType { get; set; } = "other"; // passport | national_id | drivers_license | other
    public string DateOfBirth { get; set; } = "";       // ISO 8601 date (yyyy-MM-dd)
    public string Gender { get; set; } = "";            // "M" | "F"
    public string Nationality { get; set; } = "";
    public string IssuingCountry { get; set; } = "";
    public string ExpiryDate { get; set; } = "";        // ISO 8601 date (yyyy-MM-dd)
}

public sealed class ScanImages
{
    public string? Front { get; set; }    // data URL, e.g. "data:image/jpeg;base64,...."
    public string? Back { get; set; }
    public string? Portrait { get; set; } // extracted face photo
}

public sealed class ChipData
{
    public bool Present { get; set; }
    public bool Verified { get; set; }    // ePassport RFID passive-auth result
}

/// <summary>Envelope for every frame sent to clients. Matches ScannerMessage in the frontend.</summary>
public sealed class ScannerMessage
{
    [JsonPropertyName("type")]
    public required string Type { get; init; } // "status" | "result" | "error" | "command_result"

    public string? CaptureMode { get; init; } // status: automatic | manual
    public string? RequestId { get; init; } // command_result
    public bool? Success { get; init; } // command_result
    public string? Status { get; init; }       // for type=status: idle | waiting_for_document | reading
    public DocumentScanResult? Data { get; init; } // for type=result
    public string? Code { get; init; }         // for type=error
    public string? Message { get; init; }      // for type=error

    public static ScannerMessage StatusMsg(string status, string? captureMode = null) => new() { Type = "status", Status = status, CaptureMode = captureMode };
    public static ScannerMessage CommandResult(string requestId, string? error) => new()
    { Type = "command_result", RequestId = requestId, Success = error == null, Code = error, Message = error == null ? null : $"Scanner command failed: {error}." };
    public static ScannerMessage Result(DocumentScanResult data) => new() { Type = "result", Data = data };
    public static ScannerMessage Error(string code, string message) =>
        new() { Type = "error", Code = code, Message = message };

    private static readonly JsonSerializerOptions LogJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Serialize this frame for console logging with base64 images truncated.
    /// NOTE: result frames contain patron PII — only print behind the --verbose flag,
    /// and never write this output to a file in production.
    /// </summary>
    public string ToLogJson()
    {
        var json = JsonSerializer.Serialize(this, LogJsonOpts);
        return Regex.Replace(
            json,
            "\"data:image/[^;\"]+;base64,[^\"]{24}[^\"]*\"",
            m => $"{m.Value[..40]}…({m.Value.Length - 2} chars)\"");
    }
}
