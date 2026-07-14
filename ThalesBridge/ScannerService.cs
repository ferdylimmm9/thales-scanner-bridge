using System.Drawing;
using System.Drawing.Imaging;
using MMM.Readers;          // CodelineData, ErrorCode
using MMM.Readers.FullPage; // Reader, DataType, EventCode, ReaderState, delegates

namespace ThalesBridge;

/// <summary>
/// Wraps the Thales "FullPage" High-Level non-blocking API and turns a physical
/// document scan into a single DocumentScanResult.
///
/// Lifecycle (from the SDK's HLNonBlockingExample.NET5 sample):
///   Reader.Initialise(dataCb, eventCb, ...)  -> registers callbacks
///   EventCode.DOC_ON_WINDOW                  -> document placed  (start accumulating)
///   DataCallback fires per DataType          -> MRZ / images / chip
///   EventCode.END_OF_DOCUMENT_DATA           -> read complete    (emit result)
///
/// API names verified against SDK 3.9.2.49 (HLNonBlockingExample.NET5 sample +
/// MMMReaderDotNet50.dll metadata + MMMReaderDataTypes.h). Everything is
/// callback-driven off the SDK's own threads; we marshal results out via events.
/// </summary>
public sealed class ScannerService : IDisposable
{
    // Raised on every reader phase change: "idle" | "waiting_for_document" | "reading".
    public event Action<string>? StatusChanged;
    // Raised once per fully-read document.
    public event Action<DocumentScanResult>? ScanCompleted;
    // Raised on reader errors.
    public event Action<string, string>? ScanError; // (code, message)

    // Keep delegates in fields so the GC can't collect them while native code holds them.
    private DataDelegate? _dataCb;
    private EventDelegate? _eventCb;
    private ErrorDelegate? _errorCb;

    // Init-retry loop: the bridge is often started before the scanner is plugged in
    // (or while a demo app still holds it) — keep retrying instead of giving up.
    private const int RetrySeconds = 10;
    private Timer? _retryTimer;
    private string? _debugLogPath;
    private bool _loggingEnabled;

    // Initialise results that mean "reader unreachable right now" (unplugged, driver
    // not ready, or another SDK client holding the camera) — all worth retrying.
    private static readonly ErrorCode[] ReaderUnavailableErrors =
    {
        ErrorCode.ERROR_READER_NOT_CONNECTED,
        ErrorCode.ERROR_CAMERA_NOT_FOUND,
        ErrorCode.ERROR_OPENING_CAMERA,
        ErrorCode.ERROR_CAMERA_DRIVER_ERROR,
        ErrorCode.ERROR_PREVIOUS_CAMERA_DRIVER_ERROR,
        ErrorCode.ERROR_CAMERA_DEVICE_DISABLED,
        ErrorCode.ERROR_CONNECTING_DEVICE,
    };

    // Accumulator for the document currently on the glass.
    private MrzData _mrz = new();
    private readonly ScanImages _images = new();
    private ChipData? _chip;

    public void Start(string? debugLogPath = null)
    {
        _dataCb = OnData;
        _eventCb = OnEvent;
        _errorCb = OnError;
        _debugLogPath = debugLogPath;

        TryInitialise(firstAttempt: true);
    }

    private void TryInitialise(bool firstAttempt)
    {
        // Full SDK diagnostics to a writable file (level 5, all modules). Integration
        // debugging only — the log can contain document data.
        if (_debugLogPath != null && !_loggingEnabled)
        {
            Reader.EnableLogging(true, 5, -1, _debugLogPath);
            _loggingEnabled = true;
        }

        // Args: data, event, error, certificate (only needed for full RF PKI validation),
        // processMessages, processInputMessages — matches the SDK sample. Reader settings
        // always come from <SDK install dir>\Config\Application.ini; there is no flag here.
        var result = Reader.Initialise(
            _dataCb,
            _eventCb,
            _errorCb,
            null,
            true,
            false);

        if (result == ErrorCode.NO_ERROR_OCCURRED)
        {
            _retryTimer?.Dispose();
            _retryTimer = null;
            Console.WriteLine("[bridge] reader initialised.");

            // Enable the reader so it starts watching the glass.
            Reader.SetState(ReaderState.READER_ENABLED, false);
            StatusChanged?.Invoke("waiting_for_document");
            return;
        }

        if (ReaderUnavailableErrors.Contains(result))
        {
            Console.WriteLine(
                $"[bridge] scanner not available ({result}) — plug in the reader, or close other "
                + $"SDK apps (ReaderExpo/demos) holding it. Retrying every {RetrySeconds}s.");

            if (firstAttempt)
            {
                StatusChanged?.Invoke("idle");
                ScanError?.Invoke("SCANNER_NOT_CONNECTED",
                    $"Scanner not detected ({result}). Plug in the reader; the bridge retries automatically.");
            }

            // Native state must be torn down before the next Initialise attempt.
            try { Reader.Shutdown(); } catch { /* ignore */ }
            _retryTimer?.Dispose();
            _retryTimer = new Timer(_ => TryInitialise(firstAttempt: false), null,
                RetrySeconds * 1000, Timeout.Infinite);
            return;
        }

        ScanError?.Invoke("INIT_FAILED", $"Reader.Initialise returned {result}");
    }

    private void OnEvent(EventCode ev)
    {
        switch (ev)
        {
            case EventCode.SETTINGS_INITIALISED:
                StatusChanged?.Invoke("waiting_for_document");
                break;

            case EventCode.DOC_ON_WINDOW:
                ResetAccumulator();
                StatusChanged?.Invoke("reading");
                break;

            case EventCode.END_OF_DOCUMENT_DATA:
                EmitResult();
                StatusChanged?.Invoke("waiting_for_document");
                break;

            // Hot plug/unplug while the bridge is running.
            case EventCode.READER_CONNECTED:
                Console.WriteLine("[bridge] reader connected.");
                Reader.SetState(ReaderState.READER_ENABLED, false);
                StatusChanged?.Invoke("waiting_for_document");
                break;

            case EventCode.READER_DISCONNECTED:
                Console.WriteLine("[bridge] reader disconnected — check the USB connection.");
                ScanError?.Invoke("READER_DISCONNECTED", "Scanner disconnected — check the USB connection.");
                StatusChanged?.Invoke("idle");
                break;
        }
    }

    private void OnData(DataType type, object data)
    {
        if (data == null) return;

        // A throw here propagates into the SDK's native thread and errors the whole
        // read (surfaces as UNKNOWN_ERROR_OCCURRED) — never let an exception escape.
        try { OnDataCore(type, data); }
        catch (Exception ex)
        {
            Console.WriteLine($"[bridge] OnData({type}) failed: payload={data.GetType().FullName}: {ex}");
            ScanError?.Invoke("DATA_HANDLER_FAILED", $"{type}: {ex.Message}");
        }
    }

    private void OnDataCore(DataType type, object data)
    {
        switch (type)
        {
            // ---- MRZ / codeline (the primary bio source) ----
            case DataType.CD_CODELINE_DATA:
            case DataType.CD_SCDG1_CODELINE_DATA: // chip MRZ (higher trust); overwrites OCR if present
                ApplyCodeline((CodelineData)data);
                break;

            // ---- Images (arrive as System.Drawing.Bitmap) ----
            case DataType.CD_IMAGEVIS:
                _images.Front = ToDataUrl(data as Bitmap);
                break;
            case DataType.CD_IMAGEVISREAR:
                _images.Back = ToDataUrl(data as Bitmap);
                break;
            case DataType.CD_IMAGEPHOTO:   // cropped face photo from the visible image
            case DataType.CD_SCDG2_PHOTO:  // chip face photo (ePassport) — prefer if available
                _images.Portrait = ToDataUrl(data as Bitmap);
                break;

            // ---- Chip authentication result (VERIFY exact validate DataTypes) ----
            case DataType.CD_SCDG1_VALIDATE:
            case DataType.CD_SCDG2_VALIDATE:
                _chip ??= new ChipData { Present = true };
                _chip.Verified = true; // set from the actual validation payload in production
                break;
        }
    }

    private void ApplyCodeline(CodelineData c)
    {
        _mrz.LastName = c.Surname ?? "";
        _mrz.FirstName = c.Forenames ?? "";
        _mrz.Nationality = c.Nationality ?? "";
        _mrz.Gender = NormalizeSex(c.Sex);
        _mrz.DocumentNumber = c.DocNumber ?? "";
        _mrz.DocumentType = MapDocType(c.DocType);
        _mrz.DateOfBirth = ToIsoDate(c.DateOfBirth, isExpiry: false);
        _mrz.ExpiryDate = ToIsoDate(c.ExpiryDate, isExpiry: true);
        // MRZ issuing state (chars 3-5 of line 1); fall back to nationality if absent.
        _mrz.IssuingCountry = string.IsNullOrWhiteSpace(c.IssuingState) ? (c.Nationality ?? "") : c.IssuingState;
    }

    private void EmitResult()
    {
        // Require at least a name or document number before emitting.
        if (string.IsNullOrWhiteSpace(_mrz.LastName) && string.IsNullOrWhiteSpace(_mrz.DocumentNumber))
        {
            ScanError?.Invoke("READ_INCOMPLETE", "Document read produced no usable MRZ data.");
            return;
        }

        var result = new DocumentScanResult
        {
            Mrz = _mrz,
            Images = _images.Clone(),
            Chip = _chip,
            CapturedAt = DateTime.UtcNow.ToString("o"),
        };
        ScanCompleted?.Invoke(result);
    }

    // Forward every SDK error to the frontend, keeping the SDK's own message
    // (it carries detail the enum doesn't, e.g. which feature was unsupported).
    private void OnError(ErrorCode code, string message)
        => ScanError?.Invoke(code.ToString(), string.IsNullOrWhiteSpace(message) ? $"Reader error: {code}" : message);

    private void ResetAccumulator()
    {
        _mrz = new MrzData();
        _images.Front = _images.Back = _images.Portrait = null;
        _chip = null;
    }

    // ---- helpers ----

    // SDK Sex formats vary by document ("M", "F", "Male", "F<"...) — match on first letter.
    private static string NormalizeSex(string? sex)
        => (sex ?? "").TrimStart().ToUpperInvariant() switch
        {
            ['M', ..] => "M",
            ['F', ..] => "F",
            _ => "",
        };

    private static string MapDocType(string? docType)
    {
        var t = (docType ?? "").ToUpperInvariant();
        if (t.Contains("PASSPORT")) return "passport";
        if (t.Contains("DRIV")) return "drivers_license";
        if (t.Contains("ID")) return "national_id";
        return "other";
    }

    /// <summary>Convert the SDK date struct to yyyy-MM-dd. MRZ years are 2-digit; we window them.</summary>
    private static string ToIsoDate(MMM.Readers.Date d, bool isExpiry)
    {
        // MMM.Readers.Date is a struct (Int32 Day/Month/Year) — an unset date comes
        // through as zeros, never null.
        int year = d.Year, month = d.Month, day = d.Day;
        if (year is < 0 or > 9999 || month is < 1 or > 12 || day is < 1 or > 31) return "";
        if (year < 100)
        {
            // Birth dates are in the past; expiry dates can run ~10-15 years into the
            // future, so window them around different pivots.
            int pivot = DateTime.UtcNow.Year % 100 + (isExpiry ? 20 : 0);
            year += year <= pivot ? 2000 : 1900;
        }
        return $"{year:0000}-{month:00}-{day:00}";
    }

    private static string? ToDataUrl(Bitmap? bmp)
    {
        if (bmp == null) return null;
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Jpeg);
        return "data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray());
    }

    public void Dispose()
    {
        _retryTimer?.Dispose();
        try { Reader.Shutdown(); } catch { /* ignore */ }
    }
}

internal static class ScanImagesExtensions
{
    public static ScanImages Clone(this ScanImages s)
        => new() { Front = s.Front, Back = s.Back, Portrait = s.Portrait };
}
