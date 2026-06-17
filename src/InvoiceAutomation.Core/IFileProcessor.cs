namespace InvoiceAutomation.Core;

public interface IFileProcessor
{
    Task<IReadOnlyList<string>> ExtractZipAsync(string zipPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// After extract: prefer folder path from invoice XML; fall back to row path.
    /// Moves extracted files when needed. Returns final path relative to downloadsRoot.
    /// </summary>
    string? RelocateToInvoicePath(
        string extractedFolderPath,
        string downloadsRoot,
        string? rowFallbackRelativePath,
        string? mstOverride = null,
        string invoiceKind = InvoiceKinds.Purchase);

    /// <summary>Extract all zips in staging, then relocate each folder using XML (row sidecar as fallback).</summary>
    Task<IReadOnlyList<string>> FinalizeStagingFolderAsync(
        string stagingFolder,
        string downloadsRoot,
        string? mstOverride = null,
        string invoiceKind = InvoiceKinds.Purchase,
        CancellationToken cancellationToken = default);
}
