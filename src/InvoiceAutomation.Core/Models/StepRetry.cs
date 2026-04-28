namespace InvoiceAutomation.Core.Models;

/// <summary>JSON: retry</summary>
public sealed class StepRetry
{
    public int? Count { get; set; }
    public int[]? BackoffMs { get; set; }
}
