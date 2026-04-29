namespace InvoiceAutomation.Core;

public sealed class JobParameters
{
    public string FromDate { get; init; } = "";
    public string ToDate { get; init; } = "";
    /// <summary>sales or purchase — passed as variable "invoiceKind" or "tab".</summary>
    public string InvoiceKind { get; init; } = "sales";
    public string DownloadsRoot { get; init; } = "";
    public Guid JobId { get; init; }
    /// <summary>GDT portal MST / username — overrides "gdtMst" in the flow JSON.</summary>
    public string GdtMst { get; init; } = "";
    /// <summary>GDT portal password — overrides "gdtPassword" in the flow JSON.</summary>
    public string GdtPassword { get; init; } = "";
}
