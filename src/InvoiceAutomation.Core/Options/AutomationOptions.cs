namespace InvoiceAutomation.Core.Options;

public sealed class AutomationOptions
{
    public const string SectionName = "Automation";

    public int DefaultTimeoutMs { get; set; } = 30_000;
    public int DefaultRetries { get; set; } = 3;
    public int[] RetryBackoffMs { get; set; } = [1000, 2000, 4000];
    public string[] NonRetryableSubstrings { get; set; } = ["invalid credentials"];
}
