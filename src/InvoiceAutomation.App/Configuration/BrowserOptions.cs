namespace InvoiceAutomation.App.Configuration;

public sealed class BrowserOptions
{
    public const string SectionName = "Browser";

    public bool Headless { get; set; }
    public string? Channel { get; set; }
    public string? StorageStatePath { get; set; }
}
