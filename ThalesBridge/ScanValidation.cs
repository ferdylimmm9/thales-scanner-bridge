namespace ThalesBridge;

/// <summary>A document image remains useful for IDs without machine-readable text.</summary>
internal static class ScanValidation
{
    public static bool HasUsableContent(MrzData mrz, ScanImages images) =>
        !string.IsNullOrWhiteSpace(mrz.LastName) ||
        !string.IsNullOrWhiteSpace(mrz.DocumentNumber) ||
        !string.IsNullOrWhiteSpace(images.Front) ||
        !string.IsNullOrWhiteSpace(images.Back);
}
