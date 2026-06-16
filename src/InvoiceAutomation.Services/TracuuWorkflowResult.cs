namespace InvoiceAutomation.Services;

public sealed class TracuuWorkflowResult
{
    public string? LookupUrl { get; init; }
    public string? PdfPath { get; init; }

    /// <summary>No "Link tra cứu" on tracuuhoadon after upload — workflow stopped quietly.</summary>
    public bool Skipped { get; init; }

    /// <summary>Superseded by another double-click before PDF was captured.</summary>
    public bool Cancelled { get; init; }

    /// <summary>Only "In PDF" — native print/save dialog; Playwright cannot intercept.</summary>
    public bool RequiresManualPrint { get; init; }

    public string? StatusHint { get; init; }

    public bool PdfSaved =>
        !string.IsNullOrWhiteSpace(PdfPath) && File.Exists(PdfPath);
}
