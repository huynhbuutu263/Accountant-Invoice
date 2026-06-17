namespace InvoiceAutomation.Core;

public sealed class JobParameters
{
    public string FromDate { get; init; } = "";
    public string ToDate { get; init; } = "";
    /// <summary>purchase (mua vào) | sales (bán ra)</summary>
    public string InvoiceKind { get; init; } = InvoiceKinds.Purchase;
    public string DownloadsRoot { get; init; } = "";
    public Guid JobId { get; init; }
    /// <summary>GDT portal MST / username — overrides "gdtMst" in the flow JSON.</summary>
    public string GdtMst { get; init; } = "";
    /// <summary>GDT portal password — overrides "gdtPassword" in the flow JSON.</summary>
    public string GdtPassword { get; init; } = "";
}
