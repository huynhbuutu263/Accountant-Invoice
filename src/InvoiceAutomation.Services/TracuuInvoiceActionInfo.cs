namespace InvoiceAutomation.Services;

/// <summary>What tracuuhoadon shows after XML upload (search hint vs print-only).</summary>
public sealed class TracuuInvoiceActionInfo
{
    public bool HasDirectPdfLink { get; init; }
    public string? PdfLinkHref { get; init; }
    public bool HasPrintPdfButton { get; init; }
    public string? PrintButtonLabel { get; init; }

    /// <summary>"Gợi ý tra cứu" fieldset with datatable / link rows — user clicks to download.</summary>
    public bool HasSearchHint { get; init; }

    public bool PreferManualDownload => HasSearchHint;

    public bool IsPrintOnly => HasPrintPdfButton && !PreferManualDownload;
}
