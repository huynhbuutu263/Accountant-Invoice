namespace InvoiceAutomation.Core.Models;

/// <summary>JSON: expect — post-condition after a step.</summary>
public sealed class StepExpect
{
    public string? Selector { get; set; }
    /// <summary>visible, hidden, attached</summary>
    public string? State { get; set; }
    public string? UrlContains { get; set; }
    public int? TimeoutMs { get; set; }
}
